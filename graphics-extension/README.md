# WinASM65 Graphics Designer

Draw NES graphics for [WinASM65](https://github.com/CodewinBzn/WinASM65) projects inside VS Code: tile sets, palettes,
screens, metatile maps, metasprites and their animations. Every asset is exported as `.asm` sources and binary files
that the assembler includes, so a picture goes from the editor to the game without a converter.

## Requirements

- The **WinASM65** extension (`codewinbzn.winasm65`). It is installed with this one: it provides the `winasm65`
  language (used by the hover pictures) and the `winasm65` debugger (used by the Live preview).
- **WinASM65.Designer**, the program that reads, checks, formats, renders, imports and exports the assets. The
  packaged extension ships it in `bin/`; set `winasm65Graphics.designerPath` to use another build. Without it the
  editors still draw (with a built-in NES description), but nothing is exported, rendered or imported.

## The editors

Each asset file opens in its own editor. JSON assets stay plain text: every edit goes through VS Code's undo/redo,
dirty state, save and diff, and **Open as Text** (or *Reopen Editor With…*) shows the JSON.

| File | Editor | What you do there |
| --- | --- | --- |
| `*.chr` | **Tile Set** | Draw tiles with pencil, fill, line, rectangle and picker, in 1×1 or 2×2 tile blocks; copy, paste, flip, rotate, clear; add rows of tiles. Shows the tiles in any palette of a palette file. **Import PNG…** appends a picture's tiles to this tile set. |
| `*.pal.json` | **Palettes** | The 4 background and 4 sprite palettes: pick a colour of a palette, then a colour of the master palette. *Share colour 0* gives every palette the universal background colour. |
| `*.screen.json` | **Screen** | A 32×30 tile screen: paint tiles from the sheet, fill rectangles, pick, and paint background palettes per 16×16 attribute block. Overscan shading, tile and attribute grids. Compression `none` (`.nam`) or `rle`. **Import PNG…** makes a screen from a picture. |
| `*.map.json` | **Map** | Metatiles (2×2 tiles and a palette) and a map of them, any number of screens wide and high: paint, rectangle, fill, pick; edit metatiles; resize. |
| `*.msprite.json` | **Metasprites** | Groups of hardware sprites around an origin, and animations of them (below). |

The screen, map and metasprite editors draw with the tile set (`chr`) and palette file (`palettes`) the asset names
(relative paths). An open tile editor's unsaved tiles show at once in the others.

### Metasprites and animations

- **Metasprites** (left): add, duplicate, delete; name and origin of the selected one.
- **Drawing** (middle): a 128×128 pixel area (it grows to hold every tile), zoom 2–8×, an 8 pixel grid. Click to
  place the tile chosen in the sheet; drag a tile to move it; drag the magenta cross to move the origin; right-click
  a tile to delete it; Alt+click places over a tile. *Snap* moves tiles by 1 or 8 pixels.
- **Selected tile** (toolbar): sprite palette 0–3, *Flip H*, *Flip V*, *Behind* (drawn behind the background),
  *Delete*. Keys: `1`–`4`, `H`, `V`, `B`, `Delete`, arrows (Shift: 8 px), `Esc`, `PageUp`/`PageDown` for the
  previous/next metasprite.
- **Statistics** under the drawing: tiles (hardware sprites), the most sprites on one scanline against the system's
  limit (8 on the NES; lines over it are shaded red, and those sprites would not show), size, origin, and tiles too
  far from the origin (offsets are −128..127; −128 is refused for y because `$80` ends the table).
- **Animations** (right): add, rename, delete; *Loop*; frames of a metasprite and a duration in video frames
  (1–255), reordered with ↑/↓. The preview plays at 60 frames a second (`Space`), with an onion skin of the previous
  frame. Renaming a metasprite renames it in the frames; deleting it removes its frames.

Earlier tiles are drawn over later ones, as OAM does (a tile just placed goes first).

## Import PNG

**WinASM65 Graphics: Import PNG…** (Command Palette, the Explorer's *WinASM65 Graphics* menu on a `.png`, or the
button of the screen and tile editors) turns a picture into assets:

- **Background**: tiles + palettes + a screen (at most 256×240, a multiple of 8). The most frequent colour becomes the
  shared colour 0; each 16×16 block keeps its 3 most frequent other colours; blocks are grouped into 4 palettes.
- **Sprites**: tiles + palettes + a metasprite file. Colour 0 is transparent; a palette per tile; flipped copies of a
  tile are reused.

Choose the picture, the mode, the folder, the name, and a new tile set or an existing one to append to (tiles already
there are reused; an open tile editor must be saved first). Statistics and warnings (colours merged, blocks that did
not fit a palette) go to a notification and the *WinASM65 Graphics* output; the screen or metasprite file opens.

## Export, and use from assembly

Assets are exported when saved (`winasm65Graphics.exportOnSave`), with **Export Asset** (editor title bar, Explorer
menu) or **Export All Assets in Folder**. Problems go to the Problems panel, on the line of the value at fault. Files
are written next to the asset; labels are lower snake case (`Ship Left` → `ship_left`) after the asset's `prefix`,
constants upper case.

| Asset | Writes | Defines |
| --- | --- | --- |
| `game.pal.json` | `game.pal.bin` (32 bytes of palette RAM), `game.pal.asm` | `palettes:` 8 rows of 4 bytes; `PAL_<NAME>` = index of each palette |
| `title.screen.json` | `title.nam` (1024 bytes: 960 tiles, 64 attributes) or `title.rle`, `title.screen.asm` | `title:` `.incbin "title.nam"`; `TITLE_SIZE` |
| `level1.map.json` | `level1.map.asm` | `metatile_tl/tr/bl/br`, `metatile_pal`, `level1_map` (a row per map row); `LEVEL1_WIDTH`, `LEVEL1_HEIGHT`, `METATILE_COUNT` |
| `sprites.msprite.json` | `sprites.msprite.asm` | per metasprite `name:` rows `.byte dy, tile, attributes, dx` ended by `$80`; `metasprites_lo`/`_hi`; `MS_<NAME>`, `METASPRITE_COUNT`; per animation `name_anim:` rows `.byte metasprite, duration` ended by `$FF, loop` |

```asm
        .include "gfx/game.pal.asm"
        .include "gfx/sprites.msprite.asm"

        lda #<ship              ; a metasprite table: Y, tile, attributes, X per sprite
        sta ptr
        lda #>ship
        sta ptr+1

Title:  .incbin "gfx/title.nam"  ; 1024 bytes for $2000
Tiles:  .incbin "gfx/tiles.chr"
```

## Pictures on hover

In a WinASM65 source, hover:

- a label or constant of a generated source (`ship`, `MS_SHIP`, `fly_anim`, `palettes`, `PAL_SKY`, `title`,
  `level1_map`…): the metasprite, the animation's first frame, the palettes, the screen or the map it was made from;
- an `.incbin "tiles.chr"`, `.incbin "title.nam"` / `.rle`, `.incbin "game.pal.bin"` or an `.include` of a generated
  `.asm`: the asset behind it.

The picture is drawn by the designer at the largest whole scale that fits 256 pixels (no smoothing); a metasprite's
origin shows in magenta. The extension reads the workspace's `*.pal.asm`, `*.msprite.asm`, `*.screen.asm` and
`*.map.asm` that start with `; Generated by WinASM65.Graphics from …`, and follows them as they are exported again.

## Live preview

While a **WinASM65 debug session** runs, the editors' **● Live** button writes your edits into the running machine,
so you see them in the game without rebuilding:

| Editor | Written to (space of `winasm65.memory.write`) |
| --- | --- |
| Tile Set | `CHR`: the changed tiles, at tile × 16 |
| Palettes | `PAL`: the 32 bytes of palette RAM (4 background then 4 sprite palettes) |
| Screen | `PPU` at `$2000`: the first nametable (960 tile bytes, 64 attribute bytes) |

Turning Live on writes everything once; after that only the bytes that changed. The game may overwrite them (a screen
drawn again, palettes loaded again). Metasprites and maps have no Live preview: the game rebuilds OAM every frame and
draws maps from its own tables — export and rebuild. Some emulators do not let the debugger write video memory (the
space is reported unavailable, e.g. with MesenCE); the editor says so.

## Settings

| Setting | Default | |
| --- | --- | --- |
| `winasm65Graphics.designerPath` | *(empty)* | WinASM65.Designer executable (or `.dll`, run with `dotnet`); empty: the copy shipped with the extension. |
| `winasm65Graphics.exportOnSave` | `true` | Export an asset every time it is saved, and report its problems. |
| `winasm65Graphics.hoverPictures` | `true` | Pictures when hovering generated labels and `.incbin`/`.include` lines. |

## Commands

*New Palette…*, *New Tile Set…*, *New Screen…*, *New Map…*, *New Metasprites…* (Explorer menu on a folder),
*Export Asset*, *Export All Assets in Folder*, *Import PNG…* — all under **WinASM65 Graphics**.
