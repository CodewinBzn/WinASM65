; snippet: move_sprite
; purpose: Moves the player with the d-pad (1 pixel per frame, kept on screen) and writes sprite 0 into OAM.
; inputs: buttons (from read_joypad), player_x, player_y
; outputs: player_x, player_y changed; OAM sprite 0 = (Y, tile, attributes, X)
; clobbers: A
; requires: nes.inc, read_joypad
; vars: player_x 1, player_y 1
; wrap: org=$C000
; usage: MainLoop: jsr wait_frame / jsr read_joypad / jsr move_player / jmp MainLoop
;        Speed: replace each inc/dec by clc/adc #2 or sec/sbc #2 for 2 pixels per frame.

PLAYER_TILE = $01
MIN_X = 0
MAX_X = 248                     ; 256 - 8: the sprite stays fully visible
MIN_Y = 8
MAX_Y = 224

move_player:
        lda buttons
        and #BUTTON_RIGHT
        beq @not_right
        lda player_x
        cmp #MAX_X
        bcs @not_right          ; already at the right edge
        inc player_x
@not_right:
        lda buttons
        and #BUTTON_LEFT
        beq @not_left
        lda player_x
        cmp #MIN_X+1
        bcc @not_left           ; already at the left edge
        dec player_x
@not_left:
        lda buttons
        and #BUTTON_DOWN
        beq @not_down
        lda player_y
        cmp #MAX_Y
        bcs @not_down
        inc player_y            ; Y grows downwards
@not_down:
        lda buttons
        and #BUTTON_UP
        beq @not_up
        lda player_y
        cmp #MIN_Y+1
        bcc @not_up
        dec player_y
@not_up:
        ; sprite 0 in OAM: byte 0 Y, 1 tile, 2 attributes, 3 X
        lda player_y
        sta $0200
        lda #PLAYER_TILE
        sta $0201
        lda #0                  ; palette 0, no flip, in front of the background
        sta $0202
        lda player_x
        sta $0203
        rts
