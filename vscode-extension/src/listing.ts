import * as vscode from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';

interface ListingRow {
  line: number;
  address: number;
  bytes: string;
  more: boolean;
}

const LANGUAGE = 'winasm65';
const CONTEXT_KEY = 'winasm65.listingVisible';
const STATE_KEY = 'winasm65.listingVisible';
const MAX_BYTES = 3;
// "C003 8D 01 20…" plus one space: four address digits, three bytes, the mark.
const COLUMN = 14;
const NBSP = ' ';

/**
 * The listing column drawn left of the source: the address and bytes each line
 * assembled to, as the terminal monitor's gutter shows them. The rows come from
 * the language server (winasm65/listing), which lists the unsaved text with the
 * project's names, so the column follows the edit.
 */
export class ListingColumn implements vscode.Disposable {
  private readonly decoration = vscode.window.createTextEditorDecorationType({
    before: {
      color: new vscode.ThemeColor('editorLineNumber.foreground'),
      margin: '0 1ch 0 0'
    }
  });
  private readonly subscriptions: vscode.Disposable[] = [];
  private readonly timers = new Map<string, NodeJS.Timeout>();
  private visible: boolean;

  constructor(private readonly context: vscode.ExtensionContext, private readonly client: () => Promise<LanguageClient | undefined>) {
    this.visible = context.workspaceState.get<boolean>(STATE_KEY, false);
    void vscode.commands.executeCommand('setContext', CONTEXT_KEY, this.visible);

    this.subscriptions.push(
      vscode.commands.registerCommand('winasm65.showListing', () => this.setVisible(true)),
      vscode.commands.registerCommand('winasm65.hideListing', () => this.setVisible(false)),
      vscode.window.onDidChangeVisibleTextEditors(editors => editors.forEach(e => this.schedule(e.document, 0))),
      vscode.workspace.onDidChangeTextDocument(e => this.schedule(e.document, 300)),
      // After a save the server rebuilds the project, which is what an included
      // file's column is listed from.
      vscode.workspace.onDidSaveTextDocument(d => this.schedule(d, 500))
    );
    if (this.visible) {
      vscode.window.visibleTextEditors.forEach(e => this.schedule(e.document, 0));
    }
  }

  private async setVisible(visible: boolean): Promise<void> {
    this.visible = visible;
    await this.context.workspaceState.update(STATE_KEY, visible);
    await vscode.commands.executeCommand('setContext', CONTEXT_KEY, visible);
    for (const editor of vscode.window.visibleTextEditors) {
      if (visible) {
        this.schedule(editor.document, 0);
      } else {
        editor.setDecorations(this.decoration, []);
      }
    }
  }

  private schedule(document: vscode.TextDocument, delayMs: number): void {
    if (!this.visible || document.languageId !== LANGUAGE) {
      return;
    }
    const key = document.uri.toString();
    const pending = this.timers.get(key);
    if (pending) {
      clearTimeout(pending);
    }
    this.timers.set(key, setTimeout(() => {
      this.timers.delete(key);
      void this.refresh(document);
    }, delayMs));
  }

  private async refresh(document: vscode.TextDocument): Promise<void> {
    const client = await this.client();
    if (!client || !this.visible) {
      return;
    }
    let rows: ListingRow[];
    try {
      const reply = await client.sendRequest<{ rows: ListingRow[] }>('winasm65/listing', {
        textDocument: { uri: client.code2ProtocolConverter.asUri(document.uri) }
      });
      rows = reply.rows;
    } catch {
      return;
    }

    const byLine = new Map<number, ListingRow>();
    for (const row of rows) {
      byLine.set(row.line, row);
    }

    // Every line gets the column, an empty one where nothing was assembled, so the
    // source stays aligned as in a listing.
    const blank = NBSP.repeat(COLUMN);
    const decorations: vscode.DecorationOptions[] = [];
    for (let line = 0; line < document.lineCount; line++) {
      const row = byLine.get(line);
      decorations.push({
        range: new vscode.Range(line, 0, line, 0),
        renderOptions: { before: { contentText: row ? format(row) : blank } }
      });
    }

    for (const editor of vscode.window.visibleTextEditors) {
      if (editor.document === document && this.visible) {
        editor.setDecorations(this.decoration, decorations);
      }
    }
  }

  dispose(): void {
    this.timers.forEach(t => clearTimeout(t));
    this.subscriptions.forEach(s => s.dispose());
    this.decoration.dispose();
  }
}

export function format(row: ListingRow): string {
  const bytes = row.bytes.split(' ').filter(b => b.length > 0);
  const shown = bytes.slice(0, MAX_BYTES).join(' ');
  const mark = bytes.length > MAX_BYTES || row.more ? '…' : '';
  const text = row.address.toString(16).toUpperCase().padStart(4, '0') + ' ' + shown + mark;
  return (text + ' '.repeat(Math.max(0, COLUMN - text.length))).replace(/ /g, NBSP);
}
