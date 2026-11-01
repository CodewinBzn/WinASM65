; Rock Patrol - the NES hardware: registers, buttons, and two macros.

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

; The joypad's buttons, as read_pad packs them: A in bit 7, right in bit 0.
PAD_A      = %10000000
PAD_B      = %01000000
PAD_SELECT = %00100000
PAD_START  = %00010000
PAD_UP     = %00001000
PAD_DOWN   = %00000100
PAD_LEFT   = %00000010
PAD_RIGHT  = %00000001

; Loops until the PPU starts a vertical blank.
.macro vblank_wait
-       bit PPUSTATUS
        bpl -
.endmacro

; Sets the PPU's address to a constant.
.macro set_ppu address
        bit PPUSTATUS           ; the next write is the high byte
        lda #>address
        sta PPUADDR
        lda #<address
        sta PPUADDR
.endmacro
