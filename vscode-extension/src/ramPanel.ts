import * as vscode from 'vscode';

const DEBUG_TYPE = 'winasm65';
const RAM_START = 0x0000;
const RAM_LENGTH = 0x0800;
const LIVE_INTERVAL_MS = 500;

interface RamSymbol {
  name: string;
  value: number;
}

/**
 * The machine's 2 KiB of RAM ($0000-$07FF) in a panel beside the editor, read
 * through the debug adapter with the standard readMemory request. It refreshes
 * on every stop, marks the bytes that changed since the previous stop, shades the
 * zero page and the stack (with the byte at $0100+SP outlined), names the bytes
 * the build defined, and writes a byte edited in place with writeMemory.
 */
export class RamPanel implements vscode.Disposable {
  private panel: vscode.WebviewPanel | undefined;
  private session: vscode.DebugSession | undefined;
  private previous: Uint8Array | undefined;
  private symbols: RamSymbol[] | undefined;
  private running = false;
  private live = false;
  private liveTimer: NodeJS.Timeout | undefined;
  private refreshing = false;
  private readonly subscriptions: vscode.Disposable[] = [];

  constructor(private readonly context: vscode.ExtensionContext) {
    this.subscriptions.push(
      vscode.commands.registerCommand('winasm65.showRam', () => this.show()),
      vscode.debug.registerDebugAdapterTrackerFactory(DEBUG_TYPE, {
        createDebugAdapterTracker: session => ({
          onDidSendMessage: (message: { type?: string; event?: string }) => {
            if (message.type !== 'event') {
              return;
            }
            if (message.event === 'stopped') {
              void this.onStopped(session);
            } else if (message.event === 'continued') {
              this.setRunning(session, true);
            }
          }
        })
      }),
      vscode.debug.onDidTerminateDebugSession(session => {
        if (session === this.session) {
          this.session = undefined;
          this.stopLive();
          this.post({ type: 'status', text: 'session ended' });
        }
      })
    );
  }

  private show(): void {
    if (this.panel) {
      this.panel.reveal(vscode.ViewColumn.Beside, true);
      return;
    }
    this.panel = vscode.window.createWebviewPanel('winasm65.ram', 'RAM $0000-$07FF', {
      viewColumn: vscode.ViewColumn.Beside,
      preserveFocus: true
    }, { enableScripts: true, retainContextWhenHidden: true });
    this.panel.webview.html = html(this.panel.webview);
    this.panel.onDidDispose(() => {
      this.panel = undefined;
      this.stopLive();
    }, null, this.subscriptions);
    this.panel.webview.onDidReceiveMessage(message => this.onMessage(message), null, this.subscriptions);

    const active = vscode.debug.activeDebugSession;
    if (active && active.type === DEBUG_TYPE) {
      this.session = active;
      void this.refresh();
    }
  }

  private async onStopped(session: vscode.DebugSession): Promise<void> {
    if (session !== this.session) {
      this.session = session;
      this.previous = undefined;
      this.symbols = undefined;
    }
    this.running = false;
    if (!this.panel && vscode.workspace.getConfiguration('winasm65').get<boolean>('ram.openOnStop', true)) {
      this.show();
    }
    await this.refresh();
  }

  private setRunning(session: vscode.DebugSession, running: boolean): void {
    this.session = session;
    this.running = running;
    this.post({ type: 'status', text: running ? 'running' : 'stopped' });
  }

  private async refresh(fromLive = false): Promise<void> {
    const session = this.session;
    if (!this.panel || !session || this.refreshing) {
      return;
    }
    this.refreshing = true;
    try {
      const read = await session.customRequest('readMemory', {
        memoryReference: '0x' + RAM_START.toString(16).padStart(4, '0'),
        count: RAM_LENGTH
      });
      const bytes = Uint8Array.from(Buffer.from(read.data as string, 'base64'));

      let sp: number | undefined;
      try {
        const evaluated = await session.customRequest('evaluate', { expression: 'SP', context: 'watch' });
        sp = parseInt(String(evaluated.result).replace('$', ''), 16);
      } catch {
        sp = undefined;
      }

      if (!this.symbols) {
        try {
          const reply = await session.customRequest('winasm65.symbols', { from: RAM_START, to: RAM_START + RAM_LENGTH - 1 });
          this.symbols = reply.symbols as RamSymbol[];
        } catch {
          this.symbols = [];
        }
      }

      // Changes are measured from one stop to the next; a live refresh while the
      // machine runs compares against the last frame shown.
      const changed: number[] = [];
      if (this.previous) {
        for (let i = 0; i < bytes.length; i++) {
          if (bytes[i] !== this.previous[i]) {
            changed.push(i);
          }
        }
      }
      this.previous = bytes;

      this.post({
        type: 'data',
        start: RAM_START,
        bytes: Array.from(bytes),
        changed,
        sp,
        symbols: this.symbols,
        status: this.running ? (fromLive ? 'running (live)' : 'running') : 'stopped'
      });
    } catch (error) {
      this.post({ type: 'status', text: 'cannot read RAM: ' + (error instanceof Error ? error.message : String(error)) });
    } finally {
      this.refreshing = false;
    }
  }

  private async onMessage(message: { type: string; address?: number; value?: number; on?: boolean }): Promise<void> {
    switch (message.type) {
      case 'refresh':
        await this.refresh();
        break;
      case 'live':
        this.live = !!message.on;
        if (this.live) {
          this.liveTimer = setInterval(() => {
            if (this.running) {
              void this.refresh(true);
            }
          }, LIVE_INTERVAL_MS);
        } else {
          this.stopLive();
        }
        break;
      case 'write':
        if (!this.session || message.address === undefined || message.value === undefined) {
          return;
        }
        try {
          await this.session.customRequest('writeMemory', {
            memoryReference: '0x' + message.address.toString(16).padStart(4, '0'),
            data: Buffer.from([message.value & 0xff]).toString('base64')
          });
          if (this.previous) {
            this.previous[message.address - RAM_START] = message.value & 0xff;
          }
          await this.refresh();
        } catch (error) {
          this.post({ type: 'status', text: 'write refused: ' + (error instanceof Error ? error.message : String(error)) });
        }
        break;
    }
  }

  private stopLive(): void {
    if (this.liveTimer) {
      clearInterval(this.liveTimer);
      this.liveTimer = undefined;
    }
  }

  private post(message: unknown): void {
    void this.panel?.webview.postMessage(message);
  }

  dispose(): void {
    this.stopLive();
    this.panel?.dispose();
    this.subscriptions.forEach(s => s.dispose());
  }
}

function html(webview: vscode.Webview): string {
  const nonce = Array.from({ length: 24 }, () => Math.floor(Math.random() * 36).toString(36)).join('');
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${webview.cspSource} 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>
  body { font-family: var(--vscode-editor-font-family); font-size: var(--vscode-editor-font-size); color: var(--vscode-foreground); padding: 8px 12px; }
  .bar { display: flex; gap: 12px; align-items: center; margin-bottom: 8px; font-family: var(--vscode-font-family); font-size: 12px; }
  .bar button { background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: none; padding: 3px 10px; cursor: pointer; }
  .bar button:hover { background: var(--vscode-button-hoverBackground); }
  #status { color: var(--vscode-descriptionForeground); }
  .legend span { padding: 0 6px; margin-right: 6px; }
  .row { white-space: pre; line-height: 1.45; }
  .addr { color: var(--vscode-editorLineNumber-foreground); }
  .region { color: var(--vscode-descriptionForeground); font-size: 11px; margin: 6px 0 2px; font-family: var(--vscode-font-family); }
  .zp { background: color-mix(in srgb, var(--vscode-charts-blue) 10%, transparent); }
  .stack { background: color-mix(in srgb, var(--vscode-charts-purple) 10%, transparent); }
  .b { cursor: text; padding: 0 1px; }
  .b.named { text-decoration: underline dotted var(--vscode-descriptionForeground); }
  .b.chg { background: var(--vscode-editor-findMatchHighlightBackground); color: var(--vscode-editor-foreground); font-weight: bold; }
  .b.sp { outline: 1px solid var(--vscode-charts-purple); }
  .ascii { color: var(--vscode-descriptionForeground); }
  input.edit { width: 2.2ch; font: inherit; padding: 0; border: 1px solid var(--vscode-focusBorder); background: var(--vscode-input-background); color: var(--vscode-input-foreground); }
</style>
</head>
<body>
<div class="bar">
  <button id="refresh" type="button">Refresh</button>
  <label><input id="live" type="checkbox"> Live</label>
  <span id="status">waiting for a stop…</span>
</div>
<div class="bar legend">
  <span class="zp">zero page</span><span class="stack">stack</span><span class="b chg">changed</span><span class="b sp">SP</span>
  <span>double-click a byte to edit it</span>
</div>
<div id="dump"></div>
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  const dump = document.getElementById('dump');
  const status = document.getElementById('status');
  const cells = [];
  const asciis = [];
  let start = 0;
  let names = new Map();

  function hex(n, w) { return n.toString(16).toUpperCase().padStart(w, '0'); }

  function build(base, length) {
    dump.textContent = '';
    cells.length = 0;
    asciis.length = 0;
    for (let row = 0; row < length; row += 16) {
      const address = base + row;
      if (address === 0x0000 || address === 0x0100 || address === 0x0200) {
        const label = document.createElement('div');
        label.className = 'region';
        label.textContent = address === 0 ? 'zero page' : address === 0x100 ? 'stack' : 'RAM';
        dump.appendChild(label);
      }
      const line = document.createElement('div');
      line.className = 'row' + (address < 0x100 ? ' zp' : address < 0x200 ? ' stack' : '');
      const a = document.createElement('span');
      a.className = 'addr';
      a.textContent = '$' + hex(address, 4) + '  ';
      line.appendChild(a);
      for (let i = 0; i < 16; i++) {
        const b = document.createElement('span');
        b.className = 'b';
        b.dataset.index = String(row + i);
        b.textContent = '..';
        b.addEventListener('dblclick', edit);
        line.appendChild(b);
        line.appendChild(document.createTextNode(i === 7 ? '  ' : ' '));
        cells.push(b);
      }
      const ascii = document.createElement('span');
      ascii.className = 'ascii';
      line.appendChild(document.createTextNode(' |'));
      line.appendChild(ascii);
      line.appendChild(document.createTextNode('|'));
      asciis.push(ascii);
      dump.appendChild(line);
    }
  }

  function edit(event) {
    const cell = event.currentTarget;
    const index = Number(cell.dataset.index);
    const input = document.createElement('input');
    input.className = 'edit';
    input.maxLength = 2;
    input.value = cell.textContent;
    cell.replaceWith(input);
    input.focus();
    input.select();
    let done = false;
    const finish = (commit) => {
      if (done) return;
      done = true;
      input.replaceWith(cell);
      const value = parseInt(input.value, 16);
      if (commit && /^[0-9a-fA-F]{1,2}$/.test(input.value)) {
        vscode.postMessage({ type: 'write', address: start + index, value });
      }
    };
    input.addEventListener('keydown', e => {
      if (e.key === 'Enter') finish(true);
      if (e.key === 'Escape') finish(false);
    });
    input.addEventListener('blur', () => finish(false));
  }

  window.addEventListener('message', event => {
    const m = event.data;
    if (m.type === 'status') {
      status.textContent = m.text;
      return;
    }
    if (m.type !== 'data') return;
    if (cells.length !== m.bytes.length || start !== m.start) {
      start = m.start;
      build(m.start, m.bytes.length);
    }
    // Variables here are often plain "NAME = $0B" constants, so a small constant can
    // share an address with a variable: every name is shown rather than one guessed.
    names = new Map();
    for (const s of (m.symbols || [])) {
      names.set(s.value, names.has(s.value) ? names.get(s.value) + ', ' + s.name : s.name);
    }
    const changed = new Set(m.changed);
    const spIndex = m.sp === undefined || isNaN(m.sp) ? -1 : 0x100 + m.sp - start;
    for (let i = 0; i < m.bytes.length; i++) {
      const cell = cells[i];
      const value = m.bytes[i];
      cell.textContent = hex(value, 2);
      const address = start + i;
      const name = names.get(address);
      cell.title = '$' + hex(address, 4) + (name ? '  ' + name : '') + ' = $' + hex(value, 2) + ' (' + value + ')';
      cell.classList.toggle('chg', changed.has(i));
      cell.classList.toggle('sp', i === spIndex);
      cell.classList.toggle('named', !!name);
    }
    for (let r = 0; r < asciis.length; r++) {
      let text = '';
      for (let i = 0; i < 16; i++) {
        const v = m.bytes[r * 16 + i];
        text += v >= 0x20 && v < 0x7f ? String.fromCharCode(v) : '.';
      }
      asciis[r].textContent = text;
    }
    status.textContent = m.status + (m.sp === undefined || isNaN(m.sp) ? '' : '  ·  SP=$' + hex(m.sp, 2))
      + '  ·  ' + changed.size + ' byte(s) changed';
  });

  document.getElementById('refresh').addEventListener('click', () => vscode.postMessage({ type: 'refresh' }));
  document.getElementById('live').addEventListener('change', e => vscode.postMessage({ type: 'live', on: e.target.checked }));
</script>
</body>
</html>`;
}
