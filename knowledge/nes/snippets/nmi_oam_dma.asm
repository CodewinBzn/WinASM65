; snippet: nmi_oam_dma
; purpose: The NMI handler: copies the sprite table $0200-$02FF to the PPU (OAM DMA), resets the scroll, signals the frame.
; inputs: OAM page $0200 filled by the game during the frame
; outputs: frame_ready = 1 (wait_frame returns); sprites updated on screen
; clobbers: nothing (A, X, Y saved and restored)
; cycles: OAM DMA takes 513-514 CPU cycles of the ~2273 cycles of vblank
; requires: nes.inc
; vars: frame_ready 1
; wrap: org=$C000
; usage: label NMI must be in .word NMI of vectors.asm; NMI runs only when PPUCTRL bit 7 is set.
;        Put PPUADDR/PPUDATA writes (score, text changes) where the comment says, before the scroll.

NMI:
        pha                     ; save A, X, Y
        txa
        pha
        tya
        pha

        lda #$00
        sta OAMADDR             ; start at sprite 0
        lda #$02
        sta OAMDMA              ; copy page $0200 to the PPU's sprite memory

        ; --- video memory updates go here (keep them short) ---

        lda #0                  ; PPUADDR writes changed the scroll: set it again
        sta PPUSCROLL           ; X
        sta PPUSCROLL           ; Y
        lda #%10000000          ; keep NMI on, nametable 0
        sta PPUCTRL

        lda #1
        sta frame_ready
        pla                     ; restore Y, X, A
        tay
        pla
        tax
        pla
        rti

IRQ:
        rti                     ; IRQ is not used
