; Star Catcher - the NES hardware: registers, buttons, and two macros.

; Picture processing unit
PPUCTRL   = $2000
PPUMASK   = $2001
PPUSTATUS = $2002
OAMADDR   = $2003
PPUSCROLL = $2005
PPUADDR   = $2006
PPUDATA   = $2007

; Audio processing unit and I/O
SQ1_VOL    = $4000
SQ1_SWEEP  = $4001
SQ1_LO     = $4002
SQ1_HI     = $4003
NOISE_VOL  = $400C
NOISE_LO   = $400E
NOISE_HI   = $400F
DMC_FREQ   = $4010
OAMDMA     = $4014
APU_STATUS = $4015
JOY1       = $4016
APU_FRAME  = $4017

; The buttons, in the order the joypad sends them (A first, into bit 7).
BUTTON_A      = %10000000
BUTTON_B      = %01000000
BUTTON_SELECT = %00100000
BUTTON_START  = %00010000
BUTTON_UP     = %00001000
BUTTON_DOWN   = %00000100
BUTTON_LEFT   = %00000010
BUTTON_RIGHT  = %00000001

; Waits for the start of a vertical blank by polling the PPU status.
.macro wait_vblank
-       bit PPUSTATUS
        bpl -
.endmacro

; Points the PPU at a video memory address.
.macro ppu_addr address
        bit PPUSTATUS           ; resets the PPU's high/low latch
        lda #>address
        sta PPUADDR
        lda #<address
        sta PPUADDR
.endmacro
