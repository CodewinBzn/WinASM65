; snippet: tile_address
; purpose: Computes the nametable address of a tile from a row and a column at run time, and writes one tile there.
; inputs: tile_row (0-29), tile_col (0-31); A = tile for put_tile
; outputs: tile_addr (2 bytes, low first) = $2000 + tile_row*32 + tile_col; put_tile writes the tile
; clobbers: A (tile_address), A and X (put_tile)
; cycles: about 60
; requires: nes.inc
; vars: tile_row 1, tile_col 1, tile_addr 2
; wrap: org=$C000
; usage: For a constant position use an expression instead: ppu_addr $2000+10*32+5.
;        put_tile writes video memory: screen off, or inside NMI.

tile_address:
        lda tile_row
        sta tile_addr+1         ; high = row, shifted below
        lda #0
        sta tile_addr
        lsr tile_addr+1         ; row * 32 = row * 256 / 8: shift right 3 times
        ror tile_addr
        lsr tile_addr+1
        ror tile_addr
        lsr tile_addr+1
        ror tile_addr
        lda tile_addr
        ora tile_col            ; the low 5 bits are free: OR in the column
        sta tile_addr
        lda tile_addr+1
        ora #$20                ; + $2000 (nametable 0)
        sta tile_addr+1
        rts

put_tile:
        tax                     ; keep the tile
        jsr tile_address
        bit PPUSTATUS
        lda tile_addr+1
        sta PPUADDR
        lda tile_addr
        sta PPUADDR
        stx PPUDATA
        rts
