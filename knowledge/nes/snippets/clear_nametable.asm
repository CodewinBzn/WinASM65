; snippet: clear_nametable
; purpose: Fills nametable 0 ($2000-$23FF: 960 tiles + 64 attribute bytes) with one tile, attributes with 0.
; inputs: A = tile to fill with (0 = blank)
; outputs: nametable 0 filled, all attribute bytes 0 (background palette 0 everywhere)
; clobbers: A, X, Y
; cycles: about 12,000 (too long for vblank: screen OFF only)
; requires: nes.inc
; vars: none
; wrap: org=$C000
; usage: lda #0 / jsr clear_nametable   (screen off)

clear_nametable:
        pha
        bit PPUSTATUS
        lda #$20
        sta PPUADDR
        lda #$00
        sta PPUADDR             ; PPU address = $2000
        pla
        ldy #30                 ; 30 rows
@row:   ldx #32                 ; of 32 tiles
@tile:  sta PPUDATA
        dex
        bne @tile
        dey
        bne @row
        lda #0
        ldx #64                 ; 64 attribute bytes at $23C0
@attr:  sta PPUDATA
        dex
        bne @attr
        rts
