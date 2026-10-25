import * as fs from 'fs';
import * as path from 'path';
import { MapAsset, MetaspriteAsset, MetaspriteTile, ScreenAsset, blankMap, blankPalette, blankScreen } from '../shared/assets';
import { formatAsset } from '../shared/json';
import { NES_FALLBACK } from '../shared/system';

/**
 * A folder of sample assets around Star Catcher's tile set (example_star_catcher/chr/tiles.chr):
 * its palettes, a title screen, a two-screen map, metasprites with animations,
 * and a source that uses what they export. Written by the integration test and
 * by scripts/preview.mjs, always into a temporary folder.
 */
export function writeSamples(folder: string, repo: string): { chr: string; palettes: string; screen: string; map: string; sprites: string; source: string } {
  const nes = NES_FALLBACK;
  fs.mkdirSync(folder, { recursive: true });
  const chr = path.join(folder, 'tiles.chr');
  fs.copyFileSync(path.join(repo, 'example_star_catcher', 'chr', 'tiles.chr'), chr);

  // The game's palettes (the Palettes table of src/game.asm), with the
  // background ones made different to show off attributes.
  const palette = blankPalette(nes, 'game');
  const colours = [
    [0x0f, 0x30, 0x16, 0x28], [0x0f, 0x21, 0x11, 0x30], [0x0f, 0x28, 0x38, 0x30], [0x0f, 0x27, 0x17, 0x07],
    [0x0f, 0x30, 0x11, 0x16], [0x0f, 0x28, 0x38, 0x30], [0x0f, 0x07, 0x17, 0x27], [0x0f, 0x27, 0x37, 0x30]
  ];
  const names = ['text', 'sky', 'gold', 'rock', 'ship', 'stars', 'meteors', 'burst'];
  palette.palettes.forEach((p, i) => { p.colors = colours[i]; p.name = names[i]; });
  const palettes = path.join(folder, 'game.pal.json');
  fs.writeFileSync(palettes, formatAsset('palette', palette, nes));

  const screen: ScreenAsset = blankScreen(nes, 'title', 'tiles.chr', 'game.pal.json');
  const write = (column: number, row: number, text: string) => {
    for (let i = 0; i < text.length; i++) {
      screen.tiles[row * 32 + column + i] = text.charCodeAt(i) === 32 ? 0 : text.charCodeAt(i);
    }
  };
  write(10, 8, 'STAR CATCHER');
  write(10, 20, 'PRESS START');
  write(2, 2, 'SCORE 000120');
  screen.tiles[2 * 32 + 25] = 0x0a;
  write(27, 2, '3');
  let seed = 7;
  const random = () => (seed = (seed * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff;
  for (let i = 0; i < 70; i++) {
    const cell = Math.floor(random() * 960);
    if (screen.tiles[cell] === 0 && Math.floor(cell / 32) > 3) screen.tiles[cell] = random() < 0.6 ? 0x06 : 0x07;
  }
  for (let i = 0; i < 6; i++) screen.tiles[(12 + i) * 32 + 4 + i * 4] = 0x05;
  for (let block = 0; block < 240; block++) {
    const row = Math.floor(block / 16);
    screen.attributes[block] = row === 4 ? 2 : row >= 5 && row <= 8 ? 1 : row >= 9 ? 3 : 0;
  }
  const screenPath = path.join(folder, 'title.screen.json');
  fs.writeFileSync(screenPath, formatAsset('screen', screen, nes));

  const map: MapAsset = blankMap(nes, 'level1', 'tiles.chr', 'game.pal.json');
  map.metatiles = [
    { name: 'sky', tiles: [0, 0, 0, 0], palette: 1 },
    { name: 'stars', tiles: [0x06, 0, 0, 0x07], palette: 1 },
    { name: 'gold', tiles: [0x03, 0x04, 0x04, 0x03], palette: 2 },
    { name: 'rock', tiles: [0x05, 0x05, 0x05, 0x05], palette: 3 },
    { name: 'heart', tiles: [0x0a, 0, 0, 0x0a], palette: 0 }
  ];
  map.width = 32;
  map.height = 15;
  map.cells = new Array(32 * 15).fill(0);
  for (let x = 0; x < 32; x++) {
    map.cells[14 * 32 + x] = 3;
    map.cells[13 * 32 + x] = x % 7 === 3 ? 0 : 3;
    if (x % 5 === 0) map.cells[(3 + (x % 4)) * 32 + x] = 1;
    if (x % 6 === 2) map.cells[10 * 32 + x] = 2;
  }
  map.cells[5 * 32 + 20] = 4;
  const mapPath = path.join(folder, 'level1.map.json');
  fs.writeFileSync(mapPath, formatAsset('map', map, nes));

  // The ship (tiles 1 and 2), its flame (8), a meteor of one tile flipped four
  // ways (5), and a row of stars too wide for one scanline.
  const tile = (x: number, y: number, t: number, palette: number, flipH = false, flipV = false, behind = false): MetaspriteTile =>
    ({ x, y, tile: t, palette, flipH, flipV, behind });
  const sprites: MetaspriteAsset = {
    version: 1, system: 'nes', chr: 'tiles.chr', palettes: 'game.pal.json', prefix: '',
    metasprites: [
      { name: 'ship', originX: 64, originY: 68, tiles: [tile(56, 60, 1, 0), tile(64, 60, 2, 0)] },
      { name: 'ship_thrust', originX: 64, originY: 68, tiles: [tile(56, 60, 1, 0), tile(64, 60, 2, 0), tile(60, 68, 8, 3)] },
      { name: 'meteor', originX: 64, originY: 64, tiles: [tile(56, 56, 5, 2), tile(64, 56, 5, 2, true), tile(56, 64, 5, 2, false, true), tile(64, 64, 5, 2, true, true)] },
      { name: 'star_row', originX: 0, originY: 64, tiles: Array.from({ length: 9 }, (_, i) => tile(i * 8, 60, i % 2 === 0 ? 3 : 4, 1)) }
    ],
    animations: [
      { name: 'fly', loop: true, frames: [{ metasprite: 'ship', duration: 8 }, { metasprite: 'ship_thrust', duration: 6 }] },
      { name: 'spin', loop: true, frames: [{ metasprite: 'meteor', duration: 10 }, { metasprite: 'ship', duration: 10 }] }
    ]
  };
  const spritesPath = path.join(folder, 'sprites.msprite.json');
  fs.writeFileSync(spritesPath, formatAsset('metasprite', sprites, nes));

  // A source that uses the exported labels (the hover pictures).
  const source = path.join(folder, 'main.asm');
  fs.writeFileSync(source, [
    '; Draws the ship: the tables come from sprites.msprite.json (exported).',
    '        .include "sprites.msprite.asm"',
    '        .include "game.pal.asm"',
    '',
    'DrawShip:',
    '        lda #<ship',
    '        sta ptr',
    '        lda #>ship',
    '        sta ptr+1',
    '        ldx #MS_METEOR',
    '        lda #<fly_anim',
    '        rts',
    '',
    'LoadPalettes:',
    '        ldx #0',
    '@loop:  lda palettes,x',
    '        sta $2007',
    '        inx',
    '        cpx #32',
    '        bne @loop',
    '        rts',
    '',
    'Title:  .incbin "title.nam"',
    'Tiles:  .incbin "tiles.chr"',
    ''
  ].join('\n'));
  return { chr, palettes, screen: screenPath, map: mapPath, sprites: spritesPath, source };
}
