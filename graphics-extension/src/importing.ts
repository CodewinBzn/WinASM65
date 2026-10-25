import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { DesignerClient } from './server';
import { AssetDiagnostic } from './shared/assets';
import { TilesEditorProvider } from './editors/tilesEditor';

export type ImportMode = 'background' | 'sprites';

/** What the command may be given instead of asking (the editors' buttons, the tests). */
export interface ImportOptions {
  mode?: ImportMode;
  /** Folder the assets are written to. */
  folder?: string;
  /** Name of the assets (`<name>.pal.json`, `<name>.screen.json`...). */
  name?: string;
  /** A tile set to append to; null for a new `<name>.chr`; undefined to ask. */
  chr?: string | null;
  /** The tile set preselected when asking (an editor's own). */
  suggestedChr?: string;
  /** Open the screen or metasprite made (default true). */
  open?: boolean;
  /** Replace existing files without asking (tests). */
  overwrite?: boolean;
}

export interface ImportStatistics {
  cells: number;
  newTiles: number;
  reusedTiles: number;
  tileCount: number;
  palettes: number;
  colors: number;
  warnings: number;
}

export interface ImportReply {
  written: string[];
  statistics: ImportStatistics;
  diagnostics: AssetDiagnostic[];
}

const MODES: Array<vscode.QuickPickItem & { mode: ImportMode }> = [
  { label: 'Background', description: 'tiles + palettes + a screen', detail: 'At most 256x240 pixels; a shared background colour and 4 palettes of 3 colours, one per 16x16 block.', mode: 'background' },
  { label: 'Sprites', description: 'tiles + palettes + a metasprite', detail: 'Colour 0 is transparent; up to 4 palettes of 3 colours, one per 8x8 tile; flipped tiles are reused.', mode: 'sprites' }
];

/**
 * "Import PNG…": a picture into tiles, palettes and a screen or a metasprite,
 * through the designer's `import` request. Asks for what it is not given.
 */
export class PngImport {
  constructor(
    private readonly client: DesignerClient,
    private readonly log: vscode.OutputChannel,
    private readonly tiles: TilesEditorProvider
  ) {}

  register(): vscode.Disposable {
    return vscode.commands.registerCommand('winasm65Graphics.importPng',
      (png?: vscode.Uri, options?: ImportOptions) => this.run(png instanceof vscode.Uri ? png : undefined, options ?? {}));
  }

  async run(pngArg: vscode.Uri | undefined, options: ImportOptions): Promise<ImportReply | undefined> {
    const title = 'Import PNG';
    let png = pngArg && pngArg.fsPath.toLowerCase().endsWith('.png') ? pngArg : undefined;
    if (!png) {
      png = await this.choosePicture(title, options.folder);
      if (!png) return undefined;
    }

    let mode = options.mode;
    if (!mode) {
      const picked = await vscode.window.showQuickPick(MODES, { title, placeHolder: 'Import ' + path.basename(png.fsPath) + ' as' });
      if (!picked) return undefined;
      mode = picked.mode;
    }

    let folder = options.folder;
    if (!folder) {
      folder = await this.chooseFolder(title, path.dirname(png.fsPath));
      if (!folder) return undefined;
    }

    let name = options.name;
    if (!name) {
      const suggested = path.basename(png.fsPath, path.extname(png.fsPath)).replace(/[^\w-]+/g, '_').replace(/^(\d)/, '_$1');
      name = (await vscode.window.showInputBox({
        title,
        value: suggested,
        prompt: 'Name of the assets: <name>.pal.json, <name>' + (mode === 'background' ? '.screen.json' : '.msprite.json') + ' and, for a new tile set, <name>.chr',
        validateInput: value => /^[A-Za-z_][\w-]*$/.test(value.trim()) ? undefined : 'A name of letters, digits, _ and -, not starting with a digit'
      }))?.trim();
      if (!name) return undefined;
    }

    let chr = options.chr;
    if (chr === undefined) {
      const files = await vscode.workspace.findFiles('**/*.chr', '**/node_modules/**', 500);
      const items: Array<vscode.QuickPickItem & { file?: string }> = [
        { label: 'New tile set', description: name + '.chr in ' + relativeFolder(folder) }
      ];
      const others = files
        .map(file => ({ label: path.basename(file.fsPath), description: 'append to ' + vscode.workspace.asRelativePath(file), file: file.fsPath }))
        .sort((a, b) => a.description.localeCompare(b.description));
      const preselected = options.suggestedChr;
      items.push(...others);
      const pick = vscode.window.createQuickPick<vscode.QuickPickItem & { file?: string }>();
      pick.title = title;
      pick.placeholder = 'Tiles: a new tile set, or an existing one to append the new tiles to (tiles already there are reused)';
      pick.items = items;
      const preferred = preselected ? items.find(i => i.file && path.resolve(i.file).toLowerCase() === path.resolve(preselected).toLowerCase()) : undefined;
      if (preferred) pick.activeItems = [preferred];
      const choice = await new Promise<(vscode.QuickPickItem & { file?: string }) | undefined>(resolve => {
        pick.onDidAccept(() => resolve(pick.selectedItems[0] ?? pick.activeItems[0]));
        pick.onDidHide(() => resolve(undefined));
        pick.show();
      });
      pick.dispose();
      if (!choice) return undefined;
      chr = choice.file ?? null;
    }

    // An open tile set must be saved first: the import appends to the file.
    if (chr) {
      const open = this.tiles.documentFor(vscode.Uri.file(chr));
      if (open && this.tiles.isDirty(open)) {
        void vscode.window.showErrorMessage('Import PNG: save ' + path.basename(chr) + ' first; the new tiles are appended to the file.');
        return undefined;
      }
    }

    const targets = [name + '.pal.json', name + (mode === 'background' ? '.screen.json' : '.msprite.json')];
    if (!chr) targets.push(name + '.chr');
    const existing = targets.filter(t => fs.existsSync(path.join(folder!, t)));
    if (existing.length > 0 && !options.overwrite) {
      const replace = await vscode.window.showWarningMessage(
        existing.join(', ') + (existing.length === 1 ? ' exists' : ' exist') + ' in ' + vscode.workspace.asRelativePath(folder) + '. Replace?',
        { modal: true }, 'Replace');
      if (replace !== 'Replace') return undefined;
    }

    let reply: ImportReply;
    try {
      reply = await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: 'Importing ' + path.basename(png.fsPath) + '…' },
        () => this.client.request<ImportReply>('import', { png: png!.fsPath, mode, out: folder, name, chr: chr ?? undefined }));
    } catch (error) {
      const message = 'Import of ' + path.basename(png.fsPath) + ' failed: ' + (error instanceof Error ? error.message : String(error));
      this.log.appendLine(message);
      void vscode.window.showErrorMessage(message, 'Show Output').then(c => { if (c) this.log.show(true); });
      return undefined;
    }

    // What it made and what it had to give up, in the output channel.
    const s = reply.statistics;
    const summary = s.cells + ' cells, ' + s.newTiles + ' new tiles (' + s.reusedTiles + ' reused), ' + s.tileCount + ' tiles in the set, '
      + s.palettes + ' palette' + (s.palettes === 1 ? '' : 's') + ', ' + s.colors + ' colour' + (s.colors === 1 ? '' : 's');
    this.log.appendLine('import ' + png.fsPath + ' (' + mode + ') → ' + folder + ': ' + summary);
    for (const written of reply.written) this.log.appendLine('  wrote ' + written);
    for (const d of reply.diagnostics) this.log.appendLine('  ' + d.severity + ': ' + (d.path && d.path !== '$' ? d.path + ': ' : '') + d.message);

    // A tile editor open on the tile set shows the appended tiles.
    if (chr) {
      const open = this.tiles.documentFor(vscode.Uri.file(chr));
      if (open) await this.tiles.revertCustomDocument(open);
    }

    const warnings = reply.diagnostics.filter(d => d.severity === 'warning');
    const text = 'Imported ' + path.basename(png.fsPath) + ': ' + s.newTiles + ' new tiles (' + s.reusedTiles + ' reused), '
      + s.palettes + ' palette' + (s.palettes === 1 ? '' : 's') + ', ' + s.colors + ' colour' + (s.colors === 1 ? '' : 's') + '.'
      + (warnings.length > 0 ? ' ' + warnings.length + ' warning' + (warnings.length === 1 ? '' : 's') + ': ' + warnings[0].message + (warnings.length > 1 ? '…' : '') : '');
    const show = warnings.length > 0
      ? vscode.window.showWarningMessage(text, 'Show Output')
      : vscode.window.showInformationMessage(text);
    void show.then(choice => { if (choice) this.log.show(true); });

    if (options.open !== false) {
      const made = reply.written.find(f => /\.(screen|msprite)\.json$/i.test(f));
      if (made) {
        await vscode.commands.executeCommand('vscode.openWith', vscode.Uri.file(made),
          made.toLowerCase().endsWith('.screen.json') ? 'winasm65Graphics.screen' : 'winasm65Graphics.metasprites');
      }
    }
    return reply;
  }

  /** A PNG of the workspace (nearest to `near` first), or one browsed to. */
  private async choosePicture(title: string, near: string | undefined): Promise<vscode.Uri | undefined> {
    const files = await vscode.workspace.findFiles('**/*.png', '**/node_modules/**', 500);
    const distance = (file: vscode.Uri) => near ? path.relative(near, file.fsPath).split(/[\\/]/).length : 0;
    const items: Array<vscode.QuickPickItem & { uri?: vscode.Uri }> = files
      .sort((a, b) => distance(a) - distance(b) || a.fsPath.localeCompare(b.fsPath))
      .map(uri => ({ label: path.basename(uri.fsPath), description: vscode.workspace.asRelativePath(path.dirname(uri.fsPath)), uri }));
    items.push({ label: 'Browse…', description: 'a picture outside the workspace' });
    const picked = await vscode.window.showQuickPick(items, { title, placeHolder: 'The picture to import (PNG)', matchOnDescription: true });
    if (!picked) return undefined;
    if (picked.uri) return picked.uri;
    const chosen = await vscode.window.showOpenDialog({
      title: title + ': picture',
      canSelectMany: false,
      filters: { 'PNG pictures': ['png'] },
      defaultUri: near ? vscode.Uri.file(near) : vscode.workspace.workspaceFolders?.[0]?.uri
    });
    return chosen?.[0];
  }

  /** The folder the assets go to: the picture's, a workspace folder, or one browsed to. */
  private async chooseFolder(title: string, pictureFolder: string): Promise<string | undefined> {
    const items: Array<vscode.QuickPickItem & { folder?: string }> = [
      { label: relativeFolder(pictureFolder), description: 'the picture\'s folder', folder: pictureFolder }
    ];
    for (const workspaceFolder of vscode.workspace.workspaceFolders ?? []) {
      if (path.resolve(workspaceFolder.uri.fsPath).toLowerCase() !== path.resolve(pictureFolder).toLowerCase()) {
        items.push({ label: workspaceFolder.name, description: 'workspace folder', folder: workspaceFolder.uri.fsPath });
      }
    }
    items.push({ label: 'Browse…', description: 'another folder' });
    const picked = await vscode.window.showQuickPick(items, { title, placeHolder: 'Where the tiles, palettes and screen or metasprite go' });
    if (!picked) return undefined;
    if (picked.folder) return picked.folder;
    const chosen = await vscode.window.showOpenDialog({
      title: title + ': folder for the assets',
      canSelectFiles: false,
      canSelectFolders: true,
      canSelectMany: false,
      openLabel: 'Import Here',
      defaultUri: vscode.Uri.file(pictureFolder)
    });
    return chosen?.[0]?.fsPath;
  }
}

/** A folder as the workspace names it; a workspace folder by its name. */
function relativeFolder(folder: string): string {
  const relative = vscode.workspace.asRelativePath(folder, true);
  return relative === folder ? path.basename(folder) : relative;
}
