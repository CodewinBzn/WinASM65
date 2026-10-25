# Timing, vblank and NMI

## The frame (NTSC)

- 60 frames per second; one frame = 262 scanlines = 29,780 CPU cycles.
- Lines 0-239 are drawn (rendering). Line 241 starts the vertical blank (vblank).
- Vblank lasts 20 scanlines = about 2,273 CPU cycles. Only then (or with rendering off)
  may the CPU write video memory (PPUADDR/PPUDATA, palettes, OAM DMA).
- PAL consoles: 50 frames per second, longer vblank. Code written for NTSC works.

## NMI

When PPUCTRL bit 7 = 1, the PPU interrupts the CPU at the start of every vblank: the
CPU jumps to the NMI vector ($FFFA). The template's handler (snippet `nmi_oam_dma`):

1. save A, X, Y;
2. OAM DMA (513 cycles);
3. short video memory updates (score digits, a few tiles, palette changes);
4. set the scroll (PPUSCROLL twice) and PPUCTRL - always after step 3;
5. set `frame_ready` = 1 so the main loop knows a frame passed;
6. restore A, X, Y and `rti`.

The game logic runs in the main loop, which waits for the frame with `wait_frame`:

```asm
MainLoop:
        jsr wait_frame          ; returns after the next NMI
        jsr read_joypad
        jsr update_game         ; move things, change variables, fill OAM at $0200
        jmp MainLoop
```

## Vblank budget (about 2,273 cycles)

| Work | Cycles |
|---|---|
| Save/restore registers, rti | ~30 |
| OAM DMA | 514 |
| One byte with `lda table,x / sta PPUDATA / inx / cpx / bne` | ~15 per byte |
| One byte unrolled `lda #v / sta PPUDATA` | 6 per byte |
| Setting PPUADDR | ~12 |
| Scroll + PPUCTRL | ~16 |

So after DMA, about 100 bytes of looped PPUDATA writes fit. Clearing a nametable (1024
bytes) does NOT fit: do it with rendering off.

Pattern for bigger changes: the main loop fills a small buffer (address + bytes) and a
flag; NMI copies the buffer when the flag is set, then clears it.

## Rendering off / on

Rendering off (`lda #0 / sta PPUMASK`): the screen is black (background colour), and
video memory can be written at any time, for any length. Use it to load a level or a
title screen:

```asm
screen_off:
        lda #0
        sta PPUCTRL             ; NMI off while drawing
        sta PPUMASK
        rts
screen_on:
        wait_vblank             ; start at the beginning of a vblank (NMI is off)
        lda #0
        sta PPUSCROLL
        sta PPUSCROLL
        lda #%10000000
        sta PPUCTRL             ; NMI on
        lda #%00011110
        sta PPUMASK             ; show background and sprites
        rts
```

## At power-on

The PPU ignores writes for about 30,000 cycles. Wait for two vblanks (polling
PPUSTATUS with NMI off) before writing it: snippet `reset_init`.

## Common timing bugs

- Writing PPUDATA from the main loop while rendering: garbage tiles, shaking screen.
- Forgetting the scroll reset after PPUADDR writes in NMI: the picture jumps.
- Too much work in NMI: the top of the screen is corrupted.
- Polling PPUSTATUS with NMI on: frames are missed (the NMI handler's read races it).
- The main loop takes more than a frame: the game slows down (fine, but no glitches if
  all video writes are in NMI).
