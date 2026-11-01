; Rock Patrol - a small NES game, the example of the WinASM65 documentation.
;
; Rocks fall through a starry sky that scrolls down past the ship. Fly with the
; d-pad, fire with A (hold it to keep firing). A rock shot is a point; every ten
; points they fall faster and come more often. A rock that hits the ship costs
; one of its three lives; the ship then blinks for two seconds and nothing
; hurts it. The cartridge is mapper 0: 32 KB of program (data.asm is its first
; half) and 8 KB of tiles, chr/tiles.chr for both pattern tables.

        .include "nes.inc"

; ---------------------------------------------------------------------------
; Constants

NT_TOP       = $2000            ; the title and game over screens, and the sky's first screen
NT_LOW       = $2800            ; the sky's second screen (horizontal mirroring)
OAM          = $0200            ; the sprite table, copied to the PPU every frame
CTRL_NMI     = %10000000        ; NMI on, every tile from pattern table 0
MASK_SHOW    = %00011110        ; background and sprites, left column included

STATE_TITLE  = 0
STATE_PLAY   = 1
STATE_OVER   = 2

MAX_ROCKS    = 6
MAX_SHOTS    = 3
MAX_BURSTS   = 4
SHIP_MIN_X   = 8
SHIP_MAX_X   = 232
SHIP_MIN_Y   = 120
SHIP_MAX_Y   = 200
SHOT_SPEED   = 5
FIRE_DELAY   = 10               ; frames between two shots while A is held
HURT_FRAMES  = 120              ; frames the ship blinks after a hit
ROCK_BOTTOM  = 224              ; a rock this low has left the screen
SKY_HEIGHT   = 480              ; the map, two screens high, in pixels
SKY_ROWS     = 15               ; rows of metatiles in a screen
TILE_HEART   = $18

; ---------------------------------------------------------------------------
; Variables, in zero page: .res hands out addresses from $0000 on.

        .memarea $0000
frame_done   .res 1             ; set by NMI, cleared by wait_frame
frames       .res 1             ; counts frames
pad          .res 1             ; the buttons held this frame
pad_new      .res 1             ; and those pressed this frame
state        .res 1
ptr          .res 2
tmp          .res 1
tmp2         .res 1
slot         .res 1             ; the object draw_play is at
tile_pal     .res 1             ; the sprite palette put_tile uses
ms_x         .res 1             ; where draw_sprite puts a metasprite
ms_y         .res 1
oam_at       .res 1             ; the next free entry of OAM
ctrl         .res 1             ; what NMI writes to PPUCTRL
scroll_y     .res 1             ; the sky's first line on screen, 0-239 ...
sky_pos      .res 2             ; ... from the sky's position, 0 to SKY_HEIGHT-1
ship_x       .res 1
ship_y       .res 1
lives        .res 1
hurt         .res 1             ; frames left of blinking
fire_wait    .res 1
spawn_wait   .res 1
lane         .res 1             ; the next entry of RockLanes
speed        .res 1             ; how fast rocks fall now, from FallSpeed
end_wait     .res 1             ; the last life went: frames before GAME OVER
score        .res 4             ; four decimal digits, most significant first

; Objects, in RAM after the stack and the sprite table.
        .memarea $0300
rock_x       .res MAX_ROCKS
rock_y       .res MAX_ROCKS     ; 0: the slot is free
rock_clock   .res MAX_ROCKS     ; frames since it appeared, for its animation
shot_x       .res MAX_SHOTS
shot_y       .res MAX_SHOTS     ; 0: the slot is free
burst_x      .res MAX_BURSTS
burst_y      .res MAX_BURSTS
burst_clock  .res MAX_BURSTS    ; $FF: the slot is free

; ---------------------------------------------------------------------------
; Program, $C000-$FFF9

        .org $C000

; The metasprites and their animations, as the designer exports them.
        .include "../gfx/objects.msprite.asm"

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
        vblank_wait             ; the PPU needs two frames to wake up
        txa
@clear: sta $0000,x
        sta $0300,x
        sta $0400,x
        sta $0500,x
        sta $0600,x
        sta $0700,x
        inx
        bne @clear
        jsr clear_sprites
        vblank_wait
        jsr load_palettes
        lda #%00001001          ; pulse 1 and noise on
        sta APU_STATUS
        jsr enter_title

Main:
        jsr wait_frame
        jsr read_pad
        jsr clear_sprites
        lda state
        cmp #STATE_PLAY
        beq @play
        cmp #STATE_OVER
        beq @over
        jsr update_title
        jmp Main
@play:  jsr update_play
        jmp Main
@over:  jsr update_over
        jmp Main

; ---------------------------------------------------------------------------
; Interrupts

; Once a frame, at the start of the vertical blank: the sprites and the scroll.
NMI:
        pha
        lda #0
        sta OAMADDR
        lda #>OAM
        sta OAMDMA              ; the 256 bytes of OAM to the PPU
        lda ctrl                ; the nametable the sky starts in
        sta PPUCTRL
        bit PPUSTATUS
        lda #0
        sta PPUSCROLL
        lda scroll_y
        sta PPUSCROLL
        inc frames
        lda #1
        sta frame_done
        pla
IRQ:    rti

; Waits for the next NMI.
wait_frame:
        lda #0
        sta frame_done
-       lda frame_done
        beq -
        rts

; The joypad into pad (A in bit 7 ... right in bit 0), and what went down into pad_new.
read_pad:
        lda pad
        sta tmp
        lda #1
        sta JOY1
        lda #0
        sta JOY1
        ldx #8
-       lda JOY1
        lsr a                   ; the button into carry
        rol pad
        dex
        bne -
        lda tmp
        eor #$FF
        and pad
        sta pad_new
        rts

; ---------------------------------------------------------------------------
; The picture

; Picture and NMI off, at the start of a frame, before writing to the PPU.
picture_off:
        lda ctrl                ; NMI on: wait for it, so as not to cut a frame
        beq +
        jsr wait_frame
+        lda #0
        sta PPUCTRL
        sta PPUMASK
        rts

; Picture and NMI on again, from the top of the sky's first screen.
picture_on:
        vblank_wait
        lda #CTRL_NMI
        sta ctrl
        sta PPUCTRL
        bit PPUSTATUS
        lda #0
        sta PPUSCROLL
        sta PPUSCROLL
        sta scroll_y
        lda #MASK_SHOW
        sta PPUMASK
        rts

; The 32 colours: palettes, from game.pal.asm (in data.asm).
load_palettes:
        set_ppu $3F00
        ldx #0
-       lda palettes,x
        sta PPUDATA
        inx
        cpx #32
        bne -
        rts

; A whole screen (1024 bytes: tiles and attributes) from ptr into $2000.
load_screen:
        set_ppu NT_TOP
        ldx #4                  ; four pages
        ldy #0
-       lda (ptr),y
        sta PPUDATA
        iny
        bne -
        inc ptr+1
        dex
        bne -
        rts

; The sky: the map's rows 0-14 into $2000, rows 15-29 into $2800, with their
; attributes. A metatile is four tiles and a palette (bg_metatile_* in play.map.asm).
draw_sky:
        set_ppu NT_TOP
        lda #0
        jsr draw_sky_screen
        set_ppu NT_LOW
        lda #SKY_ROWS
        ; falls through

; The 15 rows of metatiles from row A, then their attribute bytes.
draw_sky_screen:
        sta tmp2                ; the first row
        lda #<bg_play_map       ; ptr: the map's row A, 16 metatiles a row
        sta ptr
        lda #>bg_play_map
        sta ptr+1
        lda tmp2
        asl a
        asl a
        asl a
        asl a
        bcc +
        inc ptr+1
+       clc
        adc ptr
        sta ptr
        bcc +
        inc ptr+1
+       lda #SKY_ROWS
        sta tmp
@row:   ldy #0                  ; the top halves of the row's metatiles
-       lda (ptr),y
        tax
        lda bg_metatile_tl,x
        sta PPUDATA
        lda bg_metatile_tr,x
        sta PPUDATA
        iny
        cpy #16
        bne -
        ldy #0                  ; then their bottom halves
-       lda (ptr),y
        tax
        lda bg_metatile_bl,x
        sta PPUDATA
        lda bg_metatile_br,x
        sta PPUDATA
        iny
        cpy #16
        bne -
        lda ptr                 ; the next row
        clc
        adc #16
        sta ptr
        bcc +
        inc ptr+1
+       dec tmp
        bne @row

        lda ptr                 ; back to the screen's first row
        sec
        sbc #<(SKY_ROWS*16)
        sta ptr
        lda ptr+1
        sbc #>(SKY_ROWS*16)
        sta ptr+1
        lda #0                  ; 64 attribute bytes, each for 2 x 2 metatiles
        sta tmp
@attr:  lda tmp                 ; the byte's top left metatile: row 2*(n/8), column 2*(n%8)
        and #%00111000
        asl a
        asl a
        sta tmp2                ; (n/8) * 32: two rows of 16
        lda tmp
        and #%00000111
        asl a
        ora tmp2
        tay
        lda #0
        sta tmp2
        lda tmp                 ; the last row of bytes has only the top metatiles
        cmp #56
        bcs @top
        tya                     ; bottom right, bottom left
        clc
        adc #17
        tay
        jsr attr_bits
        dey
        jsr attr_bits
        tya
        sec
        sbc #15
        tay
        jmp @tops
@top:   asl tmp2                ; no bottom row: palette 0
        asl tmp2
        asl tmp2
        asl tmp2
        iny
@tops:  jsr attr_bits           ; top right, top left
        dey
        jsr attr_bits
        lda tmp2
        sta PPUDATA
        inc tmp
        lda tmp
        cmp #64
        bne @attr
        rts

; tmp2 = tmp2 * 4 + the palette of the metatile at ptr + Y.
attr_bits:
        lda (ptr),y
        tax
        lda tmp2
        asl a
        asl a
        ora bg_metatile_pal,x
        sta tmp2
        rts

; ---------------------------------------------------------------------------
; Sprites

; Every sprite of OAM off screen, and OAM empty.
clear_sprites:
        lda #$FF
        ldx #0
-       sta OAM,x
        inx
        bne -
        stx oam_at
        rts

; Metasprite A at (ms_x, ms_y): its tiles into OAM. Each tile of an exported
; metasprite is four bytes, y, tile, attributes, x; a y of $80 ends it.
draw_sprite:
        tax
        lda spr_metasprites_lo,x
        sta ptr
        lda spr_metasprites_hi,x
        sta ptr+1
        ldy #0
        ldx oam_at
@tile:  lda (ptr),y
        cmp #$80
        beq @done
        clc
        adc ms_y
        sta OAM,x
        iny
        lda (ptr),y
        sta OAM+1,x
        iny
        lda (ptr),y
        sta OAM+2,x
        iny
        lda (ptr),y
        clc
        adc ms_x
        sta OAM+3,x
        iny
        inx
        inx
        inx
        inx
        bne @tile
@done:  stx oam_at
        rts

; Where an animation is after A frames: in A, its metasprite. An exported
; animation is pairs of (metasprite, frames), then $FF and 1 if it loops.
; Carry set: it did not loop and is over.
animate:
        sta tmp
@again: ldy #0
@frame: lda (ptr),y
        cmp #$FF
        beq @end
        sta tmp2
        iny
        lda tmp
        sec
        sbc (ptr),y
        bcc @this               ; within this frame's duration
        sta tmp
        iny
        bne @frame
@this:  lda tmp2
        clc
        rts
@end:   iny
        lda (ptr),y
        bne @again              ; it loops: on from its first frame
        sec
        rts

; One sprite: tile A at (ms_x, ms_y), in sprite palette tile_pal.
put_tile:
        ldx oam_at
        sta OAM+1,x
        lda ms_y
        sta OAM,x
        lda tile_pal
        sta OAM+2,x
        lda ms_x
        sta OAM+3,x
        inx
        inx
        inx
        inx
        stx oam_at
        rts

; The score's four digits at (ms_x, ms_y), white. The font is at the characters' codes.
draw_score:
        lda #PAL_SHIP-4
        sta tile_pal
        ldy #0
-       lda score,y
        clc
        adc #'0'
        sty tmp
        jsr put_tile
        ldy tmp
        lda ms_x
        clc
        adc #8
        sta ms_x
        iny
        cpy #4
        bne -
        rts

; ---------------------------------------------------------------------------
; Title

enter_title:
        jsr picture_off
        lda #<scr_title
        sta ptr
        lda #>scr_title
        sta ptr+1
        jsr load_screen
        lda #STATE_TITLE
        sta state
        jmp picture_on

update_title:
        lda pad_new
        and #PAD_START
        beq +
        jmp enter_play
+       rts

; ---------------------------------------------------------------------------
; Game over

enter_over:
        jsr picture_off
        lda #<scr_over
        sta ptr
        lda #>scr_over
        sta ptr+1
        jsr load_screen
        lda #STATE_OVER
        sta state
        jmp picture_on

; The score under GAME OVER, and START back to the title.
update_over:
        lda #112
        sta ms_x
        lda #111
        sta ms_y
        jsr draw_score
        lda pad_new
        and #PAD_START
        beq +
        jmp enter_title
+       rts

; ---------------------------------------------------------------------------
; Playing

enter_play:
        jsr picture_off
        jsr draw_sky
        lda #0
        sta sky_pos
        sta sky_pos+1
        sta hurt
        sta fire_wait
        sta lane
        sta end_wait
        ldx #3
-       sta score,x
        dex
        bpl -
        ldx #MAX_ROCKS-1
-       sta rock_y,x
        dex
        bpl -
        ldx #MAX_SHOTS-1
-       sta shot_y,x
        dex
        bpl -
        lda #$FF
        ldx #MAX_BURSTS-1
-       sta burst_clock,x
        dex
        bpl -
        lda #3
        sta lives
        lda #120
        sta ship_x
        lda #SHIP_MAX_Y
        sta ship_y
        lda #60
        sta spawn_wait
        jsr set_speed
        lda #STATE_PLAY
        sta state
        jmp picture_on

update_play:
        jsr scroll_sky
        lda lives
        beq +
        jsr move_ship
        jsr fire
+       jsr move_shots
        jsr spawn_rock
        jsr move_rocks
        jsr hit_rocks
        jsr hit_ship
        jsr draw_play
        lda lives               ; the last life went: GAME OVER a moment later
        bne +
        dec end_wait
        bne +
        jmp enter_over
+       rts

; The sky moves one line down a frame: its position goes back by one,
; from 0 round to SKY_HEIGHT-1; the second screen is $2800.
scroll_sky:
        lda sky_pos
        bne +
        lda sky_pos+1
        bne ++
        lda #<SKY_HEIGHT        ; at 0: on to the bottom of the second screen
        sta sky_pos
        lda #>SKY_HEIGHT
        sta sky_pos+1
        jmp scroll_sky
++      dec sky_pos+1
+       dec sky_pos
        lda sky_pos+1           ; under 240, the first screen
        bne @low
        lda sky_pos
        cmp #240
        bcs @low
        sta scroll_y
        lda #CTRL_NMI
        sta ctrl
        rts
@low:   lda sky_pos
        sec
        sbc #240
        sta scroll_y
        lda #CTRL_NMI|2         ; nametable 2: $2800
        sta ctrl
        rts

; The d-pad moves the ship two pixels a frame, in the lower half of the screen.
move_ship:
        lda pad
        and #PAD_LEFT
        beq +
        lda ship_x
        cmp #SHIP_MIN_X+2
        bcc +
        sbc #2
        sta ship_x
+       lda pad
        and #PAD_RIGHT
        beq +
        lda ship_x
        cmp #SHIP_MAX_X-1
        bcs +
        adc #2
        sta ship_x
+       lda pad
        and #PAD_UP
        beq +
        lda ship_y
        cmp #SHIP_MIN_Y+2
        bcc +
        sbc #2
        sta ship_y
+       lda pad
        and #PAD_DOWN
        beq +
        lda ship_y
        cmp #SHIP_MAX_Y-1
        bcs +
        adc #2
        sta ship_y
+       rts

; A fires a shot from the ship's nose; held, another every FIRE_DELAY frames.
fire:
        lda fire_wait
        beq +
        dec fire_wait
+       lda pad
        and #PAD_A
        beq @done
        lda pad_new
        and #PAD_A
        bne @free               ; just pressed: at once
        lda fire_wait
        bne @done
@free:  ldx #MAX_SHOTS-1
-       lda shot_y,x
        beq @shoot
        dex
        bpl -
@done:  rts
@shoot: lda ship_x
        clc
        adc #4
        sta shot_x,x
        lda ship_y
        sec
        sbc #6
        sta shot_y,x
        lda #FIRE_DELAY
        sta fire_wait
        lda #%10000110          ; a short beep that falls: duty 1/2, decaying
        sta SQ1_VOL
        lda #%10111001          ; sweep down
        sta SQ1_SWEEP
        lda #$60
        sta SQ1_LO
        lda #%00001000
        sta SQ1_HI
        rts

move_shots:
        ldx #MAX_SHOTS-1
@shot:  lda shot_y,x
        beq @next
        sec
        sbc #SHOT_SPEED
        bcc @gone
        cmp #8
        bcs +
@gone:  lda #0                  ; off the top
+       sta shot_y,x
@next:  dex
        bpl @shot
        rts

; Every spawn_wait frames a rock appears at the top, in the next lane of RockLanes.
spawn_rock:
        dec spawn_wait
        bne @done
        ldx speed
        lda SpawnGap,x
        sta spawn_wait
        ldx #MAX_ROCKS-1
-       lda rock_y,x
        beq @new
        dex
        bpl -
@done:  rts
@new:   ldy lane
        lda RockLanes,y
        sta rock_x,x
        iny
        tya
        and #31
        sta lane
        lda #8
        sta rock_y,x
        lda #0
        sta rock_clock,x
        rts

; The rocks fall FallSpeed pixels a frame, by the score's tens.
move_rocks:
        ldx #MAX_ROCKS-1
@rock:  lda rock_y,x
        beq @next
        inc rock_clock,x
        ldy speed
        clc
        adc FallSpeed,y
        cmp #ROCK_BOTTOM
        bcc +
        lda #0                  ; out at the bottom
+       sta rock_y,x
@next:  dex
        bpl @rock
        rts

; A shot within a rock's 16 x 16 square breaks it: a burst, and a point.
hit_rocks:
        ldx #MAX_ROCKS-1
@rock:  lda rock_y,x
        beq @next
        ldy #MAX_SHOTS-1
@shot:  lda shot_y,y
        beq @miss
        lda shot_x,y            ; the shot's middle, x + 4, within the rock's 16 pixels
        clc
        adc #4
        sec
        sbc rock_x,x
        cmp #16
        bcs @miss
        lda shot_y,y
        clc
        adc #4
        sec
        sbc rock_y,x
        cmp #16
        bcs @miss
        lda #0                  ; hit: both go
        sta shot_y,y
        jsr break_rock
        jsr add_point
        jmp @next
@miss:  dey
        bpl @shot
@next:  dex
        bpl @rock
        rts

; Rock X goes, and a burst takes its place.
break_rock:
        lda rock_x,x
        sta tmp
        lda rock_y,x
        sta tmp2
        lda #0
        sta rock_y,x
        ; falls through

; A burst at (tmp, tmp2), with its noise, in a free slot if there is one.
burst:
        lda #%00000101          ; noise, decaying
        sta NOISE_VOL
        lda #$0C
        sta NOISE_LO
        lda #%00011000
        sta NOISE_HI
        ldy #MAX_BURSTS-1
-       lda burst_clock,y
        cmp #$FF
        beq +
        dey
        bpl -
        rts
+       lda tmp
        sta burst_x,y
        lda tmp2
        sta burst_y,y
        lda #0
        sta burst_clock,y
        rts

; One more point: the four digits count up, and every ten points rocks fall faster.
add_point:
        stx tmp
        ldx #3
-       inc score,x
        lda score,x
        cmp #10
        bcc +
        lda #0
        sta score,x
        dex
        bpl -
+       ldx tmp
        ; falls through

; speed: the score's tens, up to the last entry of FallSpeed.
set_speed:
        lda score+1             ; hundreds or more: the fastest
        ora score
        bne @top
        lda score+2
        cmp #7
        bcc +
@top:   lda #7
+       sta speed
        rts

; A rock that touches the ship costs a life, unless it is blinking.
hit_ship:
        lda lives
        beq @done
        lda hurt
        beq +
        dec hurt
@done:  rts
+       ldx #MAX_ROCKS-1
@rock:  lda rock_y,x
        beq @next
        lda rock_x,x            ; within 12 pixels across and down
        sec
        sbc ship_x
        clc
        adc #12
        cmp #24
        bcs @next
        lda rock_y,x
        sec
        sbc ship_y
        clc
        adc #12
        cmp #24
        bcs @next
        jsr break_rock
        lda #HURT_FRAMES
        sta hurt
        dec lives
        bne @done
        lda ship_x              ; the last one: the ship bursts too
        sta tmp
        lda ship_y
        sta tmp2
        jsr burst
        lda #90
        sta end_wait
        rts
@next:  dex
        bpl @rock
        rts

; Everything that moves, as sprites: the sky scrolls, so the score and the
; lives are sprites too.
draw_play:
        lda #16                 ; the score, top left
        sta ms_x
        sta ms_y
        jsr draw_score
        lda #208                ; a red heart for each life, top right
        sta ms_x
        lda #PAL_BURST-4
        sta tile_pal
        ldy lives
        beq @ship
-       sty tmp2
        lda #TILE_HEART
        jsr put_tile
        lda ms_x
        clc
        adc #10
        sta ms_x
        ldy tmp2
        dey
        bne -
@ship:  lda lives               ; the ship, blinking while it is hurt
        beq @shots
        lda hurt
        and #4
        bne @shots
        lda ship_x
        sta ms_x
        lda ship_y
        sta ms_y
        lda #SPR_MS_SHIP
        jsr draw_sprite
@shots: ldx #MAX_SHOTS-1
-       lda shot_y,x
        beq +
        sta ms_y
        lda shot_x,x
        sta ms_x
        stx slot
        lda #SPR_MS_SHOT
        jsr draw_sprite
        ldx slot
+       dex
        bpl -
        ldx #MAX_ROCKS-1        ; the rocks, turning
@rock:  lda rock_y,x
        beq @nextr
        sta ms_y
        lda rock_x,x
        sta ms_x
        stx slot
        lda #<spr_rock_anim
        sta ptr
        lda #>spr_rock_anim
        sta ptr+1
        lda rock_clock,x
        jsr animate
        jsr draw_sprite
        ldx slot
@nextr: dex
        bpl @rock
        ldx #MAX_BURSTS-1       ; the bursts, until their animation is over
@burst: lda burst_clock,x
        cmp #$FF
        beq @nextb
        inc burst_clock,x
        lda burst_x,x
        sta ms_x
        lda burst_y,x
        sta ms_y
        stx slot
        lda #<spr_burst_anim
        sta ptr
        lda #>spr_burst_anim
        sta ptr+1
        lda burst_clock,x
        jsr animate
        bcs @over
        jsr draw_sprite
        ldx slot
        jmp @nextb
@over:  ldx slot
        lda #$FF
        sta burst_clock,x
@nextb: dex
        bpl @burst
        rts
