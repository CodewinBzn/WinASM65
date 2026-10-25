# APU: sound

Five channels: pulse 1, pulse 2 (square waves), triangle, noise, DMC (samples).
Turn channels on with $4015 first, once, at reset: `lda #%00001111 / sta $4015`.

## $4015 APU_STATUS (write: enable)

Bit 0 pulse 1, bit 1 pulse 2, bit 2 triangle, bit 3 noise, bit 4 DMC.
A channel whose bit is 0 is silent and its length counter is cleared.

## Pulse 1 ($4000-$4003), pulse 2 ($4004-$4007)

| Register | Bits | Meaning |
|---|---|---|
| $4000 SQ1_VOL | DDLC VVVV | D duty (00 12.5%, 01 25%, 10 50%, 11 75%); L 1 = loop/no length counter (plays forever); C 1 = constant volume; V volume 0-15 |
| $4001 SQ1_SWEEP | EPPP NSSS | sweep: E enable, P period, N negate (1 = pitch goes up), S shift. `$08` = off |
| $4002 SQ1_LO | TTTT TTTT | period, low 8 bits |
| $4003 SQ1_HI | LLLL LTTT | L length index (how long), T period high 3 bits. Writing it starts the note |

Frequency = 1,789,773 / (16 x (period + 1)). Higher period = lower note.

| Note | Pulse period | Triangle period |
|---|---|---|
| C3 | $356 | $1AB |
| A3 | $1FB | $0FD |
| C4 | $1AB | $0D5 |
| D4 | $17C | $0BD |
| E4 | $152 | $0A9 |
| F4 | $13F | $09F |
| G4 | $11C | $08E |
| A4 (440 Hz) | $0FD | $07E |
| B4 | $0E1 | $070 |
| C5 | $0D5 | $06A |

Length index (bits 7-3 of $4003) -> duration in half-frames (1/120 s): 0 -> 10,
1 -> 254, 2 -> 20, 4 -> 40, 6 -> 80, 8 -> 160, 10 -> 60, 12 -> 14, 14 -> 26.
Example: `%00010000` = index 2 = 20 half-frames, about 1/6 second.

## Triangle ($4008, $400A, $400B)

| Register | Meaning |
|---|---|
| $4008 TRI_LINEAR | CRRR RRRR: C 1 = play until changed; R linear counter. `$FF` = sustain |
| $400A TRI_LO | period low 8 bits |
| $400B TRI_HI | LLLL LTTT: length index, period high bits (starts the note) |

Frequency = 1,789,773 / (32 x (period + 1)) (one octave lower than pulse at the same
period). No volume control: stop it with `lda #$80 / sta TRI_LINEAR`.

## Noise ($400C, $400E, $400F)

| Register | Meaning |
|---|---|
| $400C NOISE_VOL | --LC VVVV: like pulse without duty |
| $400E NOISE_LO | M--- PPPP: M 1 = metallic mode; P period index 0 (high hiss) - 15 (low rumble) |
| $400F NOISE_HI | LLLL L---: length index (starts the sound) |

## Recipes

Beep (pulse 1, A4, ~1/6 s), snippet `pulse_blip`:

```asm
        lda #%10011111          ; duty 50%, length counter on, constant volume 15
        sta SQ1_VOL
        lda #$08                ; no sweep
        sta SQ1_SWEEP
        lda #$FD
        sta SQ1_LO
        lda #%00010000          ; length index 2, period high 0
        sta SQ1_HI
```

Laser / falling pitch: same, with `lda #%10010010 / sta SQ1_SWEEP` (E=1 sweep on,
P=001, N=0, S=010: the period grows, the pitch falls until the channel mutes).

Pickup / rising pitch: `lda #%10011010 / sta SQ1_SWEEP` (N=1: the period shrinks, the
pitch rises).

Explosion (noise):

```asm
        lda #%00011111          ; length counter on, constant volume 15
        sta NOISE_VOL
        lda #$0C                ; low rumble
        sta NOISE_LO
        lda #%00100000          ; length index 4 (40 half-frames)
        sta NOISE_HI
```

Stop a pulse: `lda #%00110000 / sta SQ1_VOL` (constant volume 0).

## Tips

- Write the HI register last: it restarts the note (and the phase: a click). For a
  held note that only changes pitch, rewrite only LO when the high bits are unchanged.
- Bit 4 (C) = 1 for simple sound effects; envelope mode (C = 0) fades the volume.
- Music = a table of notes and durations read once per frame by the main loop or NMI.
