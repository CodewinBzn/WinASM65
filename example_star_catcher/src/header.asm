; Star Catcher - the iNES header: the 16 bytes an emulator reads before the ROM.

        .org $0000
        .byte "NES", $1A
        .byte 1                 ; 1 x 16 KB of program ROM
        .byte 1                 ; 1 x 8 KB of character ROM (the tiles)
        .byte %00000001         ; mapper 0 (NROM), vertical mirroring
        .byte 0
        .dsb 8                  ; unused, zero
