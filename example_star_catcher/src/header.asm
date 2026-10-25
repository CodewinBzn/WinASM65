; Star Catcher - the iNES header: the 16 bytes an emulator reads before the ROM.

        .org $0000
        .byte "NES", $1A
        .byte 1                 ; 1 x 16 KB of program ROM
        .byte 1                 ; 1 x 8 KB of character ROM (the tiles)
        .byte %00000000         ; mapper 0 (NROM), horizontal mirroring: $2000 and $2800
                                ; are two screens one above the other, for vertical scrolling
        .byte 0
        .dsb 8                  ; unused, zero
