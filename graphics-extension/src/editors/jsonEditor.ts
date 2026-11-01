import * as path from 'path';
import * as vscode from 'vscode';
import { AssetKind } from '../shared/assets';
import { parseJson } from '../shared/json';
import { EditorKind, HostMessage, WebviewMessage } from '../shared/protocol';
import { GraphicsSystem } from '../shared/system';
import { LiveBlock, LiveChannel } from '../live';
import { ResourceLink, relativePath, resolveRelative } from '../resources';
import { EditorContext } from './context';
import { editorHtml, webviewOptions } from './html';

/** What a JSON asset editor is: its view type, the kind of file, its script, and whether it draws tiles. */
export interface JsonEditorSpec {
  viewType: string;
  kind: AssetKind & EditorKind;
  /** Module in out/webview/webview, without .js. */
  script: string;
  /** Whether the asset names a tile set and a palette file the editor needs. */
  usesResources: boolean;
  /** Live preview: what is written (for the tooltip) and the bytes of an asset; or why there is none. */
  live?: { what: string; blocks: (asset: unknown, system: GraphicsSystem) => LiveBlock[] } | { unsupported: string };
  /** Mode the "Import PNG…" button of this editor preselects. */
  importMode?: 'background' | 'sprites';
}

/**
 * A JSON asset in a webview, over the file's TextDocument.
 *
 * The text is the source of truth. An edit from the webview arrives as the
 * whole new asset; the designer formats it the way the library saves files, and
 * one WorkspaceEdit replaces the text, so VS Code's undo, redo, dirty state,
 * save and diff all work as for text. Any other change of the text (undo, an
 * edit in a text editor, git) is sent to the webview again.
 */
export class JsonAssetEditorProvider implements vscode.CustomTextEditorProvider {
  /** Edits per document run one after the other: each formats the asset after the previous one is applied. */
  private readonly queues = new Map<string, Promise<unknown>>();
  /** The text each document was last given by this provider, so its own change is not sent back. */
  private readonly applied = new Map<string, string>();

  constructor(private readonly context: EditorContext, readonly spec: JsonEditorSpec) {}

  static register(context: EditorContext, spec: JsonEditorSpec): { provider: JsonAssetEditorProvider; registration: vscode.Disposable } {
    const provider = new JsonAssetEditorProvider(context, spec);
    const registration = vscode.window.registerCustomEditorProvider(spec.viewType, provider, {
      webviewOptions: { retainContextWhenHidden: true }
    });
    return { provider, registration };
  }

  /**
   * Replaces the document's text with the formatted asset (one undoable edit).
   * Whether the text changed. Rejects when the designer refuses the asset.
   */
  applyAsset(document: vscode.TextDocument, asset: unknown, echo = false): Promise<boolean> {
    const key = document.uri.toString();
    const run = async (): Promise<boolean> => {
      const formatted = await this.context.designer.format(this.spec.kind, asset, document.uri.scheme === 'file' ? document.uri.fsPath : undefined);
      if (formatted.text === document.getText()) {
        return false;
      }
      if (!echo) {
        this.applied.set(key, formatted.text);
      }
      const edit = new vscode.WorkspaceEdit();
      edit.replace(document.uri, new vscode.Range(0, 0, document.lineCount, 0), formatted.text);
      const done = await vscode.workspace.applyEdit(edit);
      if (!done) {
        this.applied.delete(key);
      }
      return done;
    };
    const previous = this.queues.get(key) ?? Promise.resolve();
    const next = previous.then(run, run);
    this.queues.set(key, next.catch(() => undefined));
    return next;
  }

  async resolveCustomTextEditor(document: vscode.TextDocument, panel: vscode.WebviewPanel): Promise<void> {
    const { context, spec } = this;
    const uri = document.uri;
    const key = uri.toString();
    const webview = panel.webview;
    webview.options = webviewOptions(context.extensionUri);
    webview.html = editorHtml(webview, context.extensionUri, spec.script, path.basename(uri.fsPath));

    const post = (message: HostMessage) => void webview.postMessage(message);
    const subscriptions: vscode.Disposable[] = [context.registry.track(panel, uri, spec.kind)];
    let ready = false;
    let system: GraphicsSystem | undefined;

    // Live preview: the asset's bytes, written into the running machine as it changes.
    const liveSpec = spec.live;
    const live = new LiveChannel(
      context.live,
      liveSpec && 'what' in liveSpec ? liveSpec.what : undefined,
      liveSpec && 'unsupported' in liveSpec ? liveSpec.unsupported : undefined,
      () => {
        if (!liveSpec || !('blocks' in liveSpec) || !system) return undefined;
        try {
          return liveSpec.blocks(parseJson(document.getText()), system);
        } catch {
          return undefined; // not JSON just now
        }
      },
      state => post({ type: 'live', state }),
      (text, level) => post({ type: 'notice', text, level }));
    subscriptions.push(live);
    subscriptions.push(vscode.workspace.onDidChangeTextDocument(event => {
      if (event.document.uri.toString() === key && event.contentChanges.length > 0) {
        live.changed();
      }
    }));

    const link = spec.usesResources
      ? new ResourceLink(context.resources, uri, (chr, palettes) => post({ type: 'resources', chr, palettes }))
      : undefined;
    if (link) {
      subscriptions.push(link);
    }

    // The asset as the text holds it, sent to the webview; references followed.
    const sendAsset = (rereadResources = false) => {
      let asset: unknown;
      try {
        asset = parseJson(document.getText());
      } catch (error) {
        post({ type: 'asset', error: 'The file is not valid JSON: ' + (error instanceof Error ? error.message : String(error)) });
        return;
      }
      if (asset === null || typeof asset !== 'object' || Array.isArray(asset)) {
        post({ type: 'asset', error: 'The file does not hold a JSON object.' });
        return;
      }
      post({ type: 'asset', asset });
      const refs = asset as { chr?: unknown; palettes?: unknown };
      link?.update(typeof refs.chr === 'string' ? refs.chr : undefined, typeof refs.palettes === 'string' ? refs.palettes : undefined, rereadResources);
    };

    let pendingSend: NodeJS.Timeout | undefined;
    subscriptions.push(vscode.workspace.onDidChangeTextDocument(event => {
      if (event.document.uri.toString() !== key || event.contentChanges.length === 0 || !ready) {
        return;
      }
      const text = event.document.getText();
      if (this.applied.get(key) === text) {
        return; // the webview's own edit, already shown there
      }
      this.applied.delete(key);
      if (pendingSend) {
        clearTimeout(pendingSend);
      }
      pendingSend = setTimeout(() => sendAsset(), 30);
    }));

    subscriptions.push(webview.onDidReceiveMessage(async (message: WebviewMessage) => {
      switch (message.type) {
        case 'ready': {
          ready = true;
          context.registry.markReady(uri, spec.kind);
          let systemId = 'nes';
          try {
            const parsed = parseJson(document.getText()) as { system?: unknown };
            if (parsed && typeof parsed.system === 'string') {
              systemId = parsed.system;
            }
          } catch {
            // reported with the asset
          }
          try {
            const described = await context.designer.system(systemId);
            system = described.system;
            post({ type: 'init', kind: spec.kind, system: described.system, fileName: path.basename(uri.fsPath), systemNote: described.note });
          } catch (error) {
            post({ type: 'notice', level: 'error', text: 'Unknown graphics system "' + systemId + '": ' + errorText(error) });
            return;
          }
          sendAsset(true);
          live.send();
          break;
        }
        case 'live':
          await live.set(message.on);
          break;
        case 'importPng': {
          let chr: string | undefined;
          try {
            const refs = parseJson(document.getText()) as { chr?: unknown };
            chr = typeof refs.chr === 'string' && refs.chr ? resolveRelative(uri, refs.chr).fsPath : undefined;
          } catch {
            // no tile set preselected
          }
          await vscode.commands.executeCommand('winasm65Graphics.importPng', undefined,
            { folder: path.dirname(uri.fsPath), suggestedChr: chr, mode: spec.importMode });
          break;
        }
        case 'edit':
          try {
            await this.applyAsset(document, message.asset);
          } catch (error) {
            post({ type: 'notice', level: 'error', text: message.label + ' refused: ' + errorText(error) });
            sendAsset(); // back to what the file holds
          }
          break;
        case 'chooseFile':
          await this.chooseFile(document, message.field);
          break;
        case 'openFile': {
          const target = vscode.Uri.joinPath(uri, '..', ...message.path.split(/[\\/]/));
          await vscode.commands.executeCommand('vscode.open', target, vscode.ViewColumn.Beside);
          break;
        }
        case 'openAsText':
          await vscode.commands.executeCommand('vscode.openWith', uri, 'default');
          break;
      }
    }));

    panel.onDidDispose(() => {
      if (pendingSend) {
        clearTimeout(pendingSend);
      }
      subscriptions.forEach(s => s.dispose());
    });
  }

  /** Lets the user pick the tile set or the palette file the asset names. */
  private async chooseFile(document: vscode.TextDocument, field: 'chr' | 'palettes'): Promise<void> {
    const pattern = field === 'chr' ? '**/*.chr' : '**/*.pal.json';
    const files = await vscode.workspace.findFiles(pattern, '**/node_modules/**', 500);
    const items: Array<vscode.QuickPickItem & { uri?: vscode.Uri }> = files
      .map(file => ({ label: relativePath(document.uri, file), description: vscode.workspace.asRelativePath(file), uri: file }))
      .sort((a, b) => a.label.localeCompare(b.label));
    items.push({ label: 'Browse…' });
    items.push({ label: 'None', description: field === 'chr' ? 'no tile set' : 'greys' });
    const picked = await vscode.window.showQuickPick(items, { placeHolder: field === 'chr' ? 'Tile set (.chr)' : 'Palette file (.pal.json)' });
    if (!picked) {
      return;
    }
    let value: string | undefined;
    if (picked.uri) {
      value = picked.label;
    } else if (picked.label === 'Browse…') {
      const chosen = await vscode.window.showOpenDialog({
        defaultUri: vscode.Uri.joinPath(document.uri, '..'),
        filters: field === 'chr' ? { 'Tile sets': ['chr'] } : { 'Palette files': ['json'] }
      });
      if (!chosen || chosen.length === 0) {
        return;
      }
      value = relativePath(document.uri, chosen[0]);
    }
    try {
      const asset = parseJson(document.getText()) as Record<string, unknown>;
      if (value === undefined) {
        delete asset[field];
      } else {
        asset[field] = value;
      }
      await this.applyAsset(document, asset, true);
    } catch (error) {
      void vscode.window.showErrorMessage('Cannot change ' + field + ': ' + errorText(error));
    }
  }
}

export function errorText(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
