; WinASM65 - the runtime stub for a shared routine
;
; These systems have no executable: what a person writes is a program, and what
; the interpreter reads is a file of tokens. A routine cannot simply be linked
; into such a program at an address the linker chose, because the file must not
; hold one absolute address — that is the whole point of a shared routine, and it
; is the one invariant every dialect here shares. So the routine travels as a
; block of bytes with no fixed address in it, and this stub puts it where the
; running program wants it:
;
;   1. it expands the block into RAM,
;   2. it shifts every absolute reference by the difference between the address
;      the block was linked at and the address it now sits at,
;   3. it calls the routine, and returns to whoever called it.
;
; The caller needs to know nothing but where the stub is:
;
;       JSR stub
;
; The stub finds its own payload rather than being handed it, because a program
; in a tokenised file has no easy way to hand an address to a routine it calls
; by name. It therefore reads a header that sits immediately after its own code,
; and STUB_HEADER is the address of that header. RuntimeBlock writes the header:
; it assembles this file, measures how long the result is, and lays the payload
; after it, so the two cannot disagree about where the header is. The second
; assembly pass is what makes STUB_HEADER true.
;
; The payload, in order:
;
;   +0  the address the block is to be written to            2 bytes
;   +2  the shift, that address less the linked address      2 bytes
;   +4  the entry point, as an offset inside the block       2 bytes
;   +6  how many bytes the block comes to                    2 bytes
;   +8  how many sites there are to shift                    2 bytes
;   +10 the coded block, then the site table
;
; A group of the coded block is a length and a value. A length of one to $7F
; means that many bytes are copied as they are; a length byte of zero means a
; run, whose length and value are the two bytes after it. The block's real
; length is in the header and is what ends the expansion, so a stream that
; runs long cannot make the stub run away with the machine.
;
; A site is an offset inside the block, low byte then high byte, then the width
; of the reference. A width of two is an absolute address, and is shifted. A
; width of one is a branch, which is already relative to the byte after it, so
; shifting it would break it: the linker puts a branch in the table so that the
; stub knows of it, not so that it is moved.
;
; The shift is added with ADC rather than compared, so a block that moves down
; the address space works as well as one that moves up: the carry out of the
; low byte is the one the high byte needs.
;
; Zero page $80 to $8F is used while the stub runs. The routine is called last,
; after the last of those bytes has been read, so a routine that uses them
; itself is safe.

Ptr     = $80            ; the next byte of the coded stream
Out     = $82            ; where the block is being written
Tmp     = $84            ; scratch, and the entry offset
Run     = $86            ; bytes left in the group being copied
Mode    = $87            ; zero for a group copied as it is, one for a run
Site    = $88            ; scratch, the site being shifted
Delta   = $8A            ; the shift itself, two bytes
Count   = $8C            ; sites left to shift, two bytes
Left    = $8E            ; bytes of the block still to write, two bytes

Stub:
;       Read the header. Each field is two bytes, because every address on
;       these machines is, and each is at a known offset from the header.
        LDX #$00
        LDA STUB_HEADER,X
        STA Out
        INX
        LDA STUB_HEADER,X
        STA Out+1

        LDX #$02
        LDA STUB_HEADER,X
        STA Delta
        INX
        LDA STUB_HEADER,X
        STA Delta+1

        LDX #$04
        LDA STUB_HEADER,X
        STA Tmp
        INX
        LDA STUB_HEADER,X
        STA Tmp+1

        LDX #$06
        LDA STUB_HEADER,X
        STA Left
        INX
        LDA STUB_HEADER,X
        STA Left+1

        LDX #$08
        LDA STUB_HEADER,X
        STA Count
        INX
        LDA STUB_HEADER,X
        STA Count+1

;       The coded stream starts ten bytes into the header. That is the header's
;       own address plus ten, not the first thing the header holds: reading
;       STUB_HEADER reads the destination's low byte, and adding ten to that
;       would put the pointer inside the block instead of after the stream's
;       length byte.
        LDA #<STUB_HEADER
        CLC
        ADC #$0A
        STA Ptr
        LDA #>STUB_HEADER
        ADC #$00
        STA Ptr+1

;       1. Expand the block into RAM.
        LDA #$00
        STA Run
Expand:
        LDA Run
        BNE Emit
        JSR Get
;       CMP, not BNE: the branch has to be about the byte just read, and Get
;       ends on INC Ptr, which sets the flags from the pointer and not from the
;       accumulator. A BNE here asks about $80's low byte and happens to be
;       right only while the pointer is nowhere near zero.
        CMP #$00
        BNE Copy
        LDA #$01
        STA Mode
        JSR Get
        STA Run
        JSR Get
        STA Tmp
        JMP Emit
Copy:
;       A group copied as it is carries its length in the byte just read, and
;       that length is what is left to copy: Run counts down, so it goes in as
;       it stands. Zeroing it instead would copy the next 256 bytes whatever
;       the group said, and the length byte would be a number nobody used.
        STA Run
        LDA #$00
        STA Mode
Emit:
        LDA Mode
        BNE Repeat
        JSR Get
        STA Tmp
Repeat:
        LDA Tmp
        JSR Put
        DEC Run
        LDA Left
        BNE One
;       Borrow, not a second step. When the low byte is already zero the high one
;       is decremented and the low one is left to wrap to $FF, so both halves
;       move once per byte written. Jumping over the DEC instead — the obvious
;       way to write a sixteen bit decrement — counts a byte written and a byte
;       lost at the same time, and a block of more than 256 bytes stops halfway
;       with the processor perfectly happy.
        DEC Left+1
One:    DEC Left
Check:  LDA Left
        ORA Left+1
        BNE Expand
        JMP Sites

;       The next byte of the stream, and the next byte of the block. Neither
;       touches Y otherwise, so the code around them need not save it.
Get:
        LDY #$00
        LDA (Ptr),Y
        INC Ptr
        BNE Got
        INC Ptr+1
Got:    RTS

Put:
        LDY #$00
        STA (Out),Y
        INC Out
        BNE Wrote
        INC Out+1
Wrote:  RTS

;       2. Shift the sites. The stream pointer walked past the block on its way
;       through, so the site table is where the stream ended.
Sites:
        LDA Count
        ORA Count+1
        BEQ Call
        JSR Get
        STA Site
        JSR Get
        STA Site+1
        JSR Get
        CMP #$02
        BNE NextSite

;       A site is an offset inside the block, so the bytes to shift are at the
;       destination plus that offset. The destination is read from the header
;       rather than taken from Out, which by now points past the end of the
;       block: Out is where the next byte will be written, not where this one
;       was.
        LDY #$00
        LDA STUB_HEADER
        CLC
        ADC Site
        STA Site
        LDA STUB_HEADER+1
        ADC Site+1
        STA Site+1

        LDY #$00
        LDA (Site),Y
        CLC
        ADC Delta
        STA (Site),Y
        INY
        LDA (Site),Y
        ADC Delta+1
        STA (Site),Y

NextSite:
        LDA Count
        BNE OneSite
        DEC Count+1
        JMP MoreSites
OneSite:
        DEC Count
MoreSites:
        LDA Count
        ORA Count+1
        BNE Sites

;       3. Call the routine, and return to the caller of the stub.
;
;       The call has to be indirect, because where the routine lands is only
;       known now: the block's address plus its entry offset. JSR (zp),Y is
;       then not a call *to* that address but a call through it, so the address
;       is put in Ptr first and the jump reads it back from there — the two
;       are not the same cell, and a stub that jumped through the address it
;       had computed would call the first two bytes of the routine.
Call:
        LDA STUB_HEADER
        CLC
        ADC STUB_HEADER+4
        STA Ptr
        LDA STUB_HEADER+1
        ADC STUB_HEADER+5
        STA Ptr+1
        LDY #$00
        JSR (Ptr),Y
        RTS
