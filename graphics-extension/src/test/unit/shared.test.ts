import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { blankMap, blankPalette, blankScreen, kindOf, removeMetatile, resizeMap } from '../../shared/assets';
import {
  decodeTile, encodeTile, floodFill, flipH, flipV, getPixel, linePoints, rectanglePoints, rotateClockwise, setPixel, tileCount, withTileCount, writeTile
} from '../../shared/chr';
import { formatAsset, lineOf, locateJsonPath, parseJson, parsePath } from '../../shared/json';
import { NES_FALLBACK as nes, attributeColumns, attributeRows, greys, hex2 } from '../../shared/system';

// The C# output the formatter must match, written by the designer's `format` (src/test/unit/fixtures).
const fixtures = path.resolve(__dirname, '..', '..', '..', 'src', 'test', 'unit', 'fixtures');

/** A tile whose pixel (x, y) has colour (x + y) % 4. */
function diagonal(): Uint8Array {
  const pixels = new Uint8Array(64);
  for (let y = 0; y < 8; y++) for (let x = 0; x < 8; x++) pixels[y * 8 + x] = (x + y) % 4;
  return pixels;
}

test('a tile is two planes of 8 bytes, the leftmost pixel in the high bit', () => {
  const pixels = new Uint8Array(64);
  pixels[0] = 1; // (0,0): plane 0
  pixels[7] = 2; // (7,0): plane 1
  pixels[63] = 3; // (7,7): both planes
  const bytes = encodeTile(nes, pixels);
  assert.equal(bytes.length, 16);
  assert.equal(bytes[0], 0x80);
  assert.equal(bytes[8], 0x01);
  assert.equal(bytes[7], 0x01);
  assert.equal(bytes[15], 0x01);
  assert.deepEqual(Array.from(decodeTile(nes, bytes, 0)), Array.from(pixels));
});

test('decode then encode gives back every byte of a real tile set', () => {
  const chr = new Uint8Array(fs.readFileSync(path.resolve(__dirname, '..', '..', '..', '..', 'example_star_catcher', 'chr', 'tiles.chr')));
  assert.equal(tileCount(nes, chr), 256);
  const again = new Uint8Array(chr.length);
  for (let t = 0; t < tileCount(nes, chr); t++) {
    writeTile(nes, again, t, decodeTile(nes, chr, t));
  }
  assert.deepEqual(again, chr);
});

test('a tile past the end of the data decodes blank; writing it there throws', () => {
  const data = new Uint8Array(16);
  assert.deepEqual(Array.from(decodeTile(nes, data, 3)), new Array(64).fill(0));
  assert.throws(() => writeTile(nes, data, 1, diagonal()), RangeError);
  assert.throws(() => encodeTile(nes, new Uint8Array(10)), RangeError);
});

test('pixels are read and set one by one', () => {
  const data = new Uint8Array(32);
  writeTile(nes, data, 1, diagonal());
  assert.equal(getPixel(nes, data, 1, 3, 2), 1);
  setPixel(nes, data, 1, 3, 2, 3);
  assert.equal(getPixel(nes, data, 1, 3, 2), 3);
  assert.equal(getPixel(nes, data, 1, 4, 2), 2, 'its neighbour is untouched');
  assert.deepEqual(Array.from(data.subarray(0, 16)), new Array(16).fill(0), 'tile 0 is untouched');
});

test('the data grows by whole blank tiles and never shrinks', () => {
  const data = new Uint8Array([1, 2, 3]);
  const grown = withTileCount(nes, data, 2);
  assert.equal(grown.length, 32);
  assert.deepEqual(Array.from(grown.subarray(0, 3)), [1, 2, 3]);
  assert.equal(withTileCount(nes, grown, 1), grown);
});

test('flips and quarter turns', () => {
  const pixels = diagonal();
  assert.deepEqual(flipH(flipH(pixels, 8, 8), 8, 8), pixels);
  assert.equal(flipH(pixels, 8, 8)[0], pixels[7]);
  assert.equal(flipV(pixels, 8, 8)[0], pixels[56]);
  let turned: Uint8Array = pixels;
  for (let i = 0; i < 4; i++) turned = rotateClockwise(turned, 8);
  assert.deepEqual(turned, pixels);
  const one = new Uint8Array(64);
  one[1] = 3; // (1, 0) goes to (7, 1)
  assert.equal(rotateClockwise(one, 8)[1 * 8 + 7], 3);
});

test('flood fill stays within the area of one colour', () => {
  const pixels = new Uint8Array(64);
  for (let y = 0; y < 8; y++) pixels[y * 8 + 4] = 1; // a wall down column 4
  assert.equal(floodFill(pixels, 8, 8, 0, 0, 2), true);
  assert.equal(pixels[0], 2);
  assert.equal(pixels[3 * 8 + 3], 2);
  assert.equal(pixels[5], 0, 'the other side of the wall');
  assert.equal(floodFill(pixels, 8, 8, 0, 0, 2), false, 'nothing to change');
});

test('lines and rectangles', () => {
  assert.deepEqual(linePoints(0, 0, 3, 0), [[0, 0], [1, 0], [2, 0], [3, 0]]);
  assert.deepEqual(linePoints(2, 2, 0, 0), [[2, 2], [1, 1], [0, 0]]);
  assert.equal(rectanglePoints(0, 0, 3, 2, false).length, 10);
  assert.equal(rectanglePoints(3, 2, 0, 0, true).length, 12);
});

test('blank assets have the sizes of the system', () => {
  const palette = blankPalette(nes, 'game');
  assert.equal(palette.palettes.length, 8);
  assert.deepEqual(palette.palettes.map(p => p.name), ['bg0', 'bg1', 'bg2', 'bg3', 'sprite0', 'sprite1', 'sprite2', 'sprite3']);
  const screen = blankScreen(nes, 'title');
  assert.equal(screen.tiles.length, 960);
  assert.equal(screen.attributes.length, attributeColumns(nes) * attributeRows(nes));
  assert.equal(screen.attributes.length, 240);
  const map = blankMap(nes, 'level');
  assert.equal(map.width, 16);
  assert.equal(map.height, 15);
  assert.equal(map.cells.length, 240);
});

test('a map keeps its cells when resized and when a metatile goes', () => {
  const map = resizeMap(blankMap(nes, 'level'), 3, 2);
  map.metatiles.push({ tiles: [1, 1, 1, 1], palette: 0 }, { tiles: [2, 2, 2, 2], palette: 1 });
  map.cells = [0, 1, 2, 2, 1, 0];
  const wider = resizeMap(map, 4, 3);
  assert.deepEqual(wider.cells, [0, 1, 2, 0, 2, 1, 0, 0, 0, 0, 0, 0]);
  assert.deepEqual(resizeMap(map, 2, 1).cells, [0, 1]);
  const removed = removeMetatile(map, 1);
  assert.equal(removed.metatiles.length, 2);
  assert.deepEqual(removed.cells, [0, 0, 1, 1, 0, 0]);
});

test('kinds of asset files', () => {
  assert.equal(kindOf('gfx/Title.SCREEN.json'), 'screen');
  assert.equal(kindOf('a.pal.json'), 'palette');
  assert.equal(kindOf('a.msprite.json'), 'metasprite');
  assert.equal(kindOf('a.map.json'), 'map');
  assert.equal(kindOf('tiles.chr'), 'chr');
  assert.equal(kindOf('config.json'), undefined);
  assert.equal(hex2(15), '$0F');
  assert.deepEqual(greys(4), ['#000000', '#555555', '#AAAAAA', '#FFFFFF']);
});

test('the fallback formatter writes what the designer writes', () => {
  const cases: Array<[Parameters<typeof formatAsset>[0], string]> = [
    ['palette', 'game.pal'], ['screen', 'title.screen'], ['map', 'level.map'], ['metasprite', 'ship.msprite']
  ];
  for (const [kind, name] of cases) {
    const input = JSON.parse(fs.readFileSync(path.join(fixtures, name + '.input.json'), 'utf8'));
    const expected = fs.readFileSync(path.join(fixtures, name + '.json'), 'utf8');
    assert.equal(formatAsset(kind, input, nes), expected, name);
  }
});

test('JSON paths of diagnostics are found in the text', () => {
  const text = fs.readFileSync(path.join(fixtures, 'game.pal.json'), 'utf8');
  assert.deepEqual(parsePath("$.palettes[2].colors[1]"), ['palettes', 2, 'colors', 1]);
  assert.deepEqual(parsePath("$['odd key'][0]"), ['odd key', 0]);
  const at = locateJsonPath(text, '$.palettes[2].colors[1]');
  assert.equal(lineOf(text, at), 8);
  assert.equal(text.slice(at, at + 2), '15');
  assert.equal(lineOf(text, locateJsonPath(text, '$.name')), 3);
  // A path that goes further than the file: the deepest part found.
  assert.equal(lineOf(text, locateJsonPath(text, '$.palettes[4].colors[9]')), 10);
  assert.equal(locateJsonPath(text, '$'), 0);
  const screen = fs.readFileSync(path.join(fixtures, 'title.screen.json'), 'utf8');
  assert.equal(lineOf(screen, locateJsonPath(screen, '$.tiles[33]')), 10, 'tile 33 is on the second row of tiles');
});

test('JSON with comments and trailing commas is read as the designer reads it', () => {
  assert.deepEqual(parseJson('{ // a comment\n "a": [1, 2,], /* b */ "s": "x//y", }'), { a: [1, 2], s: 'x//y' });
  assert.throws(() => parseJson('{ "a": '), SyntaxError);
});
