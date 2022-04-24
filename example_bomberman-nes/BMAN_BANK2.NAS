
    .ORG     $F000
    .INCLUDE "BOOM.NAS"


DUMMY:      
	.BYTE $FF,$FF,$FF,$FF,$FF,$FF,$FF,$FF,$FF
; ---------------------------------------------------------------------------

IRQ:                    
        RTI
; ---------------------------------------------------------------------------
        .WORD NMI
        .WORD RESET
        .WORD IRQ
; end of 'ROM'