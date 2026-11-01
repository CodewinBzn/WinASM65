import { PaletteAsset } from '../shared/assets.js';
import { decodeTile, floodFill, flipH, flipV, linePoints, rectanglePoints, rotateClockwise, tileCount, withTileCount, writeTile } from '../shared/chr.js';
import { HostMessage } from '../shared/protocol.js';
import { GraphicsSystem, colorsPerTile, hex2 } from '../shared/system.js';
import {
  Banner, Choice, LiveToggle, StatusBar, button, checkbox, clamp, el, fillSelect, loadState, post, saveState, selectBox, separator, start, typing
} from './common.js';
import { Rgb, TileCache, TileSheet, ZoomCanvas, contrastOf, cssOf, drawPixels, grid, onPointer, paletteColours, sheetRect } from './render.js';

/**
 * The tile set editor. The sheet on the left shows every tile, 16 to a row;
 * the tile selected there (or the 2x2 block it starts) is drawn big on the
 * right. A stroke is drawn on a copy of the block and written into the tile
 * data when the mouse is released: one stroke, one undo step.
 */

type Tool = 'pencil' | 'fill' | 'line' | 'rect' | 'picker';

const state = loadState({
  tool: 'pencil' as Tool,
  colour: 1,
  view: 1 as 1 | 2,
  sheetZoom: 3,
  grid: true,
  palettePath: '',
  paletteIndex: 0,
  selection: { from: 0, to: 0 },
  anchor: 0
});

let system: GraphicsSystem | undefined;
let data: Uint8Array = new Uint8Array(0);
let cache: TileCache | undefined;
let paletteFile: PaletteAsset | undefined;
let paletteFiles: Array<{ path: string; label: string }> = [];
let colours: Rgb[] = [];
let clipboard: { columns: number; rows: number; tiles: Uint8Array[] } | undefined;

/** The block being drawn: tiles of the editor, their pixels while a stroke is in progress. */
let stroke: { base: Uint8Array; pixels: Uint8Array; start: { x: number; y: number }; last: { x: number; y: number }; colour: number } | undefined;

const app = document.getElementById('app')!;
const banner = new Banner();
const status = new StatusBar();
const live = new LiveToggle();
let sheet: TileSheet;
let editor: ZoomCanvas;
let tools: Choice<Tool>;
let views: Choice<number>;
const colourBox = el('span', { className: 'group colours' });
const paletteSelect = el('select', { title: 'The palette file the tiles are shown in (tiles hold colour numbers, not colours)' });
const paletteIndexSelect = el('select', { title: 'Which palette of the file' });
const editorHost = el('div', { className: 'main editor-host' });

function save(): void {
  saveState(state);
}

/** The top-left tile of the editor: the first selected tile. */
function editTile(): number {
  return Math.min(state.selection.from, state.selection.to);
}

/** The editor's block: its tiles (sheet indexes; -1 beyond the data) and its size in pixels. */
function block(): { tiles: number[]; size: number; width: number; height: number } {
  const sys = system!;
  const first = editTile();
  const n = state.view;
  const tiles: number[] = [];
  for (let r = 0; r < n; r++) {
    for (let c = 0; c < n; c++) {
      const column = (first % TileSheet.COLUMNS) + c;
      const index = first + r * TileSheet.COLUMNS + c;
      tiles.push(column < TileSheet.COLUMNS && index < (cache?.count ?? 0) ? index : -1);
    }
  }
  return { tiles, size: n, width: n * sys.tileWidth, height: n * sys.tileHeight };
}

/** The pixels of the editor's block, row-major. */
function blockPixels(): Uint8Array {
  const sys = system!;
  const b = block();
  const pixels = new Uint8Array(b.width * b.height);
  b.tiles.forEach((tile, i) => {
    if (tile < 0) return;
    const tp = cache!.pixels(tile);
    const ox = (i % b.size) * sys.tileWidth;
    const oy = Math.floor(i / b.size) * sys.tileHeight;
    for (let y = 0; y < sys.tileHeight; y++) {
      for (let x = 0; x < sys.tileWidth; x++) {
        pixels[(oy + y) * b.width + ox + x] = tp[y * sys.tileWidth + x];
      }
    }
  });
  return pixels;
}

/** Writes block pixels into a copy of the data; the copy. */
function writeBlock(pixels: Uint8Array): Uint8Array {
  const sys = system!;
  const b = block();
  const next = data.slice();
  b.tiles.forEach((tile, i) => {
    if (tile < 0) return;
    const ox = (i % b.size) * sys.tileWidth;
    const oy = Math.floor(i / b.size) * sys.tileHeight;
    const tp = new Uint8Array(sys.tileWidth * sys.tileHeight);
    for (let y = 0; y < sys.tileHeight; y++) {
      for (let x = 0; x < sys.tileWidth; x++) {
        tp[y * sys.tileWidth + x] = pixels[(oy + y) * b.width + ox + x];
      }
    }
    writeTile(sys, next, tile, tp);
  });
  return next;
}

function commit(next: Uint8Array, label: string): void {
  data = next;
  cache = new TileCache(system!, data);
  sheet.cache = cache;
  render();
  post({ type: 'chrEdit', label, data: Array.from(next) });
}

function build(): void {
  const sys = system!;
  app.textContent = '';
  tools = new Choice<Tool>([
    { value: 'pencil', label: 'Pencil', title: 'Draw pixels (B). Right button: pick a colour' },
    { value: 'fill', label: 'Fill', title: 'Fill an area of one colour, within the tile (G)' },
    { value: 'line', label: 'Line', title: 'Draw a line (L)' },
    { value: 'rect', label: 'Rectangle', title: 'Draw a rectangle (R); Shift: filled' },
    { value: 'picker', label: 'Picker', title: 'Pick a colour from the tile (I)' }
  ], state.tool, tool => { state.tool = tool; save(); });

  views = new Choice<number>([
    { value: 1, label: '1×1', title: 'Edit one tile' },
    { value: 2, label: '2×2', title: 'Edit the block of 2x2 tiles the selected tile starts (as a metatile or a 16x16 sprite)' }
  ], state.view, view => { state.view = view as 1 | 2; save(); render(); });

  const zoom = selectBox([2, 3, 4, 5, 6].map(z => ({ value: z, label: z + '×' })), state.sheetZoom, z => {
    state.sheetZoom = z;
    sheet.surface.setZoom(z);
    save();
    render();
  }, 'Zoom of the tile sheet');

  paletteSelect.addEventListener('change', () => {
    state.palettePath = paletteSelect.value;
    save();
    post({ type: 'choosePalette', path: state.palettePath || undefined });
  });
  paletteIndexSelect.addEventListener('change', () => {
    state.paletteIndex = Number(paletteIndexSelect.value);
    save();
    updateColours();
    render();
  });

  const toolbar = el('div', { className: 'toolbar' },
    tools.element, separator(),
    colourBox, separator(),
    el('label', {}, 'Palette', paletteSelect, paletteIndexSelect), separator(),
    views.element,
    el('label', {}, 'Sheet', zoom),
    checkbox('Grid', 'Show the pixel grid in the editor', state.grid, on => { state.grid = on; save(); render(); }), separator(),
    button('Copy', 'Copy the selected tiles (Ctrl+C)', copy),
    button('Paste', 'Paste at the selected tile (Ctrl+V)', paste),
    button('Flip H', 'Mirror the selected tiles left to right (H)', () => transform('Flip horizontally', 'h')),
    button('Flip V', 'Mirror the selected tiles top to bottom (V)', () => transform('Flip vertically', 'v')),
    button('Rotate', 'Turn the selected tiles a quarter clockwise (a square selection turns as a whole)', () => transform('Rotate', 'r')),
    button('Clear', 'Set the selected tiles to colour 0 (Delete)', () => transform('Clear', 'c')), separator(),
    button('Import PNG…', 'Make tiles, palettes and a screen or a metasprite from a picture, appending the tiles to this tile set', () => post({ type: 'importPng' })),
    live.element
  );

  sheet = new TileSheet(sys, state.sheetZoom);
  sheet.minRows = 1;
  onPointer(sheet.surface, {
    down: (p, event) => {
      const tile = sheet.tileAt(p);
      if (tile === undefined) return;
      if (event.shiftKey) {
        state.selection = { from: state.anchor, to: tile };
      } else {
        state.anchor = tile;
        state.selection = { from: tile, to: tile };
      }
      save();
      render();
    },
    drag: p => {
      const tile = sheet.tileAt(p);
      if (tile === undefined) return;
      state.selection = { from: state.anchor, to: tile };
      render();
    },
    up: () => save(),
    hover: p => {
      const tile = p ? sheet.tileAt(p) : undefined;
      sheet.hovered = tile;
      sheet.surface.present();
      status.hover(tile === undefined ? '' : 'tile ' + hex2(tile) + ' (' + tile + ')');
    }
  });

  editor = new ZoomCanvas(sys.tileWidth, sys.tileHeight, drawEditorOverlay);
  editor.frame.classList.add('checker');
  onPointer(editor, { down: strokeDown, drag: strokeMove, up: strokeUp, hover: editorHover });
  editorHost.textContent = '';
  editorHost.append(editor.frame);

  const side = el('div', { className: 'side sheet-side' },
    el('div', { className: 'row spread' }, el('h3', { text: 'Tiles' }),
      button('+16 tiles', 'Add a row of blank tiles at the end of the tile set', addRow)),
    sheet.surface.frame);
  const body = el('div', { className: 'body' }, side, editorHost);
  app.append(toolbar, banner.element, body, status.element);

  new ResizeObserver(() => fitEditor()).observe(editorHost);
  document.addEventListener('keydown', onKey);
  updatePaletteControls();
}

function fitEditor(): void {
  if (!system) return;
  const b = block();
  const room = Math.min(editorHost.clientWidth - 32, editorHost.clientHeight - 32);
  const zoom = clamp(Math.floor(room / Math.max(b.width, b.height)), 4, 64);
  if (editor.width !== b.width || editor.height !== b.height) {
    editor.resize(b.width, b.height);
  }
  if (editor.zoom !== zoom) {
    editor.setZoom(zoom);
  }
}

function updateColours(): void {
  if (!system) return;
  colours = paletteColours(system, paletteFile, state.paletteIndex);
  colourBox.textContent = '';
  for (let c = 0; c < colorsPerTile(system); c++) {
    const rgb = colours[c];
    const swatch = el('button', {
      type: 'button',
      className: 'swatch' + (c === state.colour ? ' selected' : ''),
      title: 'Colour ' + c + ' (' + (c + 1) + ')' + (c === 0 ? ': the background, transparent in sprites' : ''),
      onclick: () => { state.colour = c; save(); updateColours(); }
    }, el('span', { className: 'tag', text: String(c) }));
    swatch.style.background = cssOf(rgb);
    swatch.style.color = contrastOf(rgb);
    colourBox.append(swatch);
  }
  if (sheet) sheet.colours = colours;
}

function updatePaletteControls(): void {
  if (!system) return;
  fillSelect(paletteSelect, [{ value: '', label: 'Greys' }, ...paletteFiles.map(f => ({ value: f.path, label: f.label }))],
    paletteFiles.some(f => f.path === state.palettePath) ? state.palettePath : '');
  const count = paletteFile?.palettes.length ?? system.backgroundPaletteCount + system.spritePaletteCount;
  const options = [];
  for (let p = 0; p < count; p++) {
    const sprite = p >= system.backgroundPaletteCount;
    const name = paletteFile?.palettes[p]?.name;
    options.push({ value: p, label: p + (sprite ? ' sprite ' : ' bg ') + (name ?? '') });
  }
  fillSelect(paletteIndexSelect, options, Math.min(state.paletteIndex, count - 1));
  paletteIndexSelect.disabled = !paletteFile;
  updateColours();
}

function render(): void {
  if (!system || !sheet) return;
  // One tile selected in 2x2 view: the block being edited is outlined.
  const single = state.selection.from === state.selection.to;
  const first = editTile();
  sheet.selection = single && state.view === 2 && first % TileSheet.COLUMNS < TileSheet.COLUMNS - 1
    ? { from: first, to: first + TileSheet.COLUMNS + 1 }
    : state.selection;
  sheet.colours = colours;
  sheet.render();
  fitEditor();
  const b = block();
  const pixels = stroke ? stroke.pixels : blockPixels();
  editor.image.data.fill(0);
  drawPixels(editor.image, pixels, b.width, b.height, 0, 0, colours);
  // Beyond the tile set: left transparent (the checkerboard shows).
  b.tiles.forEach((tile, i) => {
    if (tile >= 0) return;
    const ox = (i % b.size) * system!.tileWidth;
    const oy = Math.floor(i / b.size) * system!.tileHeight;
    for (let y = 0; y < system!.tileHeight; y++) {
      for (let x = 0; x < system!.tileWidth; x++) {
        editor.image.data[((oy + y) * b.width + ox + x) * 4 + 3] = 0;
      }
    }
  });
  editor.present();
  const r = sheetRect(state.selection.from, state.selection.to);
  status.info((r.columns * r.rows > 1 ? r.columns + '×' + r.rows + ' tiles from ' : 'tile ') + hex2(first)
    + ' · ' + (cache?.count ?? 0) + ' tiles · ' + data.length + ' bytes');
}

function drawEditorOverlay(ctx: CanvasRenderingContext2D, zoom: number): void {
  if (!system) return;
  if (state.grid && zoom >= 6) {
    grid(ctx, zoom, editor.width, editor.height, 1, 1, 'rgba(128,128,128,0.35)');
  }
  // Tile edges of a 2x2 block.
  grid(ctx, zoom, editor.width, editor.height, system.tileWidth, system.tileHeight, 'rgba(255,255,255,0.55)', 2);
}

function editorHover(p: { x: number; y: number } | undefined): void {
  if (!p || !system) {
    status.hover('');
    return;
  }
  const b = block();
  const part = Math.floor(p.y / system.tileHeight) * b.size + Math.floor(p.x / system.tileWidth);
  const tile = b.tiles[part];
  const pixels = stroke ? stroke.pixels : blockPixels();
  status.hover((tile >= 0 ? 'tile ' + hex2(tile) : 'no tile') + ' · x ' + (p.x % system.tileWidth) + ', y ' + (p.y % system.tileHeight)
    + ' · colour ' + pixels[p.y * b.width + p.x]);
}

function inBlock(p: { x: number; y: number }): boolean {
  if (!editor.inside(p) || !system) return false;
  const b = block();
  return b.tiles[Math.floor(p.y / system.tileHeight) * b.size + Math.floor(p.x / system.tileWidth)] >= 0;
}

function strokeDown(p: { x: number; y: number }, event: PointerEvent): void {
  if (!system || !inBlock(p)) return;
  const base = blockPixels();
  const b = block();
  if (state.tool === 'picker' || event.button === 2) {
    state.colour = base[p.y * b.width + p.x];
    save();
    updateColours();
    return;
  }
  stroke = { base, pixels: base.slice(), start: p, last: p, colour: state.colour };
  if (state.tool === 'fill') {
    // Within the tile under the click only.
    const sys = system;
    const ox = Math.floor(p.x / sys.tileWidth) * sys.tileWidth;
    const oy = Math.floor(p.y / sys.tileHeight) * sys.tileHeight;
    const tile = new Uint8Array(sys.tileWidth * sys.tileHeight);
    for (let y = 0; y < sys.tileHeight; y++) for (let x = 0; x < sys.tileWidth; x++) tile[y * sys.tileWidth + x] = base[(oy + y) * b.width + ox + x];
    floodFill(tile, sys.tileWidth, sys.tileHeight, p.x - ox, p.y - oy, state.colour);
    for (let y = 0; y < sys.tileHeight; y++) for (let x = 0; x < sys.tileWidth; x++) stroke.pixels[(oy + y) * b.width + ox + x] = tile[y * sys.tileWidth + x];
  } else {
    plot(stroke.pixels, [[p.x, p.y]]);
  }
  render();
}

function plot(pixels: Uint8Array, points: Array<[number, number]>): void {
  const b = block();
  for (const [x, y] of points) {
    if (inBlock({ x, y })) pixels[y * b.width + x] = stroke!.colour;
  }
}

function strokeMove(p: { x: number; y: number }, event: PointerEvent): void {
  if (!stroke) return;
  const q = { x: clamp(p.x, 0, editor.width - 1), y: clamp(p.y, 0, editor.height - 1) };
  if (state.tool === 'pencil') {
    plot(stroke.pixels, linePoints(stroke.last.x, stroke.last.y, q.x, q.y));
  } else if (state.tool === 'line') {
    stroke.pixels = stroke.base.slice();
    plot(stroke.pixels, linePoints(stroke.start.x, stroke.start.y, q.x, q.y));
  } else if (state.tool === 'rect') {
    stroke.pixels = stroke.base.slice();
    plot(stroke.pixels, rectanglePoints(stroke.start.x, stroke.start.y, q.x, q.y, event.shiftKey));
  }
  stroke.last = q;
  render();
}

function strokeUp(): void {
  if (!stroke) return;
  const done = stroke;
  stroke = undefined;
  if (done.pixels.every((v, i) => v === done.base[i])) {
    render();
    return;
  }
  const labels: Record<Tool, string> = { pencil: 'Draw', fill: 'Fill', line: 'Line', rect: 'Rectangle', picker: 'Pick' };
  commit(writeBlock(done.pixels), labels[state.tool] + ' in tile ' + hex2(editTile()));
}

/** The selected tiles as a rectangle of tile indexes (-1 beyond the data). */
function selectedTiles(): { columns: number; rows: number; tiles: number[] } {
  const r = sheetRect(state.selection.from, state.selection.to);
  const tiles: number[] = [];
  for (let y = 0; y < r.rows; y++) {
    for (let x = 0; x < r.columns; x++) {
      const index = (r.row + y) * TileSheet.COLUMNS + r.column + x;
      tiles.push(index < (cache?.count ?? 0) ? index : -1);
    }
  }
  return { columns: r.columns, rows: r.rows, tiles };
}

function copy(): void {
  if (!system) return;
  const s = selectedTiles();
  clipboard = { columns: s.columns, rows: s.rows, tiles: s.tiles.map(t => decodeTile(system!, data, t)) };
  status.show('Copied ' + s.tiles.length + ' tile' + (s.tiles.length === 1 ? '' : 's'));
}

function paste(): void {
  if (!system || !clipboard) return;
  const first = editTile();
  const next = data.slice();
  let pasted = 0;
  for (let y = 0; y < clipboard.rows; y++) {
    for (let x = 0; x < clipboard.columns; x++) {
      const column = (first % TileSheet.COLUMNS) + x;
      const index = first + y * TileSheet.COLUMNS + x;
      if (column >= TileSheet.COLUMNS || index >= tileCount(system, next)) continue;
      writeTile(system, next, index, clipboard.tiles[y * clipboard.columns + x]);
      pasted++;
    }
  }
  if (pasted > 0) {
    state.selection = { from: first, to: first + (clipboard.rows - 1) * TileSheet.COLUMNS + clipboard.columns - 1 };
    commit(next, 'Paste ' + pasted + ' tile' + (pasted === 1 ? '' : 's'));
  }
}

/** Flips, turns or clears the selection as one picture. */
function transform(label: string, how: 'h' | 'v' | 'r' | 'c'): void {
  if (!system) return;
  const sys = system;
  const s = selectedTiles();
  const width = s.columns * sys.tileWidth;
  const height = s.rows * sys.tileHeight;
  // The selection as one picture.
  let picture: Uint8Array = new Uint8Array(width * height);
  s.tiles.forEach((tile, i) => {
    const tp = decodeTile(sys, data, tile);
    const ox = (i % s.columns) * sys.tileWidth;
    const oy = Math.floor(i / s.columns) * sys.tileHeight;
    for (let y = 0; y < sys.tileHeight; y++) for (let x = 0; x < sys.tileWidth; x++) picture[(oy + y) * width + ox + x] = tp[y * sys.tileWidth + x];
  });
  if (how === 'h') picture = flipH(picture, width, height);
  else if (how === 'v') picture = flipV(picture, width, height);
  else if (how === 'c') picture = new Uint8Array(width * height);
  else if (width === height) picture = rotateClockwise(picture, width);
  else {
    // Not square: each tile turns in place.
    s.tiles.forEach((_, i) => {
      const ox = (i % s.columns) * sys.tileWidth;
      const oy = Math.floor(i / s.columns) * sys.tileHeight;
      const tp = new Uint8Array(sys.tileWidth * sys.tileHeight);
      for (let y = 0; y < sys.tileHeight; y++) for (let x = 0; x < sys.tileWidth; x++) tp[y * sys.tileWidth + x] = picture[(oy + y) * width + ox + x];
      const turned = rotateClockwise(tp, sys.tileWidth);
      for (let y = 0; y < sys.tileHeight; y++) for (let x = 0; x < sys.tileWidth; x++) picture[(oy + y) * width + ox + x] = turned[y * sys.tileWidth + x];
    });
  }
  const next = data.slice();
  s.tiles.forEach((tile, i) => {
    if (tile < 0) return;
    const ox = (i % s.columns) * sys.tileWidth;
    const oy = Math.floor(i / s.columns) * sys.tileHeight;
    const tp = new Uint8Array(sys.tileWidth * sys.tileHeight);
    for (let y = 0; y < sys.tileHeight; y++) for (let x = 0; x < sys.tileWidth; x++) tp[y * sys.tileWidth + x] = picture[(oy + y) * width + ox + x];
    writeTile(sys, next, tile, tp);
  });
  if (!next.every((v, i) => v === data[i])) {
    commit(next, label);
  }
}

function addRow(): void {
  if (!system) return;
  const count = tileCount(system, data);
  const target = (Math.floor(count / TileSheet.COLUMNS) + 1) * TileSheet.COLUMNS;
  commit(withTileCount(system, data, target), 'Add ' + (target - count) + ' tiles');
}

function onKey(event: KeyboardEvent): void {
  if (typing(event) || !system) return;
  const ctrl = event.ctrlKey || event.metaKey;
  const key = event.key.toLowerCase();
  if (ctrl && key === 'c') { copy(); event.preventDefault(); return; }
  if (ctrl && key === 'v') { paste(); event.preventDefault(); return; }
  if (ctrl || event.altKey) return;
  const toolKeys: Record<string, Tool> = { b: 'pencil', p: 'pencil', g: 'fill', l: 'line', r: 'rect', i: 'picker' };
  if (toolKeys[key]) {
    state.tool = toolKeys[key];
    tools.set(state.tool);
    save();
  } else if (key >= '1' && key <= String(colorsPerTile(system))) {
    state.colour = Number(key) - 1;
    save();
    updateColours();
  } else if (key === 'h') transform('Flip horizontally', 'h');
  else if (key === 'v') transform('Flip vertically', 'v');
  else if (key === 'delete') transform('Clear', 'c');
  else if (key.startsWith('arrow')) {
    const step = key === 'arrowleft' ? -1 : key === 'arrowright' ? 1 : key === 'arrowup' ? -TileSheet.COLUMNS : TileSheet.COLUMNS;
    const tile = clamp(editTile() + step, 0, Math.max(0, (cache?.count ?? 1) - 1));
    state.anchor = tile;
    state.selection = { from: tile, to: tile };
    save();
    render();
    event.preventDefault();
  }
}

start((message: HostMessage) => {
  switch (message.type) {
    case 'init':
      system = message.system;
      build();
      if (message.systemNote) status.show(message.systemNote, 'error');
      break;
    case 'chr':
      data = Uint8Array.from(message.data);
      cache = new TileCache(system!, data);
      if (sheet) sheet.cache = cache;
      if (system && data.length % system.bytesPerTile !== 0) {
        banner.show((data.length % system.bytesPerTile) + ' bytes after the last whole tile are kept as they are.');
      } else if (data.length === 0) {
        banner.show('The tile set is empty: add tiles with "+16 tiles".');
      } else {
        banner.hide();
      }
      render();
      break;
    case 'paletteFiles':
      paletteFiles = message.files;
      updatePaletteControls();
      // A palette chosen earlier is asked for again once the files are known.
      if (state.palettePath && !paletteFile && paletteFiles.some(f => f.path === state.palettePath)) {
        post({ type: 'choosePalette', path: state.palettePath });
      }
      break;
    case 'displayPalette':
      paletteFile = message.palettes?.asset;
      if (message.palettes?.error) status.show(message.palettes.error, 'error');
      if (message.palettes?.path) state.palettePath = message.palettes.path;
      updatePaletteControls();
      render();
      break;
    case 'notice':
      status.show(message.text, message.level);
      break;
    case 'live':
      live.update(message.state);
      break;
  }
});
