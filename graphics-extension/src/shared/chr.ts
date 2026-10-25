import { GraphicsSystem } from './system.js';

/**
 * Tile data in the machine's own bytes, as a .chr file holds it.
 *
 * The layout is the NES one, written for any size the descriptor gives: a tile
 * is `bitsPerPixel` planes stored one after the other, each plane one row of
 * bits per `tileWidth / 8` bytes, most significant bit leftmost. On the NES:
 * 8 bytes of bit 0, then 8 bytes of bit 1, 16 bytes per tile.
 *
 * Pixels are colour indexes, row-major, `tileWidth * tileHeight` of them.
 */

/** Whole tiles in the data. */
export function tileCount(system: GraphicsSystem, data: ArrayLike<number>): number {
  return Math.floor(data.length / system.bytesPerTile);
}

function rowBytes(system: GraphicsSystem): number {
  return Math.ceil(system.tileWidth / 8);
}

/** The pixels of the tile at an index; a blank tile past the end of the data. */
export function decodeTile(system: GraphicsSystem, data: ArrayLike<number>, index: number): Uint8Array {
  const pixels = new Uint8Array(system.tileWidth * system.tileHeight);
  const offset = index * system.bytesPerTile;
  if (index < 0 || offset + system.bytesPerTile > data.length) {
    return pixels;
  }
  const perRow = rowBytes(system);
  const planeSize = perRow * system.tileHeight;
  for (let y = 0; y < system.tileHeight; y++) {
    for (let x = 0; x < system.tileWidth; x++) {
      const byte = y * perRow + (x >> 3);
      const bit = 7 - (x & 7);
      let colour = 0;
      for (let p = 0; p < system.bitsPerPixel; p++) {
        colour |= ((data[offset + p * planeSize + byte] >> bit) & 1) << p;
      }
      pixels[y * system.tileWidth + x] = colour;
    }
  }
  return pixels;
}

/** The bytes of a tile. */
export function encodeTile(system: GraphicsSystem, pixels: ArrayLike<number>): Uint8Array {
  if (pixels.length !== system.tileWidth * system.tileHeight) {
    throw new RangeError('a tile is ' + system.tileWidth * system.tileHeight + ' pixels, not ' + pixels.length);
  }
  const bytes = new Uint8Array(system.bytesPerTile);
  const perRow = rowBytes(system);
  const planeSize = perRow * system.tileHeight;
  const mask = (1 << system.bitsPerPixel) - 1;
  for (let y = 0; y < system.tileHeight; y++) {
    for (let x = 0; x < system.tileWidth; x++) {
      const colour = pixels[y * system.tileWidth + x] & mask;
      const byte = y * perRow + (x >> 3);
      const bit = 7 - (x & 7);
      for (let p = 0; p < system.bitsPerPixel; p++) {
        if ((colour >> p) & 1) {
          bytes[p * planeSize + byte] |= 1 << bit;
        }
      }
    }
  }
  return bytes;
}

/**
 * Writes a tile into the data, in place. The data must already hold it: grow
 * it first with {@link withTileCount}.
 */
export function writeTile(system: GraphicsSystem, data: Uint8Array, index: number, pixels: ArrayLike<number>): void {
  const offset = index * system.bytesPerTile;
  if (index < 0 || offset + system.bytesPerTile > data.length) {
    throw new RangeError('tile ' + index + ' is beyond the ' + tileCount(system, data) + ' tiles of the data');
  }
  data.set(encodeTile(system, pixels), offset);
}

/** The data grown with blank tiles to hold at least a number of tiles (never shrunk); the same array when it already does. */
export function withTileCount(system: GraphicsSystem, data: Uint8Array, count: number): Uint8Array {
  const needed = count * system.bytesPerTile;
  if (data.length >= needed) {
    return data;
  }
  const grown = new Uint8Array(needed);
  grown.set(data);
  return grown;
}

/** The colour of one pixel of a tile. */
export function getPixel(system: GraphicsSystem, data: ArrayLike<number>, index: number, x: number, y: number): number {
  return decodeTile(system, data, index)[y * system.tileWidth + x];
}

/** Sets one pixel of a tile, in place. */
export function setPixel(system: GraphicsSystem, data: Uint8Array, index: number, x: number, y: number, colour: number): void {
  const pixels = decodeTile(system, data, index);
  pixels[y * system.tileWidth + x] = colour;
  writeTile(system, data, index, pixels);
}

// Operations on the pixels of one tile (or of any width x height block).

export function flipH(pixels: ArrayLike<number>, width: number, height: number): Uint8Array {
  const out = new Uint8Array(width * height);
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      out[y * width + x] = pixels[y * width + (width - 1 - x)];
    }
  }
  return out;
}

export function flipV(pixels: ArrayLike<number>, width: number, height: number): Uint8Array {
  const out = new Uint8Array(width * height);
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      out[y * width + x] = pixels[(height - 1 - y) * width + x];
    }
  }
  return out;
}

/** A quarter turn clockwise; the block must be square. */
export function rotateClockwise(pixels: ArrayLike<number>, size: number): Uint8Array {
  const out = new Uint8Array(size * size);
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      // The pixel at (x, y) goes to (size - 1 - y, x).
      out[x * size + (size - 1 - y)] = pixels[y * size + x];
    }
  }
  return out;
}

/** Flood fill (4-connected) from a pixel, in place; whether anything changed. */
export function floodFill(pixels: Uint8Array, width: number, height: number, x: number, y: number, colour: number): boolean {
  const target = pixels[y * width + x];
  if (target === colour) {
    return false;
  }
  const stack: number[] = [y * width + x];
  while (stack.length > 0) {
    const at = stack.pop()!;
    if (pixels[at] !== target) {
      continue;
    }
    pixels[at] = colour;
    const px = at % width;
    const py = (at - px) / width;
    if (px > 0) stack.push(at - 1);
    if (px < width - 1) stack.push(at + 1);
    if (py > 0) stack.push(at - width);
    if (py < height - 1) stack.push(at + width);
  }
  return true;
}

/** The points of a line, ends included (Bresenham). */
export function linePoints(x0: number, y0: number, x1: number, y1: number): Array<[number, number]> {
  const points: Array<[number, number]> = [];
  const dx = Math.abs(x1 - x0);
  const dy = -Math.abs(y1 - y0);
  const sx = x0 < x1 ? 1 : -1;
  const sy = y0 < y1 ? 1 : -1;
  let error = dx + dy;
  let x = x0;
  let y = y0;
  for (;;) {
    points.push([x, y]);
    if (x === x1 && y === y1) {
      break;
    }
    const e2 = 2 * error;
    if (e2 >= dy) {
      error += dy;
      x += sx;
    }
    if (e2 <= dx) {
      error += dx;
      y += sy;
    }
  }
  return points;
}

/** The points of a rectangle's outline, or of the whole rectangle when filled. */
export function rectanglePoints(x0: number, y0: number, x1: number, y1: number, filled: boolean): Array<[number, number]> {
  const points: Array<[number, number]> = [];
  const left = Math.min(x0, x1);
  const right = Math.max(x0, x1);
  const top = Math.min(y0, y1);
  const bottom = Math.max(y0, y1);
  for (let y = top; y <= bottom; y++) {
    for (let x = left; x <= right; x++) {
      if (filled || y === top || y === bottom || x === left || x === right) {
        points.push([x, y]);
      }
    }
  }
  return points;
}
