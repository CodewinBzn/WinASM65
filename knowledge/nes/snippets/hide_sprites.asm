; snippet: hide_sprites
; purpose: Hides all 64 sprites by putting them below the screen (Y = $FF) in the OAM copy at $0200.
; inputs: none
; outputs: bytes 0, 4, 8 ... 252 of page $0200 = $FF
; clobbers: A, X
; cycles: about 1,300
; requires: none
; vars: none
; wrap: org=$C000
; usage: jsr hide_sprites at reset (RAM is not cleared to $FF by itself: Y = 0 shows a sprite on line 1),
;        or at the start of every frame before drawing the sprites again.

hide_sprites:
        lda #$FF
        ldx #0
@loop:  sta $0200,x             ; Y byte of a sprite
        inx
        inx
        inx
        inx                     ; next sprite: 4 bytes further
        bne @loop               ; 64 sprites x 4 = 256: X wraps to 0
        rts
