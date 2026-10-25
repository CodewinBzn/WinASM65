# Star Catcher

A small NES game written for WinASM65, code and graphics. It is the example of
the documentation (`docs/site/example.html`).

A vertical shooter. The starry sky scrolls down past your ship, its top half
rippling like a heat haze: fly with the d-pad, fire with A or B (hold it to
keep firing). Shoot the meteors, the saucers that zigzag, and the hunters that
dive at you; on later levels come divers, turrets that aim at you, weavers that
fire fans, giant meteors that break in two, and whole squadrons. Catch the
stars for bonus points. After about forty seconds the level's boss comes down
and fights in three phases: fans of bullets, aimed bursts, then a spiral and
rings. Break it and a 3D stage follows, the ship seen from behind over a floor
that runs at it; then the next level, harder. Three hits and the game is over.
START begins a game.

## Build

```bat
compile.bat
```

or `WinASM65 -l -c config.json` from this folder. The ROM is `star_catcher.nes`
(mapper 0, 16 KB of program, 8 KB of tiles in two pattern tables); open it in Mesen2, or press F5 in
VS Code with the WinASM65 extension.

To debug it, point the `Emulator` section of `config.json` at your Mesen2
(`C:\Emulators\Mesen2\Mesen.exe` there is an example path), or set
`winasm65.emulatorPath` in VS Code, which takes precedence.

## Files

| File | What it is |
|---|---|
| `config.json` | The project: three units, combined with the tiles into the ROM |
| `src/header.asm` | The 16-byte iNES header (horizontal mirroring, for vertical scrolling) |
| `src/game.asm` | The game: start-up, main loop, NMI, scrolling and the rippling sky, ship and shots, enemies, the boss, score, sound |
| `src/bullets.asm` | The enemies' bullets in 16 directions and fixed point, aiming, and the attack patterns |
| `src/mutants.asm` | What comes down at each tier, the squadrons, and the moves of the later mutants |
| `src/stage3d.asm` | The 3D stage: projection, the floor's turning colours, its enemies, shots and bullets |
| `src/nes.inc` | NES registers, buttons, and two macros, included by `game.asm` |
| `src/vectors.asm` | The NMI, RESET and IRQ vectors; reads the addresses from `game.asm`'s symbols |
| `chr/tiles.chr` | Pattern table 0: the tiles of the screens, the sky and every sprite |
| `chr/floor.chr` | Pattern table 1: the 3D stage's background |
| `gfx/game.pal.json` | The 8 palettes |
| `gfx/objects.msprite.json` | The ship and its shot, the star and its twinkle animation, the meteor, the saucer, the hunter, the bullet, the burst, the heart, the boss; the diver, turret, weaver and giant meteor; the ship from behind and the 3D enemies in three sizes |
| `gfx/title.screen.json`, `gfx/over.screen.json` | The title and game over screens |
| `gfx/play.map.json` | The sky, a map of 2x2 metatiles two screens high that scrolls in a loop, with its nebulae |
| `gfx/stage3d.screen.json`, `gfx/stage3d.pal.json` | The 3D stage's screen and palettes |
| `gfx/*.asm`, `*.bin`, `*.nam` | What the assets export, included by `game.asm` |

## Graphics

The graphics are edited with the WinASM65 Graphics Designer extension for VS
Code: open any file of `gfx/` or `chr/tiles.chr` and its editor opens. Saving an
asset exports it (the `.asm`, `.bin` and `.nam` files next to it), and the next
build uses it. Without VS Code, `WinASM65.Designer export gfx` exports them.

The exported files are committed, so the game builds with the assembler alone;
a test checks they match their assets.

The later graphics (the four mutants, the nebulae, the ship from behind, the 3D
enemies and the 3D screen) are drawn by `tools/star_catcher_art.py` in the
repository, which writes them into the assets; run it again to redraw them.

The code and the graphics are original and may be reused freely.
