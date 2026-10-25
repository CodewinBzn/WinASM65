; Star Catcher - the 3D stage, between two vertical levels. Included by game.asm.
;
; The ship is seen from behind, low on the screen, and the enemies come at it
; from the horizon. Each has a place in front of the ship: x across (signed,
; 0 in the middle), y down (0 at the eye, FLOOR_Y on the floor) and z away
; from it (255 at the horizon). On screen it is at
;       x' = 128 + x * Scale[z] / 64,   y' = HORIZON_Y + y * Scale[z] / 64
; where Scale holds 2048 / z: what is twice as far is half as big. Its picture
; is one of three sizes, 8, 16 or 32 pixels, picked by z.
;
; The floor does not move a byte: its bands are coloured 1, 2, 3 in turn, from
; near to far, and turning the three colours of its palette round makes them
; run at the ship (gfx/stage3d.screen.json, in pattern table 1).

HORIZON_Y   = 90
FLOOR_Y     = 80
NEAR_Z      = 16                ; closer, a thing has passed the ship
SHOT_START_Z      = 24                ; where the ship's shots start
HIT_Z       = 44                ; closer, a thing can touch the ship
SHOT3D_SPEED = 6
FLOOR_STEP  = 5                 ; frames between two turns of the floor's colours
STAGE3D_LENGTH = 36             ; in units of 64 frames: about 38 seconds

SHIP3D_MAX_X = 224              ; the ship from behind is 24 x 16
SHIP3D_MIN_Y = 144
SHIP3D_MAX_Y = 184

K3_ROCK     = 1
K3_SAUCER   = 2                 ; sways from side to side
K3_FIGHTER  = 3                 ; fires at the ship
K3_PYLON    = 4                 ; stands on the floor; shots do not break it
K3_BURST    = 5

; The arrays of the vertical levels, under other names.
obj_z       = obj_base
b3_z        = bomb_y            ; a bullet: 0 when the slot is free
b3_x        = bomb_vxl
b3_y        = bomb_vyl
b3_sx       = bomb_x            ; where it is on screen
b3_sy       = bomb_yf
shot_z      = shot_y            ; a shot: 0 when the slot is free

; From a vertical level's LEVEL CLEAR to the 3D stage.
enter_3d:
        jsr screen_off
        lda #<scr_stage3d
        sta ptr
        lda #>scr_stage3d
        sta ptr+1
        jsr load_nametable
        lda #<s3d_palettes
        sta ptr
        lda #>s3d_palettes
        sta ptr+1
        jsr load_palette_set
        lda #1
        sta mode3d
        lda #PPUCTRL_BG1        ; the floor's tiles
        sta ctrl_bg
        lda #116
        sta ship_x
        lda #168
        sta ship_y
        lda #0
        sta floor_phase
        lda #FLOOR_STEP
        sta floor_timer
        jsr clear_actors
        jsr start_level
        jsr hide_sprites
        jmp screen_on

; From the 3D stage's CLEAR back to the sky.
leave_3d:
        jsr screen_off          ; the sky's palettes and tiles again
        lda #0
        jsr draw_sky
        lda #1
        jsr draw_sky
        lda #SHIP_START_Y
        sta ship_y
        jsr clear_actors
        jsr start_level
        lda #1
        sta wave_on
        jsr hide_sprites
        jmp screen_on

update_3d:
        jsr move_ship
        jsr keep_ship3d
        jsr ship_world
        jsr fire
        jsr move_shots3d
        lda phase
        bne +
        jsr spawn3d             ; PHASE_WAVES
        jsr count3d
+       jsr move_objects3d
        jsr move_bullets3d
        jsr turn_floor
        lda phase
        cmp #PHASE_CLEAR
        bne +
        jsr clear3d
+       lda lives
        bne +
        jmp enter_over
+       jmp draw3d

; The ship stays over the floor, all of it on screen.
keep_ship3d:
        lda ship_x
        cmp #SHIP3D_MAX_X
        bcc +
        lda #SHIP3D_MAX_X
        sta ship_x
+       lda ship_y
        cmp #SHIP3D_MIN_Y
        bcs +
        lda #SHIP3D_MIN_Y
+       cmp #SHIP3D_MAX_Y
        bcc +
        lda #SHIP3D_MAX_Y
+       sta ship_y
        rts

; (ship_wx, ship_wy): where the ship's middle is, at z SHOT_START_Z. There Scale is
; 85, 4/3, so the place is 3/4 of the distance on screen from the middle.
ship_world:
        lda ship_x
        clc
        adc #12
        eor #$80                ; - 128, signed
        jsr three_quarters
        sta ship_wx
        lda ship_y
        clc
        adc #8
        sec
        sbc #HORIZON_Y
        jsr three_quarters
        sta ship_wy
        rts

; A - A/4, A signed.
three_quarters:
        sta tmp
        cmp #$80                ; A/4, the sign kept
        ror a
        cmp #$80
        ror a
        eor #$FF
        sec
        adc tmp
        rts

; A shot from the ship, going away into slot X (fire found it free).
shoot3d:
        lda #SHOT_START_Z
        sta shot_z,x
        lda ship_wx
        sta shot_x,x
        lda ship_wy
        sta shot_h,x
        lda #FIRE_DELAY
        sta fire_timer
        jmp sound_shot

; The shots go away; each is checked against the enemies.
move_shots3d:
        ldx #MAX_SHOTS-1
@loop:  lda shot_z,x
        beq @next
        clc
        adc #SHOT3D_SPEED
        bcs @gone
        cmp #250
        bcc +
@gone:  lda #0                  ; at the horizon
+       sta shot_z,x
        beq @next
        stx shot_index
        jsr shot_hits3d
        ldx shot_index
@next:  dex
        bpl @loop
        rts

; Shot shot_index against every enemy: near it in z, x and y.
shot_hits3d:
        ldy #MAX_OBJECTS-1
@obj:   lda obj_type,y
        beq @next
        cmp #K3_BURST
        beq @next
        lda obj_z,y             ; |z - shot z| < 12
        sec
        sbc shot_z,x
        bcs +
        eor #$FF
        adc #1
+       cmp #12
        bcs @next
        lda obj_x,y
        sta tmp
        lda shot_x,x
        jsr distance
        cmp #16
        bcs @next
        lda obj_y,y
        sta tmp
        lda shot_h,x
        jsr distance
        cmp #16
        bcs @next
        lda #0                  ; a hit: the shot is gone
        sta shot_z,x
        tya
        tax
        lda obj_type,x
        cmp #K3_PYLON
        beq @done               ; it takes no harm
        dec obj_hp,x
        bne @done
        ldy obj_type,x
        lda K3Points,y
        jsr add_points
        jsr burst3d
        jmp sound_hit
@next:  dey
        bpl @obj
@done:  rts

; |A - tmp|, both signed, or 255 when they are too far apart for a byte.
distance:
        sec
        sbc tmp
        bvs @far
        bpl +
        eor #$FF
        clc
        adc #1
+       rts
@far:   lda #255
        rts

burst3d:
        lda #K3_BURST
        sta obj_type,x
        lda #BURST_FRAMES
        sta obj_timer,x
        rts

; Every spawn_delay frames an enemy at the horizon.
spawn3d:
        dec spawn_timer
        bne @done
        lda spawn_delay
        sta spawn_timer
        jsr free_object
        bcc @done
        jsr random
        and #7
        tay
        lda Pick3D,y
        sta obj_type,x
        tay
        lda K3Life,y
        sta obj_hp,x
        lda #250
        sta obj_z,x
        lda #0
        sta obj_dx,x
        lda #30
        sta obj_timer,x
        jsr random              ; across: -64 to 63
        and #127
        sec
        sbc #64
        sta obj_x,x
        jsr random              ; down: 44 to 75, at the ship's height
        and #31
        clc
        adc #44
        sta obj_y,x
        cpy #K3_PYLON
        bne +
        lda #FLOOR_Y-16         ; standing on the floor
        sta obj_y,x
        rts
+       cpy #K3_SAUCER
        bne @done
        lda #1
        sta obj_dx,x
        lda frame_count
        and #1
        bne @done
        lda #$FF
        sta obj_dx,x
@done:  rts

; Every 64 frames count; at the stage's length, CLEAR.
count3d:
        lda frame_count
        and #63
        bne @done
        inc level_clock
        lda level_clock
        cmp #STAGE3D_LENGTH
        bcc @done
        lda #PHASE_CLEAR
        sta phase
        lda #CLEAR_FRAMES
        sta phase_timer
@done:  rts

; CLEAR shows, then the next level, in the sky.
clear3d:
        dec phase_timer
        bne @done
        lda level
        cmp #LAST_LEVEL
        bcs +
        inc level
+       jmp leave_3d
@done:  rts

; The enemies come closer; each is put on screen, and checked against the ship.
move_objects3d:
        ldx #MAX_OBJECTS-1
@loop:  lda obj_type,x
        beq @next
        cmp #K3_BURST
        bne @move
        dec obj_timer,x
        bne @move
@free:  lda #0
        sta obj_type,x
        beq @next
@move:  ldy obj_type,x          ; 2 closer a frame; a fighter 1
        lda #2
        cpy #K3_FIGHTER
        bne +
        lda #1
+       sta tmp
        lda obj_z,x
        sec
        sbc tmp
        cmp #NEAR_Z
        bcc @free               ; it went past the ship
        sta obj_z,x
        cpy #K3_SAUCER
        bne +
        jsr sway
+       cpy #K3_FIGHTER
        bne +
        jsr fighter_fire
+       jsr place
        jsr touch3d
@next:  dex
        bpl @loop
        rts

; Object X, near enough, against the ship: it bursts, and hurts it.
touch3d:
        lda obj_size,x
        bmi @done               ; off screen
        lda obj_z,x
        cmp #HIT_Z
        bcs @done
        lda obj_type,x
        cmp #K3_BURST
        beq @done
        lda hit_timer
        bne @done
        jsr ship_box3d
        ldy obj_size,x          ; box b: its picture, around its middle
        lda SizeHalf,y
        sta tmp
        asl a
        sta box_bw
        sta box_bh
        lda obj_sx,x
        sec
        sbc tmp
        sta box_bx
        lda obj_sy,x
        sec
        sbc tmp
        sta box_by
        jsr overlap
        bcc @done
        jsr burst3d
        jmp ship_hit
@done:  rts

; A saucer sways across, between -80 and 80.
sway:
        lda obj_x,x
        clc
        adc obj_dx,x
        sta obj_x,x
        clc
        adc #80
        cmp #161
        bcc @done
        lda obj_dx,x
        eor #$FF
        clc
        adc #1
        sta obj_dx,x
@done:  rts

; Every 50 frames, while it is far enough, a fighter fires at the ship.
fighter_fire:
        dec obj_timer,x
        bne @done
        lda #50
        sta obj_timer,x
        lda obj_z,x
        cmp #64
        bcc @done
        ldy #MAX_BOMBS-1
@find:  lda b3_z,y
        beq @found
        dey
        bpl @find
        rts
@found: lda obj_z,x
        sta b3_z,y
        lda obj_x,x
        sta b3_x,y
        lda obj_y,x
        sta b3_y,y
@done:  ldy obj_type,x
        rts

; Object X on screen: (obj_sx, obj_sy) its middle and obj_size its picture's
; size (0, 1, 2 for 8, 16, 32 pixels), or $FF when it is off the screen.
place:
        lda obj_x,x
        sta p_x
        lda obj_y,x
        sta p_y
        lda obj_z,x
        sta p_z
        jsr project
        bcs @off
        lda ms_x
        sta obj_sx,x
        lda ms_y
        sta obj_sy,x
        ldy #2                  ; the size, by how far it is
        lda obj_z,x
        cmp #45
        bcc +
        dey
        cmp #90
        bcc +
        dey
+       lda ms_x                ; all of its picture on screen, not wrapped round
        cmp SizeHalf,y
        bcc @off
        clc
        adc SizeHalf,y
        bcs @off
        tya
        sta obj_size,x
        rts
@off:   lda #$FF
        sta obj_size,x
        rts

; (p_x, p_y, p_z) on screen: (ms_x, ms_y), carry clear; carry set when off it.
project:
        ldy p_z
        lda Scale,y
        sta p_s
        lda p_x
        jsr scale_by
        bcs @off
        bit p_neg
        bmi @left
        clc
        adc #128
        bcs @off
        bcc @x
@left:  sta tmp
        lda #128
        sec
        sbc tmp
        bcc @off
@x:     sta ms_x
        lda p_y
        jsr scale_by
        bcs @off
        bit p_neg
        bmi @up
        clc
        adc #HORIZON_Y
        bcs @off
        cmp #232
        bcs @off
        bcc @y
@up:    sta tmp
        lda #HORIZON_Y
        sec
        sbc tmp
        bcc @off
@y:     sta ms_y
        clc
        rts
@off:   sec
        rts

; |A| * p_s / 64, A signed (its sign in p_neg), carry clear; carry set when
; the result does not fit a byte.
scale_by:
        sta p_neg
        bpl +
        eor #$FF
        clc
        adc #1
+       sta mul_a
        lda #0                  ; A:mul_a = mul_a * p_s, a bit at a time
        ldy #8
        lsr mul_a
@bit:   bcc +
        clc
        adc p_s
+       ror a
        ror mul_a
        dey
        bne @bit
        cmp #64
        bcs @done               ; 256 or more
        sta tmp
        lda mul_a               ; / 64: two bits left, and the high byte
        asl a
        rol tmp
        asl a
        rol tmp
        lda tmp
        clc
@done:  rts

; The bullets come at the ship, closing on it a unit a frame across and down.
move_bullets3d:
        ldx #MAX_BOMBS-1
@loop:  lda b3_z,x
        beq @next
        sec
        sbc #3
        cmp #NEAR_Z
        bcc @gone
        sta b3_z,x
        sta p_z
        jsr close_in
        jsr project
        bcs @gone
        lda ms_x
        cmp #4
        bcc @gone
        cmp #252
        bcs @gone
        sta b3_sx,x
        lda ms_y
        sta b3_sy,x
        jsr bullet_touch
        bcc @next
@gone:  lda #0
        sta b3_z,x
@next:  dex
        bpl @loop
        rts

; Bullet X a unit closer to the ship across and down; (p_x, p_y) where it is.
close_in:
        lda b3_x,x              ; signed, compared as unsigned from -128 up
        eor #$80
        sta tmp
        lda ship_wx
        eor #$80
        cmp tmp
        beq ++
        bcs +
        dec b3_x,x
        bcc ++
+       inc b3_x,x
++      lda b3_y,x
        eor #$80
        sta tmp
        lda ship_wy
        eor #$80
        cmp tmp
        beq ++
        bcs +
        dec b3_y,x
        bcc ++
+       inc b3_y,x
++      lda b3_x,x
        sta p_x
        lda b3_y,x
        sta p_y
        rts

; Carry set when bullet X, near enough, hit the ship.
bullet_touch:
        lda b3_z,x
        cmp #HIT_Z
        bcs @no
        lda hit_timer
        bne @no
        jsr ship_box3d
        lda b3_sx,x
        sec
        sbc #3
        sta box_bx
        lda b3_sy,x
        sec
        sbc #3
        sta box_by
        lda #6
        sta box_bw
        sta box_bh
        jsr overlap
        bcc @no
        jsr ship_hit
        sec
        rts
@no:    clc
        rts

; Box a: the ship from behind, its body.
ship_box3d:
        lda ship_x
        clc
        adc #4
        sta box_ax
        lda ship_y
        clc
        adc #3
        sta box_ay
        lda #16
        sta box_aw
        lda #10
        sta box_ah
        rts

; The floor's colours turn round one step every FLOOR_STEP frames; NMI writes
; them.
turn_floor:
        dec floor_timer
        bne @done
        lda #FLOOR_STEP
        sta floor_timer
        ldx floor_phase
        inx
        cpx #3
        bcc +
        ldx #0
+       stx floor_phase
@done:  rts

; ---------------------------------------------------------------------------
; Sprites

draw3d:
        lda #0
        sta oam_index
        jsr draw_hud_sprites
        lda ship_x              ; the ship, from behind: its origin is its middle
        clc
        adc #12
        sta ms_x
        lda ship_y
        clc
        adc #7
        sta ms_y
        lda hit_timer
        beq @ship
        dec hit_timer
        and #4
        bne @shots
@ship:  lda #SPR_MS_SHIP_BACK
        jsr draw_metasprite
@shots: ldx #MAX_SHOTS-1
@shot:  lda shot_z,x
        beq @next_shot
        sta p_z
        lda shot_x,x
        sta p_x
        lda shot_h,x
        sta p_y
        stx shot_index
        jsr project
        bcs +
        lda #SPR_MS_SHOT3D
        jsr draw_metasprite
+       ldx shot_index
@next_shot:
        dex
        bpl @shot
        ldy #MAX_BOMBS-1
@bullet:
        lda b3_z,y
        beq @next_bullet
        lda b3_sx,y
        sta ms_x
        lda b3_sy,y
        sta ms_y
        sty shot_index
        lda #SPR_MS_BULLET3D
        jsr draw_metasprite
        ldy shot_index
@next_bullet:
        dey
        bpl @bullet
        jsr draw_message
        lda #MAX_OBJECTS-1
        sta draw_count
@obj:   ldx draw_count          ; in turn one way and the other, as draw_objects
        lda frame_count
        lsr a
        bcc +
        lda #MAX_OBJECTS-1
        sec
        sbc draw_count
        tax
+       lda obj_type,x
        beq @next_obj
        ldy obj_size,x
        bmi @next_obj
        lda obj_sx,x
        sta ms_x
        lda obj_sy,x
        sta ms_y
        lda obj_type,x
        cmp #K3_BURST
        bne +
        lda ms_x                ; a burst's origin is its corner
        sec
        sbc #4
        sta ms_x
        lda ms_y
        sec
        sbc #4
        sta ms_y
        lda #SPR_MS_BURST
        bne @draw
+       tax
        tya
        clc
        adc K3Sprite,x
@draw:  jsr draw_metasprite
@next_obj:
        dec draw_count
        bpl @obj
        jmp hide_rest

; Per kind (0, rock, saucer, fighter, pylon, burst): its smallest picture
; (the 16 and 32 pixel ones follow), its points, and the shots it takes.
K3Sprite:   .byte 0, SPR_MS_ROCK3D_8, SPR_MS_SAUCER3D_8, SPR_MS_FIGHTER3D_8, SPR_MS_PYLON3D_8, 0
K3Points:   .byte 0, 1, 3, 4, 0, 0
K3Life:     .byte 1, 1, 2, 2, 1, 1
Pick3D:     .byte K3_ROCK, K3_ROCK, K3_ROCK, K3_SAUCER, K3_SAUCER, K3_FIGHTER, K3_PYLON, K3_PYLON

; SizeHalf a picture's size, for its sizes 0, 1, 2.
SizeHalf:       .byte 4, 8, 16

; The floor's three blues, twice over: NMI writes three from floor_phase on.
FloorColors: .byte $01, $11, $21, $01, $11

; 2048 / z, at most 255: how big things are at z, in 64ths.
Scale:
        .byte 255, 255, 255, 255, 255, 255, 255, 255, 255, 228, 205, 186, 171, 158, 146, 137
        .byte 128, 120, 114, 108, 102, 98, 93, 89, 85, 82, 79, 76, 73, 71, 68, 66
        .byte 64, 62, 60, 59, 57, 55, 54, 53, 51, 50, 49, 48, 47, 46, 45, 44
        .byte 43, 42, 41, 40, 39, 39, 38, 37, 37, 36, 35, 35, 34, 34, 33, 33
        .byte 32, 32, 31, 31, 30, 30, 29, 29, 28, 28, 28, 27, 27, 27, 26, 26
        .byte 26, 25, 25, 25, 24, 24, 24, 24, 23, 23, 23, 23, 22, 22, 22, 22
        .byte 21, 21, 21, 21, 20, 20, 20, 20, 20, 20, 19, 19, 19, 19, 19, 18
        .byte 18, 18, 18, 18, 18, 18, 17, 17, 17, 17, 17, 17, 17, 16, 16, 16
        .byte 16, 16, 16, 16, 16, 15, 15, 15, 15, 15, 15, 15, 15, 15, 14, 14
        .byte 14, 14, 14, 14, 14, 14, 14, 14, 13, 13, 13, 13, 13, 13, 13, 13
        .byte 13, 13, 13, 13, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12
        .byte 12, 12, 12, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11
        .byte 11, 11, 11, 11, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10
        .byte 10, 10, 10, 10, 10, 10, 10, 10, 9, 9, 9, 9, 9, 9, 9, 9
        .byte 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9
        .byte 9, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8
