; snippet: load_palettes
; purpose: Copies 32 colours to palette memory $3F00-$3F1F (4 background + 4 sprite palettes).
; inputs: the table Palettes (32 bytes, colour indexes $00-$3F, see palette-colors.md)
; outputs: palette memory written
; clobbers: A, X
; cycles: about 450
; requires: nes.inc
; vars: none
; wrap: org=$C000
; usage: Call with the screen off (after reset) or inside NMI. Byte 0 is the
;        universal background colour shown where every pixel is colour 0.

load_palettes:
        bit PPUSTATUS           ; reset the PPUADDR latch
        lda #$3F
        sta PPUADDR             ; high byte first
        lda #$00
        sta PPUADDR
        ldx #0
@loop:  lda Palettes,x
        sta PPUDATA             ; PPUADDR increments by 1 after each write
        inx
        cpx #32
        bne @loop
        rts

Palettes:
        .byte $0F, $30, $16, $28        ; background 0: black (universal), white, red, yellow
        .byte $0F, $2A, $1A, $0A        ; background 1: greens
        .byte $0F, $21, $11, $01        ; background 2: blues
        .byte $0F, $00, $10, $20        ; background 3: greys
        .byte $0F, $30, $11, $16        ; sprite 0 (colour 0 is transparent for sprites)
        .byte $0F, $28, $38, $30        ; sprite 1
        .byte $0F, $07, $17, $27        ; sprite 2
        .byte $0F, $27, $37, $30        ; sprite 3
