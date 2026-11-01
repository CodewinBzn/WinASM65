# Sprites (OAM)

The PPU shows up to 64 sprites, each an 8x8 tile (or 8x16) that moves freely. Their
description is OAM ("object attribute memory"): 64 entries x 4 bytes = 256 bytes.
Games keep a copy in CPU RAM at $0200-$02FF and copy it every frame with OAM DMA.

## One sprite = 4 bytes

Sprite n is at $0200 + n*4:

| Byte | Address (sprite 0) | Meaning |
|---|---|---|
| 0 | $0200 | Y: top of the sprite minus 1 (the sprite appears on line Y+1). $EF-$FF = hidden |
| 1 | $0201 | tile number (pattern table chosen by PPUCTRL bit 3) |
| 2 | $0202 | attributes (below) |
| 3 | $0203 | X: left edge, 0-255 |

Attributes byte:

| Bit | Meaning |
|---|---|
| 7 | flip vertically |
| 6 | flip horizontally |
| 5 | priority: 0 = in front of the background, 1 = behind it |
| 4-2 | unused (0) |
| 1-0 | sprite palette 0-3 (colours at $3F11, $3F15, $3F19, $3F1D) |

Example: sprite 1 = tile $03 at x=60, y=80, palette 1, flipped horizontally:

```asm
        lda #80
        sta $0204               ; sprite 1 Y
        lda #$03
        sta $0205               ; tile
        lda #%01000001          ; flip H, palette 1
        sta $0206
        lda #60
        sta $0207               ; X
```

## Hiding sprites

There is no "off" bit: put Y at $FF (below the screen). RAM is 0 after the reset clear,
and Y = 0 shows a sprite at the top-left corner: fill the Y bytes with $FF at reset
(snippet `hide_sprites`). X cannot hide a sprite.

## OAM DMA ($4014)

```asm
        lda #0
        sta OAMADDR             ; $2003
        lda #$02                ; page $0200
        sta OAMDMA              ; $4014: copies $0200-$02FF, CPU paused 513-514 cycles
```

Do it in NMI every frame, first thing (sprite memory fades if not refreshed and DMA must
happen in vblank). The game changes $0200-$02FF during the frame; NMI sends it.

## Limits and order

- Max 8 sprites on one scanline: the 9th and later (higher index) are not drawn.
  Games flicker (rotate the OAM order every frame) when more are needed.
- Sprite 0 is drawn in front of sprite 1, and so on (lower index = in front).
- 8x16 mode (PPUCTRL bit 5): tile byte bit 0 selects the pattern table, the tile pair
  is (tile & $FE) on top and (tile | 1) below.
- PPUMASK bit 2 = 0 hides sprites in the leftmost 8 pixels.

## Metasprites

A character bigger than 8x8 is several sprites drawn at offsets from one position:
a 16x16 hero = 4 sprites at (x,y), (x+8,y), (x,y+8), (x+8,y+8). The graphics tools
export metasprite tables (rows `dy, tile, attr, dx`, ended by $80); snippet
`draw_metasprite` draws them. See graphics-workflow.md.

## Sprite 0 hit

PPUSTATUS bit 6 becomes 1 when an opaque pixel of sprite 0 overlaps an opaque
background pixel. Games wait for it to change the scroll mid-screen (a fixed status
bar). It is cleared at the end of vblank. It never happens at x = 255, nor in the left
8 pixels when they are hidden, nor when either layer is off.

```asm
-       bit PPUSTATUS           ; wait for the end of the previous hit
        bvs -
-       bit PPUSTATUS           ; wait for the hit (bit 6 -> V flag)
        bvc -
```
