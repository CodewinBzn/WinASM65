; game.asm - a minimal NES game: a text and one sprite (the ship).
; Build: build_project with this project's config.json -> game.nes (24,592 bytes).
; Tiles (chr/tiles.chr): $00 blank, $01-$02 ship (left, right half), $03-$04 star,
; $05 meteor, $06-$07 small dots, $08 burst, $0A heart. Letters and digits are at
; their ASCII codes: "A" = $41, "0" = $30, " " = $20 (blank). UPPERCASE only.

        .include "nes.inc"

; ---------------------------------------------------------------------------
; Constants

OAM          = $0200            ; sprite table in RAM, copied to the PPU every NMI
PPUCTRL_NMI  = %10000000        ; NMI on, all tiles from pattern table 0
PPUMASK_SHOW = %00011110        ; show background and sprites, left column too
T_SHIP       = $01              ; tile of the player sprite

; ---------------------------------------------------------------------------
; Variables in zero page: .res gives each name the next free address from $0000.

        .memarea $0000
frame_ready  .res 1             ; set to 1 by NMI, cleared by wait_frame
buttons      .res 1             ; buttons held this frame (see BUTTON_* in nes.inc)
prev_buttons .res 1
pressed      .res 1             ; buttons that went down this frame
player_x     .res 1             ; player sprite position, in pixels
player_y     .res 1

; ---------------------------------------------------------------------------
; Start-up: runs once at power-on.

        .org $C000

RESET:
        sei                     ; no IRQ
        cld                     ; no decimal mode
        ldx #$40
        stx APU_FRAME           ; no APU frame IRQ
        ldx #$FF
        txs                     ; stack at $01FF
        inx                     ; X = 0
        stx PPUCTRL             ; NMI off
        stx PPUMASK             ; screen off
        stx DMC_FREQ
        wait_vblank             ; the PPU needs two vblanks to warm up

        lda #0
@clear: sta $0000,x             ; clear the 2 KB of RAM
        sta $0100,x
        sta $0200,x
        sta $0300,x
        sta $0400,x
        sta $0500,x
        sta $0600,x
        sta $0700,x
        inx
        bne @clear
        jsr hide_sprites        ; page $0200 (OAM): every sprite off screen
        wait_vblank

        jsr load_palettes       ; the screen is off: video memory can be written now
        jsr clear_nametable
        jsr draw_title

        lda #120                ; the player starts near the centre
        sta player_x
        lda #100
        sta player_y
        jsr update_sprites

        wait_vblank             ; turn the screen on at the start of a vblank
        lda #0
        sta PPUSCROLL           ; scroll X = 0
        sta PPUSCROLL           ; scroll Y = 0
        lda #PPUCTRL_NMI
        sta PPUCTRL
        lda #PPUMASK_SHOW
        sta PPUMASK

; ---------------------------------------------------------------------------
; Main loop: once per frame.

MainLoop:
        jsr wait_frame          ; wait for NMI (one frame = 1/60 s)
        jsr read_joypad         ; buttons, pressed
        ; --- game logic goes here: read buttons, change variables ---

        jsr update_sprites      ; variables -> OAM (sent to the PPU by the next NMI)
        jmp MainLoop

; ---------------------------------------------------------------------------
; Interrupts

NMI:
        pha                     ; save A, X, Y
        txa
        pha
        tya
        pha

        lda #0                  ; copy OAM ($0200-$02FF) to the PPU's sprite memory
        sta OAMADDR
        lda #>OAM
        sta OAMDMA

        ; --- video memory writes (PPUADDR/PPUDATA) go here, before the scroll ---

        lda #0                  ; PPUADDR writes move the scroll: set it again
        sta PPUSCROLL
        sta PPUSCROLL
        lda #PPUCTRL_NMI
        sta PPUCTRL

        lda #1
        sta frame_ready
        pla                     ; restore Y, X, A
        tay
        pla
        tax
        pla
IRQ:
        rti

; ---------------------------------------------------------------------------
; Subroutines

; Waits for the next NMI.
wait_frame:
        lda #0
        sta frame_ready
-       lda frame_ready
        beq -
        rts

; Reads joypad 1. buttons = held now, pressed = went down this frame.
read_joypad:
        lda buttons
        sta prev_buttons
        lda #1                  ; strobe: latch the 8 buttons
        sta JOY1
        lda #0
        sta JOY1
        ldx #8
@bit:   lda JOY1                ; A, B, Select, Start, Up, Down, Left, Right
        lsr a                   ; bit 0 -> carry
        rol buttons             ; carry -> buttons (A ends in bit 7)
        dex
        bne @bit
        lda prev_buttons
        eor #$FF
        and buttons
        sta pressed
        rts

; Writes the player sprite (sprite 0) into OAM: Y, tile, attributes, X.
update_sprites:
        lda player_y
        sta OAM+0               ; Y (the sprite shows one line lower)
        lda #T_SHIP
        sta OAM+1               ; tile
        lda #%00000000
        sta OAM+2               ; attributes: palette 0, no flip, in front
        lda player_x
        sta OAM+3               ; X
        rts

; Puts all 64 sprites below the screen (Y = $FF).
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

; Copies the 32 colours of Palettes to $3F00. Screen off or in vblank only.
load_palettes:
        ppu_addr PAL_ADDR
        ldx #0
@loop:  lda Palettes,x
        sta PPUDATA
        inx
        cpx #32
        bne @loop
        rts

; Fills nametable 0 ($2000-$23FF: tiles and attributes) with 0. Screen off only.
clear_nametable:
        ppu_addr NAMETABLE0
        lda #0
        ldy #4
        ldx #0
@loop:  sta PPUDATA
        inx
        bne @loop
        dey
        bne @loop
        rts

; Writes TitleText at row 10, column 5. Screen off or in vblank only.
draw_title:
        ppu_addr NAMETABLE0+10*32+5
        ldx #0
@loop:  lda TitleText,x
        beq @done               ; 0 ends the text
        sta PPUDATA
        inx
        bne @loop
@done:  rts

; ---------------------------------------------------------------------------
; Data

Palettes:
        .byte $0F, $30, $16, $28        ; background 0: byte 0 = screen background colour ($0F black)
        .byte $0F, $30, $16, $28        ; background 1
        .byte $0F, $30, $16, $28        ; background 2
        .byte $0F, $30, $16, $28        ; background 3
        .byte $0F, $30, $11, $16        ; sprite 0: byte 16 is a mirror of byte 0: keep them equal
        .byte $0F, $28, $38, $30        ; sprite 1: yellows
        .byte $0F, $07, $17, $27        ; sprite 2: browns
        .byte $0F, $27, $37, $30        ; sprite 3

TitleText:
        .asciiz "GAME ON"
