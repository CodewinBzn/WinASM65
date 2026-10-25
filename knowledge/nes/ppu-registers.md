# PPU registers ($2000-$2007)

The PPU draws the picture. The CPU talks to it only through these 8 registers
(names as in the template's nes.inc).

## PPUCTRL $2000 (write)

| Bit | Meaning |
|---|---|
| 7 | 1 = generate NMI at the start of vblank |
| 6 | always 0 |
| 5 | sprite size: 0 = 8x8, 1 = 8x16 |
| 4 | background pattern table: 0 = $0000, 1 = $1000 |
| 3 | sprite pattern table (8x8 sprites): 0 = $0000, 1 = $1000 |
| 2 | PPUDATA increment: 0 = +1 (across), 1 = +32 (down a column) |
| 1-0 | base nametable: 0 = $2000, 1 = $2400, 2 = $2800, 3 = $2C00 (scroll bit 8) |

Template value: `%10000000` (NMI on, everything from pattern table 0, nametable 0).

## PPUMASK $2001 (write)

| Bit | Meaning |
|---|---|
| 7, 6, 5 | emphasize blue, green, red (usually 0) |
| 4 | 1 = show sprites |
| 3 | 1 = show background |
| 2 | 1 = show sprites in the leftmost 8 pixels |
| 1 | 1 = show background in the leftmost 8 pixels |
| 0 | 1 = greyscale |

`%00011110` ($1E) shows everything. `0` turns rendering off (then video memory can be
written at any time).

## PPUSTATUS $2002 (read)

| Bit | Meaning |
|---|---|
| 7 | 1 = vblank has started (cleared by reading $2002 and at the end of vblank) |
| 6 | sprite 0 hit |
| 5 | sprite overflow (unreliable) |

Reading $2002 also resets the write latch shared by PPUSCROLL and PPUADDR.
`bit PPUSTATUS` copies bit 7 to N: `bpl` loops while not in vblank (macro `wait_vblank`).

## OAMADDR $2003, OAMDATA $2004

Sprite memory address and data. Do not write sprites one by one: use OAM DMA
(`lda #0 / sta OAMADDR / lda #$02 / sta OAMDMA`, see oam-sprites.md).

## PPUSCROLL $2005 (write twice)

First write = X scroll (0-255), second write = Y scroll (0-239). Set it at the END of
NMI, after all PPUADDR/PPUDATA writes, together with PPUCTRL bits 1-0.

## PPUADDR $2006 (write twice) and PPUDATA $2007

PPUADDR takes the video address HIGH byte first, then LOW byte. Each PPUDATA write
stores a byte there and adds 1 (or 32) to the address.

```asm
        bit PPUSTATUS           ; reset the latch so the next write is the high byte
        lda #$21                ; $2000 + 10*32 + 5 = $2145 (row 10, column 5)
        sta PPUADDR
        lda #$45
        sta PPUADDR
        lda #'A'                ; tile $41
        sta PPUDATA             ; written at $2145, address -> $2146
        lda #'B'
        sta PPUDATA             ; written at $2146
```

With expressions: `lda #>($2000+10*32+5)` and `lda #<($2000+10*32+5)`. The parentheses
are required: `>` and `<` bind tighter than `+` (see winasm65-syntax.md).
The template's macro does it: `ppu_addr NAMETABLE0+10*32+5`.

Reading PPUDATA returns the PREVIOUS buffered byte (do one dummy read first), except
for palette addresses.

## Rules (the usual bugs)

1. Write PPUADDR/PPUDATA only while rendering is off (PPUMASK = 0) or during vblank
   (inside NMI). Otherwise the screen is corrupted.
2. PPUADDR writes overwrite the scroll: after them, write PPUSCROLL twice (0, 0) and
   PPUCTRL again, or the screen jumps.
3. Always `bit PPUSTATUS` before the first PPUADDR write if the latch state is unknown.
4. Turn the screen on (PPUMASK) right after a vblank starts, with the scroll set.
5. With NMI on, do not poll PPUSTATUS for vblank: wait for the NMI (snippet `wait_frame`).
