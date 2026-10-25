# NES knowledge base (WinASM65)

Look facts up here instead of remembering them. Tools: `nes_reference {topic}` returns
a page (topic = page name without .md, or a keyword), `nes_search {query}` finds
sections, `nes_snippet {name}` returns a tested routine.

## Hardware pages

| Page | Answers |
|---|---|
| [cpu-memory-map](cpu-memory-map.md) | CPU addresses: RAM, zero page, stack, $0200 OAM copy, registers, ROM, vectors; where variables go |
| [ppu-registers](ppu-registers.md) | $2000-$2007 bit by bit, PPUADDR high/low latch, PPUDATA, scroll, write rules |
| [ppu-memory](ppu-memory.md) | tiles, nametable address of a row/column, attribute byte packing, palettes $3F00, mirroring |
| [oam-sprites](oam-sprites.md) | the 4 bytes of a sprite, attribute bits, hiding sprites, OAM DMA, 8 per line, sprite 0 hit |
| [timing-vblank](timing-vblank.md) | frame and vblank cycles, what NMI does, rendering off/on, timing bugs |
| [controllers](controllers.md) | reading $4016, button order and masks, held vs pressed |
| [apu-sound](apu-sound.md) | pulse/triangle/noise registers, note periods, sound effect recipes |
| [mappers-nrom](mappers-nrom.md) | iNES header bytes, NROM-128/256 layout and ROM sizes, CHR |
| [palette-colors](palette-colors.md) | the 64 colour indexes with names (black $0F, white $30...) |

## WinASM65 pages

| Page | Answers |
|---|---|
| [winasm65-syntax](winasm65-syntax.md) | labels, numbers, `<`/`>` trap, `.byte`/`.res`/`.memarea`, macros, error messages and fixes |
| [winasm65-project](winasm65-project.md) | template files, config.json, building, the MCP tools, working method, checklist |
| [graphics-workflow](graphics-workflow.md) | edit_tiles, the template's tile numbers, palettes/metasprites/screens assets, export_assets |

## Snippets (`nes_snippet {name}`)

Each is a tested routine with a header: purpose, inputs, outputs, clobbered registers,
`vars:` (variables to declare with `.res`) and `requires:`.

| Name | What |
|---|---|
| reset_init | RESET: stack, clear RAM, hide sprites, wait two vblanks |
| wait_vblank | macro polling PPUSTATUS (NMI off only) |
| wait_frame | wait for the next NMI (main loop) |
| nmi_oam_dma | NMI handler: OAM DMA, scroll, frame flag |
| load_palettes | copy 32 colours to $3F00 |
| clear_nametable | fill nametable 0 with a tile |
| draw_text | write texts from a table of (address, "TEXT") |
| tile_address | address of a tile from row/column at run time, write one tile |
| hide_sprites | all sprites off screen |
| read_joypad | buttons held and newly pressed |
| move_sprite | move the player with the d-pad, write sprite 0 |
| draw_metasprite | draw an exported metasprite table into OAM |
| add16 | 16-bit add and subtract |
| compare16 | 16-bit unsigned compare |
| random_lfsr | 8-bit random numbers |
| bcd_score | decimal score digits: add a point, draw it |
| collision_aabb | do two 8x8 boxes overlap |
| pulse_blip | enable sound, beep on pulse 1 |
| scroll_setup | set the scroll in NMI, scroll right |

## Template

`new_nes_project {folder, name}` copies [templates/nrom-game](templates/nrom-game/src/game.asm):
a NROM-128 game that shows "HELLO NES" and a ship sprite, with RESET, NMI, main loop,
read_joypad, palettes and the 8 KB tile set. It builds to a 24,592-byte ROM.
