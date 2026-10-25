import { PaletteAsset } from './assets.js';
import { GraphicsSystem, attributeColumns, attributeRows } from './system.js';

/**
 * The bytes the machine holds for an asset, as the C# exporter writes them
 * (WinASM65.Graphics NesGraphics.EncodePalettes / EncodeScreen): what the Live
 * preview writes into a running emulator. The layout is the NES one, sized
 * from the system's description.
 */

/**
 * Palette RAM: the background palettes then the sprite palettes, each
 * `colorsPerPalette` master indexes (masked to 6 bits); missing entries are
 * $0F (black). 32 bytes on the NES.
 */
export function encodePalettes(system: GraphicsSystem, palettes: Pick<PaletteAsset, 'palettes'> | undefined): Uint8Array {
  const count = system.backgroundPaletteCount + system.spritePaletteCount;
  const per = system.colorsPerPalette;
  const ram = new Uint8Array(count * per).fill(0x0f);
  const list = palettes?.palettes ?? [];
  for (let p = 0; p < count && p < list.length; p++) {
    const colours = list[p]?.colors ?? [];
    for (let c = 0; c < per && c < colours.length; c++) {
      ram[p * per + c] = (colours[c] ?? 0x0f) & 0x3f;
    }
  }
  return ram;
}

/** Bytes of a screen's attribute table: one per 2x2 attribute blocks (64 on the NES). */
export function attributeTableSize(system: GraphicsSystem): number {
  return Math.ceil(attributeColumns(system) / 2) * Math.ceil(attributeRows(system) / 2);
}

/**
 * A nametable: one byte per tile, row-major, then the attribute table. Each
 * attribute byte covers 2x2 blocks: top left in bits 0-1, top right in 2-3,
 * bottom left in 4-5, bottom right in 6-7; blocks beyond the screen (the
 * bottom half of the last row on the NES) are 0. 1024 bytes on the NES.
 */
export function encodeScreen(system: GraphicsSystem, tiles: ArrayLike<number>, attributes: ArrayLike<number>): Uint8Array {
  const cells = system.screenWidthTiles * system.screenHeightTiles;
  const wide = attributeColumns(system);
  const high = attributeRows(system);
  const bytesWide = Math.ceil(wide / 2);
  const table = new Uint8Array(cells + attributeTableSize(system));
  for (let i = 0; i < cells && i < tiles.length; i++) {
    table[i] = tiles[i] & 0xff;
  }
  const block = (bx: number, by: number): number => {
    if (bx >= wide || by >= high) return 0;
    const index = by * wide + bx;
    return index < attributes.length ? attributes[index] & 3 : 0;
  };
  for (let ay = 0; ay < Math.ceil(high / 2); ay++) {
    for (let ax = 0; ax < bytesWide; ax++) {
      const bx = ax * 2;
      const by = ay * 2;
      table[cells + ay * bytesWide + ax] = block(bx, by) | (block(bx + 1, by) << 2) | (block(bx, by + 1) << 4) | (block(bx + 1, by + 1) << 6);
    }
  }
  return table;
}

/** A run of bytes to write: where it starts in the data and the bytes. */
export interface ByteRange {
  offset: number;
  data: Uint8Array;
}

/**
 * The parts of `after` that differ from `before`, as few writes as is
 * reasonable: each changed byte widened to its whole `unit` (16 for a tile),
 * and runs closer than `gap` bytes merged. Bytes `after` has beyond the end of
 * `before` count as changed; `before` undefined gives all of `after`.
 */
export function changedRanges(before: ArrayLike<number> | undefined, after: ArrayLike<number>, unit = 1, gap = 8): ByteRange[] {
  const whole = (start: number, end: number): ByteRange => ({ offset: start, data: Uint8Array.from(Array.prototype.slice.call(after, start, end) as number[]) });
  if (!before) {
    return after.length > 0 ? [whole(0, after.length)] : [];
  }
  const ranges: Array<[number, number]> = [];
  for (let i = 0; i < after.length; i++) {
    if (i >= before.length || before[i] !== after[i]) {
      const start = Math.floor(i / unit) * unit;
      const end = Math.min(after.length, start + unit);
      const last = ranges[ranges.length - 1];
      if (last && start <= last[1] + gap) {
        last[1] = Math.max(last[1], end);
      } else {
        ranges.push([start, end]);
      }
      i = end - 1;
    }
  }
  return ranges.map(([start, end]) => whole(start, end));
}
