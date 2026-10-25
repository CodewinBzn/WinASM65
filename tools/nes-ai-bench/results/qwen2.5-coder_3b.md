# qwen2.5-coder:3b

Run 2026-10-25 13:09: 0/4 passed. Budget 25 tool calls, 12 min per task, num_ctx 8192, tool set all, tool calls in text accepted.

| Task | Result | Steps | Seconds | Reason |
|---|---|---|---|---|
| 01-text | FAIL | 25 | 127 | step budget (25 tool calls) used up; row 10 col 5 reads '???????', expected 'GAME ON' |
| 02-bg-color | FAIL | 25 | 88 | step budget (25 tool calls) used up; build failed: src\vectors.asm: error: Dependency symbol file doesn't exist: src\game.symb |
| 03-sprite-position | FAIL | 25 | 98 | step budget (25 tool calls) used up; ship is sprite 0 tile $01 x=120 y=100 attr=$00 |
| 04-move-horizontal | FAIL | 25 | 101 | step budget (25 tool calls) used up; Right held 30 frames: x 120 -> 120 |

## Details

| Task | Tools used | Failed calls | Calls written as text | Nudged | Files changed | Max prompt tokens | Ended |
|---|---|---|---|---|---|---|---|
| 01-text | step x8, set_breakpoints x6, continue x4, replace_in_file x2, build_project x2, debug_launch x2, pause x1 | 4 | 25 | no | 2 | 6698 | step budget (25 tool calls) used up |
| 02-bg-color | read_file x11, build_project x6, write_file x5, replace_in_file x3 | 11 | 25 | no | 5 | 6272 | step budget (25 tool calls) used up |
| 03-sprite-position | replace_in_file x13, read_file x12 | 13 | 25 | no | 0 | 7032 | step budget (25 tool calls) used up |
| 04-move-horizontal | read_file x16, replace_in_file x9 | 13 | 25 | no | 0 | 7919 | step budget (25 tool calls) used up |
