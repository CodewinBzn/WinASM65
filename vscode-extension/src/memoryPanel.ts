import * as vscode from 'vscode';

const DEBUG_TYPE = 'winasm65';
const CPU = 'CPU';
const LIVE_INTERVAL_MS = 500;
const CHUNK = 4096;

interface MemorySymbol {
  name: string;
  value: number;
}

/** A window on one memory, as the system definition of the machine proposes it. */
interface MemoryView {
  name: string;
  space: string;
  start: number;
  length: number;
  writable: boolean;
}

interface MemorySpace {
  name: string;
  length: number;
  writable: boolean;
  description: string;
  available: boolean;
}

/** What the debug adapter's winasm65.system request says about the machine. */
interface SystemDescription {
  id: string;
  name: string;
  cpu: string;
  spaces: MemorySpace[];
  views: MemoryView[];
  emulator?: { name: string; version: string };
}

/** What an adapter that predates winasm65.system is shown: the NES work RAM, as before. */
const FALLBACK: SystemDescription = {
  id: '6502',
  name: '6502',
  cpu: '6502',
  spaces: [{ name: CPU, length: 0x10000, writable: true, description: 'CPU address space', available: true }],
  views: [{ name: 'RAM', space: CPU, start: 0x0000, length: 0x0800, writable: true }]
};

/**
 * The machine's memories in a panel beside the editor: the views its system
 * definition proposes (on the NES: RAM, PRG ROM, nametables, palettes, sprites,
 * tiles), read through the debug adapter. It refreshes on every stop, marks the
 * bytes that changed since the previous stop, shades the zero page and the stack
 * of the CPU's memory (with the byte at $0100+SP outlined), names the bytes the
 * build defined, and writes a byte edited in place.
 */
export class MemoryPanel implements vscode.Disposable {
  private panel: vscode.WebviewPanel | undefined;
  private session: vscode.DebugSession | undefined;
  private system: SystemDescription | undefined;
  private viewIndex = 0;
  private previous = new Map<number, Uint8Array>();
  private symbols: MemorySymbol[] | undefined;
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
          // The machine runs from the moment VS Code asks it to, whatever the
          // adapter then reports: a resumption it asked for has no event.
          onWillReceiveMessage: (message: { type?: string; command?: string }) => {
            if (message.type === 'request' && ['continue', 'next', 'stepIn', 'stepOut'].includes(message.command ?? '')) {
              this.setRunning(session, true);
            }
          },
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
          this.system = undefined;
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
    this.panel = vscode.window.createWebviewPanel('winasm65.ram', 'Memory', {
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
      this.system = undefined;
      this.previous.clear();
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

  /** The machine, asked once per session. */
  private async describe(session: vscode.DebugSession): Promise<SystemDescription> {
    if (!this.system) {
      try {
        this.system = (await session.customRequest('winasm65.system')) as SystemDescription;
        if (!this.system.views || this.system.views.length === 0) {
          this.system = FALLBACK;
        }
      } catch {
        this.system = FALLBACK;
      }
      this.viewIndex = Math.min(this.viewIndex, this.system.views.length - 1);
      if (this.panel) {
        this.panel.title = 'Memory — ' + this.system.name;
      }
      this.post({ type: 'views', views: this.system.views.map(v => this.describeView(v)), selected: this.viewIndex });
    }
    return this.system;
  }

  private describeView(view: MemoryView): { label: string; available: boolean } {
    const space = this.system?.spaces.find(s => s.name.toUpperCase() === view.space.toUpperCase());
    const end = view.start + view.length - 1;
    const range = '$' + view.start.toString(16).toUpperCase().padStart(4, '0') + '-$' + end.toString(16).toUpperCase().padStart(4, '0');
    const available = space ? space.available : view.space === CPU;
    const emulator = this.system?.emulator ? this.system.emulator.name : 'the emulator';
    return {
      label: view.name + (view.space === CPU ? '' : ' (' + view.space + ')') + '  ' + range
        + (available ? '' : '  — not offered by ' + emulator),
      available
    };
  }

  private async read(session: vscode.DebugSession, view: MemoryView): Promise<Uint8Array> {
    const bytes = new Uint8Array(view.length);
    for (let done = 0; done < view.length; done += CHUNK) {
      const count = Math.min(CHUNK, view.length - done);
      const reply = view.space === CPU
        ? await session.customRequest('readMemory', { memoryReference: reference(view.start + done), count })
        : await session.customRequest('winasm65.memory.read', { space: view.space, address: view.start + done, count });
      bytes.set(Buffer.from(reply.data as string, 'base64'), done);
    }
    return bytes;
  }

  private async refresh(fromLive = false): Promise<void> {
    const session = this.session;
    if (!this.panel || !session || this.refreshing) {
      return;
    }
    this.refreshing = true;
    try {
      const system = await this.describe(session);
      const view = system.views[this.viewIndex];
      const bytes = await this.read(session, view);

      let sp: number | undefined;
      if (view.space === CPU) {
        try {
          const evaluated = await session.customRequest('evaluate', { expression: 'SP', context: 'watch' });
          sp = parseInt(String(evaluated.result).replace('$', ''), 16);
        } catch {
          sp = undefined;
        }
        if (!this.symbols) {
          try {
            const reply = await session.customRequest('winasm65.symbols', { from: 0, to: 0xffff });
            this.symbols = reply.symbols as MemorySymbol[];
          } catch {
            this.symbols = [];
          }
        }
      }

      // Changes are measured from one stop to the next, per view; a live refresh
      // while the machine runs compares against the last frame shown.
      const changed: number[] = [];
      const previous = this.previous.get(this.viewIndex);
      if (previous && previous.length === bytes.length) {
        for (let i = 0; i < bytes.length; i++) {
          if (bytes[i] !== previous[i]) {
            changed.push(i);
          }
        }
      }
      this.previous.set(this.viewIndex, bytes);

      this.post({
        type: 'data',
        cpu: view.space === CPU,
        writable: view.writable,
        start: view.start,
        bytes: Array.from(bytes),
        changed,
        sp,
        symbols: view.space === CPU ? this.symbols : [],
        status: this.running ? (fromLive ? 'running (live)' : 'running') : 'stopped'
      });
    } catch (error) {
      this.post({ type: 'status', text: 'cannot read: ' + (error instanceof Error ? error.message : String(error)) });
    } finally {
      this.refreshing = false;
    }
  }

  private async onMessage(message: { type: string; address?: number; value?: number; on?: boolean; index?: number }): Promise<void> {
    switch (message.type) {
      case 'refresh':
        await this.refresh();
        break;
      case 'view':
        this.viewIndex = message.index ?? 0;
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
      case 'write': {
        const view = this.system?.views[this.viewIndex];
        if (!this.session || !view || message.address === undefined || message.value === undefined) {
          return;
        }
        const data = Buffer.from([message.value & 0xff]).toString('base64');
        try {
          if (view.space === CPU) {
            await this.session.customRequest('writeMemory', { memoryReference: reference(message.address), data });
          } else {
            await this.session.customRequest('winasm65.memory.write', { space: view.space, address: message.address, data });
          }
          const previous = this.previous.get(this.viewIndex);
          if (previous) {
            previous[message.address - view.start] = message.value & 0xff;
          }
          await this.refresh();
        } catch (error) {
          this.post({ type: 'status', text: 'write refused: ' + (error instanceof Error ? error.message : String(error)) });
        }
        break;
      }
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

function reference(address: number): string {
  return '0x' + address.toString(16).padStart(4, '0');
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
  .bar { display: flex; gap: 12px; align-items: center; margin-bottom: 8px; font-family: var(--vscode-font-family); font-size: 12px; flex-wrap: wrap; }
  .bar button { background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: none; padding: 3px 10px; cursor: pointer; }
  .bar button:hover { background: var(--vscode-button-hoverBackground); }
  .bar select { background: var(--vscode-dropdown-background); color: var(--vscode-dropdown-foreground); border: 1px solid var(--vscode-dropdown-border); padding: 2px 4px; font: inherit; }
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
  <select id="view" title="Memory"></select>
  <button id="refresh" type="button">Refresh</button>
  <label><input id="live" type="checkbox"> Live</label>
  <span id="status">waiting for a stop…</span>
</div>
<div class="bar legend" id="legend">
  <span class="zp">zero page</span><span class="stack">stack</span><span class="b chg">changed</span><span class="b sp">SP</span>
  <span>double-click a byte to edit it</span>
</div>
<div id="dump"></div>
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  const dump = document.getElementById('dump');
  const status = document.getElementById('status');
  const select = document.getElementById('view');
  const cells = [];
  const asciis = [];
  let start = -1;
  let cpu = true;
  let writable = true;
  let names = new Map();

  function hex(n, w) { return n.toString(16).toUpperCase().padStart(w, '0'); }

  function build(base, length) {
    dump.textContent = '';
    cells.length = 0;
    asciis.length = 0;
    const width = base + length > 0x10000 ? 5 : 4;
    for (let row = 0; row < length; row += 16) {
      const address = base + row;
      // The zero page and the stack are the 6502's, so only the CPU's memory has them.
      if (cpu && (address === 0x0000 || address === 0x0100 || address === 0x0200)) {
        const label = document.createElement('div');
        label.className = 'region';
        label.textContent = address === 0 ? 'zero page' : address === 0x100 ? 'stack' : 'RAM';
        dump.appendChild(label);
      }
      const line = document.createElement('div');
      line.className = 'row' + (cpu && address < 0x100 ? ' zp' : cpu && address < 0x200 ? ' stack' : '');
      const a = document.createElement('span');
      a.className = 'addr';
      a.textContent = '$' + hex(address, width) + '  ';
      line.appendChild(a);
      for (let i = 0; i < 16 && row + i < length; i++) {
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
    if (!writable) return;
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
    if (m.type === 'views') {
      select.textContent = '';
      m.views.forEach((v, i) => {
        const option = document.createElement('option');
        option.value = String(i);
        option.textContent = v.label;
        option.disabled = !v.available;
        select.appendChild(option);
      });
      select.value = String(m.selected);
      return;
    }
    if (m.type !== 'data') return;
    if (cells.length !== m.bytes.length || start !== m.start || cpu !== m.cpu) {
      start = m.start;
      cpu = m.cpu;
      build(m.start, m.bytes.length);
    }
    writable = m.writable;
    document.getElementById('legend').style.display = cpu ? '' : 'none';
    // Variables here are often plain "NAME = $0B" constants, so a small constant can
    // share an address with a variable: every name is shown rather than one guessed.
    names = new Map();
    for (const s of (m.symbols || [])) {
      names.set(s.value, names.has(s.value) ? names.get(s.value) + ', ' + s.name : s.name);
    }
    const changed = new Set(m.changed);
    const spIndex = !cpu || m.sp === undefined || isNaN(m.sp) ? -1 : 0x100 + m.sp - start;
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
      for (let i = 0; i < 16 && r * 16 + i < m.bytes.length; i++) {
        const v = m.bytes[r * 16 + i];
        text += v >= 0x20 && v < 0x7f ? String.fromCharCode(v) : '.';
      }
      asciis[r].textContent = text;
    }
    status.textContent = m.status + (!cpu || m.sp === undefined || isNaN(m.sp) ? '' : '  ·  SP=$' + hex(m.sp, 2))
      + '  ·  ' + changed.size + ' byte(s) changed';
  });

  select.addEventListener('change', () => vscode.postMessage({ type: 'view', index: Number(select.value) }));
  document.getElementById('refresh').addEventListener('click', () => vscode.postMessage({ type: 'refresh' }));
  document.getElementById('live').addEventListener('change', e => vscode.postMessage({ type: 'live', on: e.target.checked }));
</script>
</body>
</html>`;
}
