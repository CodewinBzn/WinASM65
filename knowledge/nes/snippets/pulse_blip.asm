; snippet: pulse_blip
; purpose: Turns the APU channels on, and plays a short beep on pulse channel 1.
; inputs: none
; outputs: sound_init: APU_STATUS enables pulse 1, pulse 2, triangle, noise. play_blip: a ~1/6 s note (A4, 440 Hz)
; clobbers: A
; cycles: about 20
; requires: nes.inc
; vars: none
; wrap: org=$C000
; usage: jsr sound_init once at reset. Then jsr play_blip on an event, for example:
;          lda pressed / and #BUTTON_A / beq @no_beep / jsr play_blip / @no_beep:
;        Pitch: period = 1789773 / (16 * frequency) - 1. Low 8 bits -> SQ1_LO, high 3 bits -> SQ1_HI bits 2-0.
;        Writing SQ1_HI (re)starts the note, so write it last.

sound_init:
        lda #%00001111          ; bit 0 pulse 1, bit 1 pulse 2, bit 2 triangle, bit 3 noise
        sta APU_STATUS
        lda #$40
        sta APU_FRAME           ; 4-step sequence, no frame IRQ
        rts

play_blip:
        lda #%10011111          ; duty 50% (bits 7-6 = 10), length counter on (bit 5 = 0), constant volume (bit 4), volume 15
        sta SQ1_VOL
        lda #%00001000          ; sweep off (negate set so the sweep unit never mutes low notes)
        sta SQ1_SWEEP
        lda #$FD                ; period $0FD = 253 -> 440 Hz
        sta SQ1_LO
        lda #%00010000          ; length index 2 (20 half-frames, ~1/6 s), period high bits 0
        sta SQ1_HI              ; starts the note
        rts
