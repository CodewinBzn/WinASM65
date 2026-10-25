; snippet: read_joypad
; purpose: Reads joypad 1: buttons held now and buttons that went down this frame.
; inputs: none
; outputs: buttons = held; pressed = newly pressed (held now, not last frame). Bit 7 A, 6 B, 5 Select, 4 Start, 3 Up, 2 Down, 1 Left, 0 Right
; clobbers: A, X
; cycles: about 160
; requires: nes.inc
; vars: buttons 1, prev_buttons 1, pressed 1
; wrap: org=$C000
; usage: jsr read_joypad once per frame (after wait_frame). Then:
;          lda buttons / and #BUTTON_RIGHT / beq @not_right   (held: move every frame)
;          lda pressed / and #BUTTON_A / beq @not_a           (pressed: once per press)

read_joypad:
        lda buttons
        sta prev_buttons
        lda #1
        sta JOY1                ; strobe on: latch the buttons
        lda #0
        sta JOY1                ; strobe off: the 8 bits can be read
        ldx #8
@bit:   lda JOY1                ; order: A, B, Select, Start, Up, Down, Left, Right
        lsr a                   ; bit 0 = this button -> carry
        rol buttons             ; shift it in; after 8 reads A is bit 7
        dex
        bne @bit
        lda prev_buttons        ; pressed = buttons AND NOT prev_buttons
        eor #$FF
        and buttons
        sta pressed
        rts
