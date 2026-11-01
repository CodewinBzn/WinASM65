// The browser screenshots of the documentation: the module viewer's report on
// two linked modules (samples/lib.asm, samples/app.asm).
//   node shoot-pages.mjs

import { chromium } from 'playwright';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const repo = path.resolve(import.meta.dirname, '..', '..');
const images = path.join(repo, 'docs', 'site', 'img');
const assembler = path.join(repo, 'WinASM65', 'bin', 'Release', 'net8.0', 'WinASM65.exe');

execFileSync('dotnet', ['build', path.join(repo, 'WinASM65', 'WinASM65.csproj'), '-c', 'Release', '-v', 'q', '-nologo'], { stdio: 'ignore' });

const work = fs.mkdtempSync(path.join(os.tmpdir(), 'winasm65-pages-'));
for (const file of ['lib.asm', 'app.asm']) {
  fs.copyFileSync(path.join(import.meta.dirname, 'samples', file), path.join(work, file));
}
const run = (...args) => execFileSync(assembler, args, { cwd: work, encoding: 'utf8' });
run('-f', 'lib.asm', '-t', 'c64', '-format', 'w65', '-o', 'lib.w65');
run('-f', 'app.asm', '-t', 'c64', '-format', 'w65', '-o', 'app.w65');
run('archive', 'lib.w65', 'app.w65', '-o', 'demo.w65a');
run('view', '-f', 'demo.w65a', '-o', 'demo.html');

const browser = await chromium.launch();
try {
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
  await page.goto(pathToFileURL(path.join(work, 'demo.html')).href);
  await page.waitForTimeout(500);
  fs.mkdirSync(images, { recursive: true });
  for (const [name, fullPage] of [['module-viewer', false], ['module-viewer-full', true]]) {
    const file = path.join(images, name + '.png');
    await page.screenshot({ path: file, fullPage });
    console.log('  ' + path.relative(repo, file));
  }
} finally {
  await browser.close();
  fs.rmSync(work, { recursive: true, force: true });
}
