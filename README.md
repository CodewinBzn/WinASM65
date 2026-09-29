# WinASM65

  Assembler for 6502 based systems
by CodewinBzn.

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

------------------------------
## Build

```bash
dotnet build          # compile the solution
dotnet test           # run the test suite
dotnet run --project WinASM65 -- -h
```

`dotnet publish -c Release -r win-x64 --self-contained true -o build/`
produces a standalone `WinASM65.exe` that runs without a .NET install.

------------------------------
## Command line 

### Usage 

		WinASM65 [-f source] [-o object] [-t system] [-cpu cpu] [-format fmt] [-l] [-c config] [-h]

### Options
		-h, -help		Show help
		-l 			Create listing (don't specify the file name)
		-c 			Assemble one or multiple segments
					Combine assembled segments/binary files
		-f			Source file
		-o			Object file

### Target system options

		-t <system>		Target system (nes, c64, apple2, atari8, bbc, ...).
				Use `-t list` to enumerate every known system.
		-cpu <cpu>		CPU override: 6502 or 65c02.
				Defaults to the target system's CPU.
		-format <fmt>		Output format override: bin, ines, prg, xex,
				a2bin, rom, o65, ihex, srec.
				Defaults to the target system's format.

A target selects three things at once: the CPU variant, the default assembly
address, and the executable container that gets written. When no target is
given, WinASM65 behaves as `raw`: CPU 6502, no load address, plain binary.

```bash
WinASM65 -t c64  -f game.asm -o game.prg
WinASM65 -t nes  -f game.asm -o game.nes
WinASM65 -t apple2e -f game.asm -o game.bin
WinASM65 -t c64 -format ihex -f game.asm -o game.ihex
WinASM65 -t list
```

Each target also predefines that system's hardware registers, so you can use
them as symbols without declaring them yourself:

```asm
        lda PPUCTRL        ; NES
        lda #<PPU_SCROLL
```

The same values can be set from a configuration file via the `Target` section.
The `-t`, `-cpu` and `-format` flags win over the corresponding `Target` fields,
so you can keep one config file and switch systems from the command line.

Note that `LoadAddress` and `RunAddress` have no command-line equivalent, so a
value set in `Target` always applies, even when `-t` selects a different system.

------------------------------
## Output formats

| Format  | Container                                                        |
|---------|------------------------------------------------------------------|
| `bin`   | Raw bytes, no header. Default.                                    |
| `ines`  | iNES ROM: 16-byte header, padded PRG and CHR banks.              |
| `prg`   | Commodore PRG: little-endian load address, then the code.        |
| `xex`   | Atari XEX: segment with start/end addresses, optional run address.|
| `a2bin` | Apple II binary: load address and length, then the code.         |
| `rom`   | ROM padded to a fixed size.                                       |
| `o65`   | o65 object header with one text segment.                          |
| `ihex`  | Intel HEX, for programmers and EPROM burners.                     |
| `srec`  | Motorola S-record, for programmers and EPROM burners.             |

### Load address and `.org`

Formats that carry a load address prefer the address the code was actually
assembled at over the target's default. So `.org` always wins:

```asm
        .org $0800        ; C64 PRG header will contain $0800
        lda #$00
        rts
```

If you omit `.org`, the assembler starts at the target's load address, which is
usually what you want:

```asm
        lda #$00         ; C64, assembles at $0801
        rts
```

## Assemble segments

### JSON File format 
```
{
	"Target": {
		"System": "c64",
		"Cpu": "6502",
		"Format": "prg",
		"LoadAddress": "$0801",
		"RunAddress": "$0801",
		"DefineHardwareSymbols": true
	},
	"Input": [
		  	{
				"FileName": "path_to_main_file_seg1",
				"OutputFile": "path_to_seg1_output_file",
				"Dependencies": ["path_to_main_file_seg2"]
		  	},
		  	{
				"FileName": "path_to_main_file_seg2",*
				"OutputFile": "path_to_seg2_output_file",
				"Dependencies": ["path_to_main_file_seg1"]			
		  	},
	          	{
				"FileName": "path_to_main_file_seg3",
				"OutputFile": "path_to_seg3_output_file",
				"Dependencies": []			
		  	},
		   	......
		]
}

```

The `Target` section is optional. Every field is optional too, and each one
overrides the value coming from the named system. Addresses accept the same
number formats as source code, so `"$0801"` and `2049` are equivalent.

The `-t`, `-cpu` and `-format` flags override whatever the `Target` section
says, so you can keep one config file and switch systems from the command line.

### iNES options

When the target or format is `ines`, the header can be tuned in the same
section:

```
{
	"Target": {
		"System": "nes",
		"Ines": {
			"PrgBanks": 2,
			"ChrBanks": 1,
			"Mapper": 0,
			"Mirroring": "vertical",
			"Battery": false
		}
	}
}
```

### Dependencies
If a segment refers to labels, variables ... or to routines declared in other segments 
then it must mention them in this array as ["path_to_main_file_seg1", .....].

### OutputFile
The OutputFile is optional.

## Combine assembled segments / Binary files
### JSON File format:
```
{
	"Output": { 	
			"ObjectFile": "final_object_file",
			"Files": 
			[
				{
					"FileName": "path_to_seg1_output_file",
					"Size": "$hex"
				},
				{
					"FileName": "path_to_seg2_output_file",
					"Size": "$hex"
				},
				{
					"FileName": "path_to_seg3_output_file"			
				},
				....
			]
		}
}
```

The Segments are declared in the order of their insertion in the final object file.

### Size
The size of the segment object file.
If the size of the assembled segment is less then the declared size then the assembler will 
fill the rest of bytes with the value $00 .


## Syntax

- Comments begin with a semicolon (;).
```
lda #$00 	; this is a comment
```

- Labels are declared in two ways. 

- before an instruction 
```
			ldx #$00
	label 	        lda $4000, x
			cpx #$10
			bne label
```			
- Alone in a line, A colon (:) following a label is mandatory
```
		ldx #$00
label:
	 	lda $4000, x
		cpx #$10
		bne label
```		

### Numbers
- Hexadecimal numbers begin with '$'.
- Binary numbers begin with '%'.
- Decimal.

### Operand range

Instruction operands must fit the field they are encoded in. Values outside that range are a
hard error: the assembler reports it and the build fails, instead of silently truncating the
value as it did before.

- One-byte operand: -128 to 255 (`lda #$ff`, `lda #$7F`, `lda #$80`)
- Two-byte operand: -32768 to 65535 (`jmp $C000`)

Negative values are accepted and encoded in two's complement, so `lda #-1` still assembles to
`A9 FF`. Sources that relied on the old truncation (for example `lda #300` silently becoming
`lda #$2C`) no longer assemble and must be corrected.

### Assembler directives

#### .ORG / .org

- Set the starting address of a segment.
- To use only once in each segment.
- Accepts expressions.
```
	.org $c000
lda #$00	
```

#### .MEMAREA / .memarea  

- Set the starting address of a memory area for 
memory reservation (accepts expressions).

#### .RES / .res 
Reserve a number of bytes (accepts expressions).

```
	.memarea $00  ; zero page
player_posx  .res 1
player_pos_y .res 1

ram = $0400
	.memarea ram
nbr_coins = 15	
coins_pos_x .res nbr_coins  ; to store posx of coins  
coins_pos_y .res nbr_coins  ; to store posy of coins
```

#### .INCBIN / .incbin
- Add the content of a binary file to the assembly output.
```
.incbin "path_to_binary_file"
```
#### .INCLUDE / .include
- Assemble another source file as if it were part of the current source.
```
.include "path_to_source_file"
```

#### .BYTE/.byte, .WORD/.word
- Emit byte(s) or word(s).
- Multiple arguments are separated by commas.
- Accept expressions.
```
RED = $06
palette:
.byte $00, $10, RED + 4, $5d
```

#### Strings
```
lda #"A"

.byte "A", "B"
.byte "NES"
myString: 
	.byte "Hello World"		
```
#### .IFDEF/.ifdef,  .IFNDEF/.ifndef
Conditional assembly
- Process a block of code if a symbol has been defined / not defined.
```
.ifdef _debug_
	.
	.
	.
.else 
	.
	.
	.
.endif

```
#### .IF/.if
Conditional assembly
- Process a block of code if the logical expression is evaluated to true.
- The expression must be a constant expression, that is, all operands must be defined.
```
.if expression
	.
	.
	.
.else 
	.
	.
	.
.endif

```


#### .MACRO/.macro
-  Define a macro.  Macro arguments are comma separated.
- .macro name args...
```
.macro add @a, @b
	clc 
	lda @a
	adc @b
.endmacro

red_color = $85
add #red_color, #$00	
```

#### .REP/.ENDREP
-  Repeat a block of code constant number of times.
- The command is followed by a constant expression that tells how many times the commands in the body should get repeated.
```
;clear memory
clrmem:
  LDA #$00
  {
    mem = $0000
    .rep 8
      STA mem, x
      mem = mem + $0100
    .endrep  
  }
  INX
  BNE clrmem

;fill the remaining bytes of the bank
lastbyte:
.rep $2000 - (lastbyte - $c000) 
	.byte $ff
.endrep
```

#### .END/.end
-  Forced end of assembly. Assembly stops at this point, even if the command is read from an include file.

### Expressions
#### Supported operators (listed by precedence)
```
 - ()
 - + - ~ ! < > (The unary < and > give respectively the lower and the upper byte of a value)
 - * / %
 - + -
 - << >>
 - < > <= >=
 - = == != <> 
 - &
 - ^
 - |
 - &&
 - ||
 - #   	    	 Immidate addressing.

```
 
 ### Local lexical level
 ```
 .macro vblank label, register
	label: 
		BIT register
		BPL label
.endmacro
.
.
.
 { ; All new symbols from now on are in the local lexical level and are not accessible from outside.
   ; Symbols defined outside this local level may be accessed as long as their names are not used for new symbols inside the level.
   ; Macro names are always in the global level.
   
   vblank vblankwait, $2002
 }
 .
 .
 .
 {   
	vblank vblankwait, $2002   ; Second wait for vblank, PPU is ready after this
 }
 ```
 

 	


	
	






	






