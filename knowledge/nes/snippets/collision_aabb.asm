; snippet: collision_aabb
; purpose: Do two 8x8 boxes overlap? (axis-aligned bounding boxes, positions in pixels)
; inputs: a_x, a_y, b_x, b_y (top-left corners). Size COLL_W x COLL_H (8 x 8; change for bigger objects)
; outputs: carry set = the boxes overlap, carry clear = they do not
; clobbers: A, flags
; cycles: about 40
; requires: none
; vars: a_x 1, a_y 1, b_x 1, b_y 1
; wrap: org=$C000
; usage: copy the two positions into a_x/a_y/b_x/b_y, jsr collide_boxes, bcs @hit
;        Overlap rule: |a_x - b_x| < COLL_W and |a_y - b_y| < COLL_H. Keep objects away from
;        the screen edges' wrap-around (x near 0 and 255).

COLL_W = 8
COLL_H = 8

collide_boxes:
        lda a_x
        sec
        sbc b_x                 ; A = a_x - b_x
        bcs @x_pos
        eor #$FF                ; negative: A = b_x - a_x = -(a_x - b_x)
        adc #1                  ; (carry is clear here)
@x_pos: cmp #COLL_W
        bcs @miss               ; |dx| >= width: no overlap
        lda a_y
        sec
        sbc b_y
        bcs @y_pos
        eor #$FF
        adc #1
@y_pos: cmp #COLL_H
        bcs @miss
        sec                     ; overlap
        rts
@miss:  clc
        rts
