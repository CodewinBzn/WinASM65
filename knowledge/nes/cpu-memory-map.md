# CPU and memory map

The NES CPU is a 6502 (Ricoh 2A03) at 1.789773 MHz (NTSC). It has NO decimal mode:
`sed` + `adc` does not do BCD (keep decimal digits one per byte, see snippet `bcd_score`).
Registers: A (accumulator), X, Y (index), SP (stack pointer, stack at $0100-$01FF),
P (flags N V - B D I Z C), PC.

## CPU address space

| Address | Size | What |
|---|---|---|
| $0000-$00FF | 256 B | Zero page RAM: fastest, 2-byte instructions, `(ptr),y` pointers must be here |
| $0100-$01FF | 256 B | Stack (JSR/RTS, PHA/PLA, interrupts) |
| $0200-$02FF | 256 B | By convention the OAM copy (sprite table) sent by OAM DMA |
| $0300-$07FF | 1.25 KB | Free RAM (arrays, buffers) |
| $0800-$1FFF | | Mirrors of $0000-$07FF (do not use) |
| $2000-$2007 | 8 | PPU registers (see ppu-registers.md) |
| $2008-$3FFF | | Mirrors of the PPU registers |
| $4000-$4013 | | APU sound registers (see apu-sound.md) |
| $4014 | 1 | OAMDMA: write a page number, copies 256 bytes to sprite memory |
| $4015 | 1 | APU channel enable / status |
| $4016 | 1 | JOY1: controller strobe (write), controller 1 data (read) |
| $4017 | 1 | JOY2 data (read) / APU frame counter (write) |
| $4020-$5FFF | | Cartridge expansion (unused on NROM) |
| $6000-$7FFF | 8 KB | Cartridge RAM (only if the cartridge has it; NROM usually not) |
| $8000-$FFFF | 32 KB | Program ROM (PRG). NROM-128: 16 KB at $C000, mirrored at $8000 |
| $FFFA-$FFFB | 2 | NMI vector (address of the NMI handler) |
| $FFFC-$FFFD | 2 | RESET vector (address of the start code) |
| $FFFE-$FFFF | 2 | IRQ/BRK vector |

Total RAM is only 2 KB. All of it is random at power-on: clear it in RESET
(snippet `reset_init`).

## Variables

Put variables in zero page with `.memarea` and `.res` (see winasm65-syntax.md):

```asm
        .memarea $0000
player_x  .res 1
score     .res 4
ptr       .res 2          ; pointer for lda (ptr),y: low byte first
        .memarea $0300
enemies_x .res 16         ; arrays can go above the stack
```

Do not put variables at $0100-$01FF (stack) or $0200-$02FF (OAM copy).

## The template's layout (NROM-128)

- `src/game.asm`: `.org $C000`, code and data, up to $FFF9 (16,378 bytes).
- `src/vectors.asm`: `.org $FFFA`, `.word NMI, RESET, IRQ`.
- Zero page variables from $0000.

## Interrupts

- RESET: at power-on, the CPU jumps to the address at $FFFC. Code starts with `sei`, `cld`.
- NMI: at the start of every vblank when PPUCTRL bit 7 = 1. Ends with `rti`.
  Save and restore A, X, Y (pha/txa/pha/tya/pha ... pla/tay/pla/tax/pla).
- IRQ/BRK: not used in simple games: `IRQ: rti`.

## Useful facts

- A frame is 29,780 CPU cycles (NTSC, 60 frames per second).
- Branches (`bne`, `beq`...) reach only -128..+127 bytes; use `jmp` for far targets.
- `cmp` sets carry when A >= operand (unsigned): `bcs` = ">=", `bcc` = "<".
- `inc`/`dec` work on memory, not on A; `inx`/`iny`/`dex`/`dey` on X and Y.
- 16-bit values are stored low byte first (snippets `add16`, `compare16`).
