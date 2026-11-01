"""Draws the tiles and the designer assets of the example game, Rock Patrol
(example_rock_patrol): chr/tiles.chr and the JSON files of gfx/.

    python tools/example_art.py

The designer exports the assets (WinASM65.Designer export gfx, or saving one in
VS Code) into the .asm, .bin and .nam files the game includes.

Tile map of chr/tiles.chr (256 tiles, both pattern tables use it):
  $00 blank          $01-$04 ship (2x2)     $05 shot
  $06 star           $07 bright star        $08-$0B rock     $0C-$0F rock, turned
  $10-$13 burst      $14-$17 burst, wider   $18 heart        $19-$1C moon (2x2)
  $1D-$1F dust       $20-$5F the font, at the character's code ('A' is $41)
"""

import json
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'example_rock_patrol')

# A 5 x 7 font, drawn for this game.
FONT = {
    '0': ["01110", "10001", "10011", "10101", "11001", "10001", "01110"],
    '1': ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
    '2': ["01110", "10001", "00001", "00110", "01000", "10000", "11111"],
    '3': ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
    '4': ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
    '5': ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
    '6': ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
    '7': ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
    '8': ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
    '9': ["01110", "10001", "10001", "01111", "00001", "00010", "01100"],
    'A': ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
    'B': ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
    'C': ["01110", "10001", "10000", "10000", "10000", "10001", "01110"],
    'D': ["11100", "10010", "10001", "10001", "10001", "10010", "11100"],
    'E': ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
    'F': ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
    'G': ["01110", "10001", "10000", "10111", "10001", "10001", "01111"],
    'H': ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
    'I': ["01110", "00100", "00100", "00100", "00100", "00100", "01110"],
    'J': ["00111", "00010", "00010", "00010", "00010", "10010", "01100"],
    'K': ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
    'L': ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
    'M': ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
    'N': ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
    'O': ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
    'P': ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
    'Q': ["01110", "10001", "10001", "10001", "10101", "10010", "01101"],
    'R': ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
    'S': ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
    'T': ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
    'U': ["10001", "10001", "10001", "10001", "10001", "10001", "01110"],
    'V': ["10001", "10001", "10001", "10001", "10001", "01010", "00100"],
    'W': ["10001", "10001", "10001", "10101", "10101", "10101", "01010"],
    'X': ["10001", "10001", "01010", "00100", "01010", "10001", "10001"],
    'Y': ["10001", "10001", "01010", "00100", "00100", "00100", "00100"],
    'Z': ["11111", "00001", "00010", "00100", "01000", "10000", "11111"],
    '-': ["00000", "00000", "00000", "11111", "00000", "00000", "00000"],
    '.': ["00000", "00000", "00000", "00000", "00000", "01100", "01100"],
    ':': ["00000", "01100", "01100", "00000", "01100", "01100", "00000"],
    '!': ["00100", "00100", "00100", "00100", "00100", "00000", "00100"],
    '/': ["00001", "00010", "00010", "00100", "01000", "01000", "10000"],
}

# Pictures, one character a pixel: '.' is transparent (colour 0), 1-3 the colours.
SHIP = [
    ".......11.......",
    ".......11.......",
    "......1221......",
    "......1221......",
    ".....122221.....",
    ".....123321.....",
    "....12333321....",
    "...1123333211...",
    "..112222222211..",
    ".11222222222211.",
    "1122211111122211",
    "1221.1....1.1221",
    "121...1..1...121",
    "11....1331....11",
    "......1331......",
    ".......33.......",
]
ROCK = [
    "................",
    ".....111111.....",
    "...1122222211...",
    "..122223322221..",
    ".12223333222221.",
    ".12233332222221.",
    "1222333222112221",
    "1222232221..1221",
    "12222222211.1221",
    "122112222221.221",
    "12.11222222222.1",
    ".12222221122221.",
    ".12222211.12221.",
    "..122222112221..",
    "...1122222211...",
    ".....111111.....",
]
BURST = [
    "................",
    "........1.......",
    "...1....1...1...",
    "....1..121.1....",
    ".....12321......",
    "....1233321.....",
    "..112333332111..",
    "1112333333332111",
    "..112333332111..",
    "....1233321.....",
    ".....12321......",
    "....1..121.1....",
    "...1....1...1...",
    "........1.......",
    "................",
    "................",
]
BURST_WIDE = [
    "1.......1......1",
    ".1.....121....1.",
    "..1...12321..1..",
    "...1.1233321.1..",
    "....123323321...",
    "...12332.23321..",
    "..1233.....3321.",
    "1123.........321",
    "..1233.....3321.",
    "...12332.23321..",
    "....123323321...",
    "...1.1233321.1..",
    "..1...12321..1..",
    ".1.....121....1.",
    "1.......1......1",
    "................",
]
MOON = [
    "......1111......",
    "....11222211....",
    "...1222222221...",
    "..122223322221..",
    ".12222333222221.",
    ".12222222222221.",
    "1222222222233221",
    "1223322222233321",
    "1223332222222221",
    "1222332222222221",
    ".12222222233221.",
    ".12222222333221.",
    "..122222223221..",
    "...1222222221...",
    "....11222211....",
    "......1111......",
]
SHOT = ["...33...", "...33...", "..3223..", "..3223..", "...22...", "...22...", "...11...", "...11..."]
HEART = ["........", ".11.11..", "1221221.", "1222221.", ".12221..", "..121...", "...1....", "........"]
STAR = ["........", "........", "........", "...1....", "........", "........", "........", "........"]
BRIGHT_STAR = ["........", "...1....", "...2....", ".12321..", "...2....", "...1....", "........", "........"]
DUST = [
    ["........", ".1......", "........", "........", "......1.", "........", "...1....", "........"],
    ["....1...", "........", "........", ".1......", "........", "........", "......1.", "........"],
    ["........", "........", "..1.....", "........", "........", ".....1..", "........", "1......."],
]


def picture(rows):
    return [[0 if c == '.' else int(c) for c in row] for row in rows]


def turned(rows):
    """The picture turned a quarter round, clockwise."""
    n = len(rows)
    return [[rows[n - 1 - x][y] for x in range(n)] for y in range(n)]


def quarters(pixels):
    """A 16 x 16 picture as four 8 x 8 tiles: top left, top right, bottom left, bottom right."""
    return [[row[x:x + 8] for row in pixels[y:y + 8]] for y in (0, 8) for x in (0, 8)]


def encode(tile):
    low = [sum(((tile[y][x] & 1) << (7 - x)) for x in range(8)) for y in range(8)]
    high = [sum((((tile[y][x] >> 1) & 1) << (7 - x)) for x in range(8)) for y in range(8)]
    return bytes(low + high)


def glyph(rows):
    tile = [[0] * 8 for _ in range(8)]
    for y, row in enumerate(rows):
        for x, c in enumerate(row):
            tile[y][x + 1] = 1 if c == '1' else 0
    return tile


def tiles():
    t = [[[0] * 8 for _ in range(8)] for _ in range(256)]
    t[0x01:0x05] = quarters(picture(SHIP))
    t[0x05] = picture(SHOT)
    t[0x06] = picture(STAR)
    t[0x07] = picture(BRIGHT_STAR)
    t[0x08:0x0C] = quarters(picture(ROCK))
    t[0x0C:0x10] = quarters(turned(picture(ROCK)))
    t[0x10:0x14] = quarters(picture(BURST))
    t[0x14:0x18] = quarters(picture(BURST_WIDE))
    t[0x18] = picture(HEART)
    t[0x19:0x1D] = quarters(picture(MOON))
    for i, d in enumerate(DUST):
        t[0x1D + i] = picture(d)
    for c, rows in FONT.items():
        t[ord(c)] = glyph(rows)
    return b''.join(encode(tile) for tile in t)


def dump(path, asset):
    with open(path, 'w', newline='\n') as f:
        json.dump(asset, f, indent=2)
        f.write('\n')


def palettes():
    names = ['text', 'sky', 'moon', 'unused', 'ship', 'shot', 'rock', 'burst']
    colors = [[0x0F, 0x30, 0x10, 0x00], [0x0F, 0x00, 0x10, 0x30], [0x0F, 0x07, 0x17, 0x27], [0x0F, 0x30, 0x10, 0x00],
              [0x0F, 0x30, 0x21, 0x11], [0x0F, 0x16, 0x27, 0x38], [0x0F, 0x07, 0x17, 0x27], [0x0F, 0x16, 0x27, 0x37]]
    return {"version": 1, "system": "nes", "name": "game", "prefix": "",
            "palettes": [{"name": n, "colors": c} for n, c in zip(names, colors)]}


def sprite(name, tiles_at, palette):
    return {"name": name, "originX": 0, "originY": 0,
            "tiles": [{"x": x, "y": y, "tile": t, "palette": palette, "flipH": False, "flipV": False, "behind": False}
                      for x, y, t in tiles_at]}


def square(first):
    return [(0, 0, first), (8, 0, first + 1), (0, 8, first + 2), (8, 8, first + 3)]


def metasprites():
    return {"version": 1, "system": "nes", "chr": "../chr/tiles.chr", "palettes": "game.pal.json", "prefix": "spr_",
            "metasprites": [
                sprite("ship", square(0x01), 0),
                sprite("shot", [(0, 0, 0x05)], 1),
                sprite("rock", square(0x08), 2),
                sprite("rock turned", square(0x0C), 2),
                sprite("burst", square(0x10), 3),
                sprite("burst wide", square(0x14), 3),
                sprite("heart", [(0, 0, 0x18)], 3),
            ],
            "animations": [
                {"name": "rock", "loop": True, "frames": [{"metasprite": "rock", "duration": 12},
                                                          {"metasprite": "rock turned", "duration": 12}]},
                {"name": "burst", "loop": False, "frames": [{"metasprite": "burst", "duration": 8},
                                                            {"metasprite": "burst wide", "duration": 8}]},
            ]}


def screen(name, lines, seed):
    """A 32 x 30 screen: lines of text (column, row, text), and a few stars."""
    cells = [0] * 960
    for column, row, text in lines:
        for i, c in enumerate(text):
            cells[row * 32 + column + i] = 0 if c == ' ' else ord(c)
    for i in range(40):
        seed = (seed * 1103515245 + 12345) & 0x7FFFFFFF
        cell = seed % 960
        if cells[cell] == 0 and all(cells[cell + d] == 0 for d in (-1, 1) if 0 <= cell + d < 960):
            cells[cell] = 0x07 if seed & 0x100 else 0x06
    attributes = [0] * 240     # every 16 x 16 block: palette 0
    return {"version": 1, "system": "nes", "chr": "../chr/tiles.chr", "palettes": "game.pal.json", "prefix": "scr_",
            "name": name, "compression": "none", "tiles": cells, "attributes": attributes}


def play_map():
    """The sky the ship flies over: 16 x 30 metatiles, two screens one above the
    other, scrolled down in a loop. Stars, dust, and two moons."""
    metatiles = [
        ("empty", [0, 0, 0, 0], 1), ("star", [0x06, 0, 0, 0], 1), ("star low", [0, 0, 0, 0x06], 1),
        ("bright star", [0, 0x07, 0, 0], 1), ("dust", [0x1D, 0, 0, 0x1E], 1), ("dust 2", [0, 0x1F, 0x1D, 0], 1),
        ("moon", [0x19, 0x1A, 0x1B, 0x1C], 2),
    ]
    cells = [0] * (16 * 30)
    seed = 99
    for i in range(len(cells)):
        seed = (seed * 1103515245 + 12345) & 0x7FFFFFFF
        r = (seed >> 8) % 100
        cells[i] = 1 if r < 12 else 2 if r < 20 else 3 if r < 24 else 4 if r < 30 else 5 if r < 34 else 0
    cells[4 * 16 + 11] = 6
    cells[21 * 16 + 3] = 6
    return {"version": 1, "system": "nes", "chr": "../chr/tiles.chr", "palettes": "game.pal.json", "prefix": "bg_",
            "name": "play",
            "metatiles": [{"name": n, "tiles": t, "palette": p} for n, t, p in metatiles],
            "width": 16, "height": 30, "cells": cells}


def main():
    os.makedirs(os.path.join(ROOT, 'chr'), exist_ok=True)
    os.makedirs(os.path.join(ROOT, 'gfx'), exist_ok=True)
    with open(os.path.join(ROOT, 'chr', 'tiles.chr'), 'wb') as f:
        f.write(tiles())
    gfx = os.path.join(ROOT, 'gfx')
    dump(os.path.join(gfx, 'game.pal.json'), palettes())
    dump(os.path.join(gfx, 'objects.msprite.json'), metasprites())
    dump(os.path.join(gfx, 'title.screen.json'), screen('title', [
        (10, 7, 'ROCK  PATROL'), (10, 16, 'PRESS START'), (5, 20, 'D-PAD MOVES   A FIRES'),
        (7, 25, 'MADE WITH WINASM65')], 3))
    dump(os.path.join(gfx, 'over.screen.json'), screen('over', [
        (11, 10, 'GAME  OVER'), (10, 18, 'PRESS START')], 11))
    dump(os.path.join(gfx, 'play.map.json'), play_map())


if __name__ == '__main__':
    main()
