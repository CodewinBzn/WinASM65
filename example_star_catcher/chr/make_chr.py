"""Builds the graphics of Star Catcher: tiles.chr, the 8 KB of CHR ROM.

Every tile is drawn below as 8 lines of 8 characters: '.' is colour 0
(transparent, or the background colour), '1', '2' and '3' are the three other
colours of the palette the tile is shown with. The tile number is its place in
the pattern table; letters and digits sit at their ASCII codes, so that
`.byte "SCORE"` in the source writes the right tiles.

    python make_chr.py            writes tiles.chr next to this script
    python make_chr.py --png out  also writes a 4x preview of the tiles
"""

import os
import struct
import sys
import zlib

TILES = {}

# --- Sprites -----------------------------------------------------------------
# The ship is two tiles side by side: 16 x 8 pixels.
SHIP = [
    ".......11.......",
    "......1221......",
    "......2112......",
    ".....222222.....",
    "..3..222222..3..",
    ".32222222222223.",
    "3222222332222223",
    "33...3....3...33",
]
TILES[0x01] = [row[:8] for row in SHIP]
TILES[0x02] = [row[8:] for row in SHIP]

# A falling star, in two frames: it twinkles.
TILES[0x03] = [
    "...1....",
    "..121...",
    "1112111.",
    ".12321..",
    "..121...",
    ".11.11..",
    ".1...1..",
    "........",
]
TILES[0x04] = [
    "........",
    "...1....",
    ".11211..",
    "..232...",
    ".11211..",
    "...1....",
    "........",
    "........",
]

# A meteor.
TILES[0x05] = [
    "..2222..",
    ".223322.",
    "22122332",
    "22222322",
    "21222222",
    "22221212",
    ".221222.",
    "..2222..",
]

# --- Background --------------------------------------------------------------
# Two kinds of distant stars for the starfield.
TILES[0x06] = [
    "........",
    "........",
    "........",
    "...1....",
    "........",
    "........",
    "........",
    "........",
]
TILES[0x07] = [
    "........",
    "........",
    "...3....",
    "..333...",
    "...3....",
    "........",
    "........",
    "........",
]

# The burst shown where the ship was hit.
TILES[0x08] = [
    "1..1..1.",
    ".1.2.1..",
    "..222...",
    "1223221.",
    "..222...",
    ".1.2.1..",
    "1..1..1.",
    "........",
]

# A heart, before the number of lives.
TILES[0x0A] = [
    "........",
    ".22.22..",
    "2222222.",
    "2222222.",
    ".22222..",
    "..222...",
    "...2....",
    "........",
]

# --- Font: 5 x 7 glyphs, at their ASCII codes ---------------------------------
FONT = {
    "0": [".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###."],
    "1": ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###."],
    "2": [".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####"],
    "3": ["####.", "....#", "....#", ".###.", "....#", "....#", "####."],
    "4": ["...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#."],
    "5": ["#####", "#....", "####.", "....#", "....#", "#...#", ".###."],
    "6": ["..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###."],
    "7": ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."],
    "8": [".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###."],
    "9": [".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.."],
    "A": [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
    "B": ["####.", "#...#", "#...#", "####.", "#...#", "#...#", "####."],
    "C": [".###.", "#...#", "#....", "#....", "#....", "#...#", ".###."],
    "D": ["###..", "#..#.", "#...#", "#...#", "#...#", "#..#.", "###.."],
    "E": ["#####", "#....", "#....", "####.", "#....", "#....", "#####"],
    "F": ["#####", "#....", "#....", "####.", "#....", "#....", "#...."],
    "G": [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####"],
    "H": ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
    "I": [".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###."],
    "J": ["..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.."],
    "K": ["#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#"],
    "L": ["#....", "#....", "#....", "#....", "#....", "#....", "#####"],
    "M": ["#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#"],
    "N": ["#...#", "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#"],
    "O": [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
    "P": ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
    "Q": [".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#"],
    "R": ["####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#"],
    "S": [".####", "#....", "#....", ".###.", "....#", "....#", "####."],
    "T": ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
    "U": ["#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
    "V": ["#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.."],
    "W": ["#...#", "#...#", "#...#", "#.#.#", "#.#.#", "#.#.#", ".#.#."],
    "X": ["#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#"],
    "Y": ["#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.."],
    "Z": ["#####", "....#", "...#.", "..#..", ".#...", "#....", "#####"],
    "-": [".....", ".....", ".....", ".###.", ".....", ".....", "....."],
    "!": ["..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#.."],
    ":": [".....", "..#..", ".....", ".....", ".....", "..#..", "....."],
}
for char, glyph in FONT.items():
    # One column of margin on the left, one row below: letters do not touch.
    TILES[ord(char)] = ["." + line.replace("#", "1") + ".." for line in glyph] + ["........"]


def encode(tile):
    """The 16 bytes of a tile: 8 bytes of bit 0 of each pixel, then 8 of bit 1."""
    assert len(tile) == 8 and all(len(row) == 8 for row in tile), tile
    low, high = [], []
    for row in tile:
        lo = hi = 0
        for pixel in row:
            value = 0 if pixel == "." else int(pixel)
            lo = (lo << 1) | (value & 1)
            hi = (hi << 1) | (value >> 1)
        low.append(lo)
        high.append(hi)
    return bytes(low + high)


def build():
    """8 KB: pattern table 0 holds every tile; table 1 stays empty."""
    chr_rom = bytearray(8192)
    for number, tile in TILES.items():
        chr_rom[number * 16:number * 16 + 16] = encode(tile)
    return bytes(chr_rom)


def write_png(path, chr_rom, scale=4):
    """A preview of pattern table 0, 16 x 16 tiles, in the game's colours."""
    colours = [(0, 0, 0), (255, 255, 255), (255, 214, 64), (228, 92, 16)]
    size = 128 * scale
    rows = []
    for y in range(size):
        row = bytearray([0])  # filter: none
        for x in range(size):
            tx, ty = x // scale // 8, y // scale // 8
            px, py = (x // scale) % 8, (y // scale) % 8
            base = (ty * 16 + tx) * 16
            bit = 7 - px
            value = ((chr_rom[base + py] >> bit) & 1) | (((chr_rom[base + 8 + py] >> bit) & 1) << 1)
            row += bytes(colours[value])
        rows.append(bytes(row))

    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(b"".join(rows), 9))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    data = build()
    with open(os.path.join(here, "tiles.chr"), "wb") as f:
        f.write(data)
    if "--png" in sys.argv:
        write_png(sys.argv[sys.argv.index("--png") + 1], data)
