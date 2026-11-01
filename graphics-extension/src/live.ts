import * as vscode from 'vscode';
import { ByteRange, changedRanges } from './shared/binary';
import { LiveState } from './shared/protocol';

/** The debug type of the WinASM65 extension's adapter. */
export const DEBUG_TYPE = 'winasm65';

/** A memory space of the running machine, as `winasm65.system` describes it. */
export interface MemorySpaceInfo {
  name: string;
  length: number;
  writable?: boolean;
  available?: boolean;
  description?: string;
}

interface SystemInfo {
  spaces?: MemorySpaceInfo[];
  emulator?: { name: string; version: string };
}

/** Bytes an editor wants in the machine: a space, where, and what. */
export interface LiveBlock {
  space: string;
  address: number;
  data: Uint8Array;
  /** Changed bytes are widened to this many (16: whole tiles). */
  unit?: number;
}

/**
 * The WinASM65 debug session edits are written into: the active session of
 * type `winasm65`, and what its machine offers (`winasm65.system`, asked once
 * per session). Writes go through `winasm65.memory.write { space, address,
 * data (base64) }`.
 */
export class LivePreview implements vscode.Disposable {
  private readonly emitter = new vscode.EventEmitter<void>();
  private readonly systems = new Map<string, Promise<SystemInfo | undefined>>();
  private readonly subscriptions: vscode.Disposable[] = [this.emitter];
  /** Writes made, for the tests: space, address, length. */
  readonly writes: Array<{ space: string; address: number; length: number }> = [];

  /** The session came or went. */
  readonly onDidChangeSession = this.emitter.event;

  constructor(private readonly log: vscode.OutputChannel) {
    this.subscriptions.push(
      vscode.debug.onDidStartDebugSession(() => this.emitter.fire()),
      vscode.debug.onDidChangeActiveDebugSession(() => this.emitter.fire()),
      vscode.debug.onDidTerminateDebugSession(session => {
        this.systems.delete(session.id);
        this.emitter.fire();
      })
    );
  }

  /** The WinASM65 session edits would go to, if one runs. */
  get session(): vscode.DebugSession | undefined {
    const active = vscode.debug.activeDebugSession;
    return active && active.type === DEBUG_TYPE ? active : undefined;
  }

  private system(session: vscode.DebugSession): Promise<SystemInfo | undefined> {
    let found = this.systems.get(session.id);
    if (!found) {
      found = Promise.resolve(session.customRequest('winasm65.system')).then(
        reply => reply as SystemInfo,
        error => {
          this.log.appendLine('live: winasm65.system: ' + (error instanceof Error ? error.message : String(error)));
          return undefined;
        });
      this.systems.set(session.id, found);
    }
    return found;
  }

  /** A space of the session's machine; undefined when the adapter does not describe it. */
  async space(session: vscode.DebugSession, name: string): Promise<{ space?: MemorySpaceInfo; emulator?: string }> {
    const system = await this.system(session);
    const space = system?.spaces?.find(s => s.name.toUpperCase() === name.toUpperCase());
    return { space, emulator: system?.emulator ? system.emulator.name : undefined };
  }

  async write(session: vscode.DebugSession, space: string, address: number, data: Uint8Array): Promise<void> {
    await session.customRequest('winasm65.memory.write', { space, address, data: Buffer.from(data).toString('base64') });
    this.writes.push({ space, address, length: data.length });
  }

  dispose(): void {
    this.subscriptions.forEach(s => s.dispose());
  }
}

/**
 * The Live preview of one editor: off until the user turns it on; then the
 * whole of the editor's data is written once, and after every change only
 * the bytes that changed since the last write.
 */
export class LiveChannel implements vscode.Disposable {
  private on = false;
  private readonly last = new Map<string, Uint8Array>();
  private timer: NodeJS.Timeout | undefined;
  private running: Promise<void> = Promise.resolve();
  private readonly subscription: vscode.Disposable;

  /**
   * @param what What Live writes, for the tooltip (`the palettes into PAL`); undefined when the editor has nothing to write.
   * @param unsupported Why Live does nothing for this editor (shown as the tooltip), when `what` is undefined.
   * @param compute The blocks as the editor's data now is; undefined when the data cannot be read.
   */
  constructor(
    private readonly live: LivePreview,
    private readonly what: string | undefined,
    private readonly unsupported: string | undefined,
    private readonly compute: () => LiveBlock[] | undefined,
    private readonly post: (state: LiveState) => void,
    private readonly notify: (text: string, level: 'info' | 'error') => void
  ) {
    this.subscription = live.onDidChangeSession(() => {
      if (!live.session) {
        this.on = false;
        this.last.clear();
      }
      this.send();
    });
  }

  /** Tells the webview what the toggle shows. */
  send(): void {
    const session = this.live.session;
    if (!this.what) {
      this.post({ available: false, on: false, title: this.unsupported ?? 'No live preview for this editor' });
      return;
    }
    if (!session) {
      this.post({ available: false, on: false, title: 'Live preview: start a WinASM65 debug session to write ' + this.what + ' as you edit' });
      return;
    }
    this.post({
      available: true,
      on: this.on,
      title: this.on ? 'Live: edits go into ' + session.name + ' (click to stop)' : 'Live: write ' + this.what + ' of the running ' + session.name + ' as you edit'
    });
  }

  /** Turns Live on (writing everything once) or off. */
  async set(on: boolean): Promise<void> {
    const session = this.live.session;
    if (!on || !session || !this.what) {
      this.on = false;
      this.last.clear();
      this.send();
      return;
    }
    const blocks = this.compute() ?? [];
    for (const block of blocks) {
      const { space, emulator } = await this.live.space(session, block.space);
      if (!space) {
        this.notify('Live: the debug session has no "' + block.space + '" memory (an older WinASM65 adapter?)', 'error');
        this.send();
        return;
      }
      if (space.available === false) {
        this.notify('Live: ' + (emulator ?? 'the emulator') + ' does not let the debugger write ' + block.space + ' memory, so edits cannot be shown live', 'error');
        this.send();
        return;
      }
    }
    this.on = true;
    this.last.clear();
    this.send();
    await this.push(true);
  }

  /** The editor's data changed. */
  changed(): void {
    if (!this.on) {
      return;
    }
    if (this.timer) {
      clearTimeout(this.timer);
    }
    this.timer = setTimeout(() => void this.push(false), 40);
  }

  /** Writes what changed (everything when `full`), one write at a time. */
  private push(full: boolean): Promise<void> {
    this.running = this.running.then(async () => {
      const session = this.live.session;
      if (!this.on || !session) {
        return;
      }
      const blocks = this.compute();
      if (!blocks) {
        return;
      }
      let bytes = 0;
      try {
        for (const block of blocks) {
          const { space } = await this.live.space(session, block.space);
          const data = space && space.length > 0 && block.address + block.data.length > space.length
            ? block.data.subarray(0, Math.max(0, space.length - block.address))
            : block.data;
          const key = block.space + ':' + block.address;
          const ranges: ByteRange[] = changedRanges(full ? undefined : this.last.get(key), data, block.unit ?? 1);
          for (const range of ranges) {
            await this.live.write(session, block.space, block.address + range.offset, range.data);
            bytes += range.data.length;
          }
          this.last.set(key, data.slice());
        }
      } catch (error) {
        this.on = false;
        this.last.clear();
        this.send();
        this.notify('Live stopped: ' + (error instanceof Error ? error.message : String(error)), 'error');
        return;
      }
      if (full && bytes > 0) {
        this.notify('Live: ' + bytes + ' bytes written to the running machine', 'info');
      }
    });
    return this.running;
  }

  dispose(): void {
    if (this.timer) {
      clearTimeout(this.timer);
    }
    this.subscription.dispose();
  }
}
