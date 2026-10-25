# Reference solutions

Run 2026-10-25 13:02. Each check must pass on its solution and fail on the bare template.

| Task | Solution | Check says | Template | Check says |
|---|---|---|---|---|
| 01-text | PASS | GAME ON at row 10, column 5 | fails (good) | row 10 col 5 reads '???????', expected 'GAME ON' |
| 02-bg-color | PASS | background colour $12 | fails (good) | background colour $3F00 = $0F, not a blue |
| 03-sprite-position | PASS | sprite 0 tile $01 x=200 y=50 attr=$00 | fails (good) | ship is sprite 0 tile $01 x=120 y=100 attr=$00 |
| 04-move-horizontal | PASS | x 120 -> 180 -> 120 | fails (good) | Right held 30 frames: x 120 -> 120 |
| 05-move-4way | PASS | all four directions move the ship | fails (good) | right held 20 frames: x 120 -> 120 |
| 06-second-sprite | PASS | sprite 1 tile $03 x=60 y=150 attr=$01 | fails (good) | no visible sprite with tile $03 |
| 07-beep | PASS | pulse 1 sounds after each A press | fails (good) | no sound on pulse 1 after the first A press |
| 08-counter | PASS | 00 -> 03 -> 12 | fails (good) | row 2 col 2 reads '??' before any press, expected '00' |
| 09-bounce | PASS | x range 16-232, 384 frames right, 216 left | fails (good) | no visible star (tile $03) at frame 30 |
| 10-scroll | PASS | scroll X +1 on 100 of 100 frames | fails (good) | scroll X grew by 1 on 0 of 100 frames |
