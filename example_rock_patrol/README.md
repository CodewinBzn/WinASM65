# Rock Patrol

A small NES game written for WinASM65, code and graphics. It is the example of
the documentation (`docs/site/example.html`).

Rocks fall through a starry sky that scrolls down past the ship. Fly with the
d-pad, fire with A (hold it to keep firing). A rock shot is a point; every ten
points the rocks fall faster and come more often. A rock that hits the ship
costs one of its three lives, and the ship blinks for two seconds, unhurt.
After the last life, GAME OVER shows the score; START goes back to the title.

## Build

```bat
compile.bat
```

or `WinASM65 -l -c config.json` from this folder. The ROM is `rock_patrol.nes`:
mapper 0 (NROM), 32 KB of program and 8 KB of tiles. Open it in Mesen2, or
press F5 in VS Code with the WinASM65 extension.

To debug it, point the `Emulator` section of `config.json` at your Mesen2
(`C:\Emulators\Mesen2\Mesen.exe` there is an example path), or set
`winasm65.emulatorPath` in VS Code, which takes precedence.

## Files

| File | What it is |
|---|---|
| `config.json` | The project: four units, combined with the tiles into the ROM |
| `compile.bat` | Exports the graphics if the designer is on the PATH, then builds with listings |
| `src/header.asm` | The 16-byte iNES header |
| `src/data.asm` | The first 16 KB of program, `$8000`-`$BFFF`: the screens, the sky's map, the palettes and two tables |
| `src/game.asm` | The game, `$C000`-`$FFF9`: start-up, NMI, the title, play and game over, the sprites |
| `src/vectors.asm` | The NMI, RESET and IRQ vectors |
| `src/nes.inc` | NES registers, buttons and two macros |
| `chr/tiles.chr` | The 256 tiles, for both pattern tables |
| `gfx/game.pal.json` | The 8 palettes |
| `gfx/objects.msprite.json` | The ship, the shot, the rock, the burst and the heart, and two animations |
| `gfx/title.screen.json`, `gfx/over.screen.json` | The title and game over screens |
| `gfx/play.map.json` | The sky: 16 x 30 metatiles, two screens one above the other |

## Graphics

The `gfx/*.json` files are assets of the Graphics Designer. In VS Code with the
graphics extension, open one and its editor opens; saving it exports it (the
`.asm`, `.bin` and `.nam` files next to it). Without VS Code,
`WinASM65.Designer export gfx` exports them. The exported files are committed,
so the game builds with the assembler alone; a test checks they match their
assets.

`tools/example_art.py`, in the repository, drew the tiles and wrote the first
version of the assets; run it again to start them over.

The code and the graphics are original and may be reused freely.
