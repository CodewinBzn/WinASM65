# WinASM65 syntax (what a NES game needs)

Standard 6502 syntax, close to asm6/ca65. Full reference: docs/site/syntax.html.

## Lines

```asm
; a comment starts with a semicolon
Label:                  ; a label alone on its line NEEDS the colon
loop:   lda #1          ; label before an instruction: colon recommended
        sta $10         ; indentation does not matter
NAME = 5                ; constant (redefinable); NAME EQU 5 is a fixed constant
```

One instruction per line (no `:` separator). Directives start with a dot: `.org`,
`.byte`; `org` without the dot is an error.

## Names are CASE-INSENSITIVE

`Palettes`, `PALETTES` and `palettes` are the SAME name: defining two of them is the
error "Label already declared". Mnemonics are case-insensitive too (`LDA` = `lda`).

## Numbers

| Form | Example |
|---|---|
| hex | `$FF`, `$2000` |
| binary | `%10000000` |
| decimal | `255` |
| character | `'A'` (= 65), `'0'` (= $30) |

`0x10` and `0b101` are errors. `#` means immediate: `lda #10` loads 10, `lda 10` reads
address 10.

## Expressions and the `<` `>` trap

`<expr` = low byte, `>expr` = high byte. They bind TIGHTER than `+`:
`#>$2000+4*32` is `(>$2000)+128` = $A0, WRONG. Write `#>($2000+4*32)` and
`#<($2000+4*32)`. Operators: `+ - * / % << >> & | ^ ~`, comparisons, parentheses.

## Labels

- Global: `name:` - letters, digits, `_`; starts with a letter or `_`.
- Local: `@name` belongs to the previous global label; reuse `@loop` in every routine.
  A local label before any global label is an error.
- Anonymous: a line starting with `-` or `+`; `bne -` jumps to the nearest `-` above,
  `beq +` to the nearest `+` below.

```asm
clear_ram:
        lda #0
        ldx #0
@loop:  sta $0300,x
        inx
        bne @loop
        rts
```

## Data

| Directive | Example |
|---|---|
| `.byte` (`.db`) | `.byte $0F, 30, 'A', "TEXT"` |
| `.word` (`.dw`) | `.word $1234, label` (low byte first) |
| `.asciiz` | `.asciiz "HELLO"` (adds a 0 byte) |
| `.dsb n, v` | `.dsb 8` (8 zero bytes), `.dsb 4, $FF` |
| `.lobytes`/`.hibytes` | `.lobytes a, b` / `.hibytes a, b` (address tables) |
| `.incbin "f"` | the bytes of a file |
| `.include "f"` | another source here (path relative to this file) |

## Placement and variables

```asm
        .memarea $0000          ; where .res hands out addresses
player_x  .res 1                ; player_x = $00
player_y  .res 1                ; player_y = $01
ptr       .res 2                ; ptr = $02, $03
        .org $C000              ; code starts here
```

`.res` reserves without emitting bytes; use it only after `.memarea`. Variables must
not overlap: always declare them with `.res`, never hard-code `$10` for one and `$10`
again for another.

## Macros

```asm
.macro wait_vblank
-       bit PPUSTATUS
        bpl -
.endmacro
        wait_vblank             ; called by name, no jsr
```

Parameters: `.macro ppu_addr address` ... `lda #>(address)`. No nested macros.
A macro must be defined before it is used. Do not define one twice (nes.inc already
has `wait_vblank` and `ppu_addr`).

## Conditional assembly

`.if expr` / `.ifdef name` / `.else` / `.endif`.

## Several units (config.json)

The template has 3 units. A unit sees the labels of the units listed in its
`Dependencies` (vectors.asm depends on game.asm, so `.word NMI` works). Keep all the
code in game.asm and `.include` other files into it: no `.import`/`.export` needed.
If you do use modules: `.export a, b` goes AFTER the definitions; `.import` takes ONE
name per line.

## Error messages and fixes

| Message | Fix |
|---|---|
| Undefined Macro (on a lone name) | add the colon: `Start:` |
| Label already declared | a name used twice (remember: case-insensitive) |
| Undefined symbol: x | define it (`x .res 1`, `x = 5`, `x:`) or fix the spelling |
| Unknown instruction or macro 'org' | directives need a dot: `.org` |
| Malformed expression '0x10' | use `$10` |
| Relative jump is too big | branch over a `jmp`: `beq +` / `jmp far` / `+` ... or invert the test |
| Value 256 is out of range for a 1-byte operand | an immediate is 0-255 |
| Local label '@x' has no global label before it | put a `name:` label above it |
| Undefined symbol: a (on `inc a`) | the 6502 has no `inc a`: use `clc / adc #1` |
| File doesn't exist | `.include` path is relative to the including file |
