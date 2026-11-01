import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { PaletteAsset, ScreenAsset, blankScreen } from '../../shared/assets';
import { attributeTableSize, changedRanges, encodePalettes, encodeScreen } from '../../shared/binary';
import { assetOfIncluded, describeSymbol, parseGenerated, parseIncludeLine, renderItem } from '../../shared/labels';
import { parseJson } from '../../shared/json';
import { NES_FALLBACK as nes } from '../../shared/system';

// Assets and what WinASM65.Designer's `export` wrote for them (src/test/unit/fixtures/live).
const fixtures = path.resolve(__dirname, '..', '..', '..', 'src', 'test', 'unit', 'fixtures', 'live');
const fixture = (name: string) => fs.readFileSync(path.join(fixtures, name));
const json = <T>(name: string) => parseJson(fixture(name).toString('utf8')) as T;
const hex = (bytes: ArrayLike<number>) => Array.from(bytes, b => b.toString(16).toUpperCase().padStart(2, '0')).join(' ');

test('palette RAM is the bytes the C# exporter writes (.pal.bin)', () => {
  const ram = encodePalettes(nes, json<PaletteAsset>('live.pal.json'));
  assert.equal(ram.length, 32);
  assert.deepEqual(hex(ram), hex(fixture('live.pal.bin')));
});

test('palette RAM: the vector of WinASM65.Graphics.Tests (ThePaletteBinaryIsPaletteRamInOrder)', () => {
  const colours = [
    [0x21, 0x30, 0x16, 0x28], [0x30, 0x16, 0x28, 0x30], [0x0f, 0x11, 0x21, 0x31], [0x0f, 0x07, 0x17, 0x27],
    [0x0f, 0x30, 0x11, 0x16], [0x0f, 0x28, 0x38, 0x30], [0x0f, 0x07, 0x17, 0x27], [0x0f, 0x27, 0x37, 0x30]
  ];
  const ram = encodePalettes(nes, { palettes: colours.map((c, i) => ({ name: 'p' + i, colors: c })) });
  assert.equal(hex(ram), '21 30 16 28 30 16 28 30 0F 11 21 31 0F 07 17 27 0F 30 11 16 0F 28 38 30 0F 07 17 27 0F 27 37 30');
});

test('palette RAM: missing palettes and colours are $0F, colours keep 6 bits (as NesGraphics.EncodePalettes)', () => {
  const ram = encodePalettes(nes, { palettes: [{ name: 'a', colors: [0x7d, 0x16] }] });
  assert.equal(hex(ram.subarray(0, 4)), '3D 16 0F 0F');
  assert.ok(ram.subarray(4).every(b => b === 0x0f));
  assert.ok(encodePalettes(nes, undefined).every(b => b === 0x0f));
});

test('a nametable is the bytes the C# exporter writes (.nam)', () => {
  const screen = json<ScreenAsset>('live.screen.json');
  const table = encodeScreen(nes, screen.tiles, screen.attributes);
  assert.equal(table.length, 1024);
  assert.equal(attributeTableSize(nes), 64);
  assert.deepEqual(Buffer.from(table), fixture('live.nam'));
});

test('a nametable: the vector of WinASM65.Graphics.Tests (AttributesArePackedFourBlocksToAByteAndTheLastRowIsHalf)', () => {
  const screen = blankScreen(nes, 'title');
  screen.attributes[0] = 1;
  screen.attributes[1] = 2;
  screen.attributes[16] = 3;
  screen.attributes[16 + 3] = 2;
  screen.attributes[14 * 16 + 0] = 2;
  screen.attributes[14 * 16 + 1] = 3;
  screen.attributes[14 * 16 + 15] = 1;
  screen.tiles[0] = 7;
  screen.tiles[959] = 9;
  const table = encodeScreen(nes, screen.tiles, screen.attributes);
  assert.equal(table[0], 7);
  assert.equal(table[959], 9);
  assert.equal(table[960], 0x39);
  assert.equal(table[961], 0x80);
  assert.equal(table[960 + 56], 0x0e);
  assert.equal(table[960 + 63], 0x04);
  for (let i = 962; i < 960 + 56; i++) assert.equal(table[i], 0, 'attribute byte ' + (i - 960));
});

test('changed ranges: whole data first, then only what changed, widened to tiles and merged', () => {
  const before = new Uint8Array(64);
  assert.deepEqual(changedRanges(undefined, before).map(r => [r.offset, r.data.length]), [[0, 64]]);
  assert.deepEqual(changedRanges(before, before.slice()), []);

  const after = before.slice();
  after[3] = 1;
  after[40] = 2;
  assert.deepEqual(changedRanges(before, after).map(r => [r.offset, r.data.length]), [[3, 1], [40, 1]]);
  assert.deepEqual(changedRanges(before, after, 16).map(r => [r.offset, r.data.length]), [[0, 16], [32, 16]], 'tiles 0 and 2');
  after[5] = 9;
  assert.deepEqual(changedRanges(before, after, 1, 8).map(r => [r.offset, hex(r.data)]), [[3, '01 00 09'], [40, '02']], 'near changes merge');

  const grown = new Uint8Array(80);
  grown.set(before);
  grown[70] = 1;
  assert.deepEqual(changedRanges(before, grown, 16).map(r => [r.offset, r.data.length]), [[64, 16]], 'bytes past the old end count as changed');
});

test('the symbols of a generated metasprite source', () => {
  const generated = parseGenerated(fixture('live.msprite.asm').toString('utf8'));
  assert.ok(generated);
  assert.equal(generated.source, 'live.msprite.json');
  assert.equal(generated.kind, 'metasprite');
  const byName = new Map(generated.symbols.map(s => [s.name, s]));
  assert.deepEqual([byName.get('lv_ship_idle')?.role, byName.get('lv_ship_idle')?.ordinal], ['metasprite', 0]);
  assert.deepEqual([byName.get('lv_empty')?.role, byName.get('lv_empty')?.ordinal], ['metasprite', 2]);
  assert.equal(byName.get('lv_metasprites_lo')?.role, 'metaspriteTable');
  assert.equal(byName.get('LV_METASPRITE_COUNT')?.role, 'metaspriteTable');
  assert.deepEqual([byName.get('LV_MS_SHIP_FLY')?.role, byName.get('LV_MS_SHIP_FLY')?.ordinal], ['metasprite', 1]);
  assert.deepEqual([byName.get('lv_stop_anim')?.role, byName.get('lv_stop_anim')?.ordinal], ['animation', 1]);
  assert.equal(byName.get('lv_ship_idle')?.line, 2);

  // What to draw for them: the metasprite by its name in the file, an animation's frame 0.
  const asset = json<unknown>('live.msprite.json');
  assert.equal(renderItem(byName.get('lv_ship_fly')!, asset), 'ship-fly');
  assert.equal(renderItem(byName.get('LV_MS_SHIP_IDLE')!, asset), 'Ship Idle');
  assert.equal(renderItem(byName.get('lv_fly_anim')!, asset), 'fly#0');
  assert.equal(renderItem(byName.get('lv_metasprites_hi')!, asset), undefined);
  assert.equal(describeSymbol(byName.get('lv_fly_anim')!, 'fly#0'), 'animation "fly", frame 0');
});

test('the symbols of generated palette and screen sources', () => {
  const palettes = parseGenerated(fixture('live.pal.asm').toString('utf8'));
  assert.ok(palettes);
  assert.equal(palettes.kind, 'palette');
  assert.deepEqual(palettes.symbols.find(s => s.name === 'lv_palettes'), { name: 'lv_palettes', type: 'label', line: 2, role: 'palettes', ordinal: undefined });
  assert.deepEqual(palettes.symbols.filter(s => s.name === 'LV_PAL_SKY_BLUE').map(s => [s.role, s.ordinal]), [['palette', 2]]);

  const screen = parseGenerated(fixture('live.screen.asm').toString('utf8').replace(/\n/g, '\r\n'));
  assert.ok(screen, 'CRLF line ends are read too');
  assert.equal(screen.source, 'live.screen.json');
  assert.deepEqual(screen.symbols.map(s => [s.name, s.type, s.role]), [['lv_live_screen', 'label', 'screen'], ['LV_LIVE_SCREEN_SIZE', 'constant', 'screen']]);

  assert.equal(parseGenerated('; a hand-written file\nfoo:\n'), undefined);
  assert.equal(parseGenerated('; Generated by WinASM65.Graphics from tiles.chr - do not edit\n'), undefined);
});

test('.incbin and .include lines, and the asset behind an included file', () => {
  assert.deepEqual(parseIncludeLine('        .incbin "chr/tiles.chr"'), { directive: 'incbin', file: 'chr/tiles.chr', start: 8, end: 31 });
  assert.equal(parseIncludeLine('title:  .INCBIN "title.nam" ; the screen')?.file, 'title.nam');
  assert.equal(parseIncludeLine('        .include "gfx/ship.msprite.asm"')?.directive, 'include');
  assert.equal(parseIncludeLine('        lda #0'), undefined);
  assert.equal(parseIncludeLine('; .incbin "x.chr"'), undefined);

  assert.equal(assetOfIncluded('a/tiles.chr'), 'a/tiles.chr');
  assert.equal(assetOfIncluded('title.nam'), 'title.screen.json');
  assert.equal(assetOfIncluded('title.rle'), 'title.screen.json');
  assert.equal(assetOfIncluded('game.pal.bin'), 'game.pal.json');
  assert.equal(assetOfIncluded('ship.msprite.asm'), undefined);
  assert.equal(assetOfIncluded('music.bin'), undefined);
});
