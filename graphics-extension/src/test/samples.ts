import * as fs from 'fs';
import * as path from 'path';
import { MapAsset, ScreenAsset, blankMap, blankPalette, blankScreen } from '../shared/assets';
import { formatAsset } from '../shared/json';
import { NES_FALLBACK } from '../shared/system';

/**
 * A folder of sample assets around Star Catcher's tile set (example_star_catcher/chr/tiles.chr):
 * its palettes, a title screen and a two-screen map. Written by the integration
 * test and by scripts/preview.mjs, always into a temporary folder.
 */
export function writeSamples(folder: string, repo: string): { chr: string; palettes: string; screen: string; map: string } {
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
  return { chr, palettes, screen: screenPath, map: mapPath };
}
