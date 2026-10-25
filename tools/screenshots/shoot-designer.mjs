// The screenshots of the WinASM65 Graphics Designer (docs/site/graphics.html):
// each editor on the example game's assets, a picture in a hover, and the live
// preview, a palette edited while the game runs in Mesen2.
//   node shoot-designer.mjs [--mesen C:\path\to\Mesen.exe]
// The Debug builds of WinASM65.Tooling and WinASM65.Designer are used (the
// Designer is built here).

import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { cleanCopy, imageDir, launch, openFile, repo, shot } from './vscode.mjs';

const mesenArg = process.argv.indexOf('--mesen');
let mesen = mesenArg > 0 ? process.argv[mesenArg + 1] : findMesen();
if (mesen && powershell("@(Get-Process Mesen -ErrorAction SilentlyContinue).Count").trim() !== '0') {
  console.log('  Mesen2 is already open: close it to get the live preview screenshots.');
  mesen = undefined;
}
const failures = [];

function powershell(script) {
  return execFileSync('powershell', ['-NoProfile', '-Command', script], { encoding: 'utf8' });
}

function findMesen() {
  for (const config of ['example_nes_game/config.json', 'example_star_catcher/config.json']) {
    try {
      const text = fs.readFileSync(path.join(repo, config), 'utf8');
      const match = /"Path"\s*:\s*"([^"]+Mesen[^"]*\.exe)"/i.exec(text);
      if (match && fs.existsSync(match[1].replace(/\\\\/g, '\\'))) {
        return match[1].replace(/\\\\/g, '\\');
      }
    } catch { /* not configured */ }
  }
  return undefined;
}

async function step(name, body) {
  try {
    await body();
  } catch (error) {
    failures.push(name);
    console.error(`  FAILED ${name}: ${error.message.split('\n')[0]}`);
    await shot(page, 'failed-' + name.replace(/ /g, '-')).catch(() => {});
  }
}

async function box(selector) {
  return await page.locator(selector).first().boundingBox();
}

/** The designer's page inside the webview of the active editor. */
function designer() {
  return page.frameLocator('iframe.webview >> visible=true').frameLocator('iframe#active-frame');
}

async function openAsset(name, ready) {
  await openFileByName(name);
  await designer().locator(ready).first().waitFor({ timeout: 30000 });
  await page.waitForTimeout(1500);
}

async function openFileByName(name) {
  await page.keyboard.press('Control+P');
  await page.waitForSelector('.quick-input-widget:not([style*="display: none"]) input');
  await page.keyboard.type(name);
  const row = page.locator('.quick-input-list .monaco-list-row', { hasText: name }).first();
  await row.waitFor({ timeout: 20000 });
  await page.waitForTimeout(300);
  await row.click();
}

for (const project of ['WinASM65.Designer']) { // the Tooling build may be in use by an open VS Code
  execFileSync('dotnet', ['build', path.join(repo, project, project + '.csproj'), '-v', 'q', '-nologo'], { stdio: 'inherit' });
}

const workspace = cleanCopy('example_star_catcher');
const { page, close } = await launch(workspace, {
  'editor.hover.above': false,
  'breadcrumbs.enabled': false,
  'winasm65Graphics.exportOnSave': false,
  ...(mesen ? { 'winasm65.emulatorPath': mesen } : {})
}, ['vscode-extension', 'graphics-extension']);
const editorPart = '.part.editor';

try {
  await step('palettes', async () => {
    await openAsset('game.pal.json', 'button.swatch.big');
    await shot(page, 'designer-palettes', await box(editorPart));
  });

  await step('tiles', async () => {
    await openAsset('tiles.chr', 'canvas');
    // The ship, two tiles from $01, in its own colours: the game's palettes, sprite palette 0.
    const selects = designer().locator('.toolbar select');
    await selects.nth(0).selectOption({ index: 1 });
    await page.waitForTimeout(800);
    await selects.nth(1).selectOption({ index: 4 });
    await designer().locator('button', { hasText: /^2×2$/ }).click();
    const sheet = await designer().locator('canvas').first().boundingBox();
    await page.mouse.click(sheet.x + 24 + 12, sheet.y + 12);
    await page.waitForTimeout(800);
    await shot(page, 'designer-tiles', await box(editorPart));
  });

  await step('metasprites', async () => {
    await openAsset('objects.msprite.json', 'canvas');
    await shot(page, 'designer-metasprites', await box(editorPart));
  });

  await step('screen', async () => {
    await openAsset('title.screen.json', 'canvas');
    await shot(page, 'designer-screen', await box(editorPart));
  });

  await step('map', async () => {
    await openAsset('play.map.json', 'canvas');
    await shot(page, 'designer-map', await box(editorPart));
  });

  await step('hover picture', async () => {
    await openFile(page, 'game.asm');
    await page.waitForTimeout(3000);
    const line = fs.readFileSync(path.join(workspace, 'src', 'game.asm'), 'utf8').split(/\r?\n/)
      .findIndex(text => /lda #SPR_MS_SHIP/.test(text)) + 1;
    await page.keyboard.press('Control+G');
    await page.keyboard.type(`${line}:16`);
    await page.keyboard.press('Enter');
    for (let i = 0; i < 14; i++) {
      await page.keyboard.press('Control+ArrowDown');
    }
    await page.mouse.move(24, 600);
    await page.keyboard.press('Control+K');
    await page.keyboard.press('Control+I');
    await page.locator('.monaco-resizable-hover .monaco-hover:not(.hidden) img').first().waitFor({ timeout: 20000 });
    await page.waitForTimeout(800);
    await shot(page, 'designer-hover', await box(editorPart), true);
    await page.keyboard.press('Escape');
  });

  if (mesen) {
    await step('live preview', async () => {
      await openFile(page, 'game.asm');
      await page.keyboard.press('F5');
      await page.locator('.debug-toolbar').first().waitFor({ timeout: 90000 });
      await page.waitForTimeout(8000); // the game on its title screen
      const before = path.join(imageDir, 'designer-live-before.png');
      capture(before);

      await openAsset('game.pal.json', 'button.swatch.big');
      const live = designer().locator('button.live-toggle');
      await live.click();
      await page.waitForTimeout(1500);
      // The backdrop: background palette 0, colour 0, black ($0F) becomes dark blue ($02).
      await designer().locator('button.swatch.big[title^="Background palette 0, colour 0"]').click();
      await designer().locator('button.master-cell[title^="$02"]').click();
      await page.waitForTimeout(2500);
      await shot(page, 'designer-live', await box(editorPart));
      capture(path.join(imageDir, 'designer-live-mesen2.png'));

      await page.keyboard.press('Shift+F5');
      await page.waitForTimeout(2000);
      fs.rmSync(before, { force: true });
    });
  } else {
    console.log('  Mesen2 not found: the live preview screenshots are skipped (--mesen <Mesen.exe>).');
  }
} finally {
  await close();
  powershell(`Get-CimInstance Win32_Process -Filter "Name='Mesen.exe'" | Where-Object { $_.CommandLine -like '*${path.basename(path.dirname(workspace))}*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }`);
  fs.rmSync(path.dirname(workspace), { recursive: true, force: true });
}

function capture(file) {
  execFileSync('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
    path.join(import.meta.dirname, 'capture-window.ps1'), '-ProcessName', 'Mesen', '-Out', file], { stdio: 'inherit' });
}

if (failures.length > 0) {
  console.error(`${failures.length} screenshot(s) failed: ${failures.join(', ')}`);
  process.exit(1);
}
