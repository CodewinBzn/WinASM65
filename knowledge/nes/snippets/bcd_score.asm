; snippet: bcd_score
; purpose: A decimal score kept one digit per byte (0-9), incremented with carry, and drawn as tiles.
; inputs: score (SCORE_DIGITS bytes, most significant digit first)
; outputs: add_point: score + 1 (9999 wraps to 0000). draw_score: digits written at SCORE_ADDR
; clobbers: A, X (draw_score), A, Y (add_point)
; requires: nes.inc
; vars: score 4
; wrap: org=$C000
; usage: jsr add_point when the player scores. Draw: jsr draw_score with the screen off, or
;        inside NMI (4 tiles, fast). The digit tiles of the template are at their ASCII codes
;        ("0" = $30), so tile = digit + $30.

SCORE_DIGITS = 4
SCORE_ADDR   = $2000+2*32+2     ; row 2, column 2 of nametable 0

add_point:
        ldy #SCORE_DIGITS-1     ; the last digit (units)
@carry: lda score,y
        clc
        adc #1
        cmp #10
        bcc @store              ; no carry: done
        lda #0                  ; 9 + 1 = 10: write 0, carry into the next digit
        sta score,y
        dey
        bpl @carry
        rts                     ; overflow: all digits are 0
@store: sta score,y
        rts

draw_score:
        bit PPUSTATUS
        lda #>(SCORE_ADDR)      ; parentheses: > binds tighter than +
        sta PPUADDR
        lda #<(SCORE_ADDR)
        sta PPUADDR
        ldx #0
@digit: lda score,x
        clc
        adc #'0'                ; digit -> its tile ($30-$39)
        sta PPUDATA
        inx
        cpx #SCORE_DIGITS
        bne @digit
        rts
