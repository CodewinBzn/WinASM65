import * as assert from 'assert';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import type { GraphicsApi } from '../extension';

async function waitFor<T>(what: string, probe: () => T | undefined | false, timeoutMs = 60000): Promise<T> {
  const end = Date.now() + timeoutMs;
  while (Date.now() < end) {
    const value = probe();
    if (value !== undefined && value !== false) {
      return value;
    }
    await new Promise(resolve => setTimeout(resolve, 200));
  }
  throw new Error('timed out waiting for ' + what);
}

/** Runs inside VS Code on the sample assets (src/test/samples.ts). */
export async function run(): Promise<void> {
  const folder = vscode.workspace.workspaceFolders?.[0];
  assert.ok(folder, 'the sample folder is open');
  const file = (name: string) => vscode.Uri.file(path.join(folder.uri.fsPath, name));

  assert.ok(vscode.extensions.getExtension('codewinbzn.winasm65'), 'the WinASM65 extension it depends on is there');
  const extension = vscode.extensions.getExtension<GraphicsApi>('codewinbzn.winasm65-graphics');
  assert.ok(extension, 'the extension is loaded');
  const api = await extension.activate();

  const commands = await vscode.commands.getCommands(true);
  for (const id of ['newPalette', 'newScreen', 'newMap', 'newTileSet', 'exportAsset', 'exportFolder']) {
    assert.ok(commands.includes('winasm65Graphics.' + id), id + ' is registered');
  }

  const system = await api.designer.system('nes');
  assert.strictEqual(system.note, undefined, 'the designer answered describe: ' + system.note);
  assert.strictEqual(system.system.masterPalette.length, 64);

  // Every editor opens on its file, and its webview script runs (the CSP lets it load).
  const editors: Array<[string, string, string]> = [
    ['game.pal.json', 'winasm65Graphics.palettes', 'palette'],
    ['title.screen.json', 'winasm65Graphics.screen', 'screen'],
    ['level1.map.json', 'winasm65Graphics.map', 'map'],
    ['tiles.chr', 'winasm65Graphics.tiles', 'tiles']
  ];
  for (const [name, viewType, kind] of editors) {
    const uri = file(name);
    await vscode.commands.executeCommand('vscode.openWith', uri, viewType);
    await waitFor(name + ' resolved', () => api.registry.resolved.get(uri.toString()) === kind);
    await waitFor(name + ' webview ready', () => api.registry.ready.get(uri.toString()) === kind);
    console.log('PASS ' + viewType + ' opened ' + name);
  }

  // The palette editor's edit path: the asset in, the designer's text out, one undoable edit.
  const paletteUri = file('game.pal.json');
  const document = await vscode.workspace.openTextDocument(paletteUri);
  const before = document.getText();
  const asset = JSON.parse(before);
  asset.palettes[1].colors[2] = 0x2a;
  const provider = api.jsonEditors.get('winasm65Graphics.palettes');
  assert.ok(provider);
  assert.strictEqual(await provider.applyAsset(document, asset), true, 'the text changed');
  assert.ok(document.getText().includes('{ "name": "sky", "colors": [15, 33, 42, 48] }'), document.getText());
  assert.ok(document.isDirty, 'the edit is unsaved');
  assert.strictEqual(await provider.applyAsset(document, asset), false, 'the same asset again changes nothing');
  await vscode.commands.executeCommand('vscode.openWith', paletteUri, 'winasm65Graphics.palettes');
  await vscode.commands.executeCommand('undo');
  await waitFor('undo', () => document.getText() === before);
  await vscode.commands.executeCommand('redo');
  await waitFor('redo', () => document.getText() !== before);

  // Saving exports it through the designer.
  await document.save();
  const exported = path.join(folder.uri.fsPath, 'game.pal.asm');
  await waitFor('game.pal.asm', () => fs.existsSync(exported));
  assert.ok(fs.readFileSync(exported, 'utf8').length > 0);
  console.log('PASS palette edit, undo, redo, save and export');

  // A broken asset gets its problems on the line of the value at fault.
  const screenUri = file('title.screen.json');
  const screenDocument = await vscode.workspace.openTextDocument(screenUri);
  const screen = JSON.parse(screenDocument.getText());
  screen.attributes[20] = 7;
  await api.jsonEditors.get('winasm65Graphics.screen')!.applyAsset(screenDocument, screen);
  await screenDocument.save();
  const problems = await waitFor('the screen diagnostics', () => {
    const list = api.exporter.collection.get(screenUri);
    return list && list.length > 0 ? list : undefined;
  });
  assert.strictEqual(problems[0].code, '$.attributes[20]');
  const line = screenDocument.lineAt(problems[0].range.start.line).text.trim();
  assert.ok(line.startsWith('0, 0, 0, 0, 7,'), 'line ' + problems[0].range.start.line + ': ' + line);
  console.log('PASS screen diagnostics on line ' + problems[0].range.start.line);

  // The tile editor: an edit is undoable and saved as bytes.
  const chrUri = file('tiles.chr');
  const chr = api.tiles.documentFor(chrUri);
  assert.ok(chr, 'the tile set is open');
  assert.strictEqual(chr.bytes.length, 8192);
  await vscode.commands.executeCommand('vscode.openWith', chrUri, 'winasm65Graphics.tiles');
  const edited = chr.bytes.slice();
  edited[0] = 0xff;
  api.tiles.edit(chr, edited, 'test edit');
  await vscode.commands.executeCommand('workbench.action.files.save');
  await waitFor('tiles.chr saved', () => fs.readFileSync(chrUri.fsPath)[0] === 0xff);
  console.log('PASS tile set edit and save');
}
