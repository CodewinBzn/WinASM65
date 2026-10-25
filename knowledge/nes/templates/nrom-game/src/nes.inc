; nes.inc - NES hardware names, button masks and two macros.
; Include it once at the top of game.asm:   .include "nes.inc"

; ---- PPU (picture) registers
PPUCTRL   = $2000       ; bit 7 NMI on, bit 4 bg pattern table, bit 3 sprite pattern table, bit 2 +32 increment
PPUMASK   = $2001       ; bit 4 show sprites, bit 3 show background, bits 2/1 show left 8 pixels
PPUSTATUS = $2002       ; bit 7 vblank (reading clears it and the PPUADDR latch), bit 6 sprite 0 hit
OAMADDR   = $2003
OAMDATA   = $2004
PPUSCROLL = $2005       ; write twice: X then Y
PPUADDR   = $2006       ; write twice: high byte then low byte
PPUDATA   = $2007

; ---- APU (sound) registers
SQ1_VOL    = $4000      ; pulse 1: duty, loop, constant volume, volume
SQ1_SWEEP  = $4001
SQ1_LO     = $4002      ; period low 8 bits
SQ1_HI     = $4003      ; length counter and period high 3 bits (writing starts the note)
SQ2_VOL    = $4004
SQ2_SWEEP  = $4005
SQ2_LO     = $4006
SQ2_HI     = $4007
TRI_LINEAR = $4008
TRI_LO     = $400A
TRI_HI     = $400B
NOISE_VOL  = $400C
NOISE_LO   = $400E
NOISE_HI   = $400F
DMC_FREQ   = $4010
OAMDMA     = $4014      ; write the high byte of a 256-byte page: copies it to the sprite memory
APU_STATUS = $4015      ; channel enable: bit 0 pulse 1, 1 pulse 2, 2 triangle, 3 noise, 4 DMC
JOY1       = $4016      ; write 1 then 0 to latch; read 8 times for the 8 buttons
JOY2       = $4017
APU_FRAME  = $4017      ; written: APU frame counter ($40 = no IRQ)

; ---- Button masks, in the order read_joypad builds the byte (A is bit 7)
BUTTON_A      = %10000000
BUTTON_B      = %01000000
BUTTON_SELECT = %00100000
BUTTON_START  = %00010000
BUTTON_UP     = %00001000
BUTTON_DOWN   = %00000100
BUTTON_LEFT   = %00000010
BUTTON_RIGHT  = %00000001

; ---- Video memory addresses
NAMETABLE0 = $2000      ; 32 x 30 tiles; tile (row, col) is at NAMETABLE0 + row*32 + col
ATTRIB0    = $23C0      ; 64 attribute bytes of nametable 0
PAL_ADDR   = $3F00      ; 16 background colours, then 16 sprite colours

; Waits for the start of a vertical blank by polling PPUSTATUS bit 7.
; Use it only while NMI is off (at reset, or with the screen off).
.macro wait_vblank
-       bit PPUSTATUS
        bpl -
.endmacro

; Points the PPU at a video memory address (the next PPUDATA access goes there).
; Example: ppu_addr NAMETABLE0+10*32+5    (row 10, column 5)
.macro ppu_addr address
        bit PPUSTATUS           ; resets the PPUADDR high/low latch
        lda #>(address)
        sta PPUADDR
        lda #<(address)
        sta PPUADDR
.endmacro
