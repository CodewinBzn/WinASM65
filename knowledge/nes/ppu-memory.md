# PPU memory: tiles, nametables, attributes, palettes

The PPU has its own 16 KB address space, reached through PPUADDR/PPUDATA.

| PPU address | What |
|---|---|
| $0000-$0FFF | Pattern table 0: 256 tiles (from CHR ROM, chr/tiles.chr) |
| $1000-$1FFF | Pattern table 1: 256 more tiles |
| $2000-$23BF | Nametable 0: 960 tile numbers (32 columns x 30 rows) |
| $23C0-$23FF | Attribute table 0: 64 bytes, palettes of nametable 0 |
| $2400, $2800, $2C00 | Nametables 1, 2, 3 (each with attributes at +$3C0) |
| $3F00-$3F0F | Background palettes 0-3 (4 colours each) |
| $3F10-$3F1F | Sprite palettes 0-3 |

## Tiles (pattern tables)

A tile is 8x8 pixels, 2 bits per pixel = 16 bytes: 8 bytes of bit plane 0 (one byte per
row, bit 7 = leftmost pixel), then 8 bytes of bit plane 1. Pixel value = plane0 bit +
2 x plane1 bit = 0-3, an index into a 4-colour palette. Tile N is at N*16.
Edit tiles with the `edit_tiles` tool (rows of digits 0-3) instead of computing bytes.

## Nametable: which tile goes where

Screen = 32 x 30 tiles (256 x 240 pixels). One byte per cell = tile number.

    address = $2000 + row * 32 + column      (row 0-29, column 0-31)

Examples: row 0 col 0 = $2000; row 10 col 5 = $2145; row 29 col 31 = $23BF.
In the template's tiles, a letter's tile number is its ASCII code ("A" = $41), so
`.byte "HELLO"` data can be copied directly to PPUDATA.

## Attribute table: which palette a 16x16 area uses

One attribute byte covers 32x32 pixels = 4x4 tiles. It holds four 2-bit palette numbers,
one for each 16x16 quarter (2x2 tiles):

| Bits | Quarter |
|---|---|
| 1-0 | top-left |
| 3-2 | top-right |
| 5-4 | bottom-left |
| 7-6 | bottom-right |

    attribute address = $23C0 + (row / 4) * 8 + (column / 4)
    shift in the byte   = ((row & 2) * 2) + (column & 2)    -> 0, 2, 4 or 6

Example: tile row 10, column 5 -> $23C0 + 2*8 + 1 = $23D1; row & 2 = 2, col & 2 = 0 ->
shift 4 (bottom-left quarter, bits 5-4). To give the whole 32x32 block palette 1:
`lda #%01010101 / sta` at that address. Byte value = BR<<6 | BL<<4 | TR<<2 | TL.

All attributes = 0 means every tile uses background palette 0 (clear_nametable does it).

## Palettes

32 bytes at $3F00, each a colour index $00-$3F (see palette-colors.md):

| Address | Use |
|---|---|
| $3F00 | universal background colour (shown where a pixel is 0) |
| $3F01-$3F03 | background palette 0, colours 1-3 |
| $3F05-$3F07, $3F09-$3F0B, $3F0D-$3F0F | background palettes 1, 2, 3 |
| $3F11-$3F13, $3F15-$3F17, $3F19-$3F1B, $3F1D-$3F1F | sprite palettes 0, 1, 2, 3 |

- Colour 0 of every background palette is the SAME shared colour ($3F00).
- Colour 0 of a sprite palette is transparent.
- $3F10/$3F14/$3F18/$3F1C are mirrors of $3F00/$3F04/$3F08/$3F0C: writing $3F10 changes
  the background colour. In a 32-byte table keep byte 16 = byte 0.
- To change the background colour: write one byte at $3F00 (screen off or in NMI).

## Mirroring

Only 2 KB of nametable RAM exist: two of the four nametables are copies.

| iNES flag 6 bit 0 | Mirroring | Same memory | Use for |
|---|---|---|---|
| 0 | horizontal | $2000=$2400, $2800=$2C00 | vertical scrolling |
| 1 | vertical | $2000=$2800, $2400=$2C00 | horizontal scrolling |

The template header uses vertical mirroring ($2000 and $2400 side by side).
