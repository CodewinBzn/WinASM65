; Star Catcher - an original NES game written for WinASM65.
;
; A vertical shooter. The sky scrolls down past the ship: fly with the d-pad,
; fire with A or B (hold it to keep firing). Shoot the meteors, the saucers
; and the hunters, catch the stars for points. After about forty seconds the
; level's boss comes down; break it, and the next level comes faster. Three
; hits and the game is over.

        .include "nes.inc"

; ---------------------------------------------------------------------------
; Constants

NT          = $2000             ; the title and game over screens, and the sky's first screen
NT_LOW      = $2800             ; the sky's second screen (horizontal mirroring)
OAM         = $0200             ; sprite table, copied to the PPU every frame

STATE_TITLE = 0
STATE_PLAY  = 1
STATE_OVER  = 2

PHASE_WAVES = 0                 ; enemies come
PHASE_BOSS  = 1                 ; WARNING, then the boss
PHASE_CLEAR = 2                 ; the boss broke: LEVEL CLEAR, then the next level

SHIP_SPEED  = 2
SHIP_MIN_X  = 8
SHIP_MAX_X  = 232
SHIP_MIN_Y  = 112
SHIP_MAX_Y  = 208
SHIP_START_Y = 200
HIT_FRAMES  = 64                ; the ship blinks, and nothing hurts it

MAX_SHOTS   = 3
SHOT_SPEED  = 5
FIRE_DELAY  = 8                 ; frames between two shots while A or B is held

MAX_BOMBS   = 12                ; enemy bullets at once

MAX_OBJECTS = 10
KIND_STAR   = 1                 ; caught for points
KIND_METEOR = 2                 ; falls straight
KIND_SAUCER = 3                 ; zigzags and drops bombs, two shots to break
KIND_HUNTER = 4                 ; dives at the ship
KIND_BURST  = 5                 ; an explosion, for BURST_FRAMES
KIND_DIVER  = 6                 ; stalks the ship from above, then dives
KIND_TURRET = 7                 ; drifts down, fires at the ship
KIND_WEAVER = 8                 ; swings from side to side, fires fans
KIND_GIANT  = 9                 ; a giant meteor: breaks into two
BURST_FRAMES = 12
FIRST_DELAY = 40                ; frames between two objects on level 1
MIN_DELAY   = 14

LEVEL_LENGTH = 40               ; in units of 64 frames: about 43 seconds
LAST_LEVEL  = 9                 ; the levels after it are all the same
WARNING_FRAMES = 128
CLEAR_FRAMES = 150
BOSS_TOP_Y  = 32
BOSS_MIN_X  = 16
BOSS_MAX_X  = 208
BOSS_LIFE   = 24                ; hits on level 1; 8 more on every level
BOSS_FIRE_DELAY = 48            ; frames between two fans, in the boss's first phase

WAVE_TOP    = 8                 ; the band of sky that ripples, from this line ...
WAVE_LINES  = 96                ; ... for this many (a multiple of 3)
WAIT_COARSE = 2                 ; the NMI's wait for that line, tuned in Mesen2
WAIT_FINE   = 23

HUD_X       = 16                ; the score, in sprites: the sky scrolls
HUD_Y       = 8
LIVES_X     = 208

; The graphics are assets of the designer, in ../gfx: the palettes, the
; metasprites (ship, shot, star and its twinkle, meteor, saucer, hunter,
; bomb, burst, heart, boss), the title and game over screens, and the sky as
; a map of metatiles two screens high. Their exported .asm files are
; included at the end.

PPUCTRL_NMI  = %10000000        ; NMI on, all tiles from pattern table 0
PPUCTRL_BG1  = %00010000        ; the background's tiles from pattern table 1
PPUMASK_SHOW = %00011110        ; background and sprites, left column included

HUD_SCORE   = NT+2*32+8         ; game over: where the four digits of the score go
HUD_LIVES   = NT+2*32+25        ; and the number of lives
SKY_ROWS    = 15                ; rows of metatiles in a screen

; ---------------------------------------------------------------------------
; Variables, in zero page: .res hands out addresses from $0000 on.

        .memarea $0000
frame_ready  .res 1             ; set by NMI, cleared by wait_frame
frame_count  .res 1
lag_frames   .res 1             ; frames whose work ran past the next NMI
wave_on      .res 1             ; the sky ripples (vertical levels)
wave_phase   .res 1
wave_count   .res 1
mode3d       .res 1             ; the 3D stage is on
ctrl_bg      .res 1             ; or-ed into PPUCTRL: the background's pattern table
floor_phase  .res 1             ; the 3D floor's colours: how far they turned, 0-2
floor_timer  .res 1
buttons      .res 1             ; held this frame
prev_buttons .res 1
pressed      .res 1             ; went down this frame
state        .res 1
rng          .res 1             ; random generator, never 0
ship_x       .res 1
ship_y       .res 1
lives        .res 1
score        .res 4             ; four decimal digits, most significant first
spawn_timer  .res 1
spawn_delay  .res 1
delay        .res 1             ; game over: frames before START is accepted
hit_timer    .res 1
fire_timer   .res 1
tmp          .res 1
points       .res 1
ptr          .res 2
scroll_y     .res 1             ; the sky's first line on screen, 0-239 ...
scroll_nt    .res 1             ; ... of nametable $2000 (0) or $2800 (2)
level        .res 1
level_clock  .res 1             ; units of 64 frames since the level began
phase        .res 1
phase_timer  .res 1
tier         .res 1             ; how hard the level's waves are: 1-5
tmp2         .res 1
bullet_speed .res 1             ; half pixels a frame, for fire_bullet
aim_dx       .res 1
aim_dy       .res 1
aim_ax       .res 1
aim_ay       .res 1
aim_dir      .res 1
boss_t1      .res 1             ; the boss's life where its second phase begins
boss_t2      .res 1             ; and its third
boss_spin    .res 1             ; the spiral's turning direction
boss_burst   .res 1             ; aimed shots left in a burst
boss_x       .res 1
boss_y       .res 1
boss_dx      .res 1
boss_hp      .res 1
boss_flash   .res 1             ; frames left of the hit colours
boss_fire    .res 1
new_x        .res 1             ; where fire_bullet and burst_at put a new one
new_y        .res 1
new_dx       .res 1
box_ax       .res 1             ; overlap's two boxes
box_ay       .res 1
box_aw       .res 1
box_ah       .res 1
box_bx       .res 1
box_by       .res 1
box_bw       .res 1
box_bh       .res 1
oam_index    .res 1             ; next free entry of the sprite table
obj_index    .res 1
shot_index   .res 1
draw_count   .res 1
ms_x         .res 1             ; where draw_metasprite puts the origin
ms_y         .res 1
map_row      .res 1
map_col      .res 1
map_base     .res 2
ship_wx      .res 1             ; the 3D stage: where the ship is, in its space
ship_wy      .res 1
p_x          .res 1             ; project's point
p_y          .res 1
p_z          .res 1
p_s          .res 1
p_neg        .res 1
mul_a        .res 1
obj_type     .res MAX_OBJECTS   ; 0 when the slot is free
obj_x        .res MAX_OBJECTS
obj_y        .res MAX_OBJECTS
obj_speed    .res MAX_OBJECTS   ; pixels down a frame
obj_dx       .res MAX_OBJECTS   ; pixels sideways (signed); bursts: frames left
obj_hp       .res MAX_OBJECTS   ; shots left to break it
obj_timer    .res MAX_OBJECTS   ; per kind: stalking time, fire delay, swing
obj_base     .res MAX_OBJECTS   ; the weaver's swing's middle
shot_x       .res MAX_SHOTS
shot_y       .res MAX_SHOTS     ; 0 when the slot is free
shot_h       .res MAX_SHOTS     ; the 3D stage: how far down the shot flies

; The objects' places on the 3D stage and the bullets, in RAM after the stack
; and the sprite table.
        .memarea $0300
obj_sx       .res MAX_OBJECTS   ; the 3D stage: an object's middle on screen,
obj_sy       .res MAX_OBJECTS
obj_size     .res MAX_OBJECTS   ; and its picture's size ($FF: off screen)
bomb_x       .res MAX_BOMBS     ; bullets, in 8.8 fixed point:
bomb_xf      .res MAX_BOMBS     ;   the pixel and its fraction
bomb_y       .res MAX_BOMBS     ;   (bomb_y 0: the slot is free)
bomb_yf      .res MAX_BOMBS
bomb_vxl     .res MAX_BOMBS     ; velocity, signed
bomb_vxh     .res MAX_BOMBS
bomb_vyl     .res MAX_BOMBS
bomb_vyh     .res MAX_BOMBS

; ---------------------------------------------------------------------------
; Start-up

        .org $C000

RESET:
        sei
        cld
        ldx #$40
        stx APU_FRAME           ; no APU frame interrupt
        ldx #$FF
        txs
        inx                     ; X = 0
        stx PPUCTRL             ; no NMI yet
        stx PPUMASK             ; picture off
        stx DMC_FREQ
        wait_vblank             ; the PPU needs two frames to wake up

        lda #0
@clear: sta $0000,x
        sta $0100,x
        sta $0200,x
        sta $0300,x
        sta $0400,x
        sta $0500,x
        sta $0600,x
        sta $0700,x
        inx
        bne @clear
        jsr hide_sprites
        wait_vblank

        jsr load_palettes
        lda #%00001001          ; pulse 1 and noise channels on
        sta APU_STATUS
        lda #$5A
        sta rng
        jsr enter_title

MainLoop:
        jsr wait_frame
        jsr read_joypad
        lda state
        cmp #STATE_PLAY
        beq @play
        cmp #STATE_OVER
        beq @over
        jsr update_title
        jmp MainLoop
@play:  jsr update_play
        jmp MainLoop
@over:  jsr update_over
        jmp MainLoop

; ---------------------------------------------------------------------------
; Interrupts

NMI:
        pha
        txa
        pha
        tya
        pha

        lda #0                  ; copy the sprite table to the PPU
        sta OAMADDR
        lda #>OAM
        sta OAMDMA

        lda mode3d              ; the 3D floor: its three colours, turned
        beq +
        bit PPUSTATUS
        lda #>$3F09
        sta PPUADDR
        lda #<$3F09
        sta PPUADDR
        ldx floor_phase
        lda FloorColors,x
        sta PPUDATA
        lda FloorColors+1,x
        sta PPUDATA
        lda FloorColors+2,x
        sta PPUDATA

+       bit PPUSTATUS           ; the scroll: x 0, y scroll_y of nametable scroll_nt
        lda #0
        sta PPUSCROLL
        lda scroll_y
        sta PPUSCROLL
        lda scroll_nt
        ora ctrl_bg
        ora #PPUCTRL_NMI
        sta PPUCTRL

        lda wave_on
        beq +
        jsr raster_wave
+       lda #1
        sta frame_ready
        pla
        tay
        pla
        tax
        pla
IRQ:
        rti

; Waits for the next NMI. Found set already, the NMI came while the last
; frame's work still ran: a frame was lost, and lag_frames counts it.
wait_frame:
        lda frame_ready
        beq @wait
        inc lag_frames
@wait:  lda frame_ready
        beq @wait
        lda #0
        sta frame_ready
        inc frame_count
        rts

; The ripple: called by NMI once the vblank work is done, it waits for line
; WAVE_TOP, then for each line of the band writes the horizontal scroll of
; the next one from the sine of Wave. Without a scanline interrupt the time
; is counted: a line is 113 2/3 CPU cycles, so the loop draws three lines
; (114 + 114 + 113 cycles) a turn. Only the first write to PPUSCROLL counts
; in the middle of a frame - the horizontal scroll; the vertical one stays -
; and reading PPUSTATUS first makes every write a first one. The routine
; starts a page, so that no branch inside crosses one and costs a cycle.
        .align 256
raster_wave:
        ldy #WAIT_COARSE        ; to the band's first line
@coarse:
        ldx #0
@inner: dex
        bne @inner
        dey
        bne @coarse
        ldx #WAIT_FINE
@fine:  dex
        bne @fine

        lda #WAVE_LINES/3
        sta wave_count
        ldx wave_phase
@lines: bit PPUSTATUS           ; a line: 114 cycles
        lda Wave,x
        sta PPUSCROLL
        inx
        ldy #19
@d1:    dey
        bne @d1
        nop
        nop
        bit PPUSTATUS           ; a line: 114 cycles
        lda Wave,x
        sta PPUSCROLL
        inx
        ldy #19
@d2:    dey
        bne @d2
        nop
        nop
        bit PPUSTATUS           ; a line: 113 cycles, with the loop
        lda Wave,x
        sta PPUSCROLL
        inx
        ldy #18
@d3:    dey
        bne @d3
        dec wave_count
        bne @lines

        bit PPUSTATUS           ; the rest of the screen straight
        lda #0
        sta PPUSCROLL
        inc wave_phase
        rts

; Reads the 8 buttons of joypad 1, and which of them went down this frame.
read_joypad:
        lda buttons
        sta prev_buttons
        lda #1                  ; strobe: latch the buttons
        sta JOY1
        lda #0
        sta JOY1
        ldx #8
@bit:   lda JOY1
        lsr a                   ; the button is in bit 0
        rol buttons
        dex
        bne @bit
        lda prev_buttons
        eor #$FF
        and buttons
        sta pressed
        rts

; ---------------------------------------------------------------------------
; Title screen

enter_title:
        jsr screen_off
        lda #<scr_title         ; the screen made in the designer
        sta ptr
        lda #>scr_title
        sta ptr+1
        jsr load_nametable
        jsr hide_sprites
        lda #120
        sta ship_x
        lda #SHIP_START_Y
        sta ship_y
        lda #0
        sta hit_timer
        sta oam_index
        jsr draw_ship
        lda #STATE_TITLE
        sta state
        jmp screen_on

update_title:
        lda pressed
        and #BUTTON_START
        beq @wait
        lda frame_count         ; how long the player waited seeds the sky
        ora #1
        sta rng
        jmp enter_play
@wait:  rts

; ---------------------------------------------------------------------------
; Playing

enter_play:
        jsr screen_off
        lda #0                  ; the sky: its first screen in $2000 ...
        jsr draw_sky
        lda #1                  ; ... its second in $2800
        jsr draw_sky

        lda #120
        sta ship_x
        lda #SHIP_START_Y
        sta ship_y
        lda #3
        sta lives
        lda #1
        sta level
        lda #0
        sta hit_timer
        sta fire_timer
        ldx #3
@score: sta score,x
        dex
        bpl @score
        jsr clear_actors
        jsr start_level
        lda #STATE_PLAY
        sta state
        lda #1                  ; the sky ripples
        sta wave_on
        jsr hide_sprites
        jmp screen_on

; A level's waves: the delay between two objects shrinks by 4 frames a level.
start_level:
        lda #PHASE_WAVES
        sta phase
        lda level               ; the tier: (level + 1) / 2, up to 5
        clc
        adc #1
        lsr a
        cmp #6
        bcc +
        lda #5
+       sta tier
        lda #0
        sta level_clock
        lda level
        sec
        sbc #1
        asl a
        asl a
        sta tmp
        lda #FIRST_DELAY
        sec
        sbc tmp
        bcc @least
        cmp #MIN_DELAY
        bcs @set
@least: lda #MIN_DELAY
@set:   sta spawn_delay
        sta spawn_timer
        rts

; No object, shot or bomb.
clear_actors:
        lda #0
        ldx #MAX_OBJECTS-1
-       sta obj_type,x
        dex
        bpl -
        ldx #MAX_SHOTS-1
-       sta shot_y,x
        dex
        bpl -
        ldx #MAX_BOMBS-1
-       sta bomb_y,x
        dex
        bpl -
        rts

update_play:
        lda mode3d
        beq +
        jmp update_3d
+       jsr scroll_sky
        jsr move_ship
        jsr fire
        jsr move_shots
        lda phase
        bne +
        jsr spawn_objects       ; PHASE_WAVES
        jsr count_time
+       jsr move_objects
        jsr move_bombs
        lda phase
        cmp #PHASE_BOSS
        bne +
        jsr update_boss
+       lda phase
        cmp #PHASE_CLEAR
        bne +
        jsr update_clear
+       lda lives
        bne +
        jmp enter_over
+       jmp draw_sprites

; The sky moves down one line a frame: the picture's first line climbs the
; 480 lines of the two nametables, and from the top of $2000 jumps to the
; bottom of $2800. The map's two screens join, so the sky loops.
scroll_sky:
        lda scroll_y
        bne +
        lda #240
        sta scroll_y
        lda scroll_nt
        eor #2
        sta scroll_nt
+       dec scroll_y
        rts

; The d-pad moves the ship, within the lower half of the screen.
move_ship:
        lda buttons
        and #BUTTON_LEFT
        beq +
        lda ship_x
        cmp #SHIP_MIN_X+1
        bcc +
        sec
        sbc #SHIP_SPEED
        sta ship_x
+       lda buttons
        and #BUTTON_RIGHT
        beq +
        lda ship_x
        cmp #SHIP_MAX_X
        bcs +
        clc
        adc #SHIP_SPEED
        sta ship_x
+       lda buttons
        and #BUTTON_UP
        beq +
        lda ship_y
        cmp #SHIP_MIN_Y+1
        bcc +
        sec
        sbc #SHIP_SPEED
        sta ship_y
+       lda buttons
        and #BUTTON_DOWN
        beq +
        lda ship_y
        cmp #SHIP_MAX_Y
        bcs +
        clc
        adc #SHIP_SPEED
        sta ship_y
+       rts

; A or B fires at once; held, a shot every FIRE_DELAY frames.
fire:
        lda fire_timer
        beq +
        dec fire_timer
+       lda pressed
        and #BUTTON_A|BUTTON_B
        bne @shoot
        lda buttons
        and #BUTTON_A|BUTTON_B
        beq @done
        lda fire_timer
        bne @done
@shoot: ldx #MAX_SHOTS-1
@find:  lda shot_y,x
        beq @found
        dex
        bpl @find
@done:  rts                     ; three shots in the air already
@found: lda mode3d
        beq +
        jmp shoot3d
+       lda ship_x              ; from the ship's nose
        clc
        adc #4
        sta shot_x,x
        lda ship_y
        sec
        sbc #6
        sta shot_y,x
        lda #FIRE_DELAY
        sta fire_timer
        jmp sound_shot

; Moves the shots up, and checks each against the enemies.
move_shots:
        ldx #MAX_SHOTS-1
@loop:  lda shot_y,x
        beq @next
        sec
        sbc #SHOT_SPEED
        bcc @gone
        cmp #8
        bcs @move
@gone:  lda #0                  ; off the top
@move:  sta shot_y,x
        beq @next
        stx shot_index
        jsr shot_hits
        ldx shot_index
@next:  dex
        bpl @loop
        rts

; Shot shot_index against every enemy, then the boss: the first it touches
; takes the hit, and the shot is gone. Stars and bursts let it through.
shot_hits:
        lda shot_x,x            ; the shot's two middle columns
        clc
        adc #3
        sta box_ax
        lda shot_y,x
        sta box_ay
        lda #2
        sta box_aw
        lda #6
        sta box_ah
        ldx #MAX_OBJECTS-1
@obj:   lda obj_type,x
        cmp #KIND_METEOR
        bcc @next               ; free, or a star
        cmp #KIND_BURST
        beq @next
        jsr object_box
        jsr overlap
        bcc @next
        jsr remove_shot
        jmp hit_object
@next:  dex
        bpl @obj
        jsr boss_fights
        bcc @done
        jsr boss_box
        jsr overlap
        bcc @done
        jsr remove_shot
        jmp hit_boss
@done:  rts

remove_shot:
        ldy shot_index
        lda #0
        sta shot_y,y
        rts

; Object X took a shot: at no strength left, it bursts and scores.
hit_object:
        dec obj_hp,x
        bne @done
        ldy obj_type,x
        lda KindPoints,y
        jsr add_points
        lda obj_type,x
        cmp #KIND_GIANT
        bne +
        jsr split_rock
+       jsr burst_object
        jmp sound_hit
@done:  rts

; Object X becomes an explosion, drifting down with the sky.
burst_object:
        lda #KIND_BURST
        sta obj_type,x
        lda #BURST_FRAMES
        sta obj_dx,x
        lda #1
        sta obj_speed,x
        rts

; Moves every object, and checks it against the ship.
move_objects:
        ldx #MAX_OBJECTS-1
@loop:  lda obj_type,x
        beq @next
        cmp #KIND_BURST
        bne @move
        dec obj_dx,x            ; a burst's timer
        bne @move
        lda #0
        sta obj_type,x
        beq @next
@move:  lda obj_y,x
        clc
        adc obj_speed,x
        cmp #232
        bcc +
        lda #0                  ; off the bottom of the screen
        sta obj_type,x
        beq @next
+       sta obj_y,x
        lda obj_type,x
        cmp #KIND_BURST
        beq @next
        asl a
        tay
        lda MoveKind,y
        sta ptr
        lda MoveKind+1,y
        sta ptr+1
        jsr @call
@touch: jsr object_box
        jsr ship_box
        jsr overlap
        bcc @next
        jsr collide
@next:  dex
        bpl @loop
        rts
@call:  jmp (ptr)

; A meteor goes down, and sideways when it is a piece of a giant one.
move_meteor:
        lda obj_x,x
        clc
        adc obj_dx,x
        sta obj_x,x
        rts

move_nothing:
        rts

; Each kind's move, after it went down by its speed.
MoveKind:
        .word move_nothing, move_nothing, move_meteor, move_saucer, move_hunter, move_nothing
        .word move_diver, move_turret, move_weaver, move_nothing

; Saucer X zigzags between the sides, and now and then drops a bomb.
move_saucer:
        lda obj_x,x
        clc
        adc obj_dx,x
        sta obj_x,x
        cmp #SHIP_MIN_X
        bcc @turn
        cmp #224
        bcc @bomb
@turn:  lda obj_dx,x
        eor #$FF
        clc
        adc #1
        sta obj_dx,x
@bomb:  jsr random
        cmp #3                  ; 1 or 2 of 255 (rng is never 0): a bomb in about 128 frames
        bcs @done
        lda obj_x,x
        clc
        adc #4
        sta new_x
        lda obj_y,x
        clc
        adc #6
        sta new_y
        lda #4                  ; two pixels a frame, straight down
        sta bullet_speed
        stx obj_index
        jsr fire_bullet
        ldx obj_index
@done:  rts

; Hunter X turns towards the ship, one pixel every other frame.
move_hunter:
        lda frame_count
        lsr a
        bcc @done
        lda ship_x
        clc
        adc #4
        cmp obj_x,x
        beq @done
        bcc @left
        inc obj_x,x
        rts
@left:  dec obj_x,x
@done:  rts

; Object X touched the ship: a star is caught, an enemy bursts and hurts it.
collide:
        lda obj_type,x
        cmp #KIND_STAR
        bne @enemy
        lda #0
        sta obj_type,x
        lda KindPoints+KIND_STAR
        jsr add_points
        jmp sound_catch
@enemy: lda hit_timer
        bne @safe               ; blinking: it goes through
        jsr burst_object
        jmp ship_hit
@safe:  rts

ship_hit:
        lda #HIT_FRAMES
        sta hit_timer
        dec lives
        jmp sound_hit

; Every 64 frames of waves count; at the level's length, the boss.
count_time:
        lda frame_count
        and #63
        bne @done
        inc level_clock
        lda level_clock
        cmp #LEVEL_LENGTH
        bcc @done
        jmp start_boss
@done:  rts

; ---------------------------------------------------------------------------
; The boss

start_boss:
        lda #PHASE_BOSS
        sta phase
        lda #WARNING_FRAMES     ; WARNING blinks, then the boss comes down
        sta phase_timer
        lda #112
        sta boss_x
        lda #0
        sta boss_y
        sta boss_flash
        lda #1
        sta boss_dx
        lda #BOSS_FIRE_DELAY
        sta boss_fire
        lda level               ; BOSS_LIFE, and 8 more a level
        sec
        sbc #1
        asl a
        asl a
        asl a
        clc
        adc #BOSS_LIFE
        sta boss_hp
        lsr a                   ; its phases: below 5/8 of its life, then 5/16
        lsr a
        sta tmp
        lsr a
        lsr a
        clc
        adc tmp
        sta boss_t2
        asl a
        sta boss_t1
        lda #0
        sta boss_burst
        sta boss_spin
        rts

; Carry set when the boss is on screen and can be hit.
boss_fights:
        lda phase
        cmp #PHASE_BOSS
        bne @no
        lda phase_timer
        bne @no
        sec
        rts
@no:    clc
        rts

update_boss:
        lda phase_timer
        beq @come
        dec phase_timer         ; the warning
        rts
@come:  lda boss_y
        cmp #BOSS_TOP_Y
        bcs @move
        inc boss_y              ; comes down from the top
        jmp @touch
@move:  lda boss_x              ; then from side to side
        clc
        adc boss_dx
        ldy boss_hp             ; twice as fast after its first phase
        cpy boss_t1
        bcs +
        clc
        adc boss_dx
+       sta boss_x
        cmp #BOSS_MIN_X
        bcc @turn
        cmp #BOSS_MAX_X
        bcc @fire
@turn:  lda boss_dx
        eor #$FF
        clc
        adc #1
        sta boss_dx
@fire:  lda boss_x               ; its bullets start under its middle
        clc
        adc #12
        sta new_x
        lda boss_y
        clc
        adc #20
        sta new_y
        dec boss_fire
        lda boss_hp
        cmp boss_t2
        bcc @third
        cmp boss_t1
        bcc @second
        lda boss_fire           ; first phase: a fan of five, down
        bne @touch
        lda #BOSS_FIRE_DELAY
        sta boss_fire
        lda #3
        sta bullet_speed
        lda #4
        jsr shoot_fan5
        jmp @touch
@second:
        lda boss_fire           ; second: every 64 frames, three quick shots
        and #63                 ; at the ship, 8 frames apart
        bne +
        lda #3
        sta boss_burst
+       lda boss_fire
        and #7
        bne @touch
        lda boss_burst
        beq @touch
        dec boss_burst
        lda #5
        sta bullet_speed
        jsr shoot_aimed
        jmp @touch
@third: lda #3                  ; third: a turning spiral, and a ring every
        sta bullet_speed        ; two seconds
        lda boss_fire
        and #127
        bne +
        lda boss_spin
        jsr shoot_ring
        jmp @touch
+       lda boss_fire
        and #7
        bne @touch
        lda boss_spin
        clc
        adc #3
        sta boss_spin
        jsr fire_bullet
@touch: lda hit_timer
        bne @done
        jsr boss_box
        jsr ship_box
        jsr overlap
        bcc @done
        jmp ship_hit
@done:  rts

; The boss took a shot.
hit_boss:
        lda #4
        sta boss_flash
        dec boss_hp
        beq @broken
        rts
@broken:
        lda #PHASE_CLEAR
        sta phase
        lda #CLEAR_FRAMES
        sta phase_timer
        lda #50
        jsr add_points
        ldx #4                  ; five bursts where it was
@burst: lda BurstX,x
        clc
        adc boss_x
        sta new_x
        lda BurstY,x
        clc
        adc boss_y
        sta new_y
        stx obj_index
        jsr burst_at
        ldx obj_index
        dex
        bpl @burst
        jmp sound_boom

; A burst at (new_x, new_y), if an object slot is free.
burst_at:
        ldx #MAX_OBJECTS-1
@find:  lda obj_type,x
        beq @found
        dex
        bpl @find
        rts
@found: lda new_x
        sta obj_x,x
        lda new_y
        sta obj_y,x
        jmp burst_object

; LEVEL CLEAR shows, then the next level starts.
update_clear:
        dec phase_timer
        bne @done
        lda level
        cmp #LAST_LEVEL
        bcs +
        inc level
+       jmp enter_3d            ; then the 3D stage
@done:  rts

; ---------------------------------------------------------------------------
; Collisions

; Box a: the ship's body.
ship_box:
        lda ship_x
        clc
        adc #2
        sta box_ax
        lda ship_y
        clc
        adc #1
        sta box_ay
        lda #12
        sta box_aw
        lda #7
        sta box_ah
        rts

; Box b: object X, 8 x 8, or 16 x 8 for a saucer.
object_box:
        lda obj_x,x
        sta box_bx
        lda obj_y,x
        sta box_by
        lda #8
        sta box_bh
        ldy #8
        lda obj_type,x
        cmp #KIND_DIVER
        bcc +
        lda #16                 ; the newer mutants: 16 x 16
        sta box_bh
        ldy #16
        bne ++
+       cmp #KIND_SAUCER
        bne ++
        ldy #16
++      sty box_bw
        rts

; Box b: the boss's hull.
boss_box:
        lda boss_x
        sta box_bx
        lda boss_y
        sta box_by
        lda #32
        sta box_bw
        lda #20
        sta box_bh
        rts

; Carry set when boxes a and b overlap: each starts before the other ends.
overlap:
        lda box_bx
        clc
        adc box_bw
        cmp box_ax
        bcc @no
        beq @no
        lda box_ax
        clc
        adc box_aw
        cmp box_bx
        bcc @no
        beq @no
        lda box_by
        clc
        adc box_bh
        cmp box_ay
        bcc @no
        beq @no
        lda box_ay
        clc
        adc box_ah
        cmp box_by
        bcc @no
        beq @no
        sec
        rts
@no:    clc
        rts

; ---------------------------------------------------------------------------
; Score

; Adds A points to the score.
add_points:
        sta points
-       jsr add_point
        dec points
        bne -
        rts

; Adds one to the score, a decimal number kept one digit per byte.
add_point:
        ldy #3
@carry: lda score,y
        clc
        adc #1
        cmp #10
        bcc @store
        lda #0
        sta score,y
        dey
        bpl @carry
        rts                     ; 9999 + 1: back to 0000
@store: sta score,y
        rts

; ---------------------------------------------------------------------------
; Game over

enter_over:
        jsr screen_off
        lda #<scr_over
        sta ptr
        lda #>scr_over
        sta ptr+1
        jsr load_nametable
        jsr draw_hud            ; the final score and lives over the screen
        jsr hide_sprites
        lda #120                ; two seconds before START restarts
        sta delay
        lda #STATE_OVER
        sta state
        jmp screen_on

update_over:
        lda delay
        beq @ready
        dec delay
        rts
@ready: lda pressed
        and #BUTTON_START
        beq @wait
        jmp enter_title
@wait:  rts

; ---------------------------------------------------------------------------
; Sprites

; Everything into the sprite table, as metasprites. The objects come last,
; in a different order every other frame: where more than eight sprites
; share a line, the hardware drops the last ones, and they flicker instead
; of vanishing.
draw_sprites:
        lda #0
        sta oam_index
        jsr draw_hud_sprites
        jsr draw_ship
        jsr draw_shots
        jsr draw_bombs
        jsr draw_boss
        jsr draw_message
        jsr draw_objects
        jmp hide_rest

; The score's four digits and the lives, in sprites over the scrolling sky.
draw_hud_sprites:
        lda #HUD_Y
        sta ms_y
        lda #HUD_X
        sta ms_x
        ldy #0
@digit: lda score,y
        clc
        adc #'0'                ; the tiles of digits are their ASCII codes
        jsr put_sprite
        lda ms_x
        clc
        adc #8
        sta ms_x
        iny
        cpy #4
        bne @digit
        lda #LIVES_X
        sta ms_x
        lda #SPR_MS_HEART
        jsr draw_metasprite
        lda #LIVES_X+10
        sta ms_x
        lda lives
        clc
        adc #'0'
        jmp put_sprite

; The ship. While hit_timer runs it blinks.
draw_ship:
        lda ship_x
        sta ms_x
        lda ship_y
        sec
        sbc #1                  ; a sprite is drawn one line below its Y
        sta ms_y
        lda hit_timer
        beq @show
        dec hit_timer
        and #4
        beq @show
        rts
@show:  lda #SPR_MS_SHIP
        jmp draw_metasprite

draw_shots:
        ldy #MAX_SHOTS-1
@loop:  lda shot_y,y
        beq @next
        sta ms_y
        lda shot_x,y
        sta ms_x
        sty shot_index
        lda #SPR_MS_SHOT
        jsr draw_metasprite
        ldy shot_index
@next:  dey
        bpl @loop
        rts

draw_bombs:
        ldy #MAX_BOMBS-1
@loop:  lda bomb_y,y
        beq @next
        sta ms_y
        lda bomb_x,y
        sta ms_x
        sty shot_index
        lda #SPR_MS_BOMB
        jsr draw_metasprite
        ldy shot_index
@next:  dey
        bpl @loop
        rts

; The boss, in its hit colours for a few frames after a shot.
draw_boss:
        jsr boss_fights
        bcc @done
        lda boss_x
        sta ms_x
        lda boss_y
        sta ms_y
        lda boss_flash
        beq +
        dec boss_flash
        lda #SPR_MS_BOSS_HIT
        jmp draw_metasprite
+       lda boss_hp             ; in its last phase it glows, on and off
        cmp boss_t2
        bcs +
        lda frame_count
        and #%00010000
        beq +
        lda #SPR_MS_BOSS_HIT
        jmp draw_metasprite
+       lda #SPR_MS_BOSS
        jmp draw_metasprite
@done:  rts

; WARNING blinks before the boss; LEVEL CLEAR after it. In sprites: the sky
; scrolls. Two lines, because a line shows at most eight sprites.
draw_message:
        lda phase
        cmp #PHASE_CLEAR
        beq @clear
        cmp #PHASE_BOSS
        bne @done
        lda phase_timer
        and #16
        beq @done
        lda #<WarningText
        sta ptr
        lda #>WarningText
        sta ptr+1
        lda #100
        sta ms_x
        lda #100
        sta ms_y
        jmp draw_sprite_text
@clear: lda #<LevelText
        sta ptr
        lda #>LevelText
        sta ptr+1
        lda #108
        sta ms_x
        lda #96
        sta ms_y
        jsr draw_sprite_text
        lda #<ClearText
        sta ptr
        lda #>ClearText
        sta ptr+1
        lda #108
        sta ms_x
        lda #108
        sta ms_y
        jmp draw_sprite_text
@done:  rts

draw_objects:
        lda #MAX_OBJECTS-1
        sta draw_count
@loop:  ldx draw_count
        lda frame_count
        lsr a
        bcc +
        lda #MAX_OBJECTS-1      ; odd frames: the other way round
        sec
        sbc draw_count
        tax
+       stx obj_index
        lda obj_type,x
        beq @next
        lda obj_x,x
        sta ms_x
        lda obj_y,x
        sta ms_y
        ldy obj_type,x
        cpy #KIND_STAR
        bne +
        lda #<spr_twinkle_anim  ; stars play the twinkle animation
        sta ptr
        lda #>spr_twinkle_anim
        sta ptr+1
        lda frame_count
        jsr anim_frame
        jmp @draw
+       lda KindSprite,y
        cpy #KIND_DIVER
        bcc @draw
        sta tmp                 ; the newer mutants: their two frames in turn
        lda frame_count
        lsr a
        lsr a
        lsr a
        and #1
        clc
        adc tmp
@draw:  jsr draw_metasprite
@next:  dec draw_count
        bpl @loop
        rts

; A text from ptr in sprites, from (ms_x, ms_y) on, ended by 0. The tiles of
; letters are their ASCII codes; spaces take no sprite.
draw_sprite_text:
        ldy #0
@loop:  lda (ptr),y
        beq @done
        cmp #' '
        beq +
        jsr put_sprite
+       lda ms_x
        clc
        adc #8
        sta ms_x
        iny
        bne @loop
@done:  rts

; One sprite: tile A at (ms_x, ms_y), palette 0. Keeps Y.
put_sprite:
        ldx oam_index
        sta OAM+1,x
        lda ms_y
        sta OAM,x
        lda #0
        sta OAM+2,x
        lda ms_x
        sta OAM+3,x
        inx
        inx
        inx
        inx
        stx oam_index
        rts

; Draws metasprite A with its origin at (ms_x, ms_y), from oam_index on.
; A metasprite is rows of dy, tile, attributes, dx ended by $80, as the
; designer exports them (gfx/objects.msprite.asm).
draw_metasprite:
        tax
        lda spr_metasprites_lo,x
        sta ptr
        lda spr_metasprites_hi,x
        sta ptr+1
        ldy #0
        ldx oam_index
@next:  lda (ptr),y             ; dy, or the end
        cmp #$80
        beq @done
        clc
        adc ms_y
        sta OAM,x
        iny
        lda (ptr),y             ; tile
        sta OAM+1,x
        iny
        lda (ptr),y             ; attributes: palette and flips
        sta OAM+2,x
        iny
        lda (ptr),y             ; dx
        clc
        adc ms_x
        sta OAM+3,x
        iny
        inx
        inx
        inx
        inx
        bne @next               ; the table is full: the rest is not drawn
        ldx #$FC
@done:  stx oam_index
        rts

; The metasprite an animation shows at a time: ptr = the animation (rows of
; metasprite, duration, ended by $FF), A = the time in frames. It loops.
anim_frame:
        sta tmp
@again: ldy #0
@frame: lda (ptr),y             ; metasprite, or the end
        cmp #$FF
        beq @again              ; past the end: tmp is now smaller by one turn
        iny
        lda tmp
        sec
        sbc (ptr),y             ; duration
        bcc @this
        sta tmp
        iny
        bne @frame
@this:  dey
        lda (ptr),y
        rts

; Hides the sprites from oam_index to the end of the table.
hide_rest:
        ldx oam_index
        lda #$FF
@loop:  sta OAM,x
        inx
        inx
        inx
        inx
        bne @loop
        rts

hide_sprites:
        lda #$FF
        ldx #0
@loop:  sta OAM,x
        inx
        inx
        inx
        inx
        bne @loop
        rts

; ---------------------------------------------------------------------------
; Background

; Turns the picture off, so that video memory can be written at any time,
; and puts the scroll back at the top of $2000.
screen_off:
        lda #0
        sta PPUCTRL             ; no NMI while we draw
        sta scroll_y
        sta scroll_nt
        sta wave_on
        sta mode3d
        sta ctrl_bg
        wait_vblank
        lda #0
        sta PPUMASK
        jmp load_palettes

screen_on:
        wait_vblank
        lda #0
        sta PPUSCROLL
        sta PPUSCROLL
        lda ctrl_bg
        ora #PPUCTRL_NMI
        sta PPUCTRL
        lda #PPUMASK_SHOW
        sta PPUMASK
        rts

load_palettes:
        lda #<palettes          ; gfx/game.pal.asm
        sta ptr
        lda #>palettes
        sta ptr+1
; The 32 colours from ptr.
load_palette_set:
        ppu_addr $3F00
        ldy #0
@loop:  lda (ptr),y
        sta PPUDATA
        iny
        cpy #32
        bne @loop
        rts

; Copies a screen made in the designer (960 tiles, then 64 attribute bytes)
; from ptr to the nametable. The picture is off.
load_nametable:
        ppu_addr NT
        ldx #4                  ; 4 pages of 256 bytes
        ldy #0
@loop:  lda (ptr),y
        sta PPUDATA
        iny
        bne @loop
        inc ptr+1
        dex
        bne @loop
        rts

; Draws screen A (0 or 1) of the sky, a map of 16 x 30 metatiles of 2 x 2
; tiles (gfx/play.map.asm), into nametable $2000 or $2800, then its
; attributes from the metatiles' palettes. The picture is off.
draw_sky:
        tax
        lda SkyLow,x
        sta map_base
        sta ptr
        lda SkyHigh,x
        sta map_base+1
        sta ptr+1
        bit PPUSTATUS
        lda SkyNametable,x
        sta PPUADDR
        lda #0
        sta PPUADDR
        lda #SKY_ROWS
        sta map_row
@row:   ldy #0                  ; the top tiles of a row of metatiles
@top:   lda (ptr),y
        tax
        lda bg_metatile_tl,x
        sta PPUDATA
        lda bg_metatile_tr,x
        sta PPUDATA
        iny
        cpy #BG_PLAY_WIDTH
        bne @top
        ldy #0                  ; then their bottom tiles
@bottom:
        lda (ptr),y
        tax
        lda bg_metatile_bl,x
        sta PPUDATA
        lda bg_metatile_br,x
        sta PPUDATA
        iny
        cpy #BG_PLAY_WIDTH
        bne @bottom
        lda ptr
        clc
        adc #BG_PLAY_WIDTH
        sta ptr
        bcc +
        inc ptr+1
+       dec map_row
        bne @row

        ; The attribute table follows: one byte per 2x2 metatiles, top left in
        ; bits 0-1, top right 2-3, bottom left 4-5, bottom right 6-7.
        lda #0
        sta map_row
@arow:  lda #0
        sta map_col
@acol:  lda map_row             ; the top left metatile: row*2*16 + col*2
        asl a
        asl a
        asl a
        asl a
        asl a
        sta tmp
        lda map_col
        asl a
        clc
        adc tmp
        tay
        lda (map_base),y
        tax
        lda bg_metatile_pal,x
        sta tmp
        iny
        lda (map_base),y
        tax
        lda bg_metatile_pal,x
        asl a
        asl a
        ora tmp
        sta tmp
        lda map_row
        cmp #7                  ; the last row of blocks covers one metatile row
        beq @store
        tya
        clc
        adc #BG_PLAY_WIDTH-1
        tay
        lda (map_base),y
        tax
        lda bg_metatile_pal,x
        asl a
        asl a
        asl a
        asl a
        ora tmp
        sta tmp
        iny
        lda (map_base),y
        tax
        lda bg_metatile_pal,x
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        ora tmp
        sta tmp
@store: lda tmp
        sta PPUDATA
        inc map_col
        lda map_col
        cmp #8
        bne @acol
        inc map_row
        lda map_row
        cmp #8
        bne @arow
        rts

; Game over: the score's four digits and the lives, on the screen.
draw_hud:
        ppu_addr HUD_SCORE
        ldx #0
@digit: lda score,x
        clc
        adc #'0'
        sta PPUDATA
        inx
        cpx #4
        bne @digit
        ppu_addr HUD_LIVES
        lda lives
        clc
        adc #'0'
        sta PPUDATA
        rts

; ---------------------------------------------------------------------------
; Sound

; A short falling note on pulse channel 1.
sound_shot:
        lda #%01010110          ; duty 25%, constant volume 6
        sta SQ1_VOL
        lda #%10010010          ; sweep down
        sta SQ1_SWEEP
        lda #$60
        sta SQ1_LO
        lda #%00101000          ; short length, period high bits 0
        sta SQ1_HI
        rts

; A short rising blip on pulse channel 1.
sound_catch:
        lda #%10011111          ; duty 50%, constant volume 15
        sta SQ1_VOL
        lda #%10111010          ; sweep up
        sta SQ1_SWEEP
        lda #$C9
        sta SQ1_LO
        lda #%00010000          ; short length, period high bits 0
        sta SQ1_HI
        rts

; A crash on the noise channel.
sound_hit:
        lda #%00011111
        sta NOISE_VOL
        lda #$0C                ; a low rumble
        sta NOISE_LO
        lda #%00100000
        sta NOISE_HI
        rts

; The boss breaking: a longer, lower crash.
sound_boom:
        lda #%00011111
        sta NOISE_VOL
        lda #$0F
        sta NOISE_LO
        lda #%01000000
        sta NOISE_HI
        rts

; ---------------------------------------------------------------------------
; Random numbers: an 8-bit Galois LFSR. Returns the next value in A.

random:
        lda rng
        lsr a
        bcc +
        eor #$B8
+       sta rng
        rts

        .include "bullets.asm"
        .include "mutants.asm"
        .include "stage3d.asm"

; ---------------------------------------------------------------------------
; Data

; The graphics, as the designer exports them (see ../gfx/*.json).
        .include "../gfx/game.pal.asm"
        .include "../gfx/objects.msprite.asm"
        .include "../gfx/title.screen.asm"
        .include "../gfx/over.screen.asm"
        .include "../gfx/play.map.asm"
        .include "../gfx/stage3d.screen.asm"
        .include "../gfx/stage3d.pal.asm"

; Where the boss's five bursts go, from its top left corner.
BurstX: .byte 4, 20, 12, 0, 24
BurstY: .byte 2, 4, 10, 14, 12

; The sky's two screens: where they start in the map, and their nametables.
SkyLow:       .byte <bg_play_map, <(bg_play_map+BG_PLAY_WIDTH*SKY_ROWS)
SkyHigh:      .byte >bg_play_map, >(bg_play_map+BG_PLAY_WIDTH*SKY_ROWS)
SkyNametable: .byte >NT, >NT_LOW

; The ripple's sine: 3 pixels either way over 32 lines, 256 entries so that
; the index wraps by itself, a page of its own so that no read crosses one.
        .align 256
Wave:
        .byte $00, $01, $01, $02, $02, $02, $03, $03, $03, $03, $03, $02, $02, $02, $01, $01
        .byte $00, $FF, $FF, $FE, $FE, $FE, $FD, $FD, $FD, $FD, $FD, $FE, $FE, $FE, $FF, $FF
        .byte $00, $01, $01, $02, $02, $02, $03, $03, $03, $03, $03, $02, $02, $02, $01, $01
        .byte $00, $FF, $FF, $FE, $FE, $FE, $FD, $FD, $FD, $FD, $FD, $FE, $FE, $FE, $FF, $FF
        .byte $00, $01, $01, $02, $02, $02, $03, $03, $03, $03, $03, $02, $02, $02, $01, $01
        .byte $00, $FF, $FF, $FE, $FE, $FE, $FD, $FD, $FD, $FD, $FD, $FE, $FE, $FE, $FF, $FF
        .byte $00, $01, $01, $02, $02, $02, $03, $03, $03, $03, $03, $02, $02, $02, $01, $01
        .byte $00, $FF, $FF, $FE, $FE, $FE, $FD, $FD, $FD, $FD, $FD, $FE, $FE, $FE, $FF, $FF
        .byte $00, $01, $01, $02, $02, $02, $03, $03, $03, $03, $03, $02, $02, $02, $01, $01
        .byte $00, $FF, $FF, $FE, $FE, $FE, $FD, $FD, $FD, $FD, $FD, $FE, $FE, $FE, $FF, $FF
        .byte $00, $01, $01, $02, $02, $02, $03, $03, $03, $03, $03, $02, $02, $02, $01, $01
        .byte $00, $FF, $FF, $FE, $FE, $FE, $FD, $FD, $FD, $FD, $FD, $FE, $FE, $FE, $FF, $FF
        .byte $00, $01, $01, $02, $02, $02, $03, $03, $03, $03, $03, $02, $02, $02, $01, $01
        .byte $00, $FF, $FF, $FE, $FE, $FE, $FD, $FD, $FD, $FD, $FD, $FE, $FE, $FE, $FF, $FF
        .byte $00, $01, $01, $02, $02, $02, $03, $03, $03, $03, $03, $02, $02, $02, $01, $01
        .byte $00, $FF, $FF, $FE, $FE, $FE, $FD, $FD, $FD, $FD, $FD, $FE, $FE, $FE, $FF, $FF

WarningText:  .asciiz "WARNING"
LevelText:    .asciiz "LEVEL"
ClearText:    .asciiz "CLEAR"
