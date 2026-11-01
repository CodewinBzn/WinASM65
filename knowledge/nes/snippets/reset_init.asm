; snippet: reset_init
; purpose: The RESET routine: CPU setup, stack, screen off, clear RAM, hide sprites, wait two vblanks.
; inputs: none (the CPU jumps here at power-on through the RESET vector)
; outputs: RAM $0000-$07FF = 0 except OAM page $0200 = $FF (all sprites hidden); PPU warmed up, screen still off
; clobbers: A, X, stack pointer
; cycles: about 60,000 (two frames waiting for the PPU)
; requires: nes.inc
; vars: none
; wrap: org=$C000
; usage: label RESET must be in .word RESET of vectors.asm. After it: load palettes, draw the screen, then turn NMI and the screen on.

RESET:
        sei                     ; ignore IRQs
        cld                     ; no decimal mode (the NES CPU has none anyway)
        ldx #$40
        stx APU_FRAME           ; disable the APU frame IRQ
        ldx #$FF
        txs                     ; stack pointer = $FF (stack is $0100-$01FF)
        inx                     ; X = 0
        stx PPUCTRL             ; NMI off
        stx PPUMASK             ; rendering off
        stx DMC_FREQ            ; DMC IRQ off

        wait_vblank             ; first vblank: the PPU is waking up

        lda #0
@clear: sta $0000,x             ; clear all 2 KB of RAM
        sta $0100,x
        sta $0300,x
        sta $0400,x
        sta $0500,x
        sta $0600,x
        sta $0700,x
        inx
        bne @clear

        lda #$FF                ; OAM copy at $0200: Y = $FF hides a sprite
@oam:   sta $0200,x
        inx
        bne @oam

        wait_vblank             ; second vblank: the PPU is ready
        ; Next: load palettes, fill the nametable, then
        ;   lda #%10000000 / sta PPUCTRL   (NMI on)
        ;   lda #%00011110 / sta PPUMASK   (show background and sprites)
