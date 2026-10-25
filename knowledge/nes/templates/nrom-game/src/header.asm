; header.asm - the 16-byte iNES header an emulator reads before the ROM.
; NROM-128: 1 x 16 KB of program ROM, 1 x 8 KB of tiles (CHR ROM), mapper 0.

        .org $0000
        .byte "NES", $1A
        .byte 1                 ; 16 KB program ROM banks
        .byte 1                 ; 8 KB CHR ROM banks (the tiles)
        .byte %00000001         ; mapper 0 (NROM), vertical mirroring
        .byte 0
        .dsb 8                  ; unused, zero
