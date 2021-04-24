# WinASM65
Assembler for 6502 based systems
by codewin (Abdelghani BOUZIANE).

## Command line 

### Usage 

		WinASM65 [-option] sourcefile
		
### Options
		-m 			Assemble one or several segments
		-c 			Combine assembled segments/binary files
		
		
## Assemble segments

### JSON File format 
```
[
	{
		FileName: "path_to_main_file_seg1",
		Dependencies: ["path_to_main_file_seg2"]
	},
	{
		FileName: "path_to_main_file_seg2",
		Dependencies: ["path_to_main_file_seg1"]			
	},
	{
		FileName: "path_to_main_file_seg3",
		Dependencies: []			
	},
	......
]

```

### Dependencies
If a segment refers to labels, variables ... or to routines declared in other segments 
then it must mention them in this array as ["path_to_main_file_seg1", .....].


## Combine assembled segments / Binary files
### JSON File format:
```
{
	
	ObjectFile: "final_object_file",
	Files: 
	[
		{
			FileName: "path_to_seg1_object_file",
			Size: "$hex"
		},
		{
			FileName: "path_to_seg2_object_file",
			Size: "$hex"
		},
		{
			FileName: "path_to_seg3_object_file",			
		},
		....
	]
}
```

Segment are declared by order of insertion in the final object file.

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


### Assembler directives

- .ORG / .org

Set the starting address of a segment.
To use only once in each segment.
```
	.org $c000
lda #$00	
```

- .MEMAREA / .memarea  .

Set the starting address of a memory area for 
memory reservation.

- .RES / .res 
Reserve a number of bytes (to use with decimal number).

```
	.memarea $00  ; zero page
player_posx  .res 1
player_pos_y .res 1
	.memarea $0400
ennemies_pos_x .res 15 	; to store posx of ennemies (15 ennemies) 
ennemies_pos_y .res 15  ; to store posy of ennemies (15 ennemies) 
```

- .INCBIN / .incbin

Add the contents of a binary file to the assembly output.
```
.incbin "path_to_binary_file"
```
- .INCLUDE / .include
Assemble another source file as if it were part of the current source.
```
.include "path_to_source_file"
```

- .BYTE / .byte, .WORD / .word
Emit byte(s) or word(s).  Multiple arguments are separated by commas.
```
RED = $06
palette:
.byte $00, $10, RED, $5d
```

### Expressions

 - <Label   Returns the low byte of label.
 - >Label   Returns the high byte of label.
 - #label   Immidate addressing.
 - ]label   Force the zero page addressing (usefull when reffering to a symbol in another segment).
 
 


 	


	
	






	






