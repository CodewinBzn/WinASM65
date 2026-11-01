# Graphics workflow (tiles, palettes, metasprites, screens)

WinASM65 has graphics tools that edit asset files and export them for the assembler.

## Tiles: edit_tiles and get_tiles

A tile is 8 rows of 8 digits, each 0-3 (0 = background colour / transparent for
sprites, 1-3 = palette colours 1-3).

```json
edit_tiles {
  "path": "chr/tiles.chr",
  "tiles": [ { "index": 16, "rows": [
    "00111100",
    "01222210",
    "12233221",
    "12233221",
    "12222221",
    "12222221",
    "01222210",
    "00111100" ] } ]
}
```

- `get_tiles {path, from, count}` shows existing tiles the same way.
- The template's tiles.chr already uses: $00 blank, $01-$02 ship, $03-$04 star,
  $05 meteor, $06-$07 dots, $08 burst, $0A heart, $20 space, $21 "!", $2D "-",
  $30-$39 digits, $3A ":", $41-$5A letters A-Z. Free tiles: $0B-$1F, $5B-$FF.
- After editing, rebuild: chr/tiles.chr is part of the ROM.

## Look at the result: render_asset

`render_asset {path: "chr/tiles.chr", item: "palettes.pal.json#0"}` returns a PNG
of the tiles; with a .msprite.json or .screen.json it draws the sprite or the screen.

## Palettes: .pal.json

```json
{ "version": 1, "system": "nes", "name": "game", "prefix": "gfx_",
  "palettes": [
    { "name": "bg0", "colors": [15, 48, 22, 40] },
    { "name": "bg1", "colors": [15, 42, 26, 10] },
    { "name": "bg2", "colors": [15, 33, 17, 1] },
    { "name": "bg3", "colors": [15, 0, 16, 32] },
    { "name": "player", "colors": [15, 48, 17, 22] },
    { "name": "spr1", "colors": [15, 40, 56, 48] },
    { "name": "spr2", "colors": [15, 7, 23, 39] },
    { "name": "spr3", "colors": [15, 39, 55, 48] } ] }
```

Colours are DECIMAL colour indexes in JSON (15 = $0F black, 48 = $30 white).
Write it with `update_asset {path, asset}`.

## Metasprites: .msprite.json

```json
{ "version": 1, "system": "nes", "chr": "tiles.chr", "palettes": "game.pal.json",
  "prefix": "gfx_",
  "metasprites": [ { "name": "ship", "originX": 8, "originY": 4, "tiles": [
      { "x": 0, "y": 0, "tile": 1, "palette": 0, "flipH": false, "flipV": false, "behind": false },
      { "x": 8, "y": 0, "tile": 2, "palette": 0, "flipH": false, "flipV": false, "behind": false } ] } ],
  "animations": [] }
```

## Screens: .screen.json

`tiles`: 960 tile numbers (32 per row, 30 rows); `attributes`: 240 palette numbers
0-3, one per 16x16 block (16 per row, 15 rows). The exporter packs the attributes.

## export_assets, then .include

`export_assets {path: "gfx"}` writes next to each asset:

| Asset | Files | Labels |
|---|---|---|
| game.pal.json | game.pal.asm, game.pal.bin | `<prefix>palettes:` 32 bytes, `<PREFIX>PAL_<NAME>` = index 0-7 (4-7 are sprite palettes: attribute bits = index - 4) |
| ship.msprite.json | ship.msprite.asm | `<prefix><name>:` rows `.byte dy, tile, attr, dx` ended by `.byte $80`; `<prefix>metasprites_lo/_hi`; `<PREFIX>MS_<NAME>` |
| title.screen.json | title.nam (1024 bytes), title.screen.asm | `<prefix>title:` .incbin of the nametable |

Include the generated `.asm` in game.asm, in the data part (after the code):

```asm
        .include "../gfx/game.pal.asm"
        .include "../gfx/ship.msprite.asm"
```

Then copy `gfx_palettes` (32 bytes) to $3F00 like load_palettes, draw metasprites with
snippet `draw_metasprite`, copy a 1024-byte `.nam` to $2000 with the screen off.

Use a `prefix` (like `gfx_`): label names are case-insensitive, so an exported
`palettes` label would clash with the template's `Palettes` table.
