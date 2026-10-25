; snippet: compare16
; purpose: Compares two unsigned 16-bit numbers: is value16 >= other16?
; inputs: value16, other16 (2 bytes each, low byte first)
; outputs: carry set if value16 >= other16, clear if value16 < other16; Z set if equal
; clobbers: A, flags
; cycles: about 20
; requires: none
; vars: value16 2, other16 2
; wrap: org=$C000
; usage: jsr compare16 / bcs @greater_or_equal / ...less...
;        8-bit reminder: CMP sets carry when A >= operand (unsigned). BCS = ">=", BCC = "<".

compare16:
        lda value16+1           ; the high bytes decide, unless they are equal
        cmp other16+1
        bne @done               ; carry already says which is bigger
        lda value16
        cmp other16             ; equal high bytes: the low bytes decide
@done:  rts
