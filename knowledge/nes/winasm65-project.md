# A WinASM65 NES project

## Start one

Call the tool `new_nes_project` with an EMPTY folder: it copies the template
(templates/nrom-game). Then build it with `build_project` before changing anything.

```
my-game/
  config.json        the project: units and how the ROM is assembled
  src/header.asm     iNES header (16 bytes)
  src/nes.inc        register names, BUTTON_* masks, macros wait_vblank and ppu_addr
  src/game.asm       the game: RESET, MainLoop, NMI, subroutines, data
  src/vectors.asm    NMI/RESET/IRQ vectors at $FFFA
  chr/tiles.chr      8 KB of tiles
```

Most changes happen in `src/game.asm`. Add variables after `.memarea $0000`, logic in
`MainLoop` (marked `--- game logic goes here ---`), video writes in `NMI` (marked).

## config.json (NROM-128)

```json
{
  "Input": [
    { "FileName": "src/header.asm", "Dependencies": [] },
    { "FileName": "src/game.asm", "Dependencies": [] },
    { "FileName": "src/vectors.asm", "Dependencies": ["src/game.asm"] }
  ],
  "Output": {
    "ObjectFile": "game.nes",
    "Files": [
      { "FileName": "src/header.o", "Size": "$10" },
      { "FileName": "src/game.o", "Size": "$3FFA" },
      { "FileName": "src/vectors.o" },
      { "FileName": "chr/tiles.chr" }
    ]
  },
  "Emulator": { "Name": "mesen2", "Path": "C:\\Mesen2\\Mesen.exe" }
}
```

- Input: each unit is assembled to a `.o` next to it (and a `.symb` symbol file).
- Dependencies: units whose labels this unit may use.
- Output.Files are concatenated in order into ObjectFile; `Size` (hexadecimal, with `$`)
  pads a part. The ROM must be 24,592 bytes for NROM-128.
- Emulator: optional, only for debugging (Path of Mesen.exe).
- New source files: `.include "file.asm"` from game.asm (simplest), or add a unit.

## Build

- Tool: `build_project {config: "<folder>/config.json"}`. It returns "Build succeeded."
  with the image size, or "Build failed." with `file:line: message` errors.
- Command line: `WinASM65 -c config.json` (add `-l` for listings `src/*.lst`).
- Build after EVERY change. Fix the first error first, then rebuild.

## The tools (MCP)

| Tool | Use it to |
|---|---|
| nes_reference {topic} | read a page of this knowledge base (index lists them) |
| nes_search {query} | find the sections that mention something |
| nes_snippet {name} | get a tested routine to copy (list: call with an unknown name) |
| new_nes_project {folder, name} | create a project from the template |
| build_project {config} | build; errors as file:line |
| get_listing {config, file, fromLine, toLine} | see the address and bytes of each line |
| find_symbol {config, pattern} | value of labels and variables |
| assemble {source} | try a fragment of code without a project |
| describe_instruction {mnemonic} | what an instruction does, its modes and cycles |
| debug_launch {config, breakpoints} | run in Mesen2 and stop at a line (needs Emulator) |
| read_memory {space, address, length} | RAM (CPU), OAM, PPU, PAL while debugging |
| edit_tiles / render_asset / export_assets | graphics, see graphics-workflow.md |

## Working method (small steps)

1. Read the relevant page (`nes_reference`) or snippet (`nes_snippet`) BEFORE writing
   NES code: register numbers and bit orders must not be guessed.
2. Change one thing.
3. `build_project`. Fix errors (see winasm65-syntax.md, "Error messages").
4. Check: `find_symbol` for variable addresses; `debug_launch` + `read_memory` to look
   at RAM, OAM ($0200 copy) or the screen.

## Checklist for a working ROM

- RESET clears RAM, hides sprites, waits two vblanks, loads palettes, then turns on
  NMI (PPUCTRL bit 7) and rendering (PPUMASK $1E).
- NMI does OAM DMA, short video writes, then scroll + PPUCTRL, and `rti`.
- The main loop waits for the frame (`jsr wait_frame`) and never writes PPUDATA while
  the screen is on.
- vectors.asm names NMI, RESET, IRQ, which exist as labels in game.asm.
