import { AnimationFrame, Metasprite, MetaspriteAnimation, MetaspriteAsset, MetaspriteTile } from '../shared/assets.js';
import { flipH, flipV } from '../shared/chr.js';
import { HostMessage } from '../shared/protocol.js';
import { GraphicsSystem, hex2 } from '../shared/system.js';
import {
  Banner, Choice, LiveToggle, StatusBar, button, checkbox, clamp, el, loadState, post, saveState, selectBox, separator, setInputValue, start, textInput, typing
} from './common.js';
import { Rgb, TileSheet, ZoomCanvas, accent, grid, onPointer, outline, paletteButton, paletteColours } from './render.js';
import { AssetFields, TileSources } from './tiled.js';

/**
 * The metasprite editor. Left: the metasprites of the file and the selected
 * one's name and origin. Middle: the metasprite on a grid, its origin a cross
 * that can be dragged; a click places the tile chosen in the sheet, a drag
 * moves a tile, the right button deletes one. Right: the tile sheet (in the
 * chosen sprite palette) and the animations, with a preview played at 60
 * frames a second.
 *
 * Tiles are at pixel positions of the drawing; the origin is in the same
 * pixels, and the export writes each tile as its offset from the origin.
 * Earlier tiles are drawn over later ones, as the hardware does with OAM.
 */

const AREA = 128;
const OFFSET_MIN = -128;
const OFFSET_MAX = 127;

const state = loadState({
  sprite: 0,
  animation: 0,
  frame: 0,
  tile: 0,
  palette: 0,
  snap: 8 as 1 | 8,
  zoom: 4,
  sheetZoom: 2,
  previewZoom: 3,
  grid: true,
  onion: true
});

let system: GraphicsSystem | undefined;
let asset: MetaspriteAsset | undefined;
let sources: TileSources;
/** The selected tile of the metasprite (its index), if any. */
let selected: number | undefined;
/** The metasprite as it is being changed while the mouse is down. */
let work: Metasprite | undefined;
let drag: { kind: 'tile' | 'origin'; index: number; grabX: number; grabY: number; label: string; moved: boolean } | undefined;
/** The drawing area: its top left in drawing pixels and its size, frozen while dragging. */
let view = { left: 0, top: 0, width: AREA, height: AREA };
let hoverPoint: { x: number; y: number } | undefined;

const app = document.getElementById('app')!;
const banner = new Banner();
const status = new StatusBar();
const live = new LiveToggle();
let canvas: ZoomCanvas;
let sheet: TileSheet;
let previewCanvas: ZoomCanvas;
let fields: AssetFields;
let snapChoice: Choice<number>;
const paletteButtons: Array<ReturnType<typeof paletteButton>> = [];
const flipHButton = button('Flip H', 'Mirror the selected tile left to right (H)', () => toggleTile('flipH', 'Flip horizontally'));
const flipVButton = button('Flip V', 'Mirror the selected tile top to bottom (V)', () => toggleTile('flipV', 'Flip vertically'));
const behindButton = button('Behind', 'Draw the selected tile behind the background\'s opaque pixels (B)', () => toggleTile('behind', 'Behind the background'));
const deleteButton = button('Delete', 'Remove the selected tile (Delete; or right-click it)', () => deleteTile());
const spriteList = el('div', { className: 'list' });
const spriteName = textInput('', 'name', value => renameSprite(value));
const originX = el('input', { type: 'number', min: -999, max: 999, title: 'X of the origin, in the pixels of the drawing' });
const originY = el('input', { type: 'number', min: -999, max: 999, title: 'Y of the origin' });
const stats = el('div', { className: 'stats' });
const animationList = el('div', { className: 'list' });
const animationName = textInput('', 'name', value => renameAnimation(value));
const loopBox = el('input', { type: 'checkbox' });
const framesBox = el('div', { className: 'frames' });
const playButton = button('▶ Play', 'Play the animation at 60 frames a second (Space)', () => togglePlay());
const frameInfo = el('span', { className: 'hint' });
const animationEditor = el('div');

function save(): void {
  saveState(state);
}

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T;
}

function edit(label: string, change: (a: MetaspriteAsset) => void): void {
  if (!asset) return;
  const next = clone(asset);
  change(next);
  asset = next;
  render();
  post({ type: 'edit', label, asset: next });
}

function currentSprite(): Metasprite | undefined {
  return work ?? asset?.metasprites[state.sprite];
}

function currentAnimation(): MetaspriteAnimation | undefined {
  return asset?.animations[state.animation];
}

/** Names compared as the exporter's labels are: case and punctuation do not count. */
function key(name: string): string {
  return (name ?? '').replace(/([a-z0-9])([A-Z])/g, '$1_$2').toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '');
}

function uniqueName(base: string, taken: string[]): string {
  const keys = new Set(taken.map(key));
  if (!keys.has(key(base))) return base;
  for (let n = 2; ; n++) {
    if (!keys.has(key(base + '_' + n))) return base + '_' + n;
  }
}

// ---------------------------------------------------------------- drawing

function spriteColours(palette: number): Rgb[] {
  const sys = system!;
  const colours = paletteColours(sys, sources?.palettes, sys.backgroundPaletteCount + clamp(palette, 0, sys.spritePaletteCount - 1));
  // Colour 0 of a sprite is transparent: no colour, so drawing leaves the pixel alone.
  return [undefined as unknown as Rgb, ...colours.slice(1)];
}

/** The pixels of a tile of the sheet, flipped as a metasprite tile asks; blank when the tile set lacks it. */
function tilePixels(tile: MetaspriteTile): Uint8Array {
  const sys = system!;
  const cache = sources.cache;
  let pixels: Uint8Array = cache && tile.tile >= 0 && tile.tile < cache.count ? cache.pixels(tile.tile) : new Uint8Array(sys.tileWidth * sys.tileHeight);
  if (tile.flipH) pixels = flipH(pixels, sys.tileWidth, sys.tileHeight);
  if (tile.flipV) pixels = flipV(pixels, sys.tileWidth, sys.tileHeight);
  return pixels;
}

/**
 * Draws a metasprite into an image, its origin at (ox, oy) of the image, with
 * an opacity (onion skin); the first tiles over the last.
 */
function paintSprite(image: ImageData, sprite: Metasprite, ox: number, oy: number, alpha = 255): void {
  const sys = system!;
  for (let t = sprite.tiles.length - 1; t >= 0; t--) {
    const tile = sprite.tiles[t];
    const pixels = tilePixels(tile);
    const colours = spriteColours(tile.palette);
    const left = ox + tile.x - sprite.originX;
    const top = oy + tile.y - sprite.originY;
    for (let y = 0; y < sys.tileHeight; y++) {
      const iy = top + y;
      if (iy < 0 || iy >= image.height) continue;
      for (let x = 0; x < sys.tileWidth; x++) {
        const ix = left + x;
        if (ix < 0 || ix >= image.width) continue;
        const colour = colours[pixels[y * sys.tileWidth + x]];
        if (!colour) continue;
        const o = (iy * image.width + ix) * 4;
        const a = alpha / 255;
        const keep = image.data[o + 3] / 255 * (1 - a);
        image.data[o] = Math.round(colour[0] * a + image.data[o] * keep);
        image.data[o + 1] = Math.round(colour[1] * a + image.data[o + 1] * keep);
        image.data[o + 2] = Math.round(colour[2] * a + image.data[o + 2] * keep);
        image.data[o + 3] = Math.max(image.data[o + 3], alpha);
      }
    }
  }
}

/** Box of metasprites around their origins: offsets of the top left and bottom right. */
function bounds(sprites: Metasprite[]): { minX: number; minY: number; maxX: number; maxY: number } {
  const sys = system!;
  let minX = -1, minY = -1, maxX = 1, maxY = 1;
  for (const sprite of sprites) {
    for (const tile of sprite.tiles) {
      minX = Math.min(minX, tile.x - sprite.originX);
      minY = Math.min(minY, tile.y - sprite.originY);
      maxX = Math.max(maxX, tile.x - sprite.originX + sys.tileWidth);
      maxY = Math.max(maxY, tile.y - sprite.originY + sys.tileHeight);
    }
  }
  return { minX, minY, maxX, maxY };
}

/** The drawing area for a metasprite: at least AREA pixels square, grown by whole tiles to hold every tile and the origin. */
function areaFor(sprite: Metasprite | undefined): { left: number; top: number; width: number; height: number } {
  let minX = 0, minY = 0, maxX = AREA, maxY = AREA;
  if (sprite) {
    for (const tile of sprite.tiles) {
      minX = Math.min(minX, tile.x - 8);
      minY = Math.min(minY, tile.y - 8);
      maxX = Math.max(maxX, tile.x + 16);
      maxY = Math.max(maxY, tile.y + 16);
    }
    minX = Math.min(minX, sprite.originX - 8);
    minY = Math.min(minY, sprite.originY - 8);
    maxX = Math.max(maxX, sprite.originX + 8);
    maxY = Math.max(maxY, sprite.originY + 8);
  }
  const left = Math.floor(minX / 8) * 8;
  const top = Math.floor(minY / 8) * 8;
  return { left, top, width: Math.ceil(maxX / 8) * 8 - left, height: Math.ceil(maxY / 8) * 8 - top };
}

/** Sprites on each scanline the metasprite covers, and the busiest line. */
function scanlines(sprite: Metasprite): { max: number; maxY: number; over: number[] } {
  const sys = system!;
  const counts = new Map<number, number>();
  for (const tile of sprite.tiles) {
    for (let y = tile.y; y < tile.y + sys.tileHeight; y++) counts.set(y, (counts.get(y) ?? 0) + 1);
  }
  let max = 0, maxY = 0;
  const over: number[] = [];
  for (const [y, n] of counts) {
    if (n > max || (n === max && y < maxY)) { max = n; maxY = y; }
    if (n > sys.spritesPerScanline) over.push(y);
  }
  return { max, maxY, over: over.sort((a, b) => a - b) };
}

// ---------------------------------------------------------------- building

function build(): void {
  const sys = system!;
  app.textContent = '';
  sources = new TileSources(sys, banner);

  snapChoice = new Choice<number>([
    { value: 1, label: '1 px', title: 'Tiles move by single pixels' },
    { value: 8, label: '8 px', title: 'Tiles snap to the 8 pixel grid' }
  ], state.snap, value => { state.snap = value === 1 ? 1 : 8; save(); });

  const zoom = selectBox([2, 3, 4, 5, 6, 8].map(z => ({ value: z, label: z + '×' })), state.zoom, z => {
    state.zoom = z;
    canvas.setZoom(z);
    save();
    render();
  }, 'Zoom');

  const palettes = el('span', { className: 'group' });
  paletteButtons.length = 0;
  for (let p = 0; p < sys.spritePaletteCount; p++) {
    const b = paletteButton(sys, String(p), 'Sprite palette ' + p + ' (' + (p + 1) + '): for the selected tile and the next ones placed', () => setPalette(p));
    paletteButtons.push(b);
    palettes.append(b.element);
  }

  const toolbar = el('div', { className: 'toolbar' },
    el('label', { title: 'How tiles snap when placed or dragged' }, 'Snap', snapChoice.element),
    el('label', {}, 'Zoom', zoom),
    checkbox('Grid', 'An 8 pixel grid', state.grid, on => { state.grid = on; save(); render(); }),
    separator(),
    el('label', {}, 'Palette', palettes),
    separator(),
    flipHButton, flipVButton, behindButton, deleteButton,
    separator(),
    live.element
  );

  // Left: the metasprites.
  const spriteButtons = el('span', { className: 'group' },
    button('New', 'Add an empty metasprite', addSprite),
    button('Duplicate', 'Copy the selected metasprite', duplicateSprite),
    button('Delete', 'Remove the selected metasprite (and the animation frames that show it)', deleteSprite));
  for (const input of [originX, originY]) {
    input.addEventListener('change', () => {
      const x = Math.round(Number(originX.value));
      const y = Math.round(Number(originY.value));
      if (!Number.isFinite(x) || !Number.isFinite(y)) return;
      editSprite('Move the origin', s => { s.originX = x; s.originY = y; });
    });
  }
  const props = el('div', { className: 'props' },
    el('span', { text: 'Name' }), spriteName, el('span'),
    el('span', { text: 'Origin' }), el('span', { className: 'row' }, originX, originY), el('span'));
  fields = new AssetFields(['prefix'], (field, value) => edit('Change ' + field, a => {
    const record = a as unknown as Record<string, unknown>;
    if (value || field === 'prefix') record[field] = value;
    else delete record[field];
  }));
  const left = el('div', { className: 'side ms-left' },
    el('section', {}, el('div', { className: 'row spread' }, el('h3', { text: 'Metasprites' }), spriteButtons), spriteList),
    el('section', {}, el('h3', { text: 'Metasprite' }), props),
    el('section', {}, el('h3', { text: 'File' }), fields.element));

  // Middle: the drawing.
  canvas = new ZoomCanvas(AREA, AREA, drawOverlay);
  canvas.setZoom(state.zoom);
  canvas.frame.classList.add('checker');
  onPointer(canvas, { down: onDown, drag: onDrag, up: onUp, hover: onHover });
  const main = el('div', { className: 'main ms-main' }, canvas.frame, stats,
    el('div', {
      className: 'hint',
      text: 'Click: place the tile chosen in the sheet · drag a tile to move it · drag the cross to move the origin · right-click a tile: delete · Alt+click: place over a tile · arrows nudge (Shift: 8 px)'
    }));

  // Right: the tile sheet and the animations.
  sheet = new TileSheet(sys, state.sheetZoom);
  sheet.minRows = 1;
  onPointer(sheet.surface, {
    down: p => {
      const tile = sheet.tileAt(p);
      if (tile === undefined) return;
      state.tile = tile;
      save();
      if (selected !== undefined) {
        editTile('Change the tile', t => { t.tile = tile; });
      } else {
        render();
      }
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

  previewCanvas = new ZoomCanvas(16, 16, drawPreviewOverlay);
  previewCanvas.setZoom(state.previewZoom);
  previewCanvas.frame.classList.add('checker');
  loopBox.addEventListener('change', () => editAnimation('Loop', a => { a.loop = loopBox.checked; }));
  const previewZoom = selectBox([1, 2, 3, 4].map(z => ({ value: z, label: z + '×' })), state.previewZoom, z => {
    state.previewZoom = z;
    previewCanvas.setZoom(z);
    save();
    renderPreview();
  }, 'Zoom of the preview');
  animationEditor.append(
    el('div', { className: 'props' },
      el('span', { text: 'Name' }), animationName, el('span'),
      el('span', { text: 'Loop' }), el('label', { title: 'Start again after the last frame (else stay on it)' }, loopBox, 'start again after the last frame'), el('span')),
    el('div', { className: 'row spread' }, el('span', { className: 'hint', text: 'Frames: metasprite, duration in 1/60 s' }),
      button('+ Frame', 'Add a frame showing the selected metasprite', addFrame)),
    framesBox,
    el('div', { className: 'preview' },
      el('div', { className: 'row' }, playButton,
        checkbox('Onion skin', 'Show the previous frame faintly under the current one', state.onion, on => { state.onion = on; save(); renderPreview(); }),
        previewZoom),
      previewCanvas.frame, frameInfo));
  const animationButtons = el('span', { className: 'group' },
    button('New', 'Add an animation of the selected metasprite', addAnimation),
    button('Delete', 'Remove the selected animation', deleteAnimation));
  const right = el('div', { className: 'side right ms-right' },
    el('section', {}, el('div', { className: 'row spread' }, el('h3', { text: 'Tiles' }), sheetZoom), el('div', { className: 'sheet-scroll' }, sheet.surface.frame)),
    el('section', {}, el('div', { className: 'row spread' }, el('h3', { text: 'Animations' }), animationButtons), animationList, animationEditor));

  const body = el('div', { className: 'body' }, left, main, right);
  app.append(toolbar, banner.element, body, status.element);
  document.addEventListener('keydown', onKey);
}

// ---------------------------------------------------------------- edits

function editSprite(label: string, change: (s: Metasprite) => void): void {
  if (!asset?.metasprites[state.sprite]) return;
  edit(label, a => change(a.metasprites[state.sprite]));
}

function editTile(label: string, change: (t: MetaspriteTile) => void): void {
  const index = selected;
  if (index === undefined || !asset?.metasprites[state.sprite]?.tiles[index]) return;
  editSprite(label, s => change(s.tiles[index]));
}

function toggleTile(field: 'flipH' | 'flipV' | 'behind', label: string): void {
  editTile(label, t => { t[field] = !t[field]; });
}

function setPalette(p: number): void {
  state.palette = p;
  save();
  if (selected !== undefined) {
    editTile('Change the palette', t => { t.palette = p; });
  } else {
    render();
  }
}

function deleteTile(index = selected): void {
  if (index === undefined || !asset?.metasprites[state.sprite]?.tiles[index]) return;
  selected = undefined;
  editSprite('Delete a tile', s => { s.tiles.splice(index, 1); });
}

function selectSprite(index: number): void {
  state.sprite = index;
  selected = undefined;
  save();
  render();
}

function addSprite(): void {
  if (!asset) return;
  const name = uniqueName('sprite' + (asset.metasprites.length + 1), asset.metasprites.map(m => m.name));
  edit('Add a metasprite', a => { a.metasprites.push({ name, originX: AREA / 2, originY: AREA / 2, tiles: [] }); });
  selectSprite(asset.metasprites.length - 1);
}

function duplicateSprite(): void {
  const sprite = asset?.metasprites[state.sprite];
  if (!asset || !sprite) return;
  const copy = clone(sprite);
  copy.name = uniqueName(sprite.name + '_copy', asset.metasprites.map(m => m.name));
  const at = state.sprite + 1;
  edit('Duplicate a metasprite', a => { a.metasprites.splice(at, 0, copy); });
  selectSprite(at);
}

function deleteSprite(): void {
  const sprite = asset?.metasprites[state.sprite];
  if (!asset || !sprite) return;
  const gone = key(sprite.name);
  let frames = 0;
  edit('Delete a metasprite', a => {
    a.metasprites.splice(state.sprite, 1);
    for (const animation of a.animations) {
      const before = animation.frames.length;
      animation.frames = animation.frames.filter(f => key(f.metasprite) !== gone);
      frames += before - animation.frames.length;
    }
  });
  if (frames > 0) status.show(frames + ' animation frame' + (frames === 1 ? '' : 's') + ' showing "' + sprite.name + '" removed too (Undo brings them back)');
  selectSprite(clamp(state.sprite, 0, Math.max(0, asset.metasprites.length - 1)));
}

function renameSprite(value: string): void {
  const sprite = asset?.metasprites[state.sprite];
  const name = value.trim();
  if (!asset || !sprite || !name || name === sprite.name) return;
  if (asset.metasprites.some((m, i) => i !== state.sprite && key(m.name) === key(name))) {
    status.show('A metasprite is already named "' + name + '" (names are compared as labels)', 'error');
    setInputValue(spriteName, sprite.name);
    return;
  }
  const old = key(sprite.name);
  edit('Rename a metasprite', a => {
    a.metasprites[state.sprite].name = name;
    for (const animation of a.animations) {
      for (const frame of animation.frames) {
        if (key(frame.metasprite) === old) frame.metasprite = name;
      }
    }
  });
}

function editAnimation(label: string, change: (a: MetaspriteAnimation) => void): void {
  if (!asset?.animations[state.animation]) return;
  edit(label, a => change(a.animations[state.animation]));
}

function addAnimation(): void {
  if (!asset) return;
  const name = uniqueName('anim' + (asset.animations.length + 1), asset.animations.map(a => a.name));
  const sprite = asset.metasprites[state.sprite];
  const frames: AnimationFrame[] = sprite ? [{ metasprite: sprite.name, duration: 8 }] : [];
  edit('Add an animation', a => { a.animations.push({ name, loop: true, frames }); });
  state.animation = asset.animations.length - 1;
  state.frame = 0;
  save();
  render();
}

function deleteAnimation(): void {
  if (!asset?.animations[state.animation]) return;
  edit('Delete an animation', a => { a.animations.splice(state.animation, 1); });
  state.animation = clamp(state.animation, 0, Math.max(0, asset.animations.length - 1));
  state.frame = 0;
  save();
  render();
}

function renameAnimation(value: string): void {
  const animation = currentAnimation();
  const name = value.trim();
  if (!asset || !animation || !name || name === animation.name) return;
  if (asset.animations.some((a, i) => i !== state.animation && key(a.name) === key(name))) {
    status.show('An animation is already named "' + name + '"', 'error');
    setInputValue(animationName, animation.name);
    return;
  }
  editAnimation('Rename an animation', a => { a.name = name; });
}

function addFrame(): void {
  const sprite = asset?.metasprites[state.sprite];
  if (!sprite) return;
  if (!currentAnimation()) {
    addAnimation();
    return;
  }
  const at = Math.min(state.frame + 1, currentAnimation()!.frames.length);
  editAnimation('Add a frame', a => { a.frames.splice(at, 0, { metasprite: sprite.name, duration: 8 }); });
  state.frame = at;
  save();
  renderPreview();
}

// ---------------------------------------------------------------- the drawing area

function toDrawing(p: { x: number; y: number }): { x: number; y: number } {
  return { x: p.x + view.left, y: p.y + view.top };
}

/** The topmost tile (the first in the list) under a drawing pixel. */
function tileAt(sprite: Metasprite, d: { x: number; y: number }): number | undefined {
  const sys = system!;
  for (let t = 0; t < sprite.tiles.length; t++) {
    const tile = sprite.tiles[t];
    if (d.x >= tile.x && d.x < tile.x + sys.tileWidth && d.y >= tile.y && d.y < tile.y + sys.tileHeight) return t;
  }
  return undefined;
}

function nearOrigin(sprite: Metasprite, d: { x: number; y: number }): boolean {
  const reach = Math.max(1, Math.round(6 / state.zoom));
  return Math.abs(d.x - sprite.originX) <= reach && Math.abs(d.y - sprite.originY) <= reach;
}

/** Where a tile goes for the mouse at a drawing pixel: the cell under it (8 px), or centred on it (1 px). */
function placement(d: { x: number; y: number }): { x: number; y: number } {
  const sys = system!;
  return state.snap === 8
    ? { x: Math.floor(d.x / 8) * 8, y: Math.floor(d.y / 8) * 8 }
    : { x: d.x - Math.floor(sys.tileWidth / 2), y: d.y - Math.floor(sys.tileHeight / 2) };
}

function onDown(p: { x: number; y: number }, event: PointerEvent): void {
  const sprite = asset?.metasprites[state.sprite];
  if (!asset || !sprite || !canvas.inside(p)) {
    if (asset && !sprite) status.show('Add a metasprite first (New, on the left)');
    return;
  }
  const d = toDrawing(p);
  const hit = tileAt(sprite, d);
  if (event.button === 2) {
    if (hit !== undefined) deleteTile(hit);
    return;
  }
  work = clone(sprite);
  if (nearOrigin(sprite, d) && !event.altKey) {
    drag = { kind: 'origin', index: -1, grabX: 0, grabY: 0, label: 'Move the origin', moved: false };
  } else if (hit !== undefined && !event.altKey) {
    selected = hit;
    const tile = sprite.tiles[hit];
    drag = { kind: 'tile', index: hit, grabX: d.x - tile.x, grabY: d.y - tile.y, label: 'Move a tile', moved: false };
  } else {
    const at = placement(d);
    work.tiles.unshift({ x: at.x, y: at.y, tile: state.tile, palette: state.palette, flipH: false, flipV: false, behind: false });
    // A new tile goes first: it is drawn over the others, as the one just placed should be.
    selected = 0;
    drag = { kind: 'tile', index: 0, grabX: d.x - at.x, grabY: d.y - at.y, label: 'Place a tile', moved: true };
  }
  render();
}

function onDrag(p: { x: number; y: number }): void {
  if (!drag || !work) return;
  const d = toDrawing(p);
  if (drag.kind === 'origin') {
    const x = state.snap === 8 ? Math.round(d.x / 8) * 8 : d.x;
    const y = state.snap === 8 ? Math.round(d.y / 8) * 8 : d.y;
    if (x !== work.originX || y !== work.originY) {
      work.originX = x;
      work.originY = y;
      drag.moved = true;
    }
  } else {
    const tile = work.tiles[drag.index];
    let x = d.x - drag.grabX;
    let y = d.y - drag.grabY;
    if (state.snap === 8) {
      x = Math.round(x / 8) * 8;
      y = Math.round(y / 8) * 8;
    }
    if (x !== tile.x || y !== tile.y) {
      tile.x = x;
      tile.y = y;
      drag.moved = true;
    }
  }
  onHover(canvas.inside(p) ? p : undefined);
  render();
}

function onUp(): void {
  const done = drag;
  const changed = work;
  drag = undefined;
  work = undefined;
  if (done && changed && done.moved) {
    edit(done.label, a => { a.metasprites[state.sprite] = changed; });
  } else {
    render();
  }
}

function onHover(p: { x: number; y: number } | undefined): void {
  hoverPoint = p ? toDrawing(p) : undefined;
  const sprite = currentSprite();
  if (!p || !sprite || !system) {
    status.hover('');
  } else {
    const d = hoverPoint!;
    const hit = tileAt(sprite, d);
    const tile = hit !== undefined ? sprite.tiles[hit] : undefined;
    status.hover('x ' + d.x + ', y ' + d.y + ' · from the origin ' + (d.x - sprite.originX) + ', ' + (d.y - sprite.originY)
      + (tile ? ' · tile ' + hex2(tile.tile) + ' (#' + hit + '), palette ' + tile.palette : ''));
  }
  if (!drag) renderCanvas();
}

// ---------------------------------------------------------------- rendering

function render(): void {
  if (!system || !asset || !canvas) return;
  state.sprite = clamp(state.sprite, 0, Math.max(0, asset.metasprites.length - 1));
  state.animation = clamp(state.animation, 0, Math.max(0, asset.animations.length - 1));
  const sprite = asset.metasprites[state.sprite];
  if (selected !== undefined && (!sprite || selected >= (work ?? sprite).tiles.length)) selected = undefined;

  renderCanvas();
  renderList();
  renderSelection();
  renderAnimations();

  // The sheet, in the chosen sprite palette, colour 0 transparent.
  sheet.cache = sources.cache;
  sheet.colours = spriteColours(state.palette);
  sheet.selection = { from: state.tile, to: state.tile };
  sheet.render();

  fields.set({ prefix: asset.prefix, chr: asset.chr, palettes: asset.palettes });
  sources.showProblems(!!asset.chr);
  status.info((asset.metasprites.length) + ' metasprite' + (asset.metasprites.length === 1 ? '' : 's') + ' · '
    + asset.animations.length + ' animation' + (asset.animations.length === 1 ? '' : 's') + ' · '
    + (sources.cache ? sources.cache.count + ' tiles' : 'no tile set'));
}

function renderCanvas(): void {
  const sys = system!;
  const sprite = currentSprite();
  if (!drag) {
    view = areaFor(sprite);
  }
  if (canvas.width !== view.width || canvas.height !== view.height) {
    canvas.resize(view.width, view.height);
  }
  const image = canvas.image;
  image.data.fill(0);
  if (sprite) {
    paintSprite(image, sprite, sprite.originX - view.left, sprite.originY - view.top);
    // Where a click would put the chosen tile.
    if (hoverPoint && !drag && tileAt(sprite, hoverPoint) === undefined && !nearOrigin(sprite, hoverPoint)) {
      const at = placement(hoverPoint);
      const ghost: Metasprite = { name: '', originX: 0, originY: 0, tiles: [{ x: at.x, y: at.y, tile: state.tile, palette: state.palette, flipH: false, flipV: false, behind: false }] };
      paintSprite(image, ghost, -view.left, -view.top, 110);
    }
  }
  canvas.present();

  // The numbers that matter on the hardware.
  stats.textContent = '';
  if (!sprite) {
    stats.append(el('span', { text: asset && asset.metasprites.length === 0 ? 'No metasprite yet: add one with New.' : '' }));
    return;
  }
  const lines = scanlines(sprite);
  const box = bounds([sprite]);
  stats.append(
    el('span', { text: sprite.tiles.length + ' tile' + (sprite.tiles.length === 1 ? '' : 's') + ' (sprites)' }),
    el('span', {
      className: lines.max > sys.spritesPerScanline ? 'warn' : '',
      text: 'max ' + lines.max + ' on one scanline (limit ' + sys.spritesPerScanline + ')',
      title: 'The ' + sys.displayName + ' shows at most ' + sys.spritesPerScanline + ' sprites on one line of pixels; more disappear (or flicker, if the game cycles them)'
    }),
    el('span', { text: 'size ' + (box.maxX - box.minX) + '×' + (box.maxY - box.minY) + ' px' }),
    el('span', { text: 'origin ' + sprite.originX + ', ' + sprite.originY }));
  if (lines.over.length > 0) {
    stats.append(el('span', {
      className: 'warn',
      text: '⚠ ' + lines.over.length + ' scanline' + (lines.over.length === 1 ? '' : 's') + ' (y ' + lines.over[0] + (lines.over.length > 1 ? '…' + lines.over[lines.over.length - 1] : '')
        + ') over the limit of ' + sys.spritesPerScanline + ' sprites: the extra ones will not show'
    }));
  }
  const far = sprite.tiles.filter(t => {
    const dx = t.x - sprite.originX;
    const dy = t.y - sprite.originY;
    return dx < OFFSET_MIN || dx > OFFSET_MAX || dy <= OFFSET_MIN || dy > OFFSET_MAX;
  }).length;
  if (far > 0) {
    stats.append(el('span', { className: 'warn', text: '⚠ ' + far + ' tile' + (far === 1 ? ' is' : 's are') + ' too far from the origin (offsets are -128..127, -128 not allowed for y)' }));
  }
}

function drawOverlay(ctx: CanvasRenderingContext2D, zoom: number): void {
  const sys = system;
  const sprite = currentSprite();
  if (!sys) return;
  if (state.grid) {
    grid(ctx, zoom, view.width, view.height, 8, 8, 'rgba(128,128,128,0.35)');
  }
  if (!sprite) return;

  // Scanlines with more sprites than the hardware shows.
  const lines = scanlines(sprite);
  if (lines.over.length > 0) {
    ctx.save();
    ctx.fillStyle = 'rgba(255,60,60,0.22)';
    for (const y of lines.over) ctx.fillRect(0, (y - view.top) * zoom, view.width * zoom, zoom);
    ctx.restore();
  }

  // Every tile outlined faintly, tiles behind the background dashed, the selected one in the accent colour.
  sprite.tiles.forEach((tile, t) => {
    const x = tile.x - view.left;
    const y = tile.y - view.top;
    if (t === selected) return;
    ctx.save();
    if (tile.behind) ctx.setLineDash([3, 3]);
    const missing = !sources.cache || tile.tile >= sources.cache.count;
    outline(ctx, zoom, x, y, sys.tileWidth, sys.tileHeight, missing ? 'rgba(255,80,80,0.9)' : tile.behind ? 'rgba(255,200,0,0.7)' : 'rgba(255,255,255,0.28)', 1);
    ctx.restore();
  });
  if (selected !== undefined && sprite.tiles[selected]) {
    const tile = sprite.tiles[selected];
    outline(ctx, zoom, tile.x - view.left, tile.y - view.top, sys.tileWidth, sys.tileHeight, accent(), 2);
  }

  // The origin: a cross.
  const ox = (sprite.originX - view.left) * zoom;
  const oy = (sprite.originY - view.top) * zoom;
  const arm = Math.max(8, zoom * 4);
  ctx.save();
  ctx.lineWidth = 3;
  ctx.strokeStyle = 'rgba(0,0,0,0.7)';
  ctx.beginPath();
  ctx.moveTo(ox - arm, oy); ctx.lineTo(ox + arm, oy);
  ctx.moveTo(ox, oy - arm); ctx.lineTo(ox, oy + arm);
  ctx.stroke();
  ctx.lineWidth = 1.5;
  ctx.strokeStyle = '#ff40ff';
  ctx.stroke();
  ctx.beginPath();
  ctx.arc(ox, oy, Math.max(3, zoom), 0, Math.PI * 2);
  ctx.stroke();
  ctx.restore();
  canvas.canvas.style.cursor = hoverPoint && nearOrigin(sprite, hoverPoint) ? 'move' : hoverPoint && tileAt(sprite, hoverPoint) !== undefined ? 'grab' : 'crosshair';
}

function renderList(): void {
  if (!asset) return;
  spriteList.textContent = '';
  if (asset.metasprites.length === 0) {
    spriteList.append(el('div', { className: 'list-empty', text: 'No metasprite yet.' }));
  }
  asset.metasprites.forEach((sprite, i) => {
    const item = el('button', { type: 'button', className: 'list-item' + (i === state.sprite ? ' selected' : ''), title: sprite.name, onclick: () => selectSprite(i) },
      el('span', { className: 'thumb' }, thumbnail(sprite)),
      el('span', { className: 'name', text: sprite.name }),
      el('span', { className: 'count', text: String(sprite.tiles.length) }));
    spriteList.append(item);
  });
}

/** A small picture of a metasprite, for the list. */
function thumbnail(sprite: Metasprite): HTMLCanvasElement {
  const box = bounds([sprite]);
  const width = Math.max(1, box.maxX - box.minX);
  const height = Math.max(1, box.maxY - box.minY);
  const image = new ImageData(width, height);
  paintSprite(image, sprite, -box.minX, -box.minY);
  const c = el('canvas');
  c.width = width;
  c.height = height;
  c.getContext('2d')!.putImageData(image, 0, 0);
  const scale = Math.max(1, Math.floor(26 / Math.max(width, height)));
  c.style.width = Math.min(26, width * scale) + 'px';
  c.style.height = Math.min(26, height * scale) + 'px';
  if (width * scale > 26 || height * scale > 26) {
    const fit = 26 / Math.max(width, height);
    c.style.width = Math.round(width * fit) + 'px';
    c.style.height = Math.round(height * fit) + 'px';
  }
  return c;
}

function renderSelection(): void {
  const sys = system!;
  const sprite = asset?.metasprites[state.sprite];
  setInputValue(spriteName, sprite?.name ?? '');
  spriteName.disabled = !sprite;
  if (document.activeElement !== originX) originX.value = sprite ? String(sprite.originX) : '';
  if (document.activeElement !== originY) originY.value = sprite ? String(sprite.originY) : '';
  originX.disabled = originY.disabled = !sprite;

  const tile = selected !== undefined ? (work ?? sprite)?.tiles[selected] : undefined;
  for (const [b, on] of [[flipHButton, tile?.flipH], [flipVButton, tile?.flipV], [behindButton, tile?.behind]] as Array<[HTMLButtonElement, boolean | undefined]>) {
    b.disabled = !tile;
    b.setAttribute('aria-pressed', String(!!on));
  }
  deleteButton.disabled = !tile;
  const palette = tile ? tile.palette : state.palette;
  paletteButtons.forEach((b, p) => {
    b.paint(paletteColours(sys, sources.palettes, sys.backgroundPaletteCount + p));
    b.element.setAttribute('aria-pressed', String(p === palette));
  });
  snapChoice.set(state.snap);
}

// ---------------------------------------------------------------- animations

/** One video frame, in milliseconds. */
const TICK = 1000 / 60;
let playing = false;
let playTimer: number | undefined;
let playStart = 0;
/** Ticks (1/60 s) the current frame has been shown while playing. */
let shown = 0;

function renderAnimations(): void {
  if (!asset) return;
  animationList.textContent = '';
  if (asset.animations.length === 0) {
    animationList.append(el('div', { className: 'list-empty', text: 'No animation yet.' }));
  }
  asset.animations.forEach((animation, i) => {
    animationList.append(el('button', {
      type: 'button', className: 'list-item' + (i === state.animation ? ' selected' : ''), title: animation.name,
      onclick: () => { state.animation = i; state.frame = 0; stop(); save(); render(); }
    },
    el('span', { className: 'name', text: animation.name }),
    el('span', { className: 'count', text: animation.frames.length + ' frame' + (animation.frames.length === 1 ? '' : 's') + (animation.loop ? ' · loop' : '') })));
  });

  const animation = currentAnimation();
  animationEditor.classList.toggle('hidden', !animation);
  if (!animation) {
    stop();
    return;
  }
  setInputValue(animationName, animation.name);
  loopBox.checked = animation.loop;
  state.frame = clamp(state.frame, 0, Math.max(0, animation.frames.length - 1));

  framesBox.textContent = '';
  const names = asset.metasprites.map(m => ({ value: m.name, label: m.name }));
  animation.frames.forEach((frame, f) => {
    const options = names.some(n => key(n.value) === key(frame.metasprite)) ? names : [{ value: frame.metasprite, label: frame.metasprite + ' (missing)' }, ...names];
    const sprite = selectBox(options, options.find(o => key(o.value) === key(frame.metasprite))?.value ?? frame.metasprite,
      value => editAnimation('Change a frame', a => { a.frames[f].metasprite = value; }), 'The metasprite shown');
    const duration = el('input', { type: 'number', min: 1, max: 255, value: String(frame.duration), title: 'Frames (1/60 s) it stays: 1 to 255' });
    duration.addEventListener('change', () => {
      const value = clamp(Math.round(Number(duration.value)) || 1, 1, 255);
      editAnimation('Change a duration', a => { a.frames[f].duration = value; });
    });
    const move = (to: number) => () => {
      if (to < 0 || to >= animation.frames.length) return;
      editAnimation('Move a frame', a => { const [moved] = a.frames.splice(f, 1); a.frames.splice(to, 0, moved); });
      state.frame = to;
      save();
      renderPreview();
    };
    const up = button('↑', 'Earlier', move(f - 1));
    const down = button('↓', 'Later', move(f + 1));
    up.disabled = f === 0;
    down.disabled = f === animation.frames.length - 1;
    framesBox.append(el('div', { className: 'frame-row' + (f === state.frame ? ' current' : '') },
      el('span', { className: 'index', text: String(f), title: 'Show this frame in the preview', onclick: () => { stop(); state.frame = f; save(); renderAnimations(); } }),
      sprite, duration, up, down,
      button('✕', 'Remove this frame', () => {
        editAnimation('Remove a frame', a => { a.frames.splice(f, 1); });
      })));
  });
  if (animation.frames.length === 0) {
    framesBox.append(el('div', { className: 'list-empty warn', text: 'An animation needs at least one frame.' }));
  }
  renderPreview();
}

function frameSprite(animation: MetaspriteAnimation, f: number): Metasprite | undefined {
  const frame = animation.frames[f];
  return frame ? asset?.metasprites.find(m => key(m.name) === key(frame.metasprite)) : undefined;
}

/** The preview: the current frame, the previous one faintly under it; the box holds every frame, origin fixed. */
function renderPreview(): void {
  const animation = currentAnimation();
  if (!animation || !system || !previewCanvas) return;
  const sprites = animation.frames.map((_, f) => frameSprite(animation, f)).filter((s): s is Metasprite => !!s);
  const box = bounds(sprites);
  const margin = 2;
  const width = box.maxX - box.minX + margin * 2;
  const height = box.maxY - box.minY + margin * 2;
  if (previewCanvas.width !== width || previewCanvas.height !== height) previewCanvas.resize(width, height);
  const ox = margin - box.minX;
  const oy = margin - box.minY;
  const image = previewCanvas.image;
  image.data.fill(0);
  const count = animation.frames.length;
  if (state.onion && count > 1 && (state.frame > 0 || animation.loop)) {
    const previous = frameSprite(animation, (state.frame - 1 + count) % count);
    if (previous) paintSprite(image, previous, ox, oy, 80);
  }
  const current = frameSprite(animation, state.frame);
  if (current) paintSprite(image, current, ox, oy);
  previewCanvas.present();

  const total = animation.frames.reduce((sum, f) => sum + (f.duration || 0), 0);
  const frame = animation.frames[state.frame];
  frameInfo.textContent = count === 0 ? '' : 'frame ' + (state.frame + 1) + '/' + count + ' · ' + (frame ? frame.metasprite + ', ' + frame.duration + '/60 s' : '')
    + ' · cycle ' + total + ' frames (' + (total / 60).toFixed(2) + ' s)';
  playButton.textContent = playing ? '❚❚ Pause' : '▶ Play';
  playButton.setAttribute('aria-pressed', String(playing));
  for (const [i, row] of Array.from(framesBox.children).entries()) {
    row.classList.toggle('current', i === state.frame);
  }
}

function drawPreviewOverlay(ctx: CanvasRenderingContext2D, zoom: number): void {
  const animation = currentAnimation();
  if (!animation) return;
  const sprites = animation.frames.map((_, f) => frameSprite(animation, f)).filter((s): s is Metasprite => !!s);
  const box = bounds(sprites);
  const ox = (2 - box.minX) * zoom;
  const oy = (2 - box.minY) * zoom;
  ctx.save();
  ctx.strokeStyle = 'rgba(255,64,255,0.8)';
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(ox - 4, oy + 0.5); ctx.lineTo(ox + 5, oy + 0.5);
  ctx.moveTo(ox + 0.5, oy - 4); ctx.lineTo(ox + 0.5, oy + 5);
  ctx.stroke();
  ctx.restore();
}

function togglePlay(): void {
  if (playing) {
    stop();
  } else {
    play();
  }
  renderPreview();
}

function play(): void {
  const animation = currentAnimation();
  if (!animation || animation.frames.length === 0) return;
  if (!animation.loop && state.frame === animation.frames.length - 1) state.frame = 0;
  playing = true;
  shown = 0;
  playStart = performance.now();
  const step = (now: number) => {
    if (!playing) return;
    const current = currentAnimation();
    if (!current || current.frames.length === 0) {
      stop();
      renderPreview();
      return;
    }
    // Whole 60 Hz ticks since the last step, so the timing is a frame counter's.
    const ticks = Math.floor((now - playStart) / TICK);
    if (ticks > 0) {
      shown += ticks;
      playStart += ticks * TICK;
    }
    let changed = false;
    while (shown >= Math.max(1, current.frames[state.frame]?.duration ?? 1)) {
      shown -= Math.max(1, current.frames[state.frame]?.duration ?? 1);
      if (state.frame + 1 < current.frames.length) {
        state.frame++;
      } else if (current.loop) {
        state.frame = 0;
      } else {
        stop();
        renderPreview();
        return;
      }
      changed = true;
    }
    if (changed) renderPreview();
    playTimer = requestAnimationFrame(step);
  };
  playTimer = requestAnimationFrame(step);
}

function stop(): void {
  playing = false;
  if (playTimer !== undefined) cancelAnimationFrame(playTimer);
  playTimer = undefined;
}

// ---------------------------------------------------------------- keys

function onKey(event: KeyboardEvent): void {
  if (typing(event) || !system || event.ctrlKey || event.metaKey || event.altKey) return;
  const k = event.key;
  if (k === ' ' && (event.target as HTMLElement | null)?.tagName === 'BUTTON') return; // the button's own click
  const lower = k.toLowerCase();
  if (k === 'Delete' || k === 'Backspace') {
    deleteTile();
  } else if (k.startsWith('Arrow') && selected !== undefined) {
    const step = event.shiftKey ? 8 : 1;
    const dx = k === 'ArrowLeft' ? -step : k === 'ArrowRight' ? step : 0;
    const dy = k === 'ArrowUp' ? -step : k === 'ArrowDown' ? step : 0;
    editTile('Nudge a tile', t => { t.x += dx; t.y += dy; });
  } else if (lower === 'h') {
    toggleTile('flipH', 'Flip horizontally');
  } else if (lower === 'v') {
    toggleTile('flipV', 'Flip vertically');
  } else if (lower === 'b') {
    toggleTile('behind', 'Behind the background');
  } else if (k >= '1' && k <= String(system.spritePaletteCount)) {
    setPalette(Number(k) - 1);
  } else if (k === 'Escape') {
    selected = undefined;
    render();
  } else if (k === ' ') {
    togglePlay();
  } else if (k === 'PageUp' || k === 'PageDown') {
    if (asset && asset.metasprites.length > 0) selectSprite(clamp(state.sprite + (k === 'PageUp' ? -1 : 1), 0, asset.metasprites.length - 1));
  } else {
    return;
  }
  event.preventDefault();
}

// ---------------------------------------------------------------- messages

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
      const next = message.asset as MetaspriteAsset;
      next.metasprites = Array.isArray(next.metasprites) ? next.metasprites.filter(m => m && typeof m === 'object') : [];
      next.animations = Array.isArray(next.animations) ? next.animations.filter(a => a && typeof a === 'object') : [];
      for (const sprite of next.metasprites) {
        sprite.name = typeof sprite.name === 'string' ? sprite.name : '';
        sprite.originX = Number(sprite.originX) || 0;
        sprite.originY = Number(sprite.originY) || 0;
        sprite.tiles = Array.isArray(sprite.tiles) ? sprite.tiles.filter(t => t && typeof t === 'object') : [];
        for (const tile of sprite.tiles) {
          tile.x = Number(tile.x) || 0;
          tile.y = Number(tile.y) || 0;
          tile.tile = Number(tile.tile) || 0;
          tile.palette = Number(tile.palette) || 0;
          tile.flipH = !!tile.flipH;
          tile.flipV = !!tile.flipV;
          tile.behind = !!tile.behind;
        }
      }
      for (const animation of next.animations) {
        animation.name = typeof animation.name === 'string' ? animation.name : '';
        animation.loop = animation.loop !== false;
        animation.frames = Array.isArray(animation.frames) ? animation.frames.filter(f => f && typeof f === 'object') : [];
      }
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
