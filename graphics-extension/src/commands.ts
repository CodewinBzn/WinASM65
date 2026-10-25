import * as path from 'path';
import * as vscode from 'vscode';
import { Designer } from './designer';
import { Exporter } from './exporting';
import { relativePath } from './resources';
import { AssetKind, EXTENSIONS, blankMap, blankMetasprites, blankPalette, blankScreen, kindOf } from './shared/assets';
import { EditorRegistry } from './editors/context';

const VIEW_TYPES: Partial<Record<AssetKind, string>> = {
  chr: 'winasm65Graphics.tiles',
  palette: 'winasm65Graphics.palettes',
  screen: 'winasm65Graphics.screen',
  map: 'winasm65Graphics.map',
  metasprite: 'winasm65Graphics.metasprites'
};

/** The commands of the palette and of the Explorer's "WinASM65 Graphics" menu. */
export function registerCommands(designer: Designer, exporter: Exporter, registry: EditorRegistry): vscode.Disposable[] {
  return [
    vscode.commands.registerCommand('winasm65Graphics.newPalette', (folder?: vscode.Uri) => newAsset(designer, 'palette', folder)),
    vscode.commands.registerCommand('winasm65Graphics.newScreen', (folder?: vscode.Uri) => newAsset(designer, 'screen', folder)),
    vscode.commands.registerCommand('winasm65Graphics.newMap', (folder?: vscode.Uri) => newAsset(designer, 'map', folder)),
    vscode.commands.registerCommand('winasm65Graphics.newTileSet', (folder?: vscode.Uri) => newAsset(designer, 'chr', folder)),
    vscode.commands.registerCommand('winasm65Graphics.newMetasprites', (folder?: vscode.Uri) => newAsset(designer, 'metasprite', folder)),
    vscode.commands.registerCommand('winasm65Graphics.exportAsset', (file?: vscode.Uri) => exportAsset(exporter, registry, file)),
    vscode.commands.registerCommand('winasm65Graphics.exportFolder', (folder?: vscode.Uri) => exportFolder(exporter, folder))
  ];
}

const TITLES: Record<string, string> = { palette: 'New Palette', screen: 'New Screen', map: 'New Map', chr: 'New Tile Set', metasprite: 'New Metasprites' };

async function newAsset(designer: Designer, kind: AssetKind, folderArg?: vscode.Uri): Promise<vscode.Uri | undefined> {
  const title = TITLES[kind];
  const folder = folderArg instanceof vscode.Uri ? folderArg : await chooseFolder(title);
  if (!folder) {
    return undefined;
  }
  const name = await vscode.window.showInputBox({
    title,
    prompt: 'Name of the ' + (kind === 'chr' ? 'tile set' : kind === 'metasprite' ? 'metasprite file' : kind) + ' (the file is <name>' + EXTENSIONS[kind] + ' in ' + vscode.workspace.asRelativePath(folder) + ')',
    validateInput: value => /^[A-Za-z_][\w-]*$/.test(value.trim()) ? undefined : 'A name of letters, digits, _ and -, not starting with a digit'
  });
  if (!name) {
    return undefined;
  }
  const target = vscode.Uri.joinPath(folder, name.trim() + EXTENSIONS[kind]);
  if (await exists(target)) {
    void vscode.window.showErrorMessage(vscode.workspace.asRelativePath(target) + ' already exists.');
    return undefined;
  }

  const { system } = await designer.system('nes');
  let bytes: Uint8Array;
  if (kind === 'chr') {
    const size = await vscode.window.showQuickPick([
      { label: String(system.maxTiles), description: 'tiles: one pattern table (' + system.maxTiles * system.bytesPerTile / 1024 + ' KiB)', count: system.maxTiles },
      { label: String(system.maxTiles * 2), description: 'tiles: both pattern tables (' + system.maxTiles * 2 * system.bytesPerTile / 1024 + ' KiB)', count: system.maxTiles * 2 }
    ], { title, placeHolder: 'How many tiles' });
    if (!size) {
      return undefined;
    }
    bytes = new Uint8Array(size.count * system.bytesPerTile);
  } else {
    let asset: unknown;
    if (kind === 'palette') {
      asset = blankPalette(system, name.trim());
    } else {
      const chr = await chooseReference(target, '**/*.chr', 'Tile set (.chr) the ' + kind + ' draws with', title);
      if (chr === null) return undefined;
      const palettes = await chooseReference(target, '**/*.pal.json', 'Palette file (.pal.json) of the ' + kind, title);
      if (palettes === null) return undefined;
      if (kind === 'metasprite') {
        const sprites = blankMetasprites(system, chr, palettes);
        sprites.metasprites.push({ name: name.trim(), originX: 64, originY: 64, tiles: [] });
        asset = sprites;
      } else {
        asset = kind === 'screen' ? blankScreen(system, name.trim(), chr, palettes) : blankMap(system, name.trim(), chr, palettes);
      }
    }
    const formatted = await designer.format(kind, asset);
    bytes = Buffer.from(formatted.text, 'utf8');
  }
  await vscode.workspace.fs.writeFile(target, bytes);
  await vscode.commands.executeCommand('vscode.openWith', target, VIEW_TYPES[kind]);
  return target;
}

async function chooseFolder(title: string): Promise<vscode.Uri | undefined> {
  const folders = vscode.workspace.workspaceFolders;
  const chosen = await vscode.window.showOpenDialog({
    title: title + ': folder',
    canSelectFiles: false,
    canSelectFolders: true,
    canSelectMany: false,
    openLabel: 'Create Here',
    defaultUri: folders?.[0]?.uri
  });
  return chosen?.[0];
}

/** A file of the workspace as the new asset will name it; undefined for none, null when cancelled. */
async function chooseReference(asset: vscode.Uri, pattern: string, placeHolder: string, title: string): Promise<string | undefined | null> {
  const files = await vscode.workspace.findFiles(pattern, '**/node_modules/**', 500);
  const items: Array<vscode.QuickPickItem & { value?: string }> = files
    .map(file => ({ label: relativePath(asset, file), description: vscode.workspace.asRelativePath(file), value: relativePath(asset, file) }))
    .sort((a, b) => a.label.localeCompare(b.label));
  items.push({ label: 'None', description: 'choose later in the editor' });
  const picked = await vscode.window.showQuickPick(items, { title, placeHolder });
  if (!picked) {
    return null;
  }
  return picked.value;
}

async function exists(uri: vscode.Uri): Promise<boolean> {
  try {
    await vscode.workspace.fs.stat(uri);
    return true;
  } catch {
    return false;
  }
}

async function exportAsset(exporter: Exporter, registry: EditorRegistry, fileArg?: vscode.Uri): Promise<void> {
  const file = fileArg instanceof vscode.Uri ? fileArg : registry.active ?? vscode.window.activeTextEditor?.document.uri;
  const kind = file ? kindOf(file.fsPath) : undefined;
  if (!file || !kind || kind === 'chr') {
    void vscode.window.showErrorMessage('Export Asset: open or select a .pal.json, .screen.json, .map.json or .msprite.json file.');
    return;
  }
  await saveIfDirty(file);
  const result = await exporter.export(file, true);
  if (result) {
    await reportExport(path.basename(file.fsPath), result.written, result.diagnostics.filter(d => d.severity === 'error').length);
  }
}

async function exportFolder(exporter: Exporter, folderArg?: vscode.Uri): Promise<void> {
  const folder = folderArg instanceof vscode.Uri ? folderArg : await chooseFolder('Export All Assets');
  if (!folder) {
    return;
  }
  await vscode.workspace.saveAll(false);
  const result = await exporter.export(folder, true);
  if (result) {
    await reportExport(vscode.workspace.asRelativePath(folder), result.written, result.diagnostics.filter(d => d.severity === 'error').length);
  }
}

async function saveIfDirty(file: vscode.Uri): Promise<void> {
  const open = vscode.workspace.textDocuments.find(d => d.uri.toString() === file.toString());
  if (open?.isDirty) {
    await open.save();
  }
}

/** Says what an export wrote, and offers to open it. */
async function reportExport(what: string, written: string[], errors: number): Promise<void> {
  const summary = 'Exported ' + what + ': ' + written.length + ' file' + (written.length === 1 ? '' : 's') + ' written'
    + (errors > 0 ? ', ' + errors + ' error' + (errors === 1 ? '' : 's') + ' (see Problems)' : '') + '.';
  const show = written.length > 0 ? 'Show Files' : undefined;
  const choice = errors > 0
    ? await vscode.window.showWarningMessage(summary, ...(show ? [show] : []))
    : await vscode.window.showInformationMessage(summary, ...(show ? [show] : []));
  if (choice === show && show) {
    const picked = await vscode.window.showQuickPick(
      written.map(file => ({ label: path.basename(file), description: vscode.workspace.asRelativePath(file), file })),
      { placeHolder: 'Open a written file' });
    if (picked) {
      await vscode.commands.executeCommand('vscode.open', vscode.Uri.file(picked.file));
    }
  }
}
