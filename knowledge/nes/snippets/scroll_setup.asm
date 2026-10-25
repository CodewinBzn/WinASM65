; snippet: scroll_setup
; purpose: Sets the background scroll every frame, at the end of NMI (horizontal scroll by 1 pixel per frame shown).
; inputs: scroll_x, scroll_y (pixels), scroll_nt (0-3: base nametable, PPUCTRL bits 1-0)
; outputs: PPUSCROLL and PPUCTRL written
; clobbers: A
; requires: nes.inc
; vars: scroll_x 1, scroll_y 1, scroll_nt 1
; wrap: org=$C000
; usage: In NMI, AFTER every PPUADDR/PPUDATA write (they overwrite the scroll):
;          jsr set_scroll
;        In the main loop, change scroll_x / scroll_y (inc scroll_x = scroll 1 pixel to the right).
;        scroll_y must stay 0-239. With vertical mirroring (template header), scrolling X past 255
;        shows nametable 1 ($2400): flip scroll_nt bit 0 when scroll_x wraps.

set_scroll:
        bit PPUSTATUS           ; reset the write latch shared by PPUSCROLL and PPUADDR
        lda scroll_x
        sta PPUSCROLL           ; first write: X
        lda scroll_y
        sta PPUSCROLL           ; second write: Y
        lda scroll_nt
        and #%00000011
        ora #%10000000          ; NMI on + base nametable
        sta PPUCTRL
        rts

; Main-loop side: scroll right by one pixel, switching nametable on wrap.
scroll_right:
        inc scroll_x
        bne @same
        lda scroll_nt
        eor #1                  ; 255 -> 0: continue into the other nametable
        sta scroll_nt
@same:  rts
