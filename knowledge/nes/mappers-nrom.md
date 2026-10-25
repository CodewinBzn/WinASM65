# iNES header and NROM (mapper 0)

A `.nes` file = 16-byte iNES header + program ROM (PRG) + tile ROM (CHR).

## The iNES header (16 bytes)

| Byte | Value | Meaning |
|---|---|---|
| 0-3 | "NES", $1A | signature |
| 4 | 1 or 2 | number of 16 KB PRG ROM banks |
| 5 | 1 | number of 8 KB CHR ROM banks (0 = the cartridge has CHR RAM) |
| 6 | %MMMM FTBV | M = mapper low nibble, F four-screen, T trainer, B battery RAM, V mirroring (0 horizontal, 1 vertical) |
| 7 | %MMMM 0000 | mapper high nibble |
| 8-15 | 0 | unused |

The template's `src/header.asm`:

```asm
        .org $0000
        .byte "NES", $1A
        .byte 1                 ; 1 x 16 KB PRG
        .byte 1                 ; 1 x 8 KB CHR
        .byte %00000001         ; mapper 0, vertical mirroring
        .byte 0
        .dsb 8                  ; 8 zero bytes
```

## NROM-128 (the template)

- 16 KB PRG at $C000-$FFFF (also visible at $8000-$BFFF), 8 KB CHR, mapper 0.
- `game.asm` starts with `.org $C000` and must end before $FFFA.
- `vectors.asm` holds the 6 bytes at $FFFA.
- config.json pads each part to its size (Size is HEXADECIMAL, with `$`):

| Output file | Size | Bytes |
|---|---|---|
| src/header.o | $10 | 16 |
| src/game.o | $3FFA | 16,378 ($C000-$FFF9) |
| src/vectors.o | (6) | 6 ($FFFA-$FFFF) |
| chr/tiles.chr | (8192) | 8,192 |
| total `game.nes` | | 24,592 |

A ROM of any other size means a part is too big or a Size is wrong. If game.asm grows
past $FFF9 the build reports it.

## NROM-256

32 KB of PRG at $8000-$FFFF:
- header byte 4 = 2;
- `game.asm` starts with `.org $8000`;
- config.json: `{ "FileName": "src/game.o", "Size": "$7FFA" }`;
- total ROM = 16 + 32,768 + 8,192 = 40,976 bytes.

## CHR ROM

8 KB = 512 tiles: pattern table 0 ($0000, tiles 0-255) and pattern table 1 ($1000).
PPUCTRL bit 4 chooses the table for the background, bit 3 for sprites. The template
uses table 0 for both, so tile numbers 0-255 refer to the first 4 KB of tiles.chr.
Tiles cannot be changed at run time (it is ROM): change chr/tiles.chr with the
`edit_tiles` tool and rebuild.

## Other mappers

Bigger games use bank switching (MMC1 = mapper 1, UxROM = 2, CNROM = 3, MMC3 = 4).
Not needed for a small game: stay with NROM.
