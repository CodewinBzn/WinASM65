import { PaletteAsset } from '../shared/assets.js';
import { HostMessage } from '../shared/protocol.js';
import { GraphicsSystem, hex2 } from '../shared/system.js';
import { Banner, StatusBar, button, el, post, setInputValue, start, textInput, typing } from './common.js';
import { contrastOf, cssOf, masterRgb } from './render.js';

/**
 * The palette editor: the master palette, and the background then sprite
 * palettes of the file. Click a colour of a palette, then a colour of the
 * master palette to put it there; the next colour of the palette is then
 * selected, so a palette is set in four clicks.
 */

let system: GraphicsSystem | undefined;
let asset: PaletteAsset | undefined;
let selected = { palette: 0, color: 1 };

const app = document.getElementById('app')!;
const banner = new Banner();
const status = new StatusBar();
const nameInput = textInput('', 'name', value => edit('Rename the palettes', a => { a.name = value; }));
const prefixInput = textInput('', 'none', value => edit('Change the prefix', a => { a.prefix = value; }));
const palettesBox = el('div', { className: 'palettes' });
const masterBox = el('div', { className: 'master' });
const selectedInfo = el('div', { className: 'hint' });

function edit(label: string, change: (asset: PaletteAsset) => void): void {
  if (!asset) return;
  const next = JSON.parse(JSON.stringify(asset)) as PaletteAsset;
  change(next);
  asset = next;
  render();
  post({ type: 'edit', label, asset: next });
}

function build(): void {
  app.textContent = '';
  const toolbar = el('div', { className: 'toolbar' },
    el('label', { title: 'The name of the palette file, used in the exported labels' }, 'Name', nameInput),
    el('label', { title: 'Prepended to every exported label' }, 'Prefix', prefixInput),
    el('span', { className: 'sep' }),
    button('Share colour 0', 'Give every palette the colour 0 of the first background palette (the universal background colour)', shareColourZero),
    el('span', { className: 'sep' }),
    button('Open as Text', 'Open the .pal.json file in the text editor', () => post({ type: 'openAsText' }))
  );
  const body = el('div', { className: 'body palette-body' },
    el('div', { className: 'main' },
      el('div', { className: 'palette-layout' },
        el('section', {}, el('h3', { text: 'Palettes' }), palettesBox),
        el('section', {},
          el('h3', { text: 'Master palette' }),
          masterBox,
          selectedInfo,
          el('p', {
            className: 'hint',
            text: 'Colour 0 of every palette shows the universal background colour: the hardware draws colour 0 of the first background palette '
              + 'wherever a background pixel has colour 0, and sprites are transparent there.'
          })
        )
      )
    )
  );
  app.append(toolbar, banner.element, body, status.element);
  document.addEventListener('keydown', onKey);
}

function shareColourZero(): void {
  if (!asset || !asset.palettes[0]) return;
  const zero = asset.palettes[0].colors[0];
  edit('Share colour 0', a => a.palettes.forEach(p => { if (p.colors.length > 0) p.colors[0] = zero; }));
}

function onKey(event: KeyboardEvent): void {
  if (typing(event) || !asset || !system) return;
  const count = system.colorsPerPalette;
  const palettes = asset.palettes.length;
  const moves: Record<string, [number, number]> = { ArrowLeft: [0, -1], ArrowRight: [0, 1], ArrowUp: [-1, 0], ArrowDown: [1, 0] };
  const move = moves[event.key];
  if (move) {
    selected = {
      palette: (selected.palette + move[0] + palettes) % palettes,
      color: (selected.color + move[1] + count) % count
    };
    render();
    event.preventDefault();
  }
}

function setColour(index: number): void {
  if (!asset || !system) return;
  const { palette, color } = selected;
  if (!asset.palettes[palette]) return;
  edit('Set colour ' + color + ' of ' + (asset.palettes[palette].name || 'palette ' + palette) + ' to ' + hex2(index), a => {
    a.palettes[palette].colors[color] = index;
  });
  // On to the next colour of the palette, then of the next palette.
  const count = system.colorsPerPalette;
  if (color + 1 < count) {
    selected = { palette, color: color + 1 };
  } else if (palette + 1 < asset.palettes.length) {
    selected = { palette: palette + 1, color: 1 };
  }
  render();
}

function render(): void {
  if (!system || !asset) return;
  const sys = system;
  setInputValue(nameInput, asset.name ?? '');
  setInputValue(prefixInput, asset.prefix ?? '');

  // The palettes, grouped: background then sprites.
  palettesBox.textContent = '';
  const universal = asset.palettes[0]?.colors?.[0];
  asset.palettes.forEach((entry, p) => {
    if (p === 0 || p === sys.backgroundPaletteCount) {
      palettesBox.append(el('div', { className: 'group-title', text: p === 0 ? 'Background' : 'Sprites' }));
    }
    const sprite = p >= sys.backgroundPaletteCount;
    const local = sprite ? p - sys.backgroundPaletteCount : p;
    const nameField = textInput(entry.name ?? '', 'name', value => edit('Rename palette ' + p, a => { a.palettes[p].name = value; }));
    nameField.className = 'palette-name';
    const swatches = el('div', { className: 'swatches' });
    for (let c = 0; c < sys.colorsPerPalette; c++) {
      const index = entry.colors?.[c] ?? 0;
      const rgb = masterRgb(sys, index);
      const swatch = el('button', {
        type: 'button',
        className: 'swatch big' + (selected.palette === p && selected.color === c ? ' selected' : ''),
        title: (sprite ? 'Sprite' : 'Background') + ' palette ' + local + ', colour ' + c + ': ' + hex2(index)
          + (c === 0 ? (sprite ? ' (transparent for sprites)' : p === 0 ? ' (the universal background colour)' : ' (shown as the universal background colour)') : ''),
        onclick: () => { selected = { palette: p, color: c }; render(); }
      }, el('span', { className: 'tag', text: hex2(index).slice(1) }));
      swatch.style.background = cssOf(rgb);
      swatch.style.color = contrastOf(rgb);
      if (c === 0 && p > 0) {
        swatch.classList.add('shared');
        if (index !== universal) {
          swatch.append(el('span', { className: 'mark', text: '≠', title: 'Differs from the universal background colour ' + hex2(universal ?? 0) + ', which the hardware shows instead' }));
        }
      }
      swatches.append(swatch);
    }
    palettesBox.append(el('div', { className: 'palette-row' + (selected.palette === p ? ' current' : '') },
      el('span', { className: 'palette-index mono', text: String(local) }),
      nameField,
      swatches));
  });

  // The master palette, 16 to a row, the colour in use outlined.
  masterBox.textContent = '';
  const current = asset.palettes[selected.palette]?.colors?.[selected.color];
  sys.masterPalette.forEach((hex, index) => {
    const rgb = masterRgb(sys, index);
    const avoided = !sys.selectableColors.includes(index);
    const cell = el('button', {
      type: 'button',
      className: 'master-cell' + (index === current ? ' selected' : '') + (avoided ? ' avoided' : ''),
      title: hex2(index) + '  ' + hex + (avoided ? ' (repeats black or is unsafe, as $0D: better avoided)' : ''),
      onclick: () => setColour(index)
    }, el('span', { text: index.toString(16).toUpperCase().padStart(2, '0') }));
    cell.style.background = hex;
    cell.style.color = contrastOf(rgb);
    masterBox.append(cell);
  });
  masterBox.style.gridTemplateColumns = 'repeat(16, 34px)';

  const entry = asset.palettes[selected.palette];
  const sprite = selected.palette >= sys.backgroundPaletteCount;
  selectedInfo.textContent = entry
    ? 'Selected: ' + (sprite ? 'sprite' : 'background') + ' palette ' + (sprite ? selected.palette - sys.backgroundPaletteCount : selected.palette)
      + ' "' + (entry.name ?? '') + '", colour ' + selected.color + ' = ' + hex2(current ?? 0) + '. Click a colour to set it.'
    : '';
  status.info(asset.palettes.length + ' palettes · ' + sys.displayName);
}

start((message: HostMessage) => {
  switch (message.type) {
    case 'init':
      system = message.system;
      build();
      if (message.systemNote) status.show(message.systemNote, 'error');
      break;
    case 'asset':
      if (message.error || !message.asset) {
        banner.show(message.error ?? 'No asset.', 'error', { label: 'Open as Text', run: () => post({ type: 'openAsText' }) });
        return;
      }
      banner.hide();
      asset = message.asset as PaletteAsset;
      if (!Array.isArray(asset.palettes)) asset.palettes = [];
      asset.palettes.forEach(p => { if (!Array.isArray(p.colors)) p.colors = []; });
      if (selected.palette >= asset.palettes.length) selected = { palette: 0, color: 0 };
      render();
      break;
    case 'notice':
      status.show(message.text, message.level);
      break;
  }
});
