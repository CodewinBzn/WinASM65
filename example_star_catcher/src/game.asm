; Star Catcher - an original NES game written for WinASM65.
;
; Move the ship with the d-pad, catch the falling stars, avoid the meteors.
; Every star is a point; every ten points the sky gets busier. Three meteors
; and the game is over.

        .include "nes.inc"

; ---------------------------------------------------------------------------
; Constants

NT          = $2000             ; the nametable the game draws on
OAM         = $0200             ; sprite table, copied to the PPU every frame

STATE_TITLE = 0
STATE_PLAY  = 1
STATE_OVER  = 2

SHIP_Y      = 200               ; the ship's row, in pixels
SHIP_SPEED  = 2
SHIP_MIN_X  = 8
SHIP_MAX_X  = 232

MAX_OBJECTS = 6                 ; stars and meteors falling at once
KIND_STAR   = 1
KIND_METEOR = 2
METEOR_CHANCE = 64              ; out of 256: about one object in four
FIRST_DELAY = 60                ; frames between two objects at the start
MIN_DELAY   = 20
HIT_FRAMES  = 48                ; how long the ship blinks after a hit

; Tiles (see chr/make_chr.py)
T_SHIP      = $01               ; and $02, its right half
T_STAR      = $03               ; and $04, the twinkle
T_METEOR    = $05
T_DOT       = $06               ; and $07, background stars
T_HEART     = $0A

; Sprite attributes: the palette of each kind
PAL_SHIP    = 0
PAL_STAR    = 1
PAL_METEOR  = 2

PPUCTRL_NMI  = %10000000        ; NMI on, all tiles from pattern table 0
PPUMASK_SHOW = %00011110        ; background and sprites, left column included

HUD_SCORE   = NT+2*32+8         ; where the four digits of the score go
HUD_LIVES   = NT+2*32+25        ; and the number of lives

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
lives        .res 1
score        .res 4             ; four decimal digits, most significant first
spawn_timer  .res 1
spawn_delay  .res 1
delay        .res 1             ; game over: frames before START is accepted
hit_timer    .res 1
hud_dirty    .res 1             ; NMI redraws the score and the lives
tmp          .res 1
ptr          .res 2
obj_type     .res MAX_OBJECTS   ; 0 when the slot is free
obj_x        .res MAX_OBJECTS
obj_y        .res MAX_OBJECTS
obj_speed    .res MAX_OBJECTS

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

        lda hud_dirty
        beq @no_hud
        jsr draw_hud
        lda #0
        sta hud_dirty
@no_hud:
        lda #0                  ; writing PPUADDR moved the scroll: reset it
        sta PPUSCROLL
        sta PPUSCROLL
        lda #PPUCTRL_NMI
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
        jsr clear_nametable
        jsr draw_starfield
        lda #<TitleText
        sta ptr
        lda #>TitleText
        sta ptr+1
        jsr draw_texts
        jsr hide_sprites
        lda #120
        sta ship_x
        lda #0
        sta hit_timer
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
        jsr clear_nametable
        jsr draw_starfield
        lda #<PlayText
        sta ptr
        lda #>PlayText
        sta ptr+1
        jsr draw_texts

        lda #120
        sta ship_x
        lda #3
        sta lives
        lda #0
        sta hit_timer
        ldx #3
@score: sta score,x
        dex
        bpl @score
        ldx #MAX_OBJECTS-1
@slots: sta obj_type,x
        dex
        bpl @slots
        lda #FIRST_DELAY
        sta spawn_delay
        sta spawn_timer
        lda #STATE_PLAY
        sta state

        jsr draw_hud            ; the picture is off: the HUD can be written now
        jsr hide_sprites
        jmp screen_on

update_play:
        jsr move_ship
        jsr spawn_objects
        jsr move_objects
        lda lives
        bne +
        jmp enter_over
+       jmp draw_sprites

; The d-pad moves the ship, within the screen.
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
+       rts

; Every spawn_delay frames, a star or a meteor appears at the top.
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
        cmp #SHIP_MAX_X+1
        bcc +
        lda #SHIP_MAX_X
+       cmp #SHIP_MIN_X
        bcs +
        lda #SHIP_MIN_X
+       sta obj_x,x
        lda #16
        sta obj_y,x
        jsr random
        and #1
        clc
        adc #1
        sta obj_speed,x         ; 1 or 2 pixels per frame
        jsr random
        cmp #METEOR_CHANCE
        lda #KIND_STAR          ; LDA leaves the carry of CMP alone
        bcs +
        lda #KIND_METEOR
+       sta obj_type,x
        rts

; Moves every object down and checks it against the ship.
move_objects:
        ldx #MAX_OBJECTS-1
@loop:  lda obj_type,x
        beq @next
        lda obj_y,x
        clc
        adc obj_speed,x
        sta obj_y,x
        cmp #232
        bcc @check
        lda #0                  ; fell off the bottom of the screen
        sta obj_type,x
        jmp @next

@check: cmp #SHIP_Y-7           ; rows overlap?
        bcc @next
        cmp #SHIP_Y+8
        bcs @next
        lda obj_x,x             ; columns overlap: x + 8 > ship_x ...
        clc
        adc #8
        cmp ship_x
        bcc @next
        beq @next
        lda ship_x              ; ... and ship_x + 16 > x
        clc
        adc #16
        cmp obj_x,x
        bcc @next
        beq @next
        jsr collide
@next:  dex
        bpl @loop
        rts

; Object X touched the ship.
collide:
        lda obj_type,x
        pha
        lda #0
        sta obj_type,x
        pla
        cmp #KIND_STAR
        bne @meteor
        jsr add_point
        jmp sound_catch
@meteor:
        lda #HIT_FRAMES
        sta hit_timer
        dec lives
        lda #1
        sta hud_dirty
        jmp sound_hit

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
        lda #1
        sta hud_dirty
        lda score+3             ; every ten points, objects come faster
        bne @same
        lda spawn_delay
        cmp #MIN_DELAY+8
        bcc @same
        sec
        sbc #8
        sta spawn_delay
@same:  rts

; ---------------------------------------------------------------------------
; Game over

enter_over:
        jsr screen_off
        lda #<OverText
        sta ptr
        lda #>OverText
        sta ptr+1
        jsr draw_texts
        jsr draw_hud
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

; The ship and every object, into the sprite table.
draw_sprites:
        jsr draw_ship
        ldx #MAX_OBJECTS-1
        ldy #8                  ; after the ship's two sprites
@loop:  lda obj_type,x
        beq @hidden
        lda obj_y,x
        sta OAM,y
        lda obj_type,x
        cmp #KIND_STAR
        bne @meteor
        lda frame_count         ; stars twinkle: the tile changes every 8 frames
        and #8
        lsr a
        lsr a
        lsr a
        clc
        adc #T_STAR
        sta OAM+1,y
        lda #PAL_STAR
        jmp @attr
@meteor:
        lda #T_METEOR
        sta OAM+1,y
        lda #PAL_METEOR
@attr:  sta OAM+2,y
        lda obj_x,x
        sta OAM+3,y
        jmp @next
@hidden:
        lda #$FF                ; below the screen
        sta OAM,y
@next:  iny
        iny
        iny
        iny
        dex
        bpl @loop
        rts

; The ship, two sprites side by side. It blinks while hit_timer runs.
draw_ship:
        lda hit_timer
        beq @show
        dec hit_timer
        and #4
        beq @show
        lda #$FF
        sta OAM
        sta OAM+4
        rts
@show:  lda #SHIP_Y-1           ; a sprite is drawn one line below its Y
        sta OAM
        sta OAM+4
        lda #T_SHIP
        sta OAM+1
        lda #T_SHIP+1
        sta OAM+5
        lda #PAL_SHIP
        sta OAM+2
        sta OAM+6
        lda ship_x
        sta OAM+3
        clc
        adc #8
        sta OAM+7
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

; Turns the picture off, so that video memory can be written at any time.
screen_off:
        lda #0
        sta PPUCTRL             ; no NMI while we draw
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
@loop:  lda Palettes,x
        sta PPUDATA
        inx
        cpx #32
        bne @loop
        rts

; Fills the nametable and its attributes with zeros.
clear_nametable:
        ppu_addr NT
        lda #0
        ldy #4
        ldx #0
@loop:  sta PPUDATA
        inx
        bne @loop
        dey
        bne @loop
        rts

draw_starfield:
        ldx #STAR_COUNT-1
@loop:  bit PPUSTATUS
        lda StarHi,x
        sta PPUADDR
        lda StarLo,x
        sta PPUADDR
        txa
        and #1                  ; every other star is a bright one
        clc
        adc #T_DOT
        sta PPUDATA
        dex
        bpl @loop
        rts

; Draws a list of texts: (address, characters, 0) ... then a 0 address.
; The tiles of letters and digits are their ASCII codes.
draw_texts:
        ldy #0
@next:  lda (ptr),y
        sta tmp
        iny
        lda (ptr),y
        beq @done               ; every address is above $2000
        bit PPUSTATUS
        sta PPUADDR
        lda tmp
        sta PPUADDR
        iny
@char:  lda (ptr),y
        beq @end
        sta PPUDATA
        iny
        bne @char
@end:   iny
        bne @next
@done:  rts

; The score's four digits and the lives. Called by NMI, or with the picture off.
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

Palettes:
        .byte $0F, $30, $16, $28        ; background: white text, red heart, yellow stars
        .byte $0F, $30, $16, $28
        .byte $0F, $30, $16, $28
        .byte $0F, $30, $16, $28
        .byte $0F, $30, $11, $16        ; ship: white, blue, red
        .byte $0F, $28, $38, $30        ; stars: yellow, pale yellow, white
        .byte $0F, $07, $17, $27        ; meteors: dark brown, brown, orange
        .byte $0F, $27, $37, $30

TitleText:
        .word NT+8*32+10
        .asciiz "STAR CATCHER"
        .word NT+18*32+10
        .asciiz "PRESS START"
        .word NT+21*32+8
        .asciiz "CATCH THE STARS"
        .word NT+22*32+7
        .asciiz "AVOID THE METEORS"
        .word NT+27*32+7
        .asciiz "MADE WITH WINASM65"
        .word 0

PlayText:
        .word NT+2*32+2
        .asciiz "SCORE"
        .word NT+2*32+23
        .byte T_HEART, 0
        .word 0

OverText:
        .word NT+13*32+11
        .asciiz "GAME OVER"
        .word NT+16*32+10
        .asciiz "PRESS START"
        .word 0

; The background stars: their nametable addresses, split into a table of
; low bytes and a table of high bytes.
STAR_COUNT = 24
StarLo: .lobytes NT+4*32+25, NT+5*32+9, NT+7*32+6, NT+8*32+26, NT+11*32+3, NT+11*32+15, NT+12*32+7, NT+13*32+17, NT+14*32+9, NT+14*32+27, NT+15*32+19, NT+16*32+15, NT+17*32+3, NT+17*32+10, NT+18*32+17, NT+19*32+2, NT+20*32+24, NT+21*32+15, NT+23*32+8, NT+23*32+23, NT+24*32+14, NT+25*32+27, NT+26*32+5, NT+26*32+12
StarHi: .hibytes NT+4*32+25, NT+5*32+9, NT+7*32+6, NT+8*32+26, NT+11*32+3, NT+11*32+15, NT+12*32+7, NT+13*32+17, NT+14*32+9, NT+14*32+27, NT+15*32+19, NT+16*32+15, NT+17*32+3, NT+17*32+10, NT+18*32+17, NT+19*32+2, NT+20*32+24, NT+21*32+15, NT+23*32+8, NT+23*32+23, NT+24*32+14, NT+25*32+27, NT+26*32+5, NT+26*32+12
