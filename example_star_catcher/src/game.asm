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

MAX_BOMBS   = 4
BOMB_SPEED  = 2

MAX_OBJECTS = 8
KIND_STAR   = 1                 ; caught for points
KIND_METEOR = 2                 ; falls straight
KIND_SAUCER = 3                 ; zigzags and drops bombs, two shots to break
KIND_HUNTER = 4                 ; dives at the ship
KIND_BURST  = 5                 ; an explosion, for BURST_FRAMES
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
BOSS_FIRE_DELAY = 48            ; frames between two volleys of three bombs

HUD_X       = 16                ; the score, in sprites: the sky scrolls
HUD_Y       = 8
LIVES_X     = 208

; The graphics are assets of the designer, in ../gfx: the palettes, the
; metasprites (ship, shot, star and its twinkle, meteor, saucer, hunter,
; bomb, burst, heart, boss), the title and game over screens, and the sky as
; a map of metatiles two screens high. Their exported .asm files are
; included at the end.

PPUCTRL_NMI  = %10000000        ; NMI on, all tiles from pattern table 0
PPUMASK_SHOW = %00011110        ; background and sprites, left column included

HUD_SCORE   = NT+2*32+8         ; game over: where the four digits of the score go
HUD_LIVES   = NT+2*32+25        ; and the number of lives
SKY_ROWS    = 15                ; rows of metatiles in a screen

; ---------------------------------------------------------------------------
; Variables, in zero page: .res hands out addresses from $0000 on.

        .memarea $0000
frame_ready  .res 1             ; set by NMI, cleared by wait_frame
frame_count  .res 1
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
obj_type     .res MAX_OBJECTS   ; 0 when the slot is free
obj_x        .res MAX_OBJECTS
obj_y        .res MAX_OBJECTS
obj_speed    .res MAX_OBJECTS   ; pixels down a frame
obj_dx       .res MAX_OBJECTS   ; saucers: pixels sideways (signed); bursts: frames left
obj_hp       .res MAX_OBJECTS   ; shots left to break it
shot_x       .res MAX_SHOTS
shot_y       .res MAX_SHOTS     ; 0 when the slot is free
bomb_x       .res MAX_BOMBS
bomb_y       .res MAX_BOMBS     ; 0 when the slot is free
bomb_dx      .res MAX_BOMBS
boss_x       .res 1
boss_y       .res 1
boss_dx      .res 1
boss_hp      .res 1
boss_flash   .res 1             ; frames left of the hit colours
boss_fire    .res 1
new_x        .res 1             ; where drop_bomb and burst_at put a new one
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

        bit PPUSTATUS           ; the scroll: x 0, y scroll_y of nametable scroll_nt
        lda #0
        sta PPUSCROLL
        lda scroll_y
        sta PPUSCROLL
        lda scroll_nt
        ora #PPUCTRL_NMI
        sta PPUCTRL

        lda #1
        sta frame_ready
        pla
        tay
        pla
        tax
        pla
IRQ:
        rti

; Waits for the next NMI.
wait_frame:
        lda #0
        sta frame_ready
-       lda frame_ready
        beq -
        inc frame_count
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
        jsr hide_sprites
        jmp screen_on

; A level's waves: the delay between two objects shrinks by 4 frames a level.
start_level:
        lda #PHASE_WAVES
        sta phase
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
        jsr scroll_sky
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
@found: lda ship_x              ; from the ship's nose
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
        jsr burst_object
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

; Every spawn_delay frames an object appears at the top: a star, a meteor,
; a saucer or a hunter.
spawn_objects:
        dec spawn_timer
        bne @done
        lda spawn_delay
        sta spawn_timer
        ldx #MAX_OBJECTS-1
@find:  lda obj_type,x
        beq @found
        dex
        bpl @find
@done:  rts                     ; every slot is busy: skip this one

@found: jsr random
        and #%11111000
        cmp #217                ; room for a saucer's 16 pixels
        bcc +
        lda #216
+       cmp #SHIP_MIN_X
        bcs +
        lda #SHIP_MIN_X
+       sta obj_x,x
        lda #8
        sta obj_y,x
        lda #1
        sta obj_speed,x
        sta obj_hp,x
        sta obj_dx,x
        jsr random
        cmp #48
        bcc @star
        cmp #120
        bcc @meteor
        cmp #200
        bcc @saucer
        lda #2                  ; a hunter: faster
        sta obj_speed,x
        lda #KIND_HUNTER
        bne @kind
@saucer:
        lda #2
        sta obj_hp,x
        lda frame_count         ; to the left or to the right
        and #1
        bne +
        lda #$FF
        sta obj_dx,x
+       lda #KIND_SAUCER
        bne @kind
@meteor:
        jsr random
        and #1
        clc
        adc #1
        sta obj_speed,x         ; 1 or 2 pixels a frame
        lda #KIND_METEOR
        bne @kind
@star:  lda #KIND_STAR
@kind:  sta obj_type,x
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
        cmp #KIND_SAUCER
        bne +
        jsr move_saucer
        jmp @touch
+       cmp #KIND_HUNTER
        bne @touch
        jsr move_hunter
@touch: jsr object_box
        jsr ship_box
        jsr overlap
        bcc @next
        jsr collide
@next:  dex
        bpl @loop
        rts

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
        lda #0
        sta new_dx
        jmp drop_bomb
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

; A bomb from (new_x, new_y), going down and new_dx sideways, if a slot is free.
drop_bomb:
        ldy #MAX_BOMBS-1
@find:  lda bomb_y,y
        beq @found
        dey
        bpl @find
        rts
@found: lda new_x
        sta bomb_x,y
        lda new_y
        sta bomb_y,y
        lda new_dx
        sta bomb_dx,y
        rts

; Moves the bombs, and checks each against the ship.
move_bombs:
        ldx #MAX_BOMBS-1
@loop:  lda bomb_y,x
        beq @next
        clc
        adc #BOMB_SPEED
        cmp #232
        bcs @gone
        sta bomb_y,x
        lda bomb_x,x
        clc
        adc bomb_dx,x
        sta bomb_x,x
        cmp #248                ; off a side
        bcs @gone
        lda hit_timer
        bne @next
        lda bomb_x,x            ; the bomb's round middle
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
        sta boss_x
        cmp #BOSS_MIN_X
        bcc @turn
        cmp #BOSS_MAX_X
        bcc @fire
@turn:  lda boss_dx
        eor #$FF
        clc
        adc #1
        sta boss_dx
@fire:  dec boss_fire           ; a volley of three bombs, spread
        bne @touch
        lda #BOSS_FIRE_DELAY
        sta boss_fire
        lda boss_x
        clc
        adc #12
        sta new_x
        lda boss_y
        clc
        adc #20
        sta new_y
        lda #$FF
        sta new_dx
        jsr drop_bomb
        lda #0
        sta new_dx
        jsr drop_bomb
        lda #1
        sta new_dx
        jsr drop_bomb
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
+       jmp start_level
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
        cmp #KIND_SAUCER
        bne +
        ldy #16
+       sty box_bw
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
@loop:  lda frame_count
        and #1
        beq +
        lda #MAX_OBJECTS-1      ; odd frames: the other way round
+       eor draw_count          ; 0 eor n = n, 7 eor n = 7 - n
        tax
        stx obj_index
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
        jmp @next
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
        wait_vblank
        lda #0
        sta PPUMASK
        rts

screen_on:
        wait_vblank
        lda #0
        sta PPUSCROLL
        sta PPUSCROLL
        lda #PPUCTRL_NMI
        sta PPUCTRL
        lda #PPUMASK_SHOW
        sta PPUMASK
        rts

load_palettes:
        ppu_addr $3F00
        ldx #0
@loop:  lda palettes,x            ; gfx/game.pal.asm
        sta PPUDATA
        inx
        cpx #32
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

; ---------------------------------------------------------------------------
; Data

; The graphics, as the designer exports them (see ../gfx/*.json).
        .include "../gfx/game.pal.asm"
        .include "../gfx/objects.msprite.asm"
        .include "../gfx/title.screen.asm"
        .include "../gfx/over.screen.asm"
        .include "../gfx/play.map.asm"

; Per kind of object (0, star, meteor, saucer, hunter, burst): its
; metasprite and what breaking or catching it scores.
KindSprite:
        .byte 0, SPR_MS_STAR, SPR_MS_METEOR, SPR_MS_SAUCER, SPR_MS_HUNTER, SPR_MS_BURST
KindPoints:
        .byte 0, 5, 1, 3, 2, 0

; Where the boss's five bursts go, from its top left corner.
BurstX: .byte 4, 20, 12, 0, 24
BurstY: .byte 2, 4, 10, 14, 12

; The sky's two screens: where they start in the map, and their nametables.
SkyLow:       .byte <bg_play_map, <(bg_play_map+BG_PLAY_WIDTH*SKY_ROWS)
SkyHigh:      .byte >bg_play_map, >(bg_play_map+BG_PLAY_WIDTH*SKY_ROWS)
SkyNametable: .byte >NT, >NT_LOW

WarningText:  .asciiz "WARNING"
LevelText:    .asciiz "LEVEL"
ClearText:    .asciiz "CLEAR"
