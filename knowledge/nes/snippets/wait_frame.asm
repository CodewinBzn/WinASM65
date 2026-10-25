; snippet: wait_frame
; purpose: Waits for the next NMI (one frame, 1/60 s). The main loop calls it once per frame.
; inputs: frame_ready is set to 1 by the NMI handler (see snippet nmi_oam_dma)
; outputs: frame_count incremented
; clobbers: A
; requires: none
; vars: frame_ready 1, frame_count 1
; wrap: org=$C000
; usage: MainLoop: jsr wait_frame / jsr read_joypad / ...game logic... / jmp MainLoop

wait_frame:
        lda #0
        sta frame_ready
-       lda frame_ready         ; the NMI handler sets it to 1
        beq -
        inc frame_count
        rts
