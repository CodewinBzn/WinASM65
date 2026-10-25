; snippet: wait_vblank
; purpose: Macro that waits for the start of a vertical blank by polling PPUSTATUS bit 7.
; inputs: none
; outputs: none (returns just after vblank started; the vblank flag is cleared by the read)
; clobbers: flags only (BIT does not change A)
; requires: none
; vars: none
; wrap: org=$C000
; usage: Only while NMI is OFF (reset, screen off). With NMI on, use wait_frame instead:
;        polling PPUSTATUS can miss the flag when NMI runs. nes.inc already defines
;        this macro: do not define it twice.

PPUSTATUS = $2002

.macro wait_vblank
-       bit PPUSTATUS           ; bit 7 of PPUSTATUS -> N flag
        bpl -                   ; N = 0: not in vblank yet
.endmacro

; Example call (a macro is called by its name, without jsr):
wait_two_vblanks:
        wait_vblank
        wait_vblank
        rts
