// Screenshots of the graphics editors: VS Code with this extension (and the
// WinASM65 extension it depends on) on a temporary folder of sample assets,
// each editor opened in turn. Pictures go to a temporary folder (printed), never
// into docs/.
//   npm run compile && node scripts/preview.mjs [--out <folder>]
// Needs the Playwright of tools/screenshots (npm install there) and the cached
// VS Code of vscode-extension/.vscode-test (or VSCODE_EXECUTABLE).

import { execFileSync } from 'node:child_process';
import { createRequire } from 'node:module';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const here = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'));
const extension = path.resolve(here, '..');
const repo = path.resolve(extension, '..');
const require = createRequire(import.meta.url);
const { _electron: electron } = await import(pathToFileURL(path.join(repo, 'tools', 'screenshots', 'node_modules', 'playwright', 'index.mjs')).href);
const { writeSamples } = require(path.join(extension, 'out', 'test', 'samples.js'));

const outArg = process.argv.indexOf('--out');
const outDir = outArg > 0 ? path.resolve(process.argv[outArg + 1]) : fs.mkdtempSync(path.join(os.tmpdir(), 'winasm65-graphics-preview-'));
fs.mkdirSync(outDir, { recursive: true });

function findCode() {
  if (process.env.VSCODE_EXECUTABLE) return process.env.VSCODE_EXECUTABLE;
  const cache = path.join(repo, 'vscode-extension', '.vscode-test');
  const builds = fs.existsSync(cache) ? fs.readdirSync(cache).filter(d => d.startsWith('vscode-win32')).sort() : [];
  if (builds.length === 0) throw new Error('no VS Code in ' + cache + ': run the WinASM65 extension tests once, or set VSCODE_EXECUTABLE');
  return path.join(cache, builds[builds.length - 1], 'Code.exe');
}

async function launch(workspace) {
  const userData = fs.mkdtempSync(path.join(os.tmpdir(), 'winasm65-graphics-vscode-'));
  fs.mkdirSync(path.join(userData, 'User'), { recursive: true });
  fs.writeFileSync(path.join(userData, 'User', 'settings.json'), JSON.stringify({
    'workbench.colorTheme': process.env.THEME || 'Default Dark Modern',
    'workbench.startupEditor': 'none',
    'workbench.tips.enabled': false,
    'workbench.secondarySideBar.defaultVisibility': 'hidden',
    'chat.disableAIFeatures': true,
    'chat.commandCenter.enabled': false,
    'window.commandCenter': false,
    'window.restoreWindows': 'none',
    'security.workspace.trust.enabled': false,
    'telemetry.telemetryLevel': 'off',
    'update.mode': 'none',
    'extensions.ignoreRecommendations': true,
    'files.autoSave': 'off'
  }, null, 2));
  const env = { ...process.env };
  for (const key of Object.keys(env)) {
    if (key.startsWith('VSCODE_') || key.startsWith('ELECTRON_')) delete env[key];
  }
  const app = await electron.launch({
    executablePath: findCode(),
    env,
    args: [
      workspace,
      '--extensionDevelopmentPath=' + extension,
      '--extensionDevelopmentPath=' + path.join(repo, 'vscode-extension'),
      '--extensions-dir=' + path.join(userData, 'extensions'),
      '--user-data-dir=' + userData,
      '--skip-welcome', '--skip-release-notes', '--disable-workspace-trust', '--new-window'
    ]
  });
  const page = await app.firstWindow();
  await page.waitForSelector('.monaco-workbench', { timeout: 60000 });
  await app.evaluate(({ BrowserWindow }) => {
    const window = BrowserWindow.getAllWindows()[0];
    window.unmaximize();
    window.setContentSize(1400, 860);
    window.center();
  });
  await page.waitForTimeout(1500);
  return { app, page };
}

/** The frame of the webview showing the editor whose toolbar holds a text. */
async function editorFrame(page, marker) {
  for (let i = 0; i < 100; i++) {
    for (const frame of page.frames()) {
      try {
        if (await frame.locator('.toolbar', { hasText: marker }).count() > 0) return frame;
      } catch { /* a frame that went away */ }
    }
    await page.waitForTimeout(200);
  }
  throw new Error('no editor with "' + marker + '" in its toolbar');
}

async function open(page, name) {
  await page.keyboard.press('Control+P');
  await page.waitForSelector('.quick-input-widget:not([style*="display: none"]) input');
  await page.keyboard.type(name);
  const row = page.locator('.quick-input-list .monaco-list-row', { hasText: name }).first();
  await row.waitFor({ timeout: 20000 });
  await page.waitForTimeout(300);
  await page.keyboard.press('Enter');
}

async function shot(page, name) {
  await page.mouse.move(24, 700);
  await page.waitForTimeout(400);
  const file = path.join(outDir, name + '.png');
  await page.screenshot({ path: file });
  console.log('  ' + file);
}

const workspace = fs.mkdtempSync(path.join(os.tmpdir(), 'winasm65-graphics-samples-'));
writeSamples(workspace, repo);
console.log('samples in ' + workspace);
// The exported sources (the hover reads them) and a picture to import: the title screen, drawn by the designer.
const designerDll = path.join(repo, 'WinASM65.Designer', 'bin', 'Debug', 'net8.0', 'WinASM65.Designer.dll');
execFileSync('dotnet', [designerDll, 'export', workspace], { stdio: 'ignore' });
fs.mkdirSync(path.join(workspace, 'art'));
execFileSync('dotnet', [designerDll, 'render', path.join(workspace, 'title.screen.json'), '-o', path.join(workspace, 'art', 'title-art.png')], { stdio: 'ignore' });
const { app, page } = await launch(workspace);
try {
  // Close the side bar: the editors get the room.
  await page.keyboard.press('Control+B');

  await open(page, 'game.pal.json');
  const palette = await editorFrame(page, 'Share colour 0');
  await palette.locator('.swatch.big').nth(6).click();
  await palette.locator('.master-cell').nth(0x2a).click(); // an edit: the tab turns dirty
  await page.waitForTimeout(800);
  await shot(page, 'palette');

  await open(page, 'tiles.chr');
  const tiles = await editorFrame(page, 'Pencil');
  await page.waitForTimeout(800);
  // The palette file and the ship's palette, then the ship's tile.
  await tiles.locator('select').first().selectOption({ label: 'game.pal.json' });
  await page.waitForTimeout(600);
  await tiles.locator('select').nth(1).selectOption('4');
  await page.waitForTimeout(300);
  const sheet = tiles.locator('.sheet-side canvas');
  const box = await sheet.boundingBox();
  await sheet.click({ position: { x: box.width / 16 * 1.5, y: box.width / 16 * 0.5 } });
  await page.waitForTimeout(300);
  await shot(page, 'tiles');
  await tiles.locator('button', { hasText: '2×2' }).click();
  await page.waitForTimeout(400);
  // A line across the block.
  const editor = tiles.locator('.editor-host canvas');
  const ebox = await editor.boundingBox();
  await page.mouse.move(ebox.x + ebox.width * 0.05, ebox.y + ebox.height * 0.8);
  await page.mouse.down();
  await page.mouse.move(ebox.x + ebox.width * 0.95, ebox.y + ebox.height * 0.6, { steps: 8 });
  await page.mouse.up();
  await page.waitForTimeout(500);
  await shot(page, 'tiles-2x2');

  await open(page, 'title.screen.json');
  const screen = await editorFrame(page, 'Attribute');
  await page.waitForTimeout(800);
  const canvas = screen.locator('.main canvas');
  const cbox = await canvas.boundingBox();
  // Tile 'A' from the sheet, painted along a row.
  const ssheet = screen.locator('.side canvas');
  const sbox = await ssheet.boundingBox();
  const cell = sbox.width / 16;
  await ssheet.click({ position: { x: cell * 1.5, y: cell * 4.5 } });
  await page.mouse.move(cbox.x + cbox.width * 0.1, cbox.y + cbox.height * 0.75);
  await page.mouse.down();
  await page.mouse.move(cbox.x + cbox.width * 0.3, cbox.y + cbox.height * 0.75, { steps: 6 });
  await page.mouse.up();
  await canvas.hover({ position: { x: cbox.width * 0.45, y: cbox.height * 0.3 } });
  await page.waitForTimeout(300);
  await page.screenshot({ path: path.join(outDir, 'screen.png') });
  console.log('  ' + path.join(outDir, 'screen.png'));

  await open(page, 'level1.map.json');
  const map = await editorFrame(page, 'Resize');
  await page.waitForTimeout(800);
  // Metatile 2 painted on the map.
  const set = map.locator('.side canvas').nth(0);
  const setBox = await set.boundingBox();
  await set.click({ position: { x: setBox.width / 8 * 2.5, y: setBox.height / 2 } });
  const mcanvas = map.locator('.main canvas');
  const mbox = await mcanvas.boundingBox();
  await page.mouse.move(mbox.x + 40, mbox.y + 40);
  await page.mouse.down();
  await page.mouse.move(mbox.x + 200, mbox.y + 40, { steps: 6 });
  await page.mouse.up();
  await page.waitForTimeout(800);
  await shot(page, 'map');

  // The metasprite editor: a tile placed from the sheet, then the row of stars over the scanline limit.
  await open(page, 'sprites.msprite.json');
  const sprites = await editorFrame(page, 'Snap');
  await page.waitForTimeout(1000);
  await sprites.locator('.list-item', { hasText: 'ship_thrust' }).click();
  await page.waitForTimeout(300);
  const tileSheet = sprites.locator('.ms-right canvas').first();
  const tbox = await tileSheet.boundingBox();
  const tcell = tbox.width / 16;
  await tileSheet.click({ position: { x: tcell * 10.5, y: tcell * 0.5 } }); // the heart
  const drawing = sprites.locator('.ms-main canvas');
  const dbox = await drawing.boundingBox();
  const px = dbox.width / 128;
  await page.mouse.click(dbox.x + px * 84, dbox.y + px * 44);
  await page.mouse.move(dbox.x + px * 100, dbox.y + px * 100);
  await page.waitForTimeout(600);
  await shot(page, 'metasprite');
  await sprites.locator('.list-item', { hasText: 'star_row' }).click();
  await sprites.locator('.list-item', { hasText: 'spin' }).click();
  await sprites.locator('button', { hasText: 'Play' }).click();
  await page.waitForTimeout(700);
  await shot(page, 'metasprite-scanline');
  await sprites.locator('button', { hasText: 'Pause' }).click();

  // Import PNG from the screen editor's toolbar: the picture, the name, the tile set.
  await open(page, 'title.screen.json');
  const screenAgain = await editorFrame(page, 'Attribute');
  await page.waitForTimeout(500);
  await screenAgain.locator('button', { hasText: 'Import PNG' }).click();
  await page.waitForSelector('.quick-input-widget:not([style*="display: none"]) .quick-input-list .monaco-list-row');
  await page.waitForTimeout(600);
  await page.screenshot({ path: path.join(outDir, 'import-picture.png') });
  console.log('  ' + path.join(outDir, 'import-picture.png'));
  await page.keyboard.type('title-art');
  await page.waitForTimeout(400);
  await page.keyboard.press('Enter');
  await page.waitForTimeout(600);
  await page.keyboard.press('Control+A');
  await page.keyboard.type('title_art');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(800);
  await page.screenshot({ path: path.join(outDir, 'import-tiles.png') });
  console.log('  ' + path.join(outDir, 'import-tiles.png'));
  await page.keyboard.type('New tile set');
  await page.waitForTimeout(400);
  await page.keyboard.press('Enter');
  await editorFrame(page, 'Attribute');
  await page.waitForTimeout(2500);
  await page.screenshot({ path: path.join(outDir, 'import-done.png') });
  console.log('  ' + path.join(outDir, 'import-done.png'));

  // A picture on hover: the label of an exported metasprite in a source.
  await open(page, 'main.asm');
  await page.waitForTimeout(1500);
  await page.keyboard.press('Control+G');
  await page.keyboard.type('6');
  await page.keyboard.press('Enter');
  await page.keyboard.press('End');
  await page.keyboard.press('ArrowLeft');
  await page.keyboard.press('Control+K');
  await page.keyboard.press('Control+I');
  await page.waitForSelector('.monaco-hover img', { timeout: 20000 });
  await page.waitForTimeout(800);
  await page.screenshot({ path: path.join(outDir, 'hover.png') });
  console.log('  ' + path.join(outDir, 'hover.png'));
  await page.keyboard.press('Escape');
  await page.keyboard.press('Control+G');
  await page.keyboard.type('23');
  await page.keyboard.press('Enter');
  await page.keyboard.press('End');
  await page.keyboard.press('ArrowLeft');
  await page.keyboard.press('ArrowLeft');
  await page.keyboard.press('Control+K');
  await page.keyboard.press('Control+I');
  await page.waitForSelector('.monaco-hover img', { timeout: 20000 });
  await page.waitForTimeout(800);
  await page.screenshot({ path: path.join(outDir, 'hover-incbin.png') });
  console.log('  ' + path.join(outDir, 'hover-incbin.png'));
} finally {
  await app.close();
}
console.log('screenshots in ' + outDir);
