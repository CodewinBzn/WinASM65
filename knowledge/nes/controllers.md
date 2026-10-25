# Controllers (joypads)

## Reading

1. Write 1 then 0 to $4016 (JOY1): the controllers latch their 8 buttons.
2. Read $4016 eight times for controller 1 ($4017 for controller 2). Bit 0 of each read
   is one button, 1 = pressed, in this order:

| Read | Button | Bit in `buttons` (snippet read_joypad) | Mask (nes.inc) |
|---|---|---|---|
| 1 | A | 7 | BUTTON_A = %10000000 |
| 2 | B | 6 | BUTTON_B = %01000000 |
| 3 | Select | 5 | BUTTON_SELECT = %00100000 |
| 4 | Start | 4 | BUTTON_START = %00010000 |
| 5 | Up | 3 | BUTTON_UP = %00001000 |
| 6 | Down | 2 | BUTTON_DOWN = %00000100 |
| 7 | Left | 1 | BUTTON_LEFT = %00000010 |
| 8 | Right | 0 | BUTTON_RIGHT = %00000001 |

`lsr a` (bit 0 into carry) then `rol buttons` (carry into the byte) eight times builds
the byte with A in bit 7 and Right in bit 0.

```asm
read_joypad:
        lda buttons
        sta prev_buttons
        lda #1
        sta JOY1
        lda #0
        sta JOY1
        ldx #8
@bit:   lda JOY1
        lsr a
        rol buttons
        dex
        bne @bit
        lda prev_buttons
        eor #$FF
        and buttons
        sta pressed             ; down now, up last frame
        rts
```

## Held or pressed

- `buttons` = held now. Use it for movement (every frame while held).
- `pressed` = went down this frame. Use it for actions that happen once per press
  (jump, shoot, menu, beep, counting presses).

```asm
        lda buttons
        and #BUTTON_RIGHT
        beq @no_right           ; 0 = not held
        inc player_x
@no_right:
        lda pressed
        and #BUTTON_A
        beq @no_a
        jsr play_blip           ; once per press
@no_a:
```

Test one button with `and #MASK` then `beq` (not pressed) / `bne` (pressed).
Do not compare the whole byte with `cmp #BUTTON_RIGHT`: it fails when another button
is also held.

## Notes

- Read the joypad once per frame, in the main loop after `wait_frame`.
- On emulators Up+Down or Left+Right can be held together: test each direction alone.
- When the DMC sound channel plays samples, reads can glitch (re-read until two reads
  match). Not an issue without DMC.
