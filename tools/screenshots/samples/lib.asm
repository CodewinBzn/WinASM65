; A tiny library: prints the zero-terminated string at (ptr) on a C64.
CHROUT = $FFD2
ptr = $FB
    .org $C000
print:
    ldy #0
@next:
    lda (ptr),y
    beq @done
    jsr CHROUT
    iny
    bne @next
@done:
    rts
newline:
    lda #13
    jmp CHROUT
    .export print, newline
