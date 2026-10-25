// Starts the VS Code build that @vscode/test-electron keeps in its cache, with the
// extension under development, on a clean copy of an example, and drives it with
// Playwright. Shared by shoot-vscode.mjs.

import { _electron as electron } from 'playwright';
import { downloadAndUnzipVSCode } from '@vscode/test-electron';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

export const repo = path.resolve(import.meta.dirname, '..', '..');
export const imageDir = path.join(repo, 'docs', 'site', 'img');

/** A copy of an example as committed, so pending edits in the work tree never show. */
export function cleanCopy(example) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'winasm65-shots-'));
  const archive = path.join(root, 'example.tar');
  execFileSync('git', ['-C', repo, 'archive', '-o', archive, 'HEAD', example]);
  execFileSync('tar', ['-xf', 'example.tar'], { cwd: root });
  fs.rmSync(archive);
  return path.join(root, example);
}

export async function launch(workspace, settings = {}, extensions = ['vscode-extension']) {
  const executablePath = process.env.VSCODE_EXECUTABLE
    || await downloadAndUnzipVSCode({ cachePath: path.join(repo, 'vscode-extension', '.vscode-test') });
  const userData = fs.mkdtempSync(path.join(os.tmpdir(), 'winasm65-vscode-'));
  fs.mkdirSync(path.join(userData, 'User'), { recursive: true });
  fs.writeFileSync(path.join(userData, 'User', 'settings.json'), JSON.stringify({
    'workbench.colorTheme': 'Default Dark Modern',
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
    'editor.fontSize': 14,
    'editor.minimap.enabled': false,
    'editor.hover.delay': 100,
    'files.autoSave': 'off',
    ...settings
  }, null, 2));

  const env = { ...process.env };
  for (const key of Object.keys(env)) {
    if (key.startsWith('VSCODE_') || key.startsWith('ELECTRON_')) {
      delete env[key];
    }
  }

  const app = await electron.launch({
    executablePath,
    env,
    args: [
      workspace,
      ...extensions.map(e => '--extensionDevelopmentPath=' + path.join(repo, e)),
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
  // Closes VS Code and deletes its throwaway profile (some 15 MB a run).
  const close = async () => {
    await app.close();
    fs.rmSync(userData, { recursive: true, force: true, maxRetries: 10, retryDelay: 500 });
  };
  return { app, page, close };
}

/** Runs a command by its title from the command palette. */
export async function command(page, title) {
  await page.keyboard.press('Control+Shift+P');
  await page.waitForSelector('.quick-input-widget:not([style*="display: none"]) input');
  await page.keyboard.type(title);
  // The row whose label is exactly the title: "Tasks: Run Task" also matches
  // "Tasks: Restart Running Task".
  const label = page.locator('.quick-input-list .monaco-list-row .label-name')
    .filter({ hasText: new RegExp('^' + title.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '$') }).first();
  await label.waitFor({ timeout: 10000 });
  await label.click();
  // Off the editor: a pointer left over the text would close the next hover.
  await page.mouse.move(24, 600);
  await page.waitForTimeout(600);
}

export async function openFile(page, name, line) {
  await page.keyboard.press('Control+P');
  await page.waitForSelector('.quick-input-widget:not([style*="display: none"]) input');
  await page.keyboard.type(name);
  // The file search finishes after the first keystrokes: open the row it lists.
  const row = page.locator('.quick-input-list .monaco-list-row', { hasText: name }).first();
  await row.waitFor({ timeout: 20000 });
  await page.waitForTimeout(300);
  await row.click();
  await page.waitForSelector('.monaco-editor .view-lines');
  if (line) {
    await page.keyboard.press('Control+G');
    await page.keyboard.type(String(line));
    await page.keyboard.press('Enter');
  }
  await page.waitForTimeout(600);
}

export async function shot(page, name, clip, keepMouse = false) {
  fs.mkdirSync(imageDir, { recursive: true });
  const file = path.join(imageDir, name + '.png');
  if (!keepMouse) {
    await page.mouse.move(24, 600); // the empty activity bar: no tooltip
    await page.waitForTimeout(300);
  }
  await page.screenshot({ path: file, clip });
  console.log('  ' + path.relative(repo, file));
}
