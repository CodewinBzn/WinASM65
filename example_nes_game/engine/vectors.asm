	.org $FFFA
	.word NMI        ;when an NMI happens (once per frame if enabled) the 
                   ;processor will jump to the label NMI:
	.word RESET      ;when the processor first turns on or is reset, it will jump
                   ;to the label RESET:
	.word $0000      