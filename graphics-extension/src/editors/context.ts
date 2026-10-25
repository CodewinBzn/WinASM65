import * as vscode from 'vscode';
import { Designer } from '../designer';
import { AssetResources } from '../resources';
import { EditorKind } from '../shared/protocol';

/** What every editor provider is given. */
export interface EditorContext {
  extensionUri: vscode.Uri;
  designer: Designer;
  resources: AssetResources;
  log: vscode.OutputChannel;
  registry: EditorRegistry;
}

/**
 * The editors that are open: which file the active one shows (the Export
 * command acts on it), and which webviews loaded and answered (the tests
 * check it).
 */
export class EditorRegistry {
  /** uri → kind, for every editor resolved since activation. */
  readonly resolved = new Map<string, EditorKind>();
  /** uri → kind, for every editor whose webview script ran and said it was ready. */
  readonly ready = new Map<string, EditorKind>();
  private activeUri: vscode.Uri | undefined;

  track(panel: vscode.WebviewPanel, uri: vscode.Uri, kind: EditorKind): vscode.Disposable {
    this.resolved.set(uri.toString(), kind);
    if (panel.active) {
      this.activeUri = uri;
    }
    return vscode.Disposable.from(
      panel.onDidChangeViewState(event => {
        if (event.webviewPanel.active) {
          this.activeUri = uri;
        } else if (this.activeUri?.toString() === uri.toString()) {
          this.activeUri = undefined;
        }
      }),
      panel.onDidDispose(() => {
        if (this.activeUri?.toString() === uri.toString()) {
          this.activeUri = undefined;
        }
      })
    );
  }

  markReady(uri: vscode.Uri, kind: EditorKind): void {
    this.ready.set(uri.toString(), kind);
  }

  /** The file of the graphics editor that has the focus, if one has. */
  get active(): vscode.Uri | undefined {
    return this.activeUri;
  }
}
