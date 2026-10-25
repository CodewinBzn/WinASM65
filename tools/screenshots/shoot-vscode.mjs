// The VS Code screenshots of the documentation: the extension on the
// example game (example_star_catcher), from editing to a debug session on Mesen2.
//   node shoot-vscode.mjs [--mesen C:\path\to\Mesen.exe]

import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { cleanCopy, command, imageDir, launch, openFile, repo, shot } from './vscode.mjs';

const mesenArg = process.argv.indexOf('--mesen');
let mesen = mesenArg > 0 ? process.argv[mesenArg + 1] : findMesen();
if (mesen && powershell("@(Get-Process Mesen -ErrorAction SilentlyContinue).Count").trim() !== '0') {
  // Mesen2 runs as a single instance: our launch would be handed to that window.
  console.log('  Mesen2 is already open: close it to get the debug screenshots.');
  mesen = undefined;
}
const failures = [];

function powershell(script) {
  return execFileSync('powershell', ['-NoProfile', '-Command', script], { encoding: 'utf8' });
}

function findMesen() {
  // The emulator the examples are configured with, when it is installed here.
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

async function box(page, selector, margin = 0) {
  const b = await page.locator(selector).first().boundingBox();
  return { x: Math.max(0, b.x - margin), y: Math.max(0, b.y - margin), width: b.width + 2 * margin, height: b.height + 2 * margin };
}

// Chooses an entry of the quick pick that is open, once it lists it.
async function pick(page, text) {
  const input = page.locator('.quick-input-widget:not([style*="display: none"]) input');
  await input.waitFor({ timeout: 20000 });
  await page.locator('.quick-input-list .monaco-list-row', { hasText: text }).first().waitFor({ timeout: 20000 });
  await page.keyboard.type(text);
  await page.waitForTimeout(500);
  await page.keyboard.press('Enter');
  await page.waitForTimeout(800);
}

async function hoverAt(page, line, column) {
  await page.keyboard.press('Escape');
  await page.mouse.move(24, 600); // away from the text, so the hover stays
  await page.keyboard.press('Control+G');
  await page.keyboard.type(`${line}:${column}`);
  await page.keyboard.press('Enter');
  for (let i = 0; i < 14; i++) {
    await page.keyboard.press('Control+ArrowDown'); // the line near the top: room below for the card
  }
  await page.waitForTimeout(300);
  // The keyboard shortcut, not the palette: a click would leave the pointer on the text.
  await page.keyboard.press('Control+K');
  await page.keyboard.press('Control+I');
  // The editor's hover, not a tooltip of the workbench, once the server answered.
  await page.locator('.monaco-resizable-hover .monaco-hover:not(.hidden)', { hasText: '@' }).first().waitFor({ timeout: 15000 });
  await page.waitForTimeout(500);
  // A hover is at most a quarter of the editor high and scrolls: give it the
  // height of its contents so the whole card shows in one picture.
  await page.evaluate(() => {
    const content = document.querySelector('.monaco-resizable-hover .monaco-hover-content');
    const height = content.scrollHeight + 'px';
    for (const element of document.querySelectorAll('.monaco-resizable-hover, .monaco-resizable-hover *')) {
      if (element.classList.contains('slider') || element.classList.contains('scrollbar')) continue;
      if (element.style.maxHeight) element.style.maxHeight = height;
      if (element.style.height) element.style.height = height;
    }
  });
  await page.waitForTimeout(400);
}

const workspace = cleanCopy('example_star_catcher');
const { app, page } = await launch(workspace, {
  'editor.colorDecorators': false,
  'editor.hover.above': false,
  'editor.quickSuggestions': { other: 'off', comments: 'off', strings: 'off' },
  'editor.acceptSuggestionOnEnter': 'off',
  'breadcrumbs.enabled': false,
  'editor.autoIndent': 'none',
  ...(mesen ? { 'winasm65.emulatorPath': mesen } : {})
});
const editorPart = '.part.editor';

try {
  await step('editor', async () => {
    await openFile(page, 'game.asm', 334);
    await page.waitForTimeout(4000); // the language server's first assembly
    await shot(page, 'vscode-editor');
  });

  await step('hover on an instruction', async () => {
    await hoverAt(page, 338, 10);
    await shot(page, 'vscode-hover-instruction', await box(page, editorPart), true);
  });

  await step('hover on a name', async () => {
    await hoverAt(page, 338, 14);
    await shot(page, 'vscode-hover-symbol', await box(page, editorPart), true);
    await page.keyboard.press('Escape');
  });

  await step('context menu', async () => {
    const lines = await box(page, '.monaco-editor .view-lines');
    await page.mouse.click(lines.x + 300, lines.y + 200, { button: 'right' });
    const item = page.locator('.monaco-menu .action-label', { hasText: /^WinASM65$/ }).first();
    await item.hover();
    await page.waitForTimeout(800);
    await shot(page, 'vscode-context-menu');
    await page.keyboard.press('Escape');
    await page.keyboard.press('Escape');
  });

  await step('listing column', async () => {
    await command(page, 'WinASM65: Show Listing');
    await page.waitForTimeout(2500);
    await shot(page, 'vscode-listing', await box(page, editorPart));
    await command(page, 'WinASM65: Hide Listing');
  });

  await step('diagnostics', async () => {
    await page.keyboard.press('Control+G');
    await page.keyboard.type('336');
    await page.keyboard.press('Enter');
    await page.keyboard.press('Home');
    await page.keyboard.press('Home');
    for (const bad of ['LDX ($10),Y', 'LDA #(1+', 'BNE NOWHERE']) {
      await page.keyboard.type('        ' + bad);
      await page.keyboard.press('Enter');
    }
    await page.waitForTimeout(4000);
    await page.keyboard.press('Control+Shift+M');
    await page.waitForTimeout(1500);
    await shot(page, 'vscode-diagnostics');
    await command(page, 'File: Revert File');
    await page.keyboard.press('Control+J'); // close the panel
  });

  await step('build task', async () => {
    await command(page, 'Tasks: Run Task');
    await pick(page, 'winasm65');
    await pick(page, 'build');
    await page.waitForTimeout(8000);
    await shot(page, 'vscode-build');
    await page.keyboard.press('Control+J');
  });

  if (mesen) {
    await step('debug session', async () => {
      await openFile(page, 'game.asm', 134); // the first instruction of NMI
      await page.keyboard.press('F9');
      await page.keyboard.press('F5');
      await page.getByText(/Paused on breakpoint/i).first().waitFor({ timeout: 90000 });
      await page.waitForTimeout(4000); // variables and the RAM panel
      for (const scope of ['Registers', 'Flags']) {
        await page.locator('.debug-variables .monaco-list-row', { hasText: scope }).first().click();
        await page.waitForTimeout(800);
      }
      await shot(page, 'vscode-debug');

      const ram = page.locator('.editor-group-container', { has: page.locator('.tab', { hasText: 'Memory' }) }).first();
      if (await ram.count()) {
        await shot(page, 'vscode-ram', await ram.boundingBox());

        // Another memory of the machine: the palettes, read through the bridge's PAL space.
        const panel = page.frameLocator('iframe.webview').frameLocator('iframe#active-frame');
        const views = panel.locator('#view');
        const value = await views.locator('option', { hasText: 'Palettes' }).first().getAttribute('value');
        await views.selectOption(value);
        await page.waitForTimeout(1500);
        await shot(page, 'vscode-memory-palettes', await ram.boundingBox());
      }
      execFileSync('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
        path.join(import.meta.dirname, 'capture-window.ps1'), '-ProcessName', 'Mesen',
        '-Out', path.join(imageDir, 'mesen2.png')], { stdio: 'inherit' });

      await page.keyboard.press('F10');
      await page.waitForTimeout(1500);
      await page.keyboard.press('Shift+F5');
      await page.waitForTimeout(2000);
    });
  } else {
    console.log('  Mesen2 not found: the debug screenshots are skipped (--mesen <Mesen.exe>).');
  }
} finally {
  await app.close();
  // The emulator the debug session started on the copy, if it outlived the session.
  powershell(`Get-CimInstance Win32_Process -Filter "Name='Mesen.exe'" | Where-Object { $_.CommandLine -like '*${path.basename(path.dirname(workspace))}*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }`);
  fs.rmSync(path.dirname(workspace), { recursive: true, force: true });
}

if (failures.length > 0) {
  console.error(`${failures.length} screenshot(s) failed: ${failures.join(', ')}`);
  process.exit(1);
}
