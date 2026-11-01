# qwen2.5-coder:3b

Run 2026-10-25 14:11: 0/4 passed. Budget 25 tool calls, 12 min per task, num_ctx 8192, tool set core, tool calls in text accepted.

| Task | Result | Steps | Seconds | Reason |
|---|---|---|---|---|
| 01-text | FAIL | 3 | 67 | row 10 col 5 reads '???????', expected 'GAME ON' |
| 02-bg-color | FAIL | 25 | 100 | step budget (25 tool calls) used up; background colour $3F00 = $0F, not a blue |
| 03-sprite-position | FAIL | 8 | 25 | stuck repeating the same read_file call; ship is sprite 0 tile $01 x=120 y=100 attr=$00 |
| 04-move-horizontal | FAIL | 6 | 17 | answered without tool calls (no file changed); Right held 30 frames: x 120 -> 120 |

## Details

| Task | Tools used | Failed calls | Calls written as text | Nudged | Files changed | Max prompt tokens | Ended |
|---|---|---|---|---|---|---|---|
| 01-text | read_file x1, replace_in_file x1, build_project x1 | 0 | 3 | yes | 1 | 6442 | model finished |
| 02-bg-color | read_file x14, replace_in_file x11 | 13 | 25 | no | 0 | 6142 | step budget (25 tool calls) used up |
| 03-sprite-position | read_file x6, replace_in_file x2 | 4 | 8 | no | 0 | 5452 | stuck repeating the same read_file call |
| 04-move-horizontal | read_file x2, replace_in_file x2, build_project x2 | 2 | 6 | yes | 0 | 5012 | answered without tool calls (no file changed) |
