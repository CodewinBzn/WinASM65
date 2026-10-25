; Star Catcher - who comes down the sky, and how the newer mutants move.
; Included by game.asm.

; Every spawn_delay frames something appears at the top: a single object, or
; from the second vertical level on, now and then a whole squadron. What it is
; depends on the level's tier (1 for level 1, 2 for level 3, ...): a random
; draw from that tier's line of KindPick.
spawn_objects:
        dec spawn_timer
        bne @done
        lda spawn_delay
        sta spawn_timer
        lda tier
        cmp #2
        bcc @single
        jsr random
        cmp #28                 ; about one draw in nine
        bcs @single
        jmp spawn_squadron
@single:
        jsr free_object
        bcc @done
        jsr random
        and #%11111000
        cmp #217                ; room for 16 pixels
        bcc +
        lda #216
+       cmp #SHIP_MIN_X
        bcs +
        lda #SHIP_MIN_X
+       sta new_x
        jsr random              ; the tier's line, one of 16 picks
        and #15
        sta tmp
        lda tier
        sec
        sbc #1
        asl a
        asl a
        asl a
        asl a
        ora tmp
        tay
        lda KindPick,y
        jmp spawn_kind
@done:  rts

; Carry set and X a free object slot, or carry clear when there is none.
free_object:
        ldx #MAX_OBJECTS-1
-       lda obj_type,x
        beq +
        dex
        bpl -
        clc
        rts
+       sec
        rts

; Object X becomes a new one of kind A, at (new_x, 8), set up for its kind.
spawn_kind:
        sta obj_type,x
        lda new_x
        sta obj_x,x
        sta obj_base,x
        lda #8
        sta obj_y,x
        lda #1
        sta obj_speed,x
        sta obj_hp,x
        lda #0
        sta obj_dx,x
        sta obj_timer,x
        ldy obj_type,x
        lda KindLife,y
        sta obj_hp,x
        cpy #KIND_METEOR
        bne +
        jsr random              ; 1 or 2 pixels a frame
        and #1
        clc
        adc #1
        sta obj_speed,x
        rts
+       cpy #KIND_SAUCER
        bne +
        lda #1                  ; to the right, or to the left
        sta obj_dx,x
        lda frame_count
        and #1
        bne @done
        lda #$FF
        sta obj_dx,x
@done:  rts
+       cpy #KIND_HUNTER
        bne +
        lda #2
        sta obj_speed,x
        rts
+       cpy #KIND_DIVER
        bne +
        lda #48                 ; frames of stalking before the dive
        sta obj_timer,x
        rts
+       cpy #KIND_TURRET
        bne +
        lda #0                  ; it drifts on its own, one pixel in two frames
        sta obj_speed,x
        lda #40
        sta obj_timer,x
        rts
+       cpy #KIND_WEAVER
        bne +
        lda new_x               ; its swing's middle, away from the sides
        cmp #40
        bcs ++
        lda #40
++      cmp #192
        bcc ++
        lda #192
++      sta obj_base,x
        rts
+       rts

; A squadron: five saucers in a V, or four weavers in a line, all at once.
spawn_squadron:
        jsr random
        bmi @line
        jsr random              ; the V's point
        and #%01111000
        clc
        adc #48
        sta tmp2
        ldy #4
@v:     jsr free_object
        bcc @full
        lda tmp2
        clc
        adc VeeX,y
        sta new_x
        tya
        pha
        lda #KIND_SAUCER
        jsr spawn_kind
        pla
        tay
        lda VeeY,y
        sta obj_y,x
        lda frame_count         ; the whole V goes the same way
        and #1
        beq +
        lda #1
        bne ++
+       lda #$FF
++      sta obj_dx,x
        dey
        bpl @v
@full:  rts
@line:  ldy #3
@w:     jsr free_object
        bcc @full
        lda LineX,y
        sta new_x
        tya
        pha
        lda #KIND_WEAVER
        jsr spawn_kind
        pla
        tay
        dey
        bpl @w
        rts

; ---------------------------------------------------------------------------
; The newer mutants, each called by move_objects with X its slot

; The diver stalks the ship from above, then dives, faster and faster.
move_diver:
        lda obj_timer,x
        beq @dive
        dec obj_timer,x
        lda ship_x              ; stalking: slides over the ship
        cmp obj_x,x
        beq @done
        bcc +
        inc obj_x,x
        rts
+       dec obj_x,x
@done:  rts
@dive:  lda frame_count         ; one pixel a frame more every 8 frames, up to 5
        and #7
        bne @done
        lda obj_speed,x
        cmp #5
        bcs @done
        inc obj_speed,x
        rts

; The turret drifts down, and fires at the ship: one bullet on the first
; tiers, a fan of three later.
move_turret:
        lda frame_count
        and #1
        bne +
        inc obj_y,x
+       dec obj_timer,x
        bne @done
        lda #70
        sta obj_timer,x
        lda obj_y,x
        cmp #160                ; no shot at point-blank range
        bcs @done
        jsr from_object
        lda #3
        sta bullet_speed
        stx obj_index
        lda tier
        cmp #3
        bcs @fan
        jsr shoot_aimed
        ldx obj_index
        rts
@fan:   jsr aim
        jsr shoot_fan3
        ldx obj_index
@done:  rts

; The weaver swings from side to side around its middle, and every two
; seconds fires a fan of three straight down.
move_weaver:
        inc obj_timer,x
        lda obj_timer,x
        and #63
        tay
        lda Swing,y
        clc
        adc obj_base,x
        sta obj_x,x
        lda obj_timer,x
        and #127
        cmp #64
        bne @done
        jsr from_object
        lda #4
        sta bullet_speed
        stx obj_index
        lda #4                  ; down
        jsr shoot_fan3
        ldx obj_index
@done:  rts

; (new_x, new_y): the middle of object X's bottom, where its bullets start.
from_object:
        lda obj_x,x
        clc
        adc #4
        sta new_x
        lda obj_y,x
        clc
        adc #12
        sta new_y
        rts

; The giant meteor X broke: two small meteors fly apart from where it was.
split_rock:
        lda obj_x,x
        sta new_x
        lda obj_y,x
        sta new_y
        lda #$FF                ; to the left, then to the right
        jsr rock_piece
        lda #1
rock_piece:
        sta new_dx
        stx obj_index
        jsr free_object
        bcc @done
        lda #KIND_METEOR
        jsr spawn_kind
        lda new_y
        sta obj_y,x
        lda new_dx
        sta obj_dx,x
        lda #2
        sta obj_speed,x
@done:  ldx obj_index
        rts

; Per kind (0, star, meteor, saucer, hunter, burst, diver, turret, weaver,
; giant): its first metasprite, what breaking or catching it scores, and the
; shots it takes.
KindSprite:
        .byte 0, SPR_MS_STAR, SPR_MS_METEOR, SPR_MS_SAUCER, SPR_MS_HUNTER, SPR_MS_BURST
        .byte SPR_MS_DIVER, SPR_MS_TURRET, SPR_MS_WEAVER, SPR_MS_GIANT_METEOR
KindPoints:
        .byte 0, 5, 1, 3, 2, 0, 3, 4, 3, 5
KindLife:
        .byte 1, 1, 1, 2, 1, 1, 1, 3, 2, 4

; What each tier sends: 16 picks a line, tiers 1 to 5.
KindPick:
        .byte KIND_STAR, KIND_STAR, KIND_STAR, KIND_METEOR, KIND_METEOR, KIND_METEOR, KIND_METEOR, KIND_METEOR
        .byte KIND_SAUCER, KIND_SAUCER, KIND_SAUCER, KIND_SAUCER, KIND_HUNTER, KIND_HUNTER, KIND_GIANT, KIND_GIANT
        .byte KIND_STAR, KIND_STAR, KIND_METEOR, KIND_METEOR, KIND_METEOR, KIND_SAUCER, KIND_SAUCER, KIND_HUNTER
        .byte KIND_HUNTER, KIND_GIANT, KIND_DIVER, KIND_DIVER, KIND_TURRET, KIND_TURRET, KIND_WEAVER, KIND_WEAVER
        .byte KIND_STAR, KIND_STAR, KIND_METEOR, KIND_METEOR, KIND_SAUCER, KIND_SAUCER, KIND_HUNTER, KIND_GIANT
        .byte KIND_GIANT, KIND_DIVER, KIND_DIVER, KIND_TURRET, KIND_TURRET, KIND_WEAVER, KIND_WEAVER, KIND_WEAVER
        .byte KIND_STAR, KIND_METEOR, KIND_METEOR, KIND_SAUCER, KIND_HUNTER, KIND_HUNTER, KIND_GIANT, KIND_GIANT
        .byte KIND_DIVER, KIND_DIVER, KIND_DIVER, KIND_TURRET, KIND_TURRET, KIND_TURRET, KIND_WEAVER, KIND_WEAVER
        .byte KIND_STAR, KIND_METEOR, KIND_SAUCER, KIND_HUNTER, KIND_HUNTER, KIND_GIANT, KIND_GIANT, KIND_DIVER
        .byte KIND_DIVER, KIND_DIVER, KIND_TURRET, KIND_TURRET, KIND_TURRET, KIND_WEAVER, KIND_WEAVER, KIND_WEAVER

; A V of five: offsets from its point, and how far down each starts.
VeeX:   .byte 0, $F0, 16, $E0, 32
VeeY:   .byte 40, 28, 28, 16, 16
LineX:  .byte 40, 88, 136, 184

; The weaver's swing: 24 pixels either way over 64 frames.
Swing:  .byte $00, $02, $05, $07, $09, $0B, $0D, $0F, $11, $13, $14, $15, $16, $17, $18, $18
        .byte $18, $18, $18, $17, $16, $15, $14, $13, $11, $0F, $0D, $0B, $09, $07, $05, $02
        .byte $00, $FE, $FB, $F9, $F7, $F5, $F3, $F1, $EF, $ED, $EC, $EB, $EA, $E9, $E8, $E8
        .byte $E8, $E8, $E8, $E9, $EA, $EB, $EC, $ED, $EF, $F1, $F3, $F5, $F7, $F9, $FB, $FE
