import { MapAsset, Metatile, metatilesPerScreen, removeMetatile, resizeMap } from '../shared/assets.js';
import { floodFill } from '../shared/chr.js';
import { HostMessage } from '../shared/protocol.js';
import { GraphicsSystem, hex2 } from '../shared/system.js';
import {
  Banner, Choice, LiveToggle, StatusBar, button, checkbox, clamp, el, loadState, post, saveState, selectBox, separator, start, textInput, setInputValue, typing
} from './common.js';
import { Rgb, TileSheet, ZoomCanvas, accent, drawPixels, grid, onPointer, outline, paletteButton, paletteColours } from './render.js';
import { AssetFields, TileSources } from './tiled.js';

/**
 * The map editor: metatiles (blocks of tiles sharing one background palette,
 * the size of an attribute block) laid on a grid of any number of screens.
 * The metatile set is on the left: pick a metatile to paint with, and edit it
 * by choosing a corner, then a tile of the sheet, and its palette.
 */

type Tool = 'paint' | 'rect' | 'fill' | 'pick';
const SET_COLUMNS = 8;

const state = loadState({
  tool: 'paint' as Tool,
  metatile: 0,
  slot: 0,
  zoom: 2,
  grid: true,
  screens: true
});

let system: GraphicsSystem | undefined;
let asset: MapAsset | undefined;
let sources: TileSources;
let stroke: { cells: number[]; start: { x: number; y: number }; end: { x: number; y: number }; label: string } | undefined;
let hoverCell: { x: number; y: number } | undefined;

const app = document.getElementById('app')!;
const banner = new Banner();
const status = new StatusBar();
const live = new LiveToggle();
let canvas: ZoomCanvas;
let setCanvas: ZoomCanvas;
let metaCanvas: ZoomCanvas;
let sheet: TileSheet;
let tools: Choice<Tool>;
let fields: AssetFields;
const widthInput = el('input', { type: 'number', min: 1, max: 4096, title: 'Width in metatiles' });
const heightInput = el('input', { type: 'number', min: 1, max: 4096, title: 'Height in metatiles' });
const metaName = textInput('', 'name (optional)', value => editMetatile('Rename metatile', m => {
  if (value) m.name = value;
  else delete m.name;
}));
const metaPalettes: Array<ReturnType<typeof paletteButton>> = [];
const metaTitle = el('h3');

function save(): void {
  saveState(state);
}

/** Tiles across and down a metatile (2 x 2 on the NES: an attribute block). */
function metaSize(): { columns: number; rows: number; width: number; height: number } {
  const sys = system!;
  return {
    columns: sys.attributeBlockWidth,
    rows: sys.attributeBlockHeight,
    width: sys.attributeBlockWidth * sys.tileWidth,
    height: sys.attributeBlockHeight * sys.tileHeight
  };
}

function edit(label: string, change: (a: MapAsset) => MapAsset | void): void {
  if (!asset) return;
  const copy = JSON.parse(JSON.stringify(asset)) as MapAsset;
  const next = change(copy) ?? copy;
  asset = next;
  render();
  post({ type: 'edit', label, asset: next });
}

function editMetatile(label: string, change: (m: Metatile) => void): void {
  if (!asset || !asset.metatiles[state.metatile]) return;
  edit(label + ' ' + state.metatile, a => change(a.metatiles[state.metatile]));
}

function build(): void {
  const sys = system!;
  const meta = metaSize();
  app.textContent = '';
  sources = new TileSources(sys, banner);
  tools = new Choice<Tool>([
    { value: 'paint', label: 'Paint', title: 'Paint the selected metatile (P). Right button: pick' },
    { value: 'rect', label: 'Rectangle', title: 'Fill a rectangle with the selected metatile (R)' },
    { value: 'fill', label: 'Fill', title: 'Fill the area of one metatile (G)' },
    { value: 'pick', label: 'Pick', title: 'Pick the metatile of a cell (I)' }
  ], state.tool, tool => { state.tool = tool; save(); });

  const zoom = selectBox([1, 2, 3, 4].map(z => ({ value: z, label: z + '×' })), state.zoom, z => {
    state.zoom = z;
    canvas.setZoom(z);
    save();
    render();
  }, 'Zoom');

  const screen = metatilesPerScreen(sys);
  const toolbar = el('div', { className: 'toolbar' },
    tools.element, separator(),
    el('label', {}, 'Zoom', zoom),
    checkbox('Grid', 'Metatile grid', state.grid, on => { state.grid = on; save(); render(); }),
    checkbox('Screens', 'Screen boundaries, every ' + screen.columns + 'x' + screen.rows + ' metatiles', state.screens, on => { state.screens = on; save(); render(); }),
    separator(),
    el('label', {}, 'Size', widthInput, '×', heightInput),
    button('Resize', 'Change the size of the map (cells are kept from the top left; new cells take metatile 0)', resize),
    button('+ Screen →', 'Add one screen of width', () => resizeBy(screen.columns, 0)),
    button('+ Screen ↓', 'Add one screen of height', () => resizeBy(0, screen.rows)),
    separator(),
    live.element
  );

  fields = new AssetFields(['name', 'prefix'], (field, value) => edit('Change ' + field, a => {
    const record = a as unknown as Record<string, unknown>;
    if (value || field === 'name' || field === 'prefix') record[field] = value;
    else delete record[field];
  }));

  // The metatile set.
  setCanvas = new ZoomCanvas(SET_COLUMNS * meta.width, meta.height, drawSetOverlay);
  setCanvas.setZoom(2);
  setCanvas.frame.classList.add('checker');
  onPointer(setCanvas, {
    down: p => {
      const index = setIndexAt(p);
      if (index === undefined) return;
      state.metatile = index;
      if (state.tool === 'pick') {
        state.tool = 'paint';
        tools.set('paint');
      }
      save();
      render();
    },
    hover: p => {
      const index = p ? setIndexAt(p) : undefined;
      const m = index === undefined ? undefined : asset?.metatiles[index];
      status.hover(m ? 'metatile ' + index + (m.name ? ' "' + m.name + '"' : '') + ' · palette ' + m.palette + ' · tiles ' + m.tiles.map(hex2).join(' ') : '');
    }
  });

  // The selected metatile, big: a corner is chosen, then a tile of the sheet.
  metaCanvas = new ZoomCanvas(meta.width, meta.height, drawMetaOverlay);
  metaCanvas.setZoom(5);
  onPointer(metaCanvas, {
    down: p => {
      if (!metaCanvas.inside(p)) return;
      state.slot = Math.floor(p.y / sys.tileHeight) * meta.columns + Math.floor(p.x / sys.tileWidth);
      save();
      render();
    }
  });
  const palettes = el('span', { className: 'group' });
  metaPalettes.length = 0;
  for (let p = 0; p < sys.backgroundPaletteCount; p++) {
    const b = paletteButton(sys, String(p), 'Background palette ' + p, () => editMetatile('Set the palette of metatile', m => { m.palette = p; }));
    metaPalettes.push(b);
    palettes.append(b.element);
  }

  sheet = new TileSheet(sys, 2);
  sheet.minRows = 1;
  onPointer(sheet.surface, {
    down: p => {
      const tile = sheet.tileAt(p);
      if (tile === undefined || !asset?.metatiles[state.metatile]) return;
      const slot = state.slot;
      editMetatile('Set a tile of metatile', m => {
        while (m.tiles.length < meta.columns * meta.rows) m.tiles.push(0);
        m.tiles[slot] = tile;
      });
      state.slot = (slot + 1) % (meta.columns * meta.rows);
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

  canvas = new ZoomCanvas(1, 1, drawOverlay);
  canvas.setZoom(state.zoom);
  onPointer(canvas, { down: onDown, drag: onDrag, up: onUp, hover: onHover });

  const side = el('div', { className: 'side map-side' },
    el('section', {}, el('h3', { text: 'Map' }), fields.element),
    el('section', {},
      el('div', { className: 'row spread' }, el('h3', { text: 'Metatiles' }),
        el('span', { className: 'group' },
          button('Add', 'Add a metatile', addMetatile),
          button('Duplicate', 'Copy the selected metatile', duplicateMetatile),
          button('Delete', 'Delete the selected metatile (cells using it take metatile 0)', deleteMetatile))),
      setCanvas.frame),
    el('section', { className: 'metatile-editor' },
      metaTitle,
      el('div', { className: 'row top' },
        metaCanvas.frame,
        el('div', { className: 'field grow' },
          el('span', { text: 'Name' }), metaName,
          el('span', { text: 'Palette' }), palettes,
          el('span', { className: 'hint', text: 'Click a corner, then a tile below.' })))),
    el('section', {}, el('h3', { text: 'Tiles' }), sheet.surface.frame));
  const body = el('div', { className: 'body' }, side, el('div', { className: 'main' }, canvas.frame));
  app.append(toolbar, banner.element, body, status.element);
  document.addEventListener('keydown', onKey);
}

function setIndexAt(p: { x: number; y: number }): number | undefined {
  if (!asset || !setCanvas.inside(p)) return undefined;
  const meta = metaSize();
  const index = Math.floor(p.y / meta.height) * SET_COLUMNS + Math.floor(p.x / meta.width);
  return index < asset.metatiles.length ? index : undefined;
}

function addMetatile(): void {
  if (!asset) return;
  const meta = metaSize();
  const index = asset.metatiles.length;
  edit('Add a metatile', a => { a.metatiles.push({ tiles: new Array(meta.columns * meta.rows).fill(0), palette: 0 }); });
  state.metatile = index;
  state.slot = 0;
  save();
  render();
}

function duplicateMetatile(): void {
  if (!asset || !asset.metatiles[state.metatile]) return;
  const source = asset.metatiles[state.metatile];
  const index = asset.metatiles.length;
  edit('Duplicate metatile ' + state.metatile, a => {
    const copy: Metatile = { tiles: source.tiles.slice(), palette: source.palette };
    a.metatiles.push(copy);
  });
  state.metatile = index;
  save();
  render();
}

function deleteMetatile(): void {
  if (!asset || asset.metatiles.length <= 1) {
    status.show('A map keeps at least one metatile.', 'error');
    return;
  }
  const index = state.metatile;
  edit('Delete metatile ' + index, a => removeMetatile(a, index));
  state.metatile = Math.max(0, Math.min(index, asset.metatiles.length - 1));
  save();
  render();
}

function resize(): void {
  const width = clamp(Math.floor(Number(widthInput.value) || 1), 1, 4096);
  const height = clamp(Math.floor(Number(heightInput.value) || 1), 1, 4096);
  if (!asset || (width === asset.width && height === asset.height)) return;
  edit('Resize the map to ' + width + 'x' + height, a => resizeMap(a, width, height));
}

function resizeBy(columns: number, rows: number): void {
  if (!asset) return;
  const width = asset.width + columns;
  const height = asset.height + rows;
  edit('Resize the map to ' + width + 'x' + height, a => resizeMap(a, width, height));
}

function cellOf(p: { x: number; y: number }): { x: number; y: number } {
  const meta = metaSize();
  return { x: clamp(Math.floor(p.x / meta.width), 0, asset!.width - 1), y: clamp(Math.floor(p.y / meta.height), 0, asset!.height - 1) };
}

function onDown(p: { x: number; y: number }, event: PointerEvent): void {
  if (!asset || !canvas.inside(p)) return;
  const cell = cellOf(p);
  if (state.tool === 'pick' || event.button === 2) {
    state.metatile = asset.cells[cell.y * asset.width + cell.x] ?? 0;
    save();
    render();
    return;
  }
  const labels: Record<Tool, string> = { paint: 'Paint metatiles', rect: 'Fill a rectangle', fill: 'Fill', pick: '' };
  stroke = { cells: asset.cells.slice(), start: cell, end: cell, label: labels[state.tool] };
  if (state.tool === 'fill') {
    const cells = Uint8Array.from(stroke.cells.map(c => c & 0xff));
    floodFill(cells, asset.width, asset.height, cell.x, cell.y, state.metatile);
    stroke.cells = Array.from(cells);
  } else {
    apply(cell);
  }
  render();
}

function apply(cell: { x: number; y: number }): void {
  if (!stroke || !asset) return;
  if (state.tool === 'paint') {
    stroke.cells[cell.y * asset.width + cell.x] = state.metatile;
  } else if (state.tool === 'rect') {
    stroke.cells = asset.cells.slice();
    const x0 = Math.min(stroke.start.x, cell.x), x1 = Math.max(stroke.start.x, cell.x);
    const y0 = Math.min(stroke.start.y, cell.y), y1 = Math.max(stroke.start.y, cell.y);
    for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) stroke.cells[y * asset.width + x] = state.metatile;
  }
  stroke.end = cell;
}

function onDrag(p: { x: number; y: number }): void {
  if (!stroke || state.tool === 'fill') return;
  apply(cellOf(p));
  onHover(canvas.inside(p) ? p : undefined);
  render();
}

function onUp(): void {
  if (!stroke || !asset) return;
  const done = stroke;
  stroke = undefined;
  if (done.cells.some((c, i) => c !== asset!.cells[i])) {
    edit(done.label, a => { a.cells = done.cells; });
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
  const screen = metatilesPerScreen(system);
  const cells = stroke ? stroke.cells : asset.cells;
  const index = cells[cell.y * asset.width + cell.x] ?? 0;
  const m = asset.metatiles[index];
  status.hover('x ' + cell.x + ', y ' + cell.y + ' · screen ' + Math.floor(cell.x / screen.columns) + ',' + Math.floor(cell.y / screen.rows)
    + ' · metatile ' + index + (m?.name ? ' "' + m.name + '"' : ''));
  canvas.present();
}

/** Draws a metatile into an image at (x, y). */
function drawMetatile(image: ImageData, m: Metatile | undefined, x: number, y: number, colourSets: Rgb[][]): void {
  const sys = system!;
  const meta = metaSize();
  const colours = colourSets[m?.palette ?? 0] ?? colourSets[0];
  const blank = new Uint8Array(sys.tileWidth * sys.tileHeight);
  for (let part = 0; part < meta.columns * meta.rows; part++) {
    const tile = m?.tiles?.[part] ?? 0;
    const cache = sources.cache;
    const pixels = cache && tile < cache.count ? cache.pixels(tile) : blank;
    drawPixels(image, pixels, sys.tileWidth, sys.tileHeight, x + (part % meta.columns) * sys.tileWidth, y + Math.floor(part / meta.columns) * sys.tileHeight, colours);
  }
}

function render(): void {
  if (!system || !asset || !canvas) return;
  const sys = system;
  const meta = metaSize();
  const colourSets: Rgb[][] = [];
  for (let p = 0; p < sys.backgroundPaletteCount; p++) colourSets.push(paletteColours(sys, sources.palettes, p, true));
  if (state.metatile >= asset.metatiles.length) state.metatile = Math.max(0, asset.metatiles.length - 1);

  // The map.
  const width = Math.max(1, asset.width) * meta.width;
  const height = Math.max(1, asset.height) * meta.height;
  if (canvas.width !== width || canvas.height !== height) canvas.resize(width, height);
  const cells = stroke ? stroke.cells : asset.cells;
  canvas.image.data.fill(0);
  for (let y = 0; y < asset.height; y++) {
    for (let x = 0; x < asset.width; x++) {
      drawMetatile(canvas.image, asset.metatiles[cells[y * asset.width + x] ?? 0], x * meta.width, y * meta.height, colourSets);
    }
  }
  canvas.present();

  // The metatile set.
  const rows = Math.max(1, Math.ceil(asset.metatiles.length / SET_COLUMNS));
  if (setCanvas.height !== rows * meta.height) setCanvas.resize(SET_COLUMNS * meta.width, rows * meta.height);
  setCanvas.image.data.fill(0);
  asset.metatiles.forEach((m, i) => drawMetatile(setCanvas.image, m, (i % SET_COLUMNS) * meta.width, Math.floor(i / SET_COLUMNS) * meta.height, colourSets));
  setCanvas.present();

  // The selected metatile.
  const m = asset.metatiles[state.metatile];
  metaCanvas.image.data.fill(0);
  if (m) drawMetatile(metaCanvas.image, m, 0, 0, colourSets);
  metaCanvas.present();
  metaTitle.textContent = m ? 'Metatile ' + state.metatile : 'No metatile';
  setInputValue(metaName, m?.name ?? '');
  metaPalettes.forEach((b, p) => {
    b.paint(colourSets[p]);
    b.element.setAttribute('aria-pressed', String(p === (m?.palette ?? -1)));
  });

  // The sheet, in the palette of the selected metatile; its tile in the chosen corner outlined.
  sheet.cache = sources.cache;
  sheet.colours = colourSets[m?.palette ?? 0] ?? colourSets[0];
  const slotTile = m?.tiles?.[state.slot];
  sheet.selection = slotTile === undefined ? undefined : { from: slotTile, to: slotTile };
  sheet.render();

  tools.set(state.tool);
  fields.set({ name: asset.name, prefix: asset.prefix, chr: asset.chr, palettes: asset.palettes });
  if (document.activeElement !== widthInput) widthInput.value = String(asset.width);
  if (document.activeElement !== heightInput) heightInput.value = String(asset.height);
  sources.showProblems(!!asset.chr);
  const screen = metatilesPerScreen(sys);
  status.info(asset.width + '×' + asset.height + ' metatiles · ' + Math.ceil(asset.width / screen.columns) + '×' + Math.ceil(asset.height / screen.rows)
    + ' screens · ' + asset.metatiles.length + ' metatiles in the set');
}

function drawOverlay(ctx: CanvasRenderingContext2D, zoom: number): void {
  if (!system || !asset) return;
  const meta = metaSize();
  const width = asset.width * meta.width;
  const height = asset.height * meta.height;
  if (state.grid && zoom >= 2) {
    grid(ctx, zoom, width, height, meta.width, meta.height, 'rgba(128,128,128,0.35)');
  }
  if (state.screens) {
    const screen = metatilesPerScreen(system);
    grid(ctx, zoom, width, height, screen.columns * meta.width, screen.rows * meta.height, 'rgba(255,200,0,0.85)', 2);
  }
  if (stroke && state.tool === 'rect') {
    const x0 = Math.min(stroke.start.x, stroke.end.x), y0 = Math.min(stroke.start.y, stroke.end.y);
    outline(ctx, zoom, x0 * meta.width, y0 * meta.height, (Math.abs(stroke.end.x - stroke.start.x) + 1) * meta.width,
      (Math.abs(stroke.end.y - stroke.start.y) + 1) * meta.height, accent());
  } else if (hoverCell) {
    outline(ctx, zoom, hoverCell.x * meta.width, hoverCell.y * meta.height, meta.width, meta.height, accent());
  }
}

function drawSetOverlay(ctx: CanvasRenderingContext2D, zoom: number): void {
  if (!asset) return;
  const meta = metaSize();
  grid(ctx, zoom, setCanvas.width, setCanvas.height, meta.width, meta.height, 'rgba(128,128,128,0.35)');
  const i = state.metatile;
  if (i < asset.metatiles.length) {
    outline(ctx, zoom, (i % SET_COLUMNS) * meta.width, Math.floor(i / SET_COLUMNS) * meta.height, meta.width, meta.height, accent());
  }
}

function drawMetaOverlay(ctx: CanvasRenderingContext2D, zoom: number): void {
  if (!system) return;
  const meta = metaSize();
  grid(ctx, zoom, meta.width, meta.height, system.tileWidth, system.tileHeight, 'rgba(255,255,255,0.5)');
  outline(ctx, zoom, (state.slot % meta.columns) * system.tileWidth, Math.floor(state.slot / meta.columns) * system.tileHeight,
    system.tileWidth, system.tileHeight, accent(), 3);
}

function onKey(event: KeyboardEvent): void {
  if (typing(event) || event.ctrlKey || event.metaKey || event.altKey) return;
  const toolKeys: Record<string, Tool> = { p: 'paint', b: 'paint', r: 'rect', g: 'fill', i: 'pick' };
  const tool = toolKeys[event.key.toLowerCase()];
  if (tool) {
    state.tool = tool;
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
      const next = message.asset as MapAsset;
      next.metatiles = Array.isArray(next.metatiles) ? next.metatiles : [];
      next.metatiles.forEach(m => { if (!Array.isArray(m.tiles)) m.tiles = []; });
      next.width = Math.max(1, Math.floor(Number(next.width) || 1));
      next.height = Math.max(1, Math.floor(Number(next.height) || 1));
      next.cells = Array.isArray(next.cells) ? next.cells : [];
      while (next.cells.length < next.width * next.height) next.cells.push(0);
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
    case 'live':
      live.update(message.state);
      break;
  }
});
