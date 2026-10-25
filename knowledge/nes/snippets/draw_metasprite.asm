; snippet: draw_metasprite
; purpose: Draws a metasprite (several 8x8 sprites moved together) into OAM, in the format export_assets writes.
; inputs: ms_ptr (2 bytes) = metasprite table; ms_x, ms_y = origin on screen; oam_index = first free byte in OAM (0, 4, 8...)
; outputs: one OAM entry per row of the table; oam_index advanced past them
; clobbers: A, X, Y
; cycles: about 60 per sprite
; requires: none
; vars: ms_ptr 2, ms_x 1, ms_y 1, oam_index 1
; wrap: org=$C000
; usage: Table format (.msprite.asm from export_assets): rows ".byte dy, tile, attributes, dx"
;        (dy/dx = signed offsets from the origin, $F8 = -8), ended by ".byte $80".
;        Each frame: lda #0 / sta oam_index / jsr hide_sprites, then for each object:
;          ldx #MS_SHIP / lda metasprites_lo,x / sta ms_ptr / lda metasprites_hi,x / sta ms_ptr+1
;          lda obj_x / sta ms_x / lda obj_y / sta ms_y / jsr draw_metasprite

draw_metasprite:
        ldy #0
        ldx oam_index
@loop:  lda (ms_ptr),y          ; dy
        cmp #$80
        beq @done               ; $80 ends the table
        clc
        adc ms_y
        sta $0200,x             ; OAM byte 0: Y
        iny
        lda (ms_ptr),y
        sta $0201,x             ; OAM byte 1: tile
        iny
        lda (ms_ptr),y
        sta $0202,x             ; OAM byte 2: attributes
        iny
        lda (ms_ptr),y          ; dx
        clc
        adc ms_x
        sta $0203,x             ; OAM byte 3: X
        iny
        inx
        inx
        inx
        inx
        bne @loop               ; X wrapped to 0: OAM is full (64 sprites)
@done:  stx oam_index
        rts

; Example data, as export_assets writes it for a 16x8 ship with its origin at its centre.
ship:
        .byte $FC, $01, $00, $F8        ; dy -4, tile 1 (left half), palette 0, dx -8
        .byte $FC, $02, $00, $00        ; dy -4, tile 2 (right half), palette 0, dx 0
        .byte $80

metasprites_lo:
        .lobytes ship
metasprites_hi:
        .hibytes ship

MS_SHIP = 0
METASPRITE_COUNT = 1
