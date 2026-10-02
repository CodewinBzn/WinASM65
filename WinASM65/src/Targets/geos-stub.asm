; WinASM65 - the stub that relocates a GEOS application onto itself
;
; The GEOS kernal does not correct anything when it loads a file: the bytes
; arrive verbatim. An application that is not loaded at the address it was
; assembled for therefore keeps wrong pointers, and silently so, because nothing
; reports it. The relocation table that WinASM65 writes describes the places to
; correct; this is the code that walks it.
;
; It is placed at the head of the first record, and the table immediately after
; it, and the program's own bytes after that:
;
;   record 0:   [ stub ][ table ][ code ... ]
;   load addr:  ^
;
; The kernal jumps to the load address, so the stub runs first, corrects the
; code, and jumps to the real entry. Nothing is loaded anywhere else, so the
; table is in RAM and needs no disk access to read.
;
; Nothing here is an address. A stub that carried the table's address as a link
; time constant -- or that used JMP to a label of its own, which is absolute on
; the 6502 -- would work at exactly one load address and corrupt memory at every
; other. So the stub reaches everything through the load address and through
; relative branches, which is why it moves with the code:
;
;   TABLE_OFFSET  the table's distance from the load address (the stub's length)
;   CODE_OFFSET   the code's distance from the load address
;
; The one absolute thing the stub reads is the load address itself, which the
; kernal leaves in $886C-$886D. Everything else is that address plus an offset.
;
; The bias is the distance the code has moved: the address the code really sits
; at, less the address it was assembled for, which the table carries. Adding the
; bias to a whole address is exact. Adding it to a high byte is not, which is why
; the writer refuses to describe one; see GeosRelocationTable.
;
; Zero page $70-$7F is used while the stub runs. That is application-only GEOS
; space (the pseudoregisters a2-a9), and the stub leaves before the program it
; corrects starts, so the program is free to use it as its own.

Load    = $70            ; the address the record was loaded at
Ptr     = $72            ; the table
Bias    = $74            ; how far the code moved
Site    = $76            ; the site being corrected
Count   = $78            ; entries still to apply
Entry   = $7A            ; where to go when the table is done
Ep      = $7C            ; the entry being read
Tmp     = $7E            ; a word, and then a width

GEOS_LOAD = $886C

Stub:
        LDA GEOS_LOAD
        STA Load
        LDA GEOS_LOAD+1
        STA Load+1

;       Ptr = the table, which is the load address plus a fixed offset.
        CLC
        LDA Load
        ADC #<TABLE_OFFSET
        STA Ptr
        LDA Load+1
        ADC #>TABLE_OFFSET
        STA Ptr+1

;       Tmp = the address the code was assembled for, out of the table.
        LDY #$04
        LDA (Ptr),Y
        STA Tmp
        INY
        LDA (Ptr),Y
        STA Tmp+1

;       Bias = (load address + CODE_OFFSET) - Tmp. Site is free here and is
;       used as the scratch for the code's real address.
        CLC
        LDA Load
        ADC #<CODE_OFFSET
        STA Site
        LDA Load+1
        ADC #>CODE_OFFSET
        STA Site+1
        LDA Site
        SEC
        SBC Tmp
        STA Bias
        LDA Site+1
        SBC Tmp+1
        STA Bias+1

;       Count = how many entries the table holds.
        LDY #$06
        LDA (Ptr),Y
        STA Count
        INY
        LDA (Ptr),Y
        STA Count+1

;       Ep = the first entry, which is the table plus its header.
        CLC
        LDA Ptr
        ADC #$0A
        STA Ep
        LDA Ptr+1
        ADC #$00
        STA Ep+1

Loop:
        LDA Count
        ORA Count+1
        BEQ Done

;       Site = the entry's address, moved by the bias, because the entry names
;       the site as it was assembled and the bytes are where the code now is.
        LDY #$00
        LDA (Ep),Y
        CLC
        ADC Bias
        STA Site
        INY
        LDA (Ep),Y
        ADC Bias+1
        STA Site+1

;       The width decides how many bytes the bias is added to. A width the
;       table does not use leaves the site alone rather than guessing: a wrong
;       guess here would move a byte the writer never described.
        INY
        LDA (Ep),Y
        CMP #$01
        BEQ One
        CMP #$02
        BNE Next
        LDY #$00
        LDA (Site),Y
        CLC
        ADC Bias
        STA (Site),Y
        INY
        LDA (Site),Y
        ADC Bias+1
        STA (Site),Y
;       SEC so the branch below is taken whatever the patch left in the flags:
;       it is an unconditional jump written as a relative one, because JMP to a
;       label would put an address in the stub.
        SEC
        BCS Next
One:
        LDY #$00
        LDA (Site),Y
        CLC
        ADC Bias
        STA (Site),Y
Next:
;       The next entry is three bytes on.
        CLC
        LDA Ep
        ADC #$03
        STA Ep
        LDA Ep+1
        ADC #$00
        STA Ep+1

;       One entry used: the count goes down by one, and the low byte going zero
;       is the high byte's cue, so a table of more than 256 entries counts down
;       through the page the way the 16 bit numbers in the header do.
        LDA Count
        BNE Down
        DEC Count+1
Down:   DEC Count

;       Back to the top while any of the count is left; both halves zero ends
;       the loop.
        LDA Count
        BNE Loop
        LDA Count+1
        BNE Loop

;       The entry point is named in the header and moves with everything else.
Done:
        LDY #$08
        LDA (Ptr),Y
        CLC
        ADC Bias
        STA Entry
        INY
        LDA (Ptr),Y
        ADC Bias+1
        STA Entry+1
        JMP (Entry)
