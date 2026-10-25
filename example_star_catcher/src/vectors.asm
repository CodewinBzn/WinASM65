; Star Catcher - the three CPU vectors, at the top of the address space.
; NMI, RESET and IRQ are defined in game.asm: config.json makes this unit
; depend on it, so their addresses come from game.symb.

        .org $FFFA
        .word NMI               ; once per frame, at the start of the vertical blank
        .word RESET             ; at power-on and reset
        .word IRQ               ; not used
