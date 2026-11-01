; snippet: draw_text
; purpose: Writes texts on the background from a table: (.word address / .asciiz "TEXT") entries, ended by .word 0.
; inputs: text_ptr (2 bytes, zero page) = address of the table
; outputs: tiles written to the nametable (tile number = ASCII code of the character)
; clobbers: A, Y
; requires: nes.inc
; vars: text_ptr 2, text_tmp 1
; wrap: org=$C000
; usage: Screen off, or in NMI for a few characters only.
;          lda #<Texts / sta text_ptr / lda #>Texts / sta text_ptr+1 / jsr draw_text
;        Address of row R, column C of nametable 0 = $2000 + R*32 + C (R 0-29, C 0-31).
;        The template's tiles have only UPPERCASE letters, digits, space - ! :

draw_text:
        ldy #0
@next:  lda (text_ptr),y        ; low byte of the address
        sta text_tmp
        iny
        lda (text_ptr),y        ; high byte; 0 ends the table
        beq @done
        bit PPUSTATUS           ; reset the PPUADDR latch
        sta PPUADDR             ; high byte first
        lda text_tmp
        sta PPUADDR
        iny
@char:  lda (text_ptr),y
        beq @end                ; 0 ends this text
        sta PPUDATA             ; the tile of this character
        iny
        bne @char
@end:   iny
        bne @next
@done:  rts

; Example table: two texts.
Texts:
        .word $2000+10*32+5     ; row 10, column 5
        .asciiz "GAME ON"
        .word $2000+12*32+8     ; row 12, column 8
        .asciiz "PRESS START"
        .word 0                 ; end of the table
