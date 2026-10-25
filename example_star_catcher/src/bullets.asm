; Star Catcher - enemy bullets and attack patterns. Included by game.asm.
;
; A bullet moves in one of 16 directions (0 right, 4 down, 8 left, 12 up) at a
; speed counted in half pixels a frame. Its position and velocity are 8.8 fixed
; point - a pixel and 256ths of a pixel - so a bullet going 22.5 degrees off
; the vertical slides one pixel sideways every few frames instead of not at all.

; A bullet from (new_x, new_y) in direction A, at bullet_speed half pixels a
; frame, if a slot is free. Keeps Y; X is spoilt.
fire_bullet:
        and #15
        sta tmp2
        ldx #MAX_BOMBS-1
@find:  lda bomb_y,x
        beq @found
        dex
        bpl @find
        rts                     ; the sky is full of bullets already
@found: tya
        pha
        ldy tmp2
        lda new_x
        sta bomb_x,x
        lda new_y
        bne +
        lda #1                  ; 0 means a free slot
+       sta bomb_y,x
        lda #$80                ; the middle of the pixel
        sta bomb_xf,x
        sta bomb_yf,x
        lda #0
        sta bomb_vxl,x
        sta bomb_vxh,x
        sta bomb_vyl,x
        sta bomb_vyh,x
        lda bullet_speed
        sta tmp
@speed: lda bomb_vxl,x          ; velocity = the direction's half pixel, speed times
        clc
        adc DirXL,y
        sta bomb_vxl,x
        lda bomb_vxh,x
        adc DirXH,y
        sta bomb_vxh,x
        lda bomb_vyl,x
        clc
        adc DirYL,y
        sta bomb_vyl,x
        lda bomb_vyh,x
        adc DirYH,y
        sta bomb_vyh,x
        dec tmp
        bne @speed
        pla
        tay
        rts

; Moves the bullets, and checks each against the ship.
move_bombs:
        ldx #MAX_BOMBS-1
@loop:  lda bomb_y,x
        beq @next
        lda bomb_yf,x
        clc
        adc bomb_vyl,x
        sta bomb_yf,x
        lda bomb_y,x
        adc bomb_vyh,x
        cmp #232                ; off the bottom, or above the top and wrapped
        bcs @gone
        cmp #8
        bcc @gone
        sta bomb_y,x
        lda bomb_xf,x
        clc
        adc bomb_vxl,x
        sta bomb_xf,x
        lda bomb_x,x
        adc bomb_vxh,x
        cmp #248                ; off a side, either one
        bcs @gone
        sta bomb_x,x
        lda hit_timer
        bne @next
        lda bomb_x,x            ; the bullet's round middle
        clc
        adc #2
        sta box_bx
        lda bomb_y,x
        clc
        adc #2
        sta box_by
        lda #4
        sta box_bw
        sta box_bh
        jsr ship_box
        jsr overlap
        bcc @next
        jsr ship_hit
@gone:  lda #0
        sta bomb_y,x
@next:  dex
        bpl @loop
        rts

; ---------------------------------------------------------------------------
; Aiming

; The direction (0-15) from (new_x, new_y) to the middle of the ship, in A.
; The differences are halved to fit a signed byte; the angle comes from their
; signs, which is larger, and how far the smaller is from 0, from a fifth and
; from two thirds of the larger: on an axis, 22.5 degrees off it, or 45.
aim:
        lda ship_x
        clc
        adc #8
        sec
        sbc new_x
        jsr half
        sta aim_dx
        lda ship_y
        clc
        adc #4
        sec
        sbc new_y
        jsr half
        sta aim_dy
        lda aim_dx              ; |dx| and |dy|
        jsr absolute
        sta aim_ax
        lda aim_dy
        jsr absolute
        sta aim_ay
        cmp aim_ax
        bcs @vertical
        ; closer to horizontal: big = |dx|, small = |dy|
        lda aim_ay
        ldy aim_ax
        jsr offset              ; 0, 1 or 2 steps off the axis
        sta tmp
        lda aim_dx
        bmi @left
        lda aim_dy              ; right (0): +steps if going down
        bmi +
        lda tmp
        rts
+       lda #16
        sec
        sbc tmp
        and #15
        rts
@left:  lda aim_dy              ; left (8): -steps if going down
        bmi +
        lda #8
        sec
        sbc tmp
        rts
+       lda #8
        clc
        adc tmp
        rts
@vertical:                      ; closer to vertical: big = |dy|, small = |dx|
        lda aim_ax
        ldy aim_ay
        jsr offset
        sta tmp
        lda aim_dy
        bmi @up
        lda aim_dx              ; down (4): -steps if going right
        bmi +
        lda #4
        sec
        sbc tmp
        rts
+       lda #4
        clc
        adc tmp
        rts
@up:    lda aim_dx              ; up (12): +steps if going right
        bmi +
        lda #12
        clc
        adc tmp
        and #15
        rts
+       lda #12
        sec
        sbc tmp
        rts

; A, a 9-bit difference (carry clear: negative), halved into a signed byte.
half:
        bcs +
        sec                     ; negative: a 1 comes in on the left
        ror a
        rts
+       lsr a
        rts

absolute:
        bpl +
        eor #$FF
        clc
        adc #1
+       rts

; Steps off the axis, for small A and big Y: 0 if A < Y/5, 1 if A < 2Y/3, else 2.
offset:
        sta tmp
        sty tmp2
        cmp #52                 ; 5 * 52 > 255 > any big
        bcs @two_thirds
        asl a
        asl a
        adc tmp                 ; 5 * small (no carry: below 256)
        cmp tmp2
        bcs @two_thirds
        lda #0
        rts
@two_thirds:
        lda tmp                 ; 1.5 * small against big
        lsr a
        clc
        adc tmp
        bcs @diagonal
        cmp tmp2
        bcs @diagonal
        lda #1
        rts
@diagonal:
        lda #2
        rts

; ---------------------------------------------------------------------------
; Patterns, all from (new_x, new_y)

; One bullet at the ship.
shoot_aimed:
        jsr aim
        jmp fire_bullet

; Three bullets around direction A, 22.5 degrees apart.
shoot_fan3:
        sta aim_dir
        sec
        sbc #1
        jsr fire_bullet
        lda aim_dir
        jsr fire_bullet
        lda aim_dir
        clc
        adc #1
        jmp fire_bullet

; Five bullets around direction A.
shoot_fan5:
        sta aim_dir
        sec
        sbc #2
        jsr fire_bullet
        lda aim_dir
        clc
        adc #2
        jsr fire_bullet
        lda aim_dir
        jmp shoot_fan3

; Eight bullets all around, starting at direction A.
shoot_ring:
        sta aim_dir
        ldy #8
-       lda aim_dir
        jsr fire_bullet
        lda aim_dir
        clc
        adc #2
        sta aim_dir
        dey
        bne -
        rts

; A direction's x and y velocity at half a pixel a frame, in 8.8 fixed point.
DirXL: .byte $80, $76, $5B, $31, $00, $CF, $A5, $8A, $80, $8A, $A5, $CF, $00, $31, $5B, $76
DirXH: .byte $00, $00, $00, $00, $00, $FF, $FF, $FF, $FF, $FF, $FF, $FF, $00, $00, $00, $00
DirYL: .byte $00, $31, $5B, $76, $80, $76, $5B, $31, $00, $CF, $A5, $8A, $80, $8A, $A5, $CF
DirYH: .byte $00, $00, $00, $00, $00, $00, $00, $00, $00, $FF, $FF, $FF, $FF, $FF, $FF, $FF
