/**
 * A machine's video hardware as the designer's `describe` request reports it
 * (WinASM65.Graphics GraphicsJson.Describe). The editors size and colour
 * everything from it rather than from NES constants.
 */
export interface GraphicsSystem {
  id: string;
  displayName: string;
  tileWidth: number;
  tileHeight: number;
  bitsPerPixel: number;
  bytesPerTile: number;
  maxTiles: number;
  colorsPerPalette: number;
  backgroundPaletteCount: number;
  spritePaletteCount: number;
  screenWidthTiles: number;
  screenHeightTiles: number;
  attributeBlockWidth: number;
  attributeBlockHeight: number;
  spritesPerScanline: number;
  /** "#RRGGBB" per master palette index. */
  masterPalette: string[];
  /** The master indexes worth choosing (the others repeat black or upset televisions). */
  selectableColors: number[];
}

/**
 * The NES as the designer described it when this extension was written: what
 * the editors use when the designer cannot be started, so that a tile set can
 * still be drawn.
 */
export const NES_FALLBACK: GraphicsSystem = {
  id: 'nes',
  displayName: 'Nintendo Entertainment System',
  tileWidth: 8,
  tileHeight: 8,
  bitsPerPixel: 2,
  bytesPerTile: 16,
  maxTiles: 256,
  colorsPerPalette: 4,
  backgroundPaletteCount: 4,
  spritePaletteCount: 4,
  screenWidthTiles: 32,
  screenHeightTiles: 30,
  attributeBlockWidth: 2,
  attributeBlockHeight: 2,
  spritesPerScanline: 8,
  masterPalette: [
    '#7C7C7C', '#0000FC', '#0000BC', '#4428BC', '#940084', '#A80020', '#A81000', '#881400',
    '#503000', '#007800', '#006800', '#005800', '#004058', '#000000', '#000000', '#000000',
    '#BCBCBC', '#0078F8', '#0058F8', '#6844FC', '#D800CC', '#E40058', '#F83800', '#E45C10',
    '#AC7C00', '#00B800', '#00A800', '#00A844', '#008888', '#000000', '#000000', '#000000',
    '#F8F8F8', '#3CBCFC', '#6888FC', '#9878F8', '#F878F8', '#F85898', '#F87858', '#FCA044',
    '#F8B800', '#B8F818', '#58D854', '#58F898', '#00E8D8', '#787878', '#000000', '#000000',
    '#FCFCFC', '#A4E4FC', '#B8B8F8', '#D8B8F8', '#F8B8F8', '#F8A4C0', '#F0D0B0', '#FCE0A8',
    '#F8D878', '#D8F878', '#B8F8B8', '#B8F8D8', '#00FCFC', '#F8D8F8', '#000000', '#000000'
  ],
  selectableColors: [
    0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28,
    32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61
  ]
};

/** Colours a pixel of a tile can take: 4 on the NES. */
export function colorsPerTile(system: GraphicsSystem): number {
  return 1 << system.bitsPerPixel;
}

/** Attribute blocks across a screen (16 on the NES). */
export function attributeColumns(system: GraphicsSystem): number {
  return Math.ceil(system.screenWidthTiles / system.attributeBlockWidth);
}

/** Attribute blocks down a screen (15 on the NES). */
export function attributeRows(system: GraphicsSystem): number {
  return Math.ceil(system.screenHeightTiles / system.attributeBlockHeight);
}

/** Pixel width of a screen. */
export function screenWidth(system: GraphicsSystem): number {
  return system.screenWidthTiles * system.tileWidth;
}

/** Pixel height of a screen. */
export function screenHeight(system: GraphicsSystem): number {
  return system.screenHeightTiles * system.tileHeight;
}

/** [r, g, b] of a "#RRGGBB" colour. */
export function rgbOf(hex: string): [number, number, number] {
  const value = parseInt(hex.replace('#', ''), 16) || 0;
  return [(value >> 16) & 0xff, (value >> 8) & 0xff, value & 0xff];
}

/** "$0F": a master palette index or a tile index as 6502 people write them. */
export function hex2(value: number): string {
  return '$' + (value & 0xff).toString(16).toUpperCase().padStart(2, '0');
}

/** The four greys a tile set is drawn in without a palette file (as the designer's renderer). */
export const GREYS = ['#000000', '#555555', '#AAAAAA', '#FFFFFF'];

/** Greys for any number of colours per tile, spread as the designer's renderer does. */
export function greys(count: number): string[] {
  const result: string[] = [];
  for (let c = 0; c < count; c++) {
    result.push(GREYS[Math.min(Math.floor(c * (GREYS.length - 1) / Math.max(1, count - 1)), GREYS.length - 1)]);
  }
  return result;
}
