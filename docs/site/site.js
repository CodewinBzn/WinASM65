// WinASM65 documentation site: the parts every page shares. The page list lives
// here only, so a new page is added once.
(function () {
  const PAGES = [
    { group: 'Overview' },
    { href: 'index.html', title: 'WinASM65' },
    { href: 'getting-started.html', title: 'Getting started' },
    { href: 'example.html', title: 'Example game' },
    { group: 'Assembler' },
    { href: 'assembler.html', title: 'Assembly pipeline' },
    { href: 'syntax.html', title: 'Syntax reference' },
    { href: 'cpus.html', title: 'CPUs and emulation core' },
    { href: 'segments.html', title: 'Projects and segments' },
    { href: 'modules.html', title: 'Modules, linker, viewer' },
    { href: 'targets.html', title: 'Targets and formats' },
    { group: 'Debugging' },
    { href: 'monitor.html', title: 'Monitor' },
    { href: 'bridges.html', title: 'Emulator bridges' },
    { href: 'tooling.html', title: 'Tooling: DAP and LSP' },
    { href: 'vscode.html', title: 'VS Code extension' },
    { group: 'Graphics and AI' },
    { href: 'graphics.html', title: 'Graphics Designer' },
    { href: 'ai.html', title: 'Tools for AI assistants' },
    { group: 'Project' },
    { href: 'testing.html', title: 'Tests and tools' }
  ];

  const here = location.pathname.split('/').pop() || 'index.html';

  // The theme: the system's, unless the reader chose one.
  function storedTheme() {
    try { return localStorage.getItem('winasm65-theme'); } catch (e) { return null; }
  }
  const theme = storedTheme();
  if (theme) document.documentElement.setAttribute('data-theme', theme);

  function toggleTheme() {
    const current = document.documentElement.getAttribute('data-theme')
      || (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
    const next = current === 'dark' ? 'light' : 'dark';
    document.documentElement.setAttribute('data-theme', next);
    try { localStorage.setItem('winasm65-theme', next); } catch (e) { /* private window */ }
  }

  function siteNav(nav) {
    let html = '';
    let open = false;
    for (const page of PAGES) {
      if (page.group) {
        if (open) html += '</ul>';
        html += '<p>' + page.group + '</p><ul>';
        open = true;
      } else {
        const current = page.href === here ? ' aria-current="page"' : '';
        html += '<li><a href="' + page.href + '"' + current + '>' + page.title + '</a></li>';
      }
    }
    nav.innerHTML = html + (open ? '</ul>' : '');
  }

  // The page's sections, from its h2 headings, and the one being read.
  function toc(aside) {
    const headings = Array.from(document.querySelectorAll('main h2[id]'));
    if (headings.length < 2) { aside.remove(); return; }
    aside.innerHTML = '<p>On this page</p><ul>' + headings.map(h =>
      '<li><a href="#' + h.id + '">' + h.textContent + '</a></li>').join('') + '</ul>';
    const links = Array.from(aside.querySelectorAll('a'));
    const observer = new IntersectionObserver(entries => {
      for (const entry of entries) {
        if (entry.isIntersecting) {
          links.forEach(a => a.classList.toggle('active', a.getAttribute('href') === '#' + entry.target.id));
        }
      }
    }, { rootMargin: '-80px 0px -70% 0px' });
    headings.forEach(h => observer.observe(h));
  }

  // Previous and next page, in the order of the navigation.
  function pager(main) {
    const pages = PAGES.filter(p => p.href);
    const index = pages.findIndex(p => p.href === here);
    if (index < 0) return;
    const prev = pages[index - 1];
    const next = pages[index + 1];
    const div = document.createElement('div');
    div.className = 'pager';
    div.innerHTML = (prev ? '<a class="card prev" href="' + prev.href + '"><small>Previous</small>' + prev.title + '</a>' : '<span></span>')
      + (next ? '<a class="card next" href="' + next.href + '"><small>Next</small>' + next.title + '</a>' : '<span></span>');
    const footer = main.querySelector('footer');
    main.insertBefore(div, footer);
  }

  document.addEventListener('DOMContentLoaded', () => {
    const top = document.createElement('div');
    top.className = 'topbar';
    top.innerHTML = '<button class="menu-toggle" type="button" aria-label="Pages">☰</button>'
      + '<a class="brand" href="index.html">WinASM<span>65</span> documentation</a>'
      + '<span class="spacer"></span><button class="theme-toggle" type="button">Theme</button>';
    document.body.insertBefore(top, document.body.firstChild);
    top.querySelector('.theme-toggle').addEventListener('click', toggleTheme);
    top.querySelector('.menu-toggle').addEventListener('click', () => document.body.classList.toggle('menu-open'));

    const nav = document.querySelector('.site-nav');
    if (nav) siteNav(nav);
    const aside = document.querySelector('.toc');
    if (aside) toc(aside);
    const main = document.querySelector('main');
    if (main) pager(main);
  });
})();
