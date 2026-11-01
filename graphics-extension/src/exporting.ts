import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { Designer, ExportResult } from './designer';
import { AssetDiagnostic, kindOf } from './shared/assets';
import { lineOf, locateJsonPath } from './shared/json';

/**
 * Exports assets through the designer and shows what it found wrong in the
 * Problems panel, each diagnostic on the line of the JSON value its path names.
 */
export class Exporter implements vscode.Disposable {
  readonly collection = vscode.languages.createDiagnosticCollection('winasm65Graphics');
  private readonly subscriptions: vscode.Disposable[] = [this.collection];

  constructor(private readonly designer: Designer, private readonly log: vscode.OutputChannel) {
    this.subscriptions.push(vscode.workspace.onDidSaveTextDocument(document => {
      const kind = kindOf(document.uri.fsPath);
      if (!kind || kind === 'chr' || document.uri.scheme !== 'file') {
        return;
      }
      if (!vscode.workspace.getConfiguration('winasm65Graphics').get<boolean>('exportOnSave', true)) {
        return;
      }
      void this.export(document.uri, false);
    }));
  }

  /**
   * Exports one asset or every asset of a folder. The result, or undefined when
   * the designer could not be asked (the error is shown when `loud`).
   */
  async export(target: vscode.Uri, loud: boolean): Promise<ExportResult | undefined> {
    let result: ExportResult;
    try {
      result = await this.designer.export(target.fsPath);
    } catch (error) {
      const message = 'Export of ' + path.basename(target.fsPath) + ' failed: ' + (error instanceof Error ? error.message : String(error));
      this.log.appendLine(message);
      if (loud) {
        void vscode.window.showErrorMessage(message);
      }
      return undefined;
    }
    for (const written of result.written) {
      this.log.appendLine('wrote ' + written);
    }
    for (const diagnostic of result.diagnostics) {
      this.log.appendLine((diagnostic.file ?? target.fsPath) + ': ' + diagnostic.severity + ': ' + diagnostic.path + ': ' + diagnostic.message);
    }
    this.report(target, result.diagnostics);
    return result;
  }

  /** Replaces the diagnostics of a file (or of every file under a folder) with the designer's. */
  report(target: vscode.Uri, diagnostics: AssetDiagnostic[]): void {
    const isFolder = isDirectory(target.fsPath);
    const prefix = target.fsPath.toLowerCase() + path.sep;
    this.collection.forEach(uri => {
      const file = uri.fsPath.toLowerCase();
      if (isFolder ? file.startsWith(prefix) : file === target.fsPath.toLowerCase()) {
        this.collection.delete(uri);
      }
    });

    const byFile = new Map<string, vscode.Diagnostic[]>();
    for (const diagnostic of diagnostics) {
      const file = diagnostic.file || target.fsPath;
      const line = lineOfPath(file, diagnostic.path);
      const range = new vscode.Range(line, 0, line, Number.MAX_SAFE_INTEGER);
      const item = new vscode.Diagnostic(range, diagnostic.message,
        diagnostic.severity === 'error' ? vscode.DiagnosticSeverity.Error : vscode.DiagnosticSeverity.Warning);
      item.source = 'WinASM65 Graphics';
      item.code = diagnostic.path;
      const list = byFile.get(file) ?? [];
      list.push(item);
      byFile.set(file, list);
    }
    for (const [file, list] of byFile) {
      this.collection.set(vscode.Uri.file(file), list);
    }
  }

  dispose(): void {
    this.subscriptions.forEach(s => s.dispose());
  }
}

function isDirectory(file: string): boolean {
  try {
    return fs.statSync(file).isDirectory();
  } catch {
    return false;
  }
}

/** The line of a JSON file a diagnostic's path names; 0 when it cannot be found. */
function lineOfPath(file: string, jsonPath: string): number {
  if (!file.toLowerCase().endsWith('.json') || !jsonPath || jsonPath === '$') {
    return 0;
  }
  const open = vscode.workspace.textDocuments.find(d => d.uri.scheme === 'file' && d.uri.fsPath.toLowerCase() === file.toLowerCase());
  let text: string;
  try {
    text = open ? open.getText() : fs.readFileSync(file, 'utf8');
  } catch {
    return 0;
  }
  return lineOf(text, locateJsonPath(text, jsonPath));
}
