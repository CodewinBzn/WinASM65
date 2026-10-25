import { PaletteAsset } from '../shared/assets.js';
import { decodeTile, tileCount } from '../shared/chr.js';
import { GraphicsSystem, colorsPerTile, greys, hex2, rgbOf } from '../shared/system.js';
import { el } from './common.js';

export type Rgb = [number, number, number];

/** Decoded tiles of a tile set, decoded once each. */
export class TileCache {
  private readonly decoded = new Map<number, Uint8Array>();
  readonly count: number;

  constructor(readonly system: GraphicsSystem, readonly data: Uint8Array) {
    this.count = tileCount(system, data);
  }

  pixels(index: number): Uint8Array {
    let pixels = this.decoded.get(index);
    if (!pixels) {
      pixels = decodeTile(this.system, this.data, index);
      this.decoded.set(index, pixels);
    }
    return pixels;
  }
}

/**
 * The RGB colours of a palette of a palette file. Without a file, greys. For a
 * background palette drawn on a screen (`universal`), colour 0 is palette 0's
 * colour 0, as the hardware shows it (and as the designer renders it).
 */
export function paletteColours(system: GraphicsSystem, palettes: PaletteAsset | undefined, index: number, universal = false): Rgb[] {
  const count = colorsPerTile(system);
  if (!palettes) {
    return greys(count).map(rgbOf);
  }
  const colours: Rgb[] = [];
  for (let c = 0; c < count; c++) {
    colours.push(masterRgb(system, palettes.palettes?.[index]?.colors?.[c] ?? 0x0f));
  }
  if (universal) {
    colours[0] = masterRgb(system, palettes.palettes?.[0]?.colors?.[0] ?? 0x0f);
  }
  return colours;
}

export function masterRgb(system: GraphicsSystem, index: number): Rgb {
  const hex = system.masterPalette[index];
  return hex ? rgbOf(hex) : [0, 0, 0];
}

export function cssOf(rgb: Rgb): string {
  return 'rgb(' + rgb[0] + ',' + rgb[1] + ',' + rgb[2] + ')';
}

/** Black or white, whichever reads on a colour. */
export function contrastOf(rgb: Rgb): string {
  return rgb[0] * 0.299 + rgb[1] * 0.587 + rgb[2] * 0.114 > 140 ? '#000' : '#fff';
}

/** Draws tile pixels into an image at (x, y); colour indexes absent from the colours stay as they are. */
export function drawPixels(image: ImageData, pixels: ArrayLike<number>, width: number, height: number, x: number, y: number, colours: Rgb[]): void {
  for (let py = 0; py < height; py++) {
    const iy = y + py;
    if (iy < 0 || iy >= image.height) continue;
    for (let px = 0; px < width; px++) {
      const ix = x + px;
      if (ix < 0 || ix >= image.width) continue;
      const colour = colours[pixels[py * width + px]];
      if (!colour) continue;
      const o = (iy * image.width + ix) * 4;
      image.data[o] = colour[0];
      image.data[o + 1] = colour[1];
      image.data[o + 2] = colour[2];
      image.data[o + 3] = 255;
    }
  }
}

/** Fills a rectangle of an image with one colour (alpha 0 for none). */
export function fillRect(image: ImageData, x: number, y: number, width: number, height: number, rgb: Rgb | undefined): void {
  for (let py = Math.max(0, y); py < Math.min(image.height, y + height); py++) {
    for (let px = Math.max(0, x); px < Math.min(image.width, x + width); px++) {
      const o = (py * image.width + px) * 4;
      image.data[o] = rgb ? rgb[0] : 0;
      image.data[o + 1] = rgb ? rgb[1] : 0;
      image.data[o + 2] = rgb ? rgb[2] : 0;
      image.data[o + 3] = rgb ? 255 : 0;
    }
  }
}

/**
 * A picture of a given size in pixels, shown enlarged by a whole zoom factor
 * (nearest neighbour), with an overlay drawn at the enlarged size (grids,
 * selections). Mouse positions are reported in picture pixels.
 */
export class ZoomCanvas {
  readonly canvas = el('canvas');
  readonly frame = el('div', { className: 'canvas-frame' }, this.canvas);
  private source = document.createElement('canvas');
  image: ImageData;
  zoom = 1;

  constructor(public width: number, public height: number, private readonly overlay?: (ctx: CanvasRenderingContext2D, zoom: number) => void) {
    this.image = new ImageData(Math.max(1, width), Math.max(1, height));
    this.resize(width, height);
  }

  resize(width: number, height: number): void {
    this.width = Math.max(1, width);
    this.height = Math.max(1, height);
    this.image = new ImageData(this.width, this.height);
    this.source.width = this.width;
    this.source.height = this.height;
    this.setZoom(this.zoom);
  }

  setZoom(zoom: number): void {
    this.zoom = zoom;
    this.canvas.width = this.width * zoom;
    this.canvas.height = this.height * zoom;
    this.canvas.style.width = this.width * zoom + 'px';
    this.canvas.style.height = this.height * zoom + 'px';
  }

  /** Shows the image, then the overlay. */
  present(): void {
    this.source.getContext('2d')!.putImageData(this.image, 0, 0);
    const ctx = this.canvas.getContext('2d')!;
    ctx.imageSmoothingEnabled = false;
    ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
    ctx.drawImage(this.source, 0, 0, this.width * this.zoom, this.height * this.zoom);
    this.overlay?.(ctx, this.zoom);
  }

  /** The picture pixel under a mouse event. */
  at(event: MouseEvent): { x: number; y: number } {
    const box = this.canvas.getBoundingClientRect();
    const scaleX = this.width / box.width;
    const scaleY = this.height / box.height;
    return { x: Math.floor((event.clientX - box.left) * scaleX), y: Math.floor((event.clientY - box.top) * scaleY) };
  }

  inside(p: { x: number; y: number }): boolean {
    return p.x >= 0 && p.y >= 0 && p.x < this.width && p.y < this.height;
  }
}

/** Mouse handling of a drawing surface: down, drag, up, hover and leave, in picture pixels. */
export function onPointer(
  surface: ZoomCanvas,
  handlers: {
    down?: (p: { x: number; y: number }, event: PointerEvent) => void;
    drag?: (p: { x: number; y: number }, event: PointerEvent) => void;
    up?: (p: { x: number; y: number }, event: PointerEvent) => void;
    hover?: (p: { x: number; y: number } | undefined) => void;
  }
): void {
  const canvas = surface.canvas;
  let dragging = false;
  canvas.addEventListener('contextmenu', e => e.preventDefault());
  canvas.addEventListener('pointerdown', event => {
    if (event.button !== 0 && event.button !== 2) return;
    dragging = true;
    canvas.setPointerCapture(event.pointerId);
    handlers.down?.(surface.at(event), event);
  });
  canvas.addEventListener('pointermove', event => {
    const p = surface.at(event);
    handlers.hover?.(surface.inside(p) ? p : undefined);
    if (dragging) handlers.drag?.(p, event);
  });
  const finish = (event: PointerEvent) => {
    if (!dragging) return;
    dragging = false;
    handlers.up?.(surface.at(event), event);
  };
  canvas.addEventListener('pointerup', finish);
  canvas.addEventListener('pointercancel', finish);
  canvas.addEventListener('pointerleave', () => handlers.hover?.(undefined));
}

/** Strokes a rectangle of picture pixels on an overlay, on whole screen pixels. */
export function outline(ctx: CanvasRenderingContext2D, zoom: number, x: number, y: number, width: number, height: number, colour: string, lineWidth = 2): void {
  ctx.save();
  ctx.strokeStyle = colour;
  ctx.lineWidth = lineWidth;
  ctx.strokeRect(x * zoom + lineWidth / 2, y * zoom + lineWidth / 2, width * zoom - lineWidth, height * zoom - lineWidth);
  ctx.restore();
}

/** Grid lines every `step` picture pixels. */
export function grid(ctx: CanvasRenderingContext2D, zoom: number, width: number, height: number, stepX: number, stepY: number, colour: string, lineWidth = 1, offsetY = 0): void {
  ctx.save();
  ctx.strokeStyle = colour;
  ctx.lineWidth = lineWidth;
  ctx.beginPath();
  for (let x = stepX; x < width; x += stepX) {
    ctx.moveTo(x * zoom + 0.5, 0);
    ctx.lineTo(x * zoom + 0.5, height * zoom);
  }
  for (let y = stepY + offsetY; y < height; y += stepY) {
    ctx.moveTo(0, y * zoom + 0.5);
    ctx.lineTo(width * zoom, y * zoom + 0.5);
  }
  ctx.stroke();
  ctx.restore();
}

/** The colour of selections and cursors: the theme's focus colour. */
export function accent(): string {
  return getComputedStyle(document.body).getPropertyValue('--vscode-focusBorder').trim() || '#007fd4';
}

/**
 * A tile set as a sheet, 16 tiles to a row, drawn in one palette: the panel
 * tiles are chosen from (screen, map) or selected in (tile editor).
 */
export class TileSheet {
  static readonly COLUMNS = 16;
  readonly surface: ZoomCanvas;
  cache: TileCache | undefined;
  colours: Rgb[] = [];
  /** Tiles outlined: [first, last] of a rectangle in sheet order, or one tile. */
  selection: { from: number; to: number } | undefined;
  hovered: number | undefined;
  /** Rows shown even when the tile set is shorter (a tile table's worth). */
  minRows = 0;
  extraOverlay?: (ctx: CanvasRenderingContext2D, zoom: number) => void;

  constructor(private readonly system: GraphicsSystem, zoom: number) {
    this.surface = new ZoomCanvas(TileSheet.COLUMNS * system.tileWidth, system.tileHeight, (ctx, z) => this.drawOverlay(ctx, z));
    this.surface.setZoom(zoom);
    this.surface.frame.classList.add('checker');
  }

  rows(): number {
    return Math.max(this.minRows, Math.ceil((this.cache?.count ?? 0) / TileSheet.COLUMNS), 1);
  }

  /** The tile under a picture pixel, or undefined outside the tiles. */
  tileAt(p: { x: number; y: number }): number | undefined {
    if (!this.surface.inside(p)) return undefined;
    const index = Math.floor(p.y / this.system.tileHeight) * TileSheet.COLUMNS + Math.floor(p.x / this.system.tileWidth);
    return index < (this.cache?.count ?? 0) ? index : undefined;
  }

  render(): void {
    const { system } = this;
    const rows = this.rows();
    const width = TileSheet.COLUMNS * system.tileWidth;
    const height = rows * system.tileHeight;
    if (this.surface.width !== width || this.surface.height !== height) {
      this.surface.resize(width, height);
    }
    const image = this.surface.image;
    image.data.fill(0);
    const count = this.cache?.count ?? 0;
    for (let t = 0; t < count; t++) {
      drawPixels(image, this.cache!.pixels(t), system.tileWidth, system.tileHeight,
        (t % TileSheet.COLUMNS) * system.tileWidth, Math.floor(t / TileSheet.COLUMNS) * system.tileHeight, this.colours);
    }
    this.surface.present();
  }

  private drawOverlay(ctx: CanvasRenderingContext2D, zoom: number): void {
    const { system } = this;
    const tw = system.tileWidth;
    const th = system.tileHeight;
    grid(ctx, zoom, this.surface.width, this.surface.height, tw, th, 'rgba(128,128,128,0.35)');
    if (this.hovered !== undefined) {
      outline(ctx, zoom, (this.hovered % TileSheet.COLUMNS) * tw, Math.floor(this.hovered / TileSheet.COLUMNS) * th, tw, th, 'rgba(255,255,255,0.6)', 1);
    }
    if (this.selection) {
      const r = sheetRect(this.selection.from, this.selection.to);
      outline(ctx, zoom, r.column * tw, r.row * th, r.columns * tw, r.rows * th, accent(), 2);
    }
    this.extraOverlay?.(ctx, zoom);
  }
}

/** The rectangle of the sheet two tiles span: first column and row, and size in tiles. */
export function sheetRect(a: number, b: number): { column: number; row: number; columns: number; rows: number } {
  const c = TileSheet.COLUMNS;
  const column = Math.min(a % c, b % c);
  const row = Math.min(Math.floor(a / c), Math.floor(b / c));
  return {
    column,
    row,
    columns: Math.abs((a % c) - (b % c)) + 1,
    rows: Math.abs(Math.floor(a / c) - Math.floor(b / c)) + 1
  };
}

/** A palette of four colours as a small button: what attribute painting chooses from. */
export function paletteButton(system: GraphicsSystem, label: string, title: string, onClick: () => void): { element: HTMLButtonElement; paint(colours: Rgb[]): void } {
  const strip = el('span', { className: 'strip' });
  const element = el('button', { type: 'button', className: 'palette-button', title, onclick: onClick }, el('span', { className: 'label', text: label }), strip);
  const cells: HTMLElement[] = [];
  for (let c = 0; c < colorsPerTile(system); c++) {
    const cell = el('span');
    cells.push(cell);
    strip.append(cell);
  }
  return {
    element,
    paint(colours: Rgb[]) {
      colours.forEach((rgb, i) => {
        if (cells[i]) cells[i].style.background = cssOf(rgb);
      });
    }
  };
}

export { hex2 };
