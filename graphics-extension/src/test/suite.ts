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
  for (const id of ['newPalette', 'newScreen', 'newMap', 'newTileSet', 'newMetasprites', 'exportAsset', 'exportFolder', 'importPng']) {
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
    ['sprites.msprite.json', 'winasm65Graphics.metasprites', 'metasprite'],
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

  // The metasprite editor's edit path: a tile moved and a frame added change the text, as one undoable edit.
  const spritesUri = file('sprites.msprite.json');
  const spritesDocument = await vscode.workspace.openTextDocument(spritesUri);
  const spritesBefore = spritesDocument.getText();
  const sprites = JSON.parse(spritesBefore);
  sprites.metasprites[0].tiles[0].x = 48;
  sprites.metasprites[0].tiles[0].flipH = true;
  sprites.animations[0].frames.push({ metasprite: 'meteor', duration: 12 });
  const spritesProvider = api.jsonEditors.get('winasm65Graphics.metasprites');
  assert.ok(spritesProvider, 'the metasprite editor is registered');
  assert.strictEqual(await spritesProvider.applyAsset(spritesDocument, sprites), true);
  const spritesText = spritesDocument.getText();
  assert.ok(spritesDocument.isDirty);
  const spritesAfter = JSON.parse(spritesText);
  assert.deepStrictEqual(spritesAfter.metasprites[0].tiles[0], { x: 48, y: 60, tile: 1, palette: 0, flipH: true, flipV: false, behind: false });
  assert.ok(spritesText.includes('{ "metasprite": "meteor", "duration": 12 }'), spritesText);
  await vscode.commands.executeCommand('vscode.openWith', spritesUri, 'winasm65Graphics.metasprites');
  await vscode.commands.executeCommand('undo');
  await waitFor('metasprite undo', () => spritesDocument.getText() === spritesBefore);
  console.log('PASS metasprite edit and undo');

  // Hover pictures: export the metasprites and palettes, then hover their labels in a source.
  const exportedSprites = await api.designer.export(spritesUri.fsPath);
  assert.ok(exportedSprites.written.some(f => f.endsWith('sprites.msprite.asm')), JSON.stringify(exportedSprites));
  await api.designer.export(paletteUri.fsPath);
  await api.designer.export(screenUri.fsPath).catch(() => undefined);
  api.hover.index.read(path.join(folder.uri.fsPath, 'sprites.msprite.asm'));
  api.hover.index.read(path.join(folder.uri.fsPath, 'game.pal.asm'));
  const source = await vscode.workspace.openTextDocument(file('main.asm'));
  await vscode.window.showTextDocument(source);
  assert.strictEqual(source.languageId, 'winasm65', 'main.asm is a WinASM65 source');
  const hoverAt = async (needle: string, offset: number): Promise<string> => {
    const line = source.getText().split('\n').findIndex(l => l.includes(needle));
    assert.ok(line >= 0, needle);
    const character = source.lineAt(line).text.indexOf(needle) + offset;
    const hovers = await vscode.commands.executeCommand<vscode.Hover[]>('vscode.executeHoverProvider', source.uri, new vscode.Position(line, character));
    return (hovers ?? []).flatMap(h => h.contents.map(c => typeof c === 'string' ? c : (c as vscode.MarkdownString).value)).join('\n');
  };
  const shipHover = await hoverAt('#<ship', 3);
  assert.ok(shipHover.includes('<img src="data:image/png;base64,'), shipHover.slice(0, 300));
  assert.ok(shipHover.includes('metasprite "ship"'), shipHover.slice(-300));
  const animationHover = await hoverAt('fly_anim', 2);
  assert.ok(animationHover.includes('data:image/png;base64,') && animationHover.includes('frame 0'), animationHover.slice(-300));
  const paletteHover = await hoverAt('palettes,x', 2);
  assert.ok(paletteHover.includes('data:image/png;base64,'), paletteHover.slice(-300));
  const incbinHover = await hoverAt('.incbin "tiles.chr"', 10);
  assert.ok(incbinHover.includes('data:image/png;base64,') && incbinHover.includes('tiles.chr'), incbinHover.slice(-300));
  const nothing = await hoverAt('sta $2007', 1);
  assert.ok(!nothing.includes('data:image/png'), 'an instruction has no picture');
  console.log('PASS hover pictures of generated labels and .incbin');

  // Import PNG: a picture of the title screen (drawn by the designer) back into assets.
  const picture = await api.designer.client.request<{ png: string; width: number; height: number }>('render', { path: screenUri.fsPath });
  assert.strictEqual(picture.width, 256);
  const pngPath = path.join(folder.uri.fsPath, 'picture.png');
  fs.writeFileSync(pngPath, Buffer.from(picture.png, 'base64'));
  const importFolder = path.join(folder.uri.fsPath, 'imported');
  const imported = await vscode.commands.executeCommand<{ written: string[]; statistics: { cells: number; newTiles: number; tileCount: number } }>(
    'winasm65Graphics.importPng', vscode.Uri.file(pngPath), { mode: 'background', folder: importFolder, name: 'picture', chr: null, overwrite: true, open: true });
  assert.ok(imported, 'the import ran');
  for (const name of ['picture.chr', 'picture.pal.json', 'picture.screen.json']) {
    assert.ok(fs.existsSync(path.join(importFolder, name)), name + ' written');
  }
  assert.strictEqual(imported.statistics.cells, 960);
  assert.ok(imported.statistics.newTiles > 1 && imported.statistics.newTiles === imported.statistics.tileCount, JSON.stringify(imported.statistics));
  const importedScreen = vscode.Uri.file(path.join(importFolder, 'picture.screen.json'));
  await waitFor('the imported screen opened', () => api.registry.ready.get(importedScreen.toString()) === 'screen');
  console.log('PASS import of a PNG: ' + JSON.stringify(imported.statistics));

  await runLive();
}

/**
 * The Live channel against a stand-in for the debug session (no emulator in
 * the tests): everything written once when turned on, then only what changed;
 * a space the emulator does not offer is reported, not written.
 */
async function runLive(): Promise<void> {
  const { LiveChannel } = await import('../live');
  const { encodePalettes } = await import('../shared/binary');
  const { NES_FALLBACK } = await import('../shared/system');
  const writes: Array<{ space: string; address: number; data: number[] }> = [];
  let available = true;
  const session = { id: 'fake', name: 'Fake NES', type: 'winasm65' } as unknown as vscode.DebugSession;
  const fake = {
    session,
    onDidChangeSession: () => new vscode.Disposable(() => undefined),
    space: async (_: vscode.DebugSession, name: string) => ({ space: { name, length: 32, available }, emulator: 'FakeEmu' }),
    write: async (_: vscode.DebugSession, space: string, address: number, data: Uint8Array) => { writes.push({ space, address, data: Array.from(data) }); }
  };
  const palettes = { palettes: [{ name: 'bg0', colors: [0x0f, 0x30, 0x16, 0x28] }] };
  const states: boolean[] = [];
  const notices: string[] = [];
  const channel = new LiveChannel(fake as never, 'the palettes', undefined,
    () => [{ space: 'PAL', address: 0, data: encodePalettes(NES_FALLBACK, palettes) }],
    state => states.push(state.on), text => notices.push(text));

  await channel.set(true);
  await waitFor('the first write', () => writes.length > 0);
  assert.deepStrictEqual(writes[0], { space: 'PAL', address: 0, data: Array.from(encodePalettes(NES_FALLBACK, palettes)) });
  assert.strictEqual(states[states.length - 1], true);

  palettes.palettes[0].colors[2] = 0x2a;
  channel.changed();
  await waitFor('the change written', () => writes.length > 1);
  assert.deepStrictEqual(writes[1], { space: 'PAL', address: 2, data: [0x2a] }, 'only the changed byte');

  await channel.set(false);
  palettes.palettes[0].colors[3] = 0x01;
  channel.changed();
  await new Promise(resolve => setTimeout(resolve, 150));
  assert.strictEqual(writes.length, 2, 'nothing written while off');

  available = false;
  await channel.set(true);
  assert.strictEqual(states[states.length - 1], false, 'stays off');
  assert.ok(notices.some(n => n.includes('FakeEmu') && n.includes('PAL')), notices.join(' | '));
  channel.dispose();
  console.log('PASS live channel: full write, then changed bytes only; unavailable space reported');
}
