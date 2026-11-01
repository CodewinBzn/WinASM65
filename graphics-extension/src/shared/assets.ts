import { GraphicsSystem, attributeColumns, attributeRows } from './system.js';

/**
 * The asset files, as WinASM65.Graphics reads and writes them (Assets/*.cs).
 * Every JSON asset has `version` 1 and the `system` it is drawn for; paths
 * (`chr`, `palettes`) are relative to the asset's own folder.
 */

export const ASSET_VERSION = 1;

export type AssetKind = 'chr' | 'palette' | 'metasprite' | 'screen' | 'map';

export const EXTENSIONS: Record<AssetKind, string> = {
  chr: '.chr',
  palette: '.pal.json',
  metasprite: '.msprite.json',
  screen: '.screen.json',
  map: '.map.json'
};

/** The kind of an asset file from its name, or undefined. */
export function kindOf(fileName: string): AssetKind | undefined {
  const name = fileName.toLowerCase();
  for (const kind of ['palette', 'metasprite', 'screen', 'map', 'chr'] as AssetKind[]) {
    if (name.endsWith(EXTENSIONS[kind])) {
      return kind;
    }
  }
  return undefined;
}

export interface PaletteEntry {
  name: string;
  /** Master palette indexes, colour 0 first. */
  colors: number[];
}

/** `*.pal.json`: the background palettes, then the sprite palettes. */
export interface PaletteAsset {
  version: number;
  system: string;
  name: string;
  prefix: string;
  palettes: PaletteEntry[];
}

/** `*.screen.json`: a tile per cell and a background palette per attribute block, both row-major. */
export interface ScreenAsset {
  version: number;
  system: string;
  chr?: string;
  palettes?: string;
  prefix: string;
  name: string;
  /** `none` or `rle`. */
  compression: string;
  tiles: number[];
  attributes: number[];
}

/** Four tiles (top left, top right, bottom left, bottom right) and the background palette they share. */
export interface Metatile {
  name?: string;
  tiles: number[];
  palette: number;
}

/** `*.map.json`: metatile indexes on a grid of `width` x `height`, row-major. */
export interface MapAsset {
  version: number;
  system: string;
  chr?: string;
  palettes?: string;
  prefix: string;
  name: string;
  metatiles: Metatile[];
  width: number;
  height: number;
  cells: number[];
}

export interface MetaspriteTile {
  x: number;
  y: number;
  tile: number;
  palette: number;
  flipH: boolean;
  flipV: boolean;
  behind: boolean;
}

export interface Metasprite {
  name: string;
  originX: number;
  originY: number;
  tiles: MetaspriteTile[];
}

export interface AnimationFrame {
  metasprite: string;
  duration: number;
}

export interface MetaspriteAnimation {
  name: string;
  loop: boolean;
  frames: AnimationFrame[];
}

/** `*.msprite.json`: metasprites around their origins, and animations of them. */
export interface MetaspriteAsset {
  version: number;
  system: string;
  chr?: string;
  palettes?: string;
  prefix: string;
  metasprites: Metasprite[];
  animations: MetaspriteAnimation[];
}

/** One diagnostic of the designer: `{ severity, message, path ($.json.path), file }`. */
export interface AssetDiagnostic {
  severity: 'error' | 'warning';
  message: string;
  path: string;
  file?: string | null;
}

/** Every palette of the system set to one colour (as PaletteAsset.Blank). */
export function blankPalette(system: GraphicsSystem, name: string, color = 0x0f): PaletteAsset {
  const palettes: PaletteEntry[] = [];
  for (let p = 0; p < system.backgroundPaletteCount + system.spritePaletteCount; p++) {
    const sprite = p >= system.backgroundPaletteCount;
    palettes.push({
      name: sprite ? 'sprite' + (p - system.backgroundPaletteCount) : 'bg' + p,
      colors: new Array(system.colorsPerPalette).fill(color)
    });
  }
  return { version: ASSET_VERSION, system: system.id, name, prefix: '', palettes };
}

/** A screen of tile 0 and palette 0 (as ScreenAsset.Blank). */
export function blankScreen(system: GraphicsSystem, name: string, chr?: string, palettes?: string): ScreenAsset {
  return {
    version: ASSET_VERSION,
    system: system.id,
    chr,
    palettes,
    prefix: '',
    name,
    compression: 'none',
    tiles: new Array(system.screenWidthTiles * system.screenHeightTiles).fill(0),
    attributes: new Array(attributeColumns(system) * attributeRows(system)).fill(0)
  };
}

/** Metatiles across and down one screen: 16 x 15 on the NES. */
export function metatilesPerScreen(system: GraphicsSystem): { columns: number; rows: number } {
  return { columns: attributeColumns(system), rows: attributeRows(system) };
}

/** A map one screen big, of one blank metatile. */
export function blankMap(system: GraphicsSystem, name: string, chr?: string, palettes?: string): MapAsset {
  const screen = metatilesPerScreen(system);
  return {
    version: ASSET_VERSION,
    system: system.id,
    chr,
    palettes,
    prefix: '',
    name,
    metatiles: [{ name: 'blank', tiles: [0, 0, 0, 0], palette: 0 }],
    width: screen.columns,
    height: screen.rows,
    cells: new Array(screen.columns * screen.rows).fill(0)
  };
}

/** An empty metasprite file. */
export function blankMetasprites(system: GraphicsSystem, chr?: string, palettes?: string): MetaspriteAsset {
  return { version: ASSET_VERSION, system: system.id, chr, palettes, prefix: '', metasprites: [], animations: [] };
}

/**
 * A map resized to a new width and height, cells kept where they were (top
 * left anchored), new cells metatile 0.
 */
export function resizeMap(map: MapAsset, width: number, height: number): MapAsset {
  const cells: number[] = [];
  for (let row = 0; row < height; row++) {
    for (let column = 0; column < width; column++) {
      cells.push(column < map.width && row < map.height ? map.cells[row * map.width + column] ?? 0 : 0);
    }
  }
  return { ...map, width, height, cells };
}

/**
 * A map without one of its metatiles: cells that used it take metatile 0 (or
 * the one that took its place), and the indexes after it move down by one.
 */
export function removeMetatile(map: MapAsset, index: number): MapAsset {
  const metatiles = map.metatiles.filter((_, i) => i !== index);
  const cells = map.cells.map(cell => (cell === index ? 0 : cell > index ? cell - 1 : cell));
  return { ...map, metatiles, cells };
}
