// Opens every page of the documentation site in Chromium and fails on a broken
// internal link or anchor, a missing image, or a script error.
//   node check-site.mjs

import { chromium } from 'playwright';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const site = path.resolve(import.meta.dirname, '..', '..', 'docs', 'site');
const pages = fs.readdirSync(site).filter(f => f.endsWith('.html'));
const problems = new Set();

const browser = await chromium.launch();
try {
  const page = await browser.newPage({ viewport: { width: 1400, height: 900 } });
  for (const name of pages) {
    const errors = [];
    page.removeAllListeners('pageerror');
    page.removeAllListeners('console');
    page.on('pageerror', e => errors.push(e.message));
    page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
    await page.goto(pathToFileURL(path.join(site, name)).href);
    await page.waitForTimeout(200);
    errors.forEach(e => problems.add(`${name}: script error: ${e}`));

    const report = await page.evaluate(() => ({
      links: Array.from(document.querySelectorAll('a[href]')).map(a => a.href),
      images: Array.from(document.images).map(i => ({ src: i.src, ok: i.complete && i.naturalWidth > 0 })),
      ids: Array.from(document.querySelectorAll('[id]')).map(e => e.id),
      nav: document.querySelectorAll('.site-nav a').length
    }));
    if (report.nav === 0) problems.add(`${name}: the site navigation is empty`);
    report.images.filter(i => !i.ok).forEach(i => problems.add(`${name}: image not loaded: ${i.src}`));

    for (const href of report.links) {
      if (!href.startsWith('file:')) continue; // external links are not checked
      const url = new URL(href);
      const file = fileURLToPath(url);
      if (!fs.existsSync(file)) {
        problems.add(`${name}: broken link: ${path.relative(site, file)}`);
        continue;
      }
      if (url.hash && file.endsWith('.html')) {
        const html = fs.readFileSync(file, 'utf8');
        const id = decodeURIComponent(url.hash.slice(1));
        if (!html.includes(`id="${id}"`) && !html.includes(`id='${id}'`)) {
          problems.add(`${name}: missing anchor: ${path.basename(file)}${url.hash}`);
        }
      }
    }
    console.log(`  ${name}: ${report.links.length} links, ${report.images.length} images`);
  }
} finally {
  await browser.close();
}

if (problems.size > 0) {
  console.error([...problems].join('\n'));
  console.error(`${problems.size} problem(s)`);
  process.exit(1);
}
console.log(`${pages.length} pages OK`);
