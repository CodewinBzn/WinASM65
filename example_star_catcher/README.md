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

## Files

| File | What it is |
|---|---|
| `config.json` | The project: three units, combined with the tiles into the ROM |
| `src/header.asm` | The 16-byte iNES header |
| `src/game.asm` | The game: start-up, main loop, NMI, ship, falling objects, score, sound |
| `src/nes.inc` | NES registers, buttons, and two macros, included by `game.asm` |
| `src/vectors.asm` | The NMI, RESET and IRQ vectors; reads the addresses from `game.asm`'s symbols |
| `chr/make_chr.py` | Draws every tile as ASCII art and writes `chr/tiles.chr` |
| `chr/tiles.chr` | The 8 KB of tiles, generated |

To change a tile, edit `chr/make_chr.py` and run `python chr/make_chr.py`
(`--png preview.png` also writes a picture of the tiles).

The code and the graphics are original and may be reused freely.
