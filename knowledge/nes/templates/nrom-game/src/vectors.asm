; vectors.asm - the three CPU vectors, at the top of the address space.
; NMI, RESET and IRQ are labels of game.asm: config.json makes this unit
; depend on src/game.asm, so their addresses come from game.symb.

        .org $FFFA
        .word NMI               ; once per frame, at the start of vblank
        .word RESET             ; at power-on and reset
        .word IRQ               ; not used
