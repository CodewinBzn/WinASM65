import { ChildProcessWithoutNullStreams, spawn } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { AssetDiagnostic } from './shared/assets';

/** A request the designer answered with a JSON-RPC error. */
export class DesignerError extends Error {
  constructor(message: string, readonly code: number, readonly diagnostics: AssetDiagnostic[] = []) {
    super(message);
  }
}

interface Pending {
  method: string;
  resolve: (value: unknown) => void;
  reject: (error: Error) => void;
}

/**
 * Where WinASM65.Designer is: the `winasm65Graphics.designerPath` setting, the
 * single-file executable shipped in bin/, else the Debug build of the
 * repository (the extension's development layout), run with dotnet.
 */
export function resolveDesigner(extensionPath: string): { command: string; args: string[] } | undefined {
  const configured = vscode.workspace.getConfiguration('winasm65Graphics').get<string>('designerPath')?.trim();
  const candidates: string[] = [];
  if (configured) {
    candidates.push(configured);
  }
  candidates.push(path.join(extensionPath, 'bin', 'WinASM65.Designer.exe'));
  candidates.push(path.join(extensionPath, 'bin', 'WinASM65.Designer'));
  candidates.push(path.join(extensionPath, '..', 'WinASM65.Designer', 'bin', 'Debug', 'net8.0', 'WinASM65.Designer.dll'));
  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) {
      return candidate.toLowerCase().endsWith('.dll')
        ? { command: 'dotnet', args: [candidate] }
        : { command: candidate, args: [] };
    }
  }
  return undefined;
}

/**
 * The designer server (`WinASM65.Designer --server`): JSON-RPC 2.0 over its
 * stdin/stdout with Content-Length framing. It is started on the first request
 * and started again on the next request after it dies; a request in flight when
 * it dies is rejected.
 */
export class DesignerClient implements vscode.Disposable {
  private process: ChildProcessWithoutNullStreams | undefined;
  private buffer = Buffer.alloc(0);
  private nextId = 1;
  private readonly pending = new Map<number, Pending>();
  private readonly crashes: number[] = [];
  private disposed = false;
  private stopping = false;

  constructor(private readonly extensionPath: string, private readonly log: vscode.OutputChannel) {}

  /** Sends a request; the result, or a {@link DesignerError}. */
  request<T>(method: string, params: object = {}): Promise<T> {
    if (this.disposed) {
      return Promise.reject(new Error('the designer client is closed'));
    }
    let child: ChildProcessWithoutNullStreams;
    try {
      child = this.ensureStarted();
    } catch (error) {
      return Promise.reject(error);
    }
    const id = this.nextId++;
    const body = Buffer.from(JSON.stringify({ jsonrpc: '2.0', id, method, params }), 'utf8');
    return new Promise<T>((resolve, reject) => {
      this.pending.set(id, { method, resolve: value => resolve(value as T), reject });
      child.stdin.write(Buffer.concat([Buffer.from('Content-Length: ' + body.length + '\r\n\r\n', 'ascii'), body]));
    });
  }

  /** Whether the designer can be found at all. */
  available(): boolean {
    return resolveDesigner(this.extensionPath) !== undefined;
  }

  private ensureStarted(): ChildProcessWithoutNullStreams {
    if (this.process) {
      return this.process;
    }
    this.stopping = false;
    const now = Date.now();
    while (this.crashes.length > 0 && now - this.crashes[0] > 60000) {
      this.crashes.shift();
    }
    if (this.crashes.length >= 5) {
      throw new Error('the designer stopped 5 times in a minute; see the "WinASM65 Graphics" output');
    }
    const designer = resolveDesigner(this.extensionPath);
    if (!designer) {
      throw new Error('WinASM65.Designer was not found. Set "winasm65Graphics.designerPath".');
    }
    this.log.appendLine('starting ' + [designer.command, ...designer.args, '--server'].join(' '));
    const child = spawn(designer.command, [...designer.args, '--server'], { stdio: 'pipe', windowsHide: true });
    this.process = child;
    this.buffer = Buffer.alloc(0);
    child.stdout.on('data', (chunk: Buffer) => this.onData(chunk));
    child.stderr.on('data', (chunk: Buffer) => this.log.append(chunk.toString('utf8')));
    child.on('error', error => this.onExit(child, 'cannot start the designer: ' + error.message));
    child.on('exit', (code, signal) => this.onExit(child, 'the designer exited (' + (signal ?? code) + ')'));
    child.stdin.on('error', () => { /* reported by 'exit' */ });
    return child;
  }

  private onExit(child: ChildProcessWithoutNullStreams, reason: string): void {
    if (this.process !== child) {
      return;
    }
    this.process = undefined;
    if (this.stopping) {
      this.log.appendLine('the designer stopped');
    } else {
      this.crashes.push(Date.now());
      this.log.appendLine(reason);
    }
    const error = new Error(reason);
    for (const pending of this.pending.values()) {
      pending.reject(error);
    }
    this.pending.clear();
  }

  private onData(chunk: Buffer): void {
    this.buffer = Buffer.concat([this.buffer, chunk]);
    for (;;) {
      const headerEnd = this.buffer.indexOf('\r\n\r\n');
      if (headerEnd < 0) {
        return;
      }
      const header = this.buffer.subarray(0, headerEnd).toString('ascii');
      const match = /Content-Length:\s*(\d+)/i.exec(header);
      if (!match) {
        this.log.appendLine('designer: a message without Content-Length: ' + header);
        this.buffer = this.buffer.subarray(headerEnd + 4);
        continue;
      }
      const length = Number(match[1]);
      if (this.buffer.length < headerEnd + 4 + length) {
        return;
      }
      const body = this.buffer.subarray(headerEnd + 4, headerEnd + 4 + length).toString('utf8');
      this.buffer = this.buffer.subarray(headerEnd + 4 + length);
      this.onMessage(body);
    }
  }

  private onMessage(body: string): void {
    let message: { id?: number; result?: unknown; error?: { code: number; message: string; data?: { diagnostics?: AssetDiagnostic[] } } };
    try {
      message = JSON.parse(body);
    } catch {
      this.log.appendLine('designer: unreadable message: ' + body.slice(0, 200));
      return;
    }
    if (typeof message.id !== 'number') {
      if (message.error) {
        this.log.appendLine('designer: ' + message.error.message);
      }
      return;
    }
    const pending = this.pending.get(message.id);
    if (!pending) {
      return;
    }
    this.pending.delete(message.id);
    if (message.error) {
      const diagnostics = message.error.data?.diagnostics ?? [];
      const detail = diagnostics.length > 0 ? ': ' + diagnostics.map(d => d.path + ' ' + d.message).join('; ') : '';
      pending.reject(new DesignerError(message.error.message + detail, message.error.code, diagnostics));
    } else {
      pending.resolve(message.result ?? null);
    }
  }

  /** Asks the designer to stop, and stops it if it does not within a second. */
  async shutdown(): Promise<void> {
    const child = this.process;
    if (!child) {
      return;
    }
    this.stopping = true;
    try {
      await Promise.race([this.request('shutdown'), new Promise(resolve => setTimeout(resolve, 1000))]);
    } catch {
      // It is going anyway.
    }
    child.stdin.end();
    setTimeout(() => {
      if (child.exitCode === null) {
        child.kill();
      }
    }, 1000).unref();
  }

  dispose(): void {
    void this.shutdown().finally(() => {
      this.disposed = true;
    });
  }
}
