import * as vscode from 'vscode';
import { AssetDiagnostic, AssetKind } from './shared/assets';
import { formatAsset } from './shared/json';
import { GraphicsSystem, NES_FALLBACK } from './shared/system';
import { DesignerClient } from './server';

export interface ExportResult {
  written: string[];
  diagnostics: AssetDiagnostic[];
}

/**
 * The designer's requests as the editors use them, with what to do when the
 * designer cannot be reached: the built-in NES description and the TypeScript
 * copy of the file layout, so drawing never depends on the server.
 */
export class Designer {
  private readonly systems = new Map<string, Promise<{ system: GraphicsSystem; note?: string }>>();

  constructor(readonly client: DesignerClient, private readonly log: vscode.OutputChannel) {}

  /** The machine an asset is drawn for, asked once per id. */
  system(id = 'nes'): Promise<{ system: GraphicsSystem; note?: string }> {
    const key = id.toLowerCase();
    let found = this.systems.get(key);
    if (!found) {
      found = this.client.request<GraphicsSystem>('describe', { system: key }).then(
        system => ({ system }),
        error => {
          this.systems.delete(key);
          const message = error instanceof Error ? error.message : String(error);
          this.log.appendLine('describe ' + key + ': ' + message);
          if (key === 'nes' || key === 'famicom') {
            return { system: NES_FALLBACK, note: 'designer unavailable (' + message + '): built-in NES description' };
          }
          throw error;
        });
      this.systems.set(key, found);
    }
    return found;
  }

  /** The text the designer writes for a JSON asset. */
  async format(kind: AssetKind, asset: unknown, filePath?: string): Promise<{ text: string; diagnostics: AssetDiagnostic[] }> {
    try {
      return await this.client.request<{ text: string; diagnostics: AssetDiagnostic[] }>('format', { kind, asset, path: filePath });
    } catch (error) {
      if (error instanceof Error && 'code' in error) {
        throw error; // the designer answered: the asset is not of the right shape
      }
      this.log.appendLine('format: ' + (error instanceof Error ? error.message : String(error)) + ' (formatted by the extension)');
      const system = (await this.system((asset as { system?: string }).system ?? 'nes')).system;
      return { text: formatAsset(kind, asset, system), diagnostics: [] };
    }
  }

  /** Writes the .asm and binary files of an asset, or of every asset in a folder. */
  export(filePath: string): Promise<ExportResult> {
    return this.client.request<ExportResult>('export', { path: filePath });
  }
}
