import { ScreenAsset } from '../shared/assets.js';
import { HostMessage } from '../shared/protocol.js';
import { GraphicsSystem, attributeColumns, attributeRows, hex2, screenHeight, screenWidth } from '../shared/system.js';
import { Banner, Choice, StatusBar, checkbox, clamp, el, loadState, post, saveState, selectBox, separator, start, typing } from './common.js';
import { Rgb, TileSheet, ZoomCanvas, accent, drawPixels, grid, onPointer, outline, paletteButton, paletteColours } from './render.js';
import { AssetFields, TileSources } from './tiled.js';

/**
 * The screen editor: one screen of background, a tile per cell and a
 * background palette per attribute block. Tiles are chosen in the sheet on the
 * left and painted on the screen; the attribute tool paints palettes per block.
 */

type Tool = 'paint' | 'rect' | 'pick' | 'attribute';

const state = loadState({
  tool: 'paint' as Tool,
  tile: 0,
  palette: 0,
  zoom: 2,
  sheetZoom: 2,
  grid: false,
  attributeGrid: true,
  overscan: false
});

let system: GraphicsSystem | undefined;
let asset: ScreenAsset | undefined;
let sources: TileSources;
/** The cells being painted: a copy of the asset's arrays while the mouse is down. */
let stroke: { tiles: number[]; attributes: number[]; start: { x: number; y: number }; end: { x: number; y: number }; label: string } | undefined;
let hoverCell: { column: number; row: number } | undefined;

const app = document.getElementById('app')!;
const banner = new Banner();
const status = new StatusBar();
let canvas: ZoomCanvas;
let sheet: TileSheet;
let tools: Choice<Tool>;
let fields: AssetFields;
const paletteButtons: Array<ReturnType<typeof paletteButton>> = [];
const compression = el('select', { title: 'none: the nametable as it is (.nam); rle: run-length encoded (.rle)' });

function save(): void {
  saveState(state);
}

function edit(label: string, change: (a: ScreenAsset) => void): void {
  if (!asset) return;
  const next = JSON.parse(JSON.stringify(asset)) as ScreenAsset;
  change(next);
  asset = next;
  render();
  post({ type: 'edit', label, asset: next });
}

function build(): void {
  const sys = system!;
  app.textContent = '';
  sources = new TileSources(sys, banner);
  tools = new Choice<Tool>([
    { value: 'paint', label: 'Paint', title: 'Paint the selected tile (P). Right button: pick' },
    { value: 'rect', label: 'Rectangle', title: 'Fill a rectangle with the selected tile (R)' },
    { value: 'pick', label: 'Pick', title: 'Pick the tile and palette of a cell (I)' },
    { value: 'attribute', label: 'Attribute', title: 'Paint the selected palette on 16x16 blocks (A)' }
  ], state.tool, tool => { state.tool = tool; save(); render(); });

  const palettes = el('span', { className: 'group' });
  paletteButtons.length = 0;
  for (let p = 0; p < sys.backgroundPaletteCount; p++) {
    const b = paletteButton(sys, String(p), 'Background palette ' + p + ' (' + (p + 1) + '): for the attribute tool, and the colours of the tile sheet', () => {
      state.palette = p;
      save();
      render();
    });
    paletteButtons.push(b);
    palettes.append(b.element);
  }

  const zoom = selectBox([1, 2, 3, 4].map(z => ({ value: z, label: z + '×' })), state.zoom, z => {
    state.zoom = z;
    canvas.setZoom(z);
    save();
    render();
  }, 'Zoom');

  const toolbar = el('div', { className: 'toolbar' },
    tools.element, separator(),
    el('label', {}, 'Palette', palettes), separator(),
    el('label', {}, 'Zoom', zoom),
    checkbox('Grid', 'Tile grid, every ' + sys.tileWidth + ' pixels', state.grid, on => { state.grid = on; save(); render(); }),
    checkbox('Attributes', 'Attribute grid, every ' + sys.tileWidth * sys.attributeBlockWidth + ' pixels', state.attributeGrid, on => { state.attributeGrid = on; save(); render(); }),
    checkbox('Overscan', 'Shade the top and bottom 8 pixel rows, which many televisions do not show', state.overscan, on => { state.overscan = on; save(); render(); })
  );

  fields = new AssetFields(['name', 'prefix'], (field, value) => edit('Change ' + field, a => {
    const record = a as unknown as Record<string, unknown>;
    if (value || field === 'name' || field === 'prefix') record[field] = value;
    else delete record[field];
  }));
  compression.append(el('option', { value: 'none', text: 'none (.nam)' }), el('option', { value: 'rle', text: 'rle (.rle)' }));
  compression.addEventListener('change', () => edit('Change compression', a => { a.compression = compression.value; }));
  fields.element.append(el('span', { text: 'Compression' }), compression, el('span'));

  sheet = new TileSheet(sys, state.sheetZoom);
  sheet.minRows = 1;
  onPointer(sheet.surface, {
    down: p => {
      const tile = sheet.tileAt(p);
      if (tile === undefined) return;
      state.tile = tile;
      if (state.tool === 'pick' || state.tool === 'attribute') {
        state.tool = 'paint';
        tools.set('paint');
      }
      save();
      render();
    },
    hover: p => {
      const tile = p ? sheet.tileAt(p) : undefined;
      sheet.hovered = tile;
      sheet.surface.present();
      status.hover(tile === undefined ? '' : 'tile ' + hex2(tile));
    }
  });
  const sheetZoom = selectBox([1, 2, 3].map(z => ({ value: z, label: z + '×' })), state.sheetZoom, z => {
    state.sheetZoom = z;
    sheet.surface.setZoom(z);
    save();
    render();
  }, 'Zoom of the tile sheet');

  canvas = new ZoomCanvas(screenWidth(sys), screenHeight(sys), drawOverlay);
  canvas.setZoom(state.zoom);
  onPointer(canvas, { down: onDown, drag: onDrag, up: onUp, hover: onHover });

  const side = el('div', { className: 'side screen-side' },
    el('section', {}, el('h3', { text: 'Screen' }), fields.element),
    el('section', {}, el('div', { className: 'row spread' }, el('h3', { text: 'Tiles' }), sheetZoom), sheet.surface.frame));
  const body = el('div', { className: 'body' }, side, el('div', { className: 'main' }, canvas.frame));
  app.append(toolbar, banner.element, body, status.element);
  document.addEventListener('keydown', onKey);
}

function cellOf(p: { x: number; y: number }): { column: number; row: number } {
  const sys = system!;
  return {
    column: clamp(Math.floor(p.x / sys.tileWidth), 0, sys.screenWidthTiles - 1),
    row: clamp(Math.floor(p.y / sys.tileHeight), 0, sys.screenHeightTiles - 1)
  };
}

function blockIndex(cell: { column: number; row: number }): number {
  const sys = system!;
  return Math.floor(cell.row / sys.attributeBlockHeight) * attributeColumns(sys) + Math.floor(cell.column / sys.attributeBlockWidth);
}

function onDown(p: { x: number; y: number }, event: PointerEvent): void {
  if (!asset || !system || !canvas.inside(p)) return;
  const cell = cellOf(p);
  if (state.tool === 'pick' || event.button === 2) {
    state.tile = asset.tiles[cell.row * system.screenWidthTiles + cell.column] ?? 0;
    state.palette = asset.attributes[blockIndex(cell)] ?? 0;
    save();
    render();
    return;
  }
  const labels: Record<Tool, string> = { paint: 'Paint tiles', rect: 'Fill a rectangle', pick: '', attribute: 'Paint attributes' };
  const at = { x: cell.column, y: cell.row };
  stroke = { tiles: asset.tiles.slice(), attributes: asset.attributes.slice(), start: at, end: at, label: labels[state.tool] };
  apply(cell);
  render();
}

function apply(cell: { column: number; row: number }): void {
  if (!stroke || !system) return;
  if (state.tool === 'paint') {
    stroke.tiles[cell.row * system.screenWidthTiles + cell.column] = state.tile;
  } else if (state.tool === 'attribute') {
    stroke.attributes[blockIndex(cell)] = state.palette;
  } else if (state.tool === 'rect') {
    stroke.tiles = asset!.tiles.slice();
    const x0 = Math.min(stroke.start.x, cell.column), x1 = Math.max(stroke.start.x, cell.column);
    const y0 = Math.min(stroke.start.y, cell.row), y1 = Math.max(stroke.start.y, cell.row);
    for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) stroke.tiles[y * system.screenWidthTiles + x] = state.tile;
  }
  stroke.end = { x: cell.column, y: cell.row };
}

function onDrag(p: { x: number; y: number }): void {
  if (!stroke) return;
  apply(cellOf(p));
  onHover(canvas.inside(p) ? p : undefined);
  render();
}

function onUp(): void {
  if (!stroke || !asset) return;
  const done = stroke;
  stroke = undefined;
  const changed = done.tiles.some((t, i) => t !== asset!.tiles[i]) || done.attributes.some((a, i) => a !== asset!.attributes[i]);
  if (changed) {
    edit(done.label, a => { a.tiles = done.tiles; a.attributes = done.attributes; });
  } else {
    render();
  }
}

function onHover(p: { x: number; y: number } | undefined): void {
  if (!p || !asset || !system) {
    hoverCell = undefined;
    status.hover('');
    canvas.present();
    return;
  }
  const cell = cellOf(p);
  hoverCell = cell;
  const tiles = stroke ? stroke.tiles : asset.tiles;
  const attributes = stroke ? stroke.attributes : asset.attributes;
  const tile = tiles[cell.row * system.screenWidthTiles + cell.column] ?? 0;
  const block = blockIndex(cell);
  status.hover('x ' + cell.column + ', y ' + cell.row + ' · tile ' + hex2(tile) + ' · palette ' + (attributes[block] ?? 0)
    + ' · block ' + (cell.column >> 1) + ',' + (cell.row >> 1) + ' · pixel ' + p.x + ',' + p.y);
  canvas.present();
}

function render(): void {
  if (!system || !asset || !canvas) return;
  const sys = system;
  const tiles = stroke ? stroke.tiles : asset.tiles;
  const attributes = stroke ? stroke.attributes : asset.attributes;
  const cache = sources.cache;

  // The screen.
  const image = canvas.image;
  const columns = attributeColumns(sys);
  const colourSets: Rgb[][] = [];
  for (let p = 0; p < sys.backgroundPaletteCount; p++) colourSets.push(paletteColours(sys, sources.palettes, p, true));
  image.data.fill(0);
  for (let row = 0; row < sys.screenHeightTiles; row++) {
    for (let column = 0; column < sys.screenWidthTiles; column++) {
      const palette = attributes[Math.floor(row / sys.attributeBlockHeight) * columns + Math.floor(column / sys.attributeBlockWidth)] ?? 0;
      const tile = tiles[row * sys.screenWidthTiles + column] ?? 0;
      const colours = colourSets[palette] ?? colourSets[0];
      const pixels = cache && tile < cache.count ? cache.pixels(tile) : undefined;
      if (pixels) {
        drawPixels(image, pixels, sys.tileWidth, sys.tileHeight, column * sys.tileWidth, row * sys.tileHeight, colours);
      } else {
        drawPixels(image, new Uint8Array(sys.tileWidth * sys.tileHeight), sys.tileWidth, sys.tileHeight, column * sys.tileWidth, row * sys.tileHeight, colours);
      }
    }
  }
  canvas.present();

  // The sheet, in the chosen palette.
  sheet.cache = cache;
  sheet.colours = colourSets[state.palette] ?? colourSets[0];
  sheet.selection = { from: state.tile, to: state.tile };
  sheet.render();
  paletteButtons.forEach((b, p) => {
    b.paint(colourSets[p]);
    b.element.setAttribute('aria-pressed', String(p === state.palette));
  });
  tools.set(state.tool);

  fields.set({ name: asset.name, prefix: asset.prefix, chr: asset.chr, palettes: asset.palettes });
  compression.value = asset.compression === 'rle' ? 'rle' : 'none';
  sources.showProblems(!!asset.chr);
  status.info('tile ' + hex2(state.tile) + ' · palette ' + state.palette + ' · ' + (cache ? cache.count + ' tiles' : 'no tile set'));
}

function drawOverlay(ctx: CanvasRenderingContext2D, zoom: number): void {
  if (!system) return;
  const sys = system;
  const width = screenWidth(sys);
  const height = screenHeight(sys);
  if (state.overscan) {
    ctx.save();
    ctx.fillStyle = 'rgba(0,0,0,0.45)';
    ctx.fillRect(0, 0, width * zoom, 8 * zoom);
    ctx.fillRect(0, (height - 8) * zoom, width * zoom, 8 * zoom);
    ctx.restore();
  }
  if (state.grid && zoom >= 2) {
    grid(ctx, zoom, width, height, sys.tileWidth, sys.tileHeight, 'rgba(128,128,128,0.4)');
  }
  if (state.attributeGrid || state.tool === 'attribute') {
    grid(ctx, zoom, width, height, sys.tileWidth * sys.attributeBlockWidth, sys.tileHeight * sys.attributeBlockHeight, 'rgba(255,255,255,0.45)');
  }
  if (stroke && state.tool === 'rect') {
    const x0 = Math.min(stroke.start.x, stroke.end.x), y0 = Math.min(stroke.start.y, stroke.end.y);
    outline(ctx, zoom, x0 * sys.tileWidth, y0 * sys.tileHeight,
      (Math.abs(stroke.end.x - stroke.start.x) + 1) * sys.tileWidth, (Math.abs(stroke.end.y - stroke.start.y) + 1) * sys.tileHeight, accent());
  } else if (hoverCell) {
    if (state.tool === 'attribute') {
      const bw = sys.tileWidth * sys.attributeBlockWidth;
      const bh = sys.tileHeight * sys.attributeBlockHeight;
      outline(ctx, zoom, Math.floor(hoverCell.column / sys.attributeBlockWidth) * bw, Math.floor(hoverCell.row / sys.attributeBlockHeight) * bh, bw, bh, accent());
    } else {
      outline(ctx, zoom, hoverCell.column * sys.tileWidth, hoverCell.row * sys.tileHeight, sys.tileWidth, sys.tileHeight, accent());
    }
  }
}

function onKey(event: KeyboardEvent): void {
  if (typing(event) || !system || event.ctrlKey || event.metaKey || event.altKey) return;
  const key = event.key.toLowerCase();
  const toolKeys: Record<string, Tool> = { p: 'paint', b: 'paint', r: 'rect', i: 'pick', a: 'attribute' };
  if (toolKeys[key]) {
    state.tool = toolKeys[key];
    save();
    render();
  } else if (key >= '1' && key <= String(system.backgroundPaletteCount)) {
    state.palette = Number(key) - 1;
    save();
    render();
  }
}

start((message: HostMessage) => {
  switch (message.type) {
    case 'init':
      system = message.system;
      build();
      if (message.systemNote) status.show(message.systemNote, 'error');
      break;
    case 'asset': {
      if (message.error || !message.asset) {
        banner.show(message.error ?? 'No asset.', 'error', { label: 'Open as Text', run: () => post({ type: 'openAsText' }) });
        return;
      }
      const next = message.asset as ScreenAsset;
      const sys = system!;
      next.tiles = Array.isArray(next.tiles) ? next.tiles : [];
      next.attributes = Array.isArray(next.attributes) ? next.attributes : [];
      while (next.tiles.length < sys.screenWidthTiles * sys.screenHeightTiles) next.tiles.push(0);
      while (next.attributes.length < attributeColumns(sys) * attributeRows(sys)) next.attributes.push(0);
      asset = next;
      render();
      break;
    }
    case 'resources':
      sources.update(message.chr, message.palettes);
      render();
      break;
    case 'notice':
      status.show(message.text, message.level);
      break;
  }
});
