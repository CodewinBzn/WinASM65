import * as path from 'path';
import * as vscode from 'vscode';
import { HostMessage, WebviewMessage } from '../shared/protocol';
import { EditorContext } from './context';
import { editorHtml, webviewOptions } from './html';
import { errorText } from './jsonEditor';

export const TILES_VIEW_TYPE = 'winasm65Graphics.tiles';
const PALETTE_STATE_KEY = 'winasm65Graphics.tilePalette';

/**
 * A .chr file: its bytes, and the edits made to them since it was opened. Each
 * edit holds the bytes before and after, so undo and redo swap whole arrays (a
 * tile set is a few KiB).
 */
export class ChrDocument implements vscode.CustomDocument {
  private readonly changed = new vscode.EventEmitter<void>();
  private readonly disposed = new vscode.EventEmitter<void>();

  /** The bytes changed by an edit, an undo, a redo or a revert. */
  readonly onDidChangeBytes = this.changed.event;
  readonly onDidDispose = this.disposed.event;

  constructor(readonly uri: vscode.Uri, public bytes: Uint8Array) {}

  setBytes(bytes: Uint8Array): void {
    this.bytes = bytes;
    this.changed.fire();
  }

  dispose(): void {
    this.disposed.fire();
    this.changed.dispose();
    this.disposed.dispose();
  }
}

/**
 * The tile set editor: a binary custom editor whose document is the file's
 * bytes. Edits from the webview become CustomDocumentEditEvents, so VS Code
 * keeps the undo stack and the dirty state; save, save as, revert and hot exit
 * backups write or read the bytes.
 */
export class TilesEditorProvider implements vscode.CustomEditorProvider<ChrDocument> {
  private readonly changeEmitter = new vscode.EventEmitter<vscode.CustomDocumentEditEvent<ChrDocument>>();
  readonly onDidChangeCustomDocument = this.changeEmitter.event;
  private readonly documents = new Set<ChrDocument>();

  constructor(private readonly context: EditorContext, private readonly memento: vscode.Memento) {}

  static register(context: EditorContext, memento: vscode.Memento): { provider: TilesEditorProvider; registration: vscode.Disposable } {
    const provider = new TilesEditorProvider(context, memento);
    const registration = vscode.window.registerCustomEditorProvider(TILES_VIEW_TYPE, provider, {
      webviewOptions: { retainContextWhenHidden: true },
      supportsMultipleEditorsPerDocument: true
    });
    return { provider, registration };
  }

  /** The open document of a file, if a tile editor has it. */
  documentFor(uri: vscode.Uri): ChrDocument | undefined {
    for (const document of this.documents) {
      if (document.uri.toString() === uri.toString()) {
        return document;
      }
    }
    return undefined;
  }

  async openCustomDocument(uri: vscode.Uri, openContext: vscode.CustomDocumentOpenContext): Promise<ChrDocument> {
    const source = openContext.backupId ? vscode.Uri.parse(openContext.backupId) : uri;
    let bytes: Uint8Array;
    try {
      bytes = new Uint8Array(await vscode.workspace.fs.readFile(source));
    } catch (error) {
      if (openContext.untitledDocumentData) {
        bytes = new Uint8Array(openContext.untitledDocumentData);
      } else {
        throw error;
      }
    }
    const document = new ChrDocument(uri, bytes);
    this.documents.add(document);
    const live = this.context.resources.registerChr(uri, () => document.bytes);
    const changes = document.onDidChangeBytes(() => this.context.resources.changed(uri));
    document.onDidDispose(() => {
      this.documents.delete(document);
      live.dispose();
      changes.dispose();
    });
    return document;
  }

  /** Records an edit (the tests use it as the webview does). */
  edit(document: ChrDocument, after: Uint8Array, label: string): void {
    const before = document.bytes;
    document.setBytes(after);
    this.changeEmitter.fire({
      document,
      label,
      undo: () => document.setBytes(before),
      redo: () => document.setBytes(after)
    });
  }

  async resolveCustomEditor(document: ChrDocument, panel: vscode.WebviewPanel): Promise<void> {
    const { context } = this;
    const webview = panel.webview;
    webview.options = webviewOptions(context.extensionUri);
    webview.html = editorHtml(webview, context.extensionUri, 'tiles', path.basename(document.uri.fsPath));
    const post = (message: HostMessage) => void webview.postMessage(message);
    const subscriptions: vscode.Disposable[] = [context.registry.track(panel, document.uri, 'tiles')];

    // The bytes this webview sent last: not sent back to it.
    let own: Uint8Array | undefined;
    subscriptions.push(document.onDidChangeBytes(() => {
      if (document.bytes !== own) {
        post({ type: 'chr', data: Array.from(document.bytes) });
      }
      own = undefined;
    }));

    // The display palette: a palette file, remembered per tile set.
    const stateKey = PALETTE_STATE_KEY + ':' + document.uri.toString();
    let palettePath = this.memento.get<string>(stateKey);
    const paletteUri = () => palettePath ? vscode.Uri.joinPath(document.uri, '..', ...palettePath.split('/')) : undefined;
    const sendPalette = async () => {
      const target = paletteUri();
      if (!target || !palettePath) {
        post({ type: 'displayPalette' });
        return;
      }
      try {
        post({ type: 'displayPalette', palettes: { path: palettePath, asset: await context.resources.readPalette(target) } });
      } catch (error) {
        post({ type: 'displayPalette', palettes: { path: palettePath, error: errorText(error) } });
      }
    };
    subscriptions.push(context.resources.onDidChange(uri => {
      if (uri.toString() === paletteUri()?.toString()) {
        void sendPalette();
      }
    }));
    const paletteWatcher = vscode.workspace.createFileSystemWatcher('**/*.pal.json');
    const sendFiles = async () => {
      const files = await vscode.workspace.findFiles('**/*.pal.json', '**/node_modules/**', 200);
      post({
        type: 'paletteFiles',
        files: files
          .map(file => ({ path: path.relative(path.dirname(document.uri.fsPath), file.fsPath).replace(/\\/g, '/'), label: vscode.workspace.asRelativePath(file) }))
          .sort((a, b) => a.label.localeCompare(b.label))
      });
    };
    subscriptions.push(
      paletteWatcher,
      paletteWatcher.onDidCreate(() => void sendFiles()),
      paletteWatcher.onDidDelete(() => void sendFiles()),
      paletteWatcher.onDidChange(uri => context.resources.changed(uri))
    );

    subscriptions.push(webview.onDidReceiveMessage(async (message: WebviewMessage) => {
      switch (message.type) {
        case 'ready': {
          context.registry.markReady(document.uri, 'tiles');
          const { system, note } = await context.designer.system('nes');
          post({ type: 'init', kind: 'tiles', system, fileName: path.basename(document.uri.fsPath), systemNote: note });
          post({ type: 'chr', data: Array.from(document.bytes) });
          await sendFiles();
          await sendPalette();
          break;
        }
        case 'chrEdit': {
          const after = Uint8Array.from(message.data);
          own = after;
          this.edit(document, after, message.label);
          break;
        }
        case 'choosePalette':
          palettePath = message.path || undefined;
          await this.memento.update(stateKey, palettePath);
          await sendPalette();
          break;
      }
    }));

    panel.onDidDispose(() => subscriptions.forEach(s => s.dispose()));
  }

  async saveCustomDocument(document: ChrDocument): Promise<void> {
    await vscode.workspace.fs.writeFile(document.uri, document.bytes);
  }

  async saveCustomDocumentAs(document: ChrDocument, destination: vscode.Uri): Promise<void> {
    await vscode.workspace.fs.writeFile(destination, document.bytes);
  }

  async revertCustomDocument(document: ChrDocument): Promise<void> {
    document.setBytes(new Uint8Array(await vscode.workspace.fs.readFile(document.uri)));
  }

  async backupCustomDocument(document: ChrDocument, backup: vscode.CustomDocumentBackupContext): Promise<vscode.CustomDocumentBackup> {
    await vscode.workspace.fs.writeFile(backup.destination, document.bytes);
    return {
      id: backup.destination.toString(),
      delete: async () => {
        try {
          await vscode.workspace.fs.delete(backup.destination);
        } catch {
          // already gone
        }
      }
    };
  }
}
