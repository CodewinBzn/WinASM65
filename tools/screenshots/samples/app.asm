; The program: imports print and newline from the library.
ptr = $FB
    .import print
    .import newline
    .import clear_screen
    .org $0801
start:
    jsr clear_screen
    lda #<message
    sta ptr
    lda #>message
    sta ptr+1
    jsr print
    jsr newline
    rts
message:
    .asciiz "HELLO FROM WINASM65"
