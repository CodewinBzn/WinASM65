# Star Catcher

A small NES game written for WinASM65, code and graphics. It is the example of
the documentation (`docs/site/example.html`).

Move the ship with the d-pad, catch the falling stars, avoid the meteors. Every
star is a point; every ten points the sky gets busier. Three meteors and the
game is over. START begins a game.

## Build

```bat
compile.bat
```

or `WinASM65 -l -c config.json` from this folder. The ROM is `star_catcher.nes`
(mapper 0, 16 KB of program, 8 KB of tiles); open it in Mesen2, or press F5 in
VS Code with the WinASM65 extension.

To debug it, point the `Emulator` section of `config.json` at your Mesen2
(`C:\Emulators\Mesen2\Mesen.exe` there is an example path), or set
`winasm65.emulatorPath` in VS Code, which takes precedence.

## Files

| File | What it is |
|---|---|
| `config.json` | The project: three units, combined with the tiles into the ROM |
| `src/header.asm` | The 16-byte iNES header |
| `src/game.asm` | The game: start-up, main loop, NMI, ship, falling objects, score, sound |
| `src/nes.inc` | NES registers, buttons, and two macros, included by `game.asm` |
| `src/vectors.asm` | The NMI, RESET and IRQ vectors; reads the addresses from `game.asm`'s symbols |
| `chr/tiles.chr` | The 8 KB of tiles |
| `gfx/game.pal.json` | The 8 palettes |
| `gfx/objects.msprite.json` | The ship, the star and its twinkle animation, the meteor, the burst |
| `gfx/title.screen.json`, `gfx/over.screen.json` | The title and game over screens |
| `gfx/play.map.json` | The play background, a map of 2x2 metatiles |
| `gfx/*.asm`, `*.bin`, `*.nam` | What the assets export, included by `game.asm` |

## Graphics

The graphics are edited with the WinASM65 Graphics Designer extension for VS
Code: open any file of `gfx/` or `chr/tiles.chr` and its editor opens. Saving an
asset exports it (the `.asm`, `.bin` and `.nam` files next to it), and the next
build uses it. Without VS Code, `WinASM65.Designer export gfx` exports them.

The exported files are committed, so the game builds with the assembler alone;
a test checks they match their assets.

The code and the graphics are original and may be reused freely.
