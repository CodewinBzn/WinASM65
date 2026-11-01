; Rock Patrol - the first 16 KB of program ROM, $8000-$BFFF: the screens, the
; sky's map and the palettes, as the designer exports them, and a table. A unit
; of its own: game.asm finds these names in data.symb (config.json makes it
; depend on this unit), as vectors.asm finds NMI in game.symb.

        .org $8000

        .include "../gfx/game.pal.asm"
        .include "../gfx/title.screen.asm"
        .include "../gfx/over.screen.asm"
        .include "../gfx/play.map.asm"

; Where a rock may appear across the screen: 32 places, 8 pixels apart,
; from x = 8 to x = 232, shuffled, so that rocks come down all over the sky.
RockLanes:
        .byte 112, 24, 200, 64, 152, 8, 176, 96, 40, 224, 128, 16, 184, 72, 144, 56
        .byte 208, 32, 120, 168, 88, 232, 48, 160, 104, 192, 80, 136, 24, 216, 64, 176

; Each score's tens decide how fast rocks fall (pixels a frame) and how many
; frames apart they come: the game gets harder every 10 rocks.
FallSpeed:
        .byte 1, 1, 2, 2, 2, 3, 3, 3
SpawnGap:
        .byte 48, 40, 40, 32, 28, 28, 24, 20
