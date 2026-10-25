; snippet: add16
; purpose: 16-bit additions and subtraction: value16 += 8-bit A, value16 += other16, value16 -= other16.
; inputs: value16 (2 bytes, low byte first), A or other16 (2 bytes)
; outputs: value16 updated; carry set after add16_* if the result overflowed $FFFF, carry clear after sub16 if it went below 0
; clobbers: A, flags
; cycles: about 20 each
; requires: none
; vars: value16 2, other16 2
; wrap: org=$C000
; usage: Low byte first, always: CLC before ADC (SEC before SBC), then the high byte with the carry.
;        Typical use: a 16-bit score, a sub-pixel position (high byte = pixel, low byte = fraction).

; value16 = value16 + A (A unsigned 0-255)
add16_8:
        clc
        adc value16             ; low byte
        sta value16
        lda value16+1
        adc #0                  ; add the carry to the high byte
        sta value16+1
        rts

; value16 = value16 + other16
add16_16:
        clc
        lda value16
        adc other16
        sta value16
        lda value16+1
        adc other16+1
        sta value16+1
        rts

; value16 = value16 - other16
sub16_16:
        sec
        lda value16
        sbc other16
        sta value16
        lda value16+1
        sbc other16+1
        sta value16+1
        rts
