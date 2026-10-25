; snippet: random_lfsr
; purpose: 8-bit pseudo-random numbers (Galois LFSR, period 255).
; inputs: rng must be NON-ZERO before the first call (0 stays 0 forever)
; outputs: A = rng = next number (1-255)
; clobbers: A, flags
; cycles: about 15
; requires: none
; vars: rng 1
; wrap: org=$C000
; usage: At reset: lda #$5A / sta rng. Better seed: count frames until the player presses START, ORA #1, store in rng.
;        Random 0-7: jsr random / and #7.  Random X position: jsr random / and #%11111000.

random:
        lda rng
        lsr a                   ; shift right, bit 0 -> carry
        bcc @no_eor
        eor #$B8                ; feedback taps (maximal length polynomial)
@no_eor:
        sta rng
        rts
