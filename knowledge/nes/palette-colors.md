# NES palette colours (the 64 colour indexes)

A palette entry is a colour INDEX $00-$3F, not RGB. Put these numbers in the 32-byte palette
table copied to $3F00 (see ppu-memory.md). RGB is approximate (the TV decides).

Quick picks: black $0F, white $30, grey $00/$10, red $16, orange $27, yellow $28,
green $1A, dark green $0A, blue $11/$12, sky blue $21, purple $13, pink $25, brown $07/$17.

Rule: high nibble = brightness (0 dark, 1 normal, 2 light, 3 pale); low nibble = hue
(0 grey, 1 blue ... C cyan, D-F black). Never use $0D. Use $0F for black.

| Index | Name | RGB |
|---|---|---|
| $00 | dark grey | #7C7C7C |
| $01 | dark blue | #0000FC |
| $02 | dark navy blue | #0000BC |
| $03 | dark purple | #4428BC |
| $04 | dark magenta | #940084 |
| $05 | dark crimson | #A80020 |
| $06 | dark red-brown | #A81000 |
| $07 | dark brown | #881400 |
| $08 | dark olive | #503000 |
| $09 | dark forest green | #007800 |
| $0A | dark green | #006800 |
| $0B | dark sea green | #005800 |
| $0C | dark teal | #004058 |
| $0D | black - AVOID ('blacker than black', upsets TVs) | #000000 |
| $0E | black (duplicate, prefer $0F) | #000000 |
| $0F | black - THE black to use | #000000 |
| $10 | light grey | #BCBCBC |
| $11 | blue | #0078F8 |
| $12 | royal blue | #0058F8 |
| $13 | purple | #6844FC |
| $14 | magenta | #D800CC |
| $15 | crimson / deep pink-red | #E40058 |
| $16 | red | #F83800 |
| $17 | orange-brown | #E45C10 |
| $18 | olive / mustard | #AC7C00 |
| $19 | bright green | #00B800 |
| $1A | green | #00A800 |
| $1B | sea green | #00A844 |
| $1C | teal / dark cyan | #008888 |
| $1D | black (duplicate, prefer $0F) | #000000 |
| $1E | black (duplicate, prefer $0F) | #000000 |
| $1F | black (duplicate, prefer $0F) | #000000 |
| $20 | white (slightly grey) | #F8F8F8 |
| $21 | sky blue | #3CBCFC |
| $22 | periwinkle | #6888FC |
| $23 | lavender | #9878F8 |
| $24 | light magenta | #F878F8 |
| $25 | pink | #F85898 |
| $26 | salmon / light red | #F87858 |
| $27 | orange | #FCA044 |
| $28 | yellow | #F8B800 |
| $29 | lime green | #B8F818 |
| $2A | light green | #58D854 |
| $2B | mint green | #58F898 |
| $2C | cyan | #00E8D8 |
| $2D | dark grey | #787878 |
| $2E | black (duplicate, prefer $0F) | #000000 |
| $2F | black (duplicate, prefer $0F) | #000000 |
| $30 | white | #FCFCFC |
| $31 | pale sky blue | #A4E4FC |
| $32 | pale periwinkle | #B8B8F8 |
| $33 | pale lavender | #D8B8F8 |
| $34 | pale magenta | #F8B8F8 |
| $35 | pale pink | #F8A4C0 |
| $36 | peach | #F0D0B0 |
| $37 | beige / pale orange | #FCE0A8 |
| $38 | pale yellow | #F8D878 |
| $39 | pale lime | #D8F878 |
| $3A | pale green | #B8F8B8 |
| $3B | pale mint | #B8F8D8 |
| $3C | pale cyan | #00FCFC |
| $3D | light grey (pale pink-grey) | #F8D8F8 |
| $3E | black (duplicate, prefer $0F) | #000000 |
| $3F | black (duplicate, prefer $0F) | #000000 |

Sprite palettes: colour 0 of each sprite palette is transparent; the bytes $3F10, $3F14,
$3F18, $3F1C are mirrors of $3F00, $3F04, $3F08, $3F0C, so byte 16 of the 32-byte table
overwrites the universal background colour: keep it equal to byte 0.
