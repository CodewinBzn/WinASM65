-- Plays Star Catcher in Mesen2's test runner and saves four pictures: the
-- title, a moment of play, the boss and the game over screen. shoot-game.ps1
-- replaces __OUT__ with the folder to write to, and each __SYM_<name>__ with
-- the address of that variable in src/game.symb.
--
-- The player is a small autopilot: it presses START, fires all the time and
-- steers under the lowest enemy, or under the boss. To reach the boss in
-- seconds it moves the level's clock on, and it keeps its lives until the
-- boss's picture is taken; then it lets them go.

local out = "__OUT__"
local frame = 0
local mem = emu.memType.nesDebug

local STATE, SHIP_X, SHIP_Y, LIVES = __SYM_state__, __SYM_ship_x__, __SYM_ship_y__, __SYM_lives__
local PHASE, PHASE_TIMER, LEVEL_CLOCK = __SYM_phase__, __SYM_phase_timer__, __SYM_level_clock__
local OBJ_TYPE, OBJ_X, OBJ_Y = __SYM_obj_type__, __SYM_obj_x__, __SYM_obj_y__
local BOSS_X, BOSS_Y, BOSS_HP = __SYM_boss_x__, __SYM_boss_y__, __SYM_boss_hp__

local played, bossShot, overFrame = false, false, nil

local function rd(address) return emu.read(address, mem) end
local function wr(address, value) emu.write(address, value, mem) end

local function shot(name)
  local f = io.open(out .. "/" .. name .. ".png", "wb")
  f:write(emu.takeScreenshot())
  f:close()
end

local function fighting() return rd(PHASE) == 1 and rd(PHASE_TIMER) == 0 end

emu.addEventCallback(function()
  local input = {}
  if frame >= 60 and frame < 64 then input.start = true end
  if rd(STATE) == 1 then
    input.a = (frame % 2 == 0)
    local shipX, target = rd(SHIP_X), nil
    if fighting() then
      target = rd(BOSS_X) + 12
    else
      local lowest = -1
      for i = 0, 7 do
        local kind, y = rd(OBJ_TYPE + i), rd(OBJ_Y + i)
        if kind >= 2 and kind <= 4 and y > lowest then target, lowest = rd(OBJ_X + i), y end
      end
    end
    if target then
      if target < shipX + 2 then input.left = true end
      if target > shipX + 6 then input.right = true end
    end
    if rd(SHIP_Y) > 176 then input.up = true end
  end
  emu.setInput(input, 0)
end, emu.eventType.inputPolled)

emu.addEventCallback(function()
  frame = frame + 1
  if frame == 40 then shot("game-title") end
  if rd(STATE) == 1 then
    if not bossShot then wr(LIVES, 3) end
    -- The moment of play: a shot in the air, and at least three enemies.
    if not played and frame > 300 then
      local enemies = 0
      for i = 0, 7 do
        local kind, y = rd(OBJ_TYPE + i), rd(OBJ_Y + i)
        if kind >= 2 and kind <= 4 and y > 40 and y < 170 then enemies = enemies + 1 end
      end
      if enemies >= 3 then
        shot("game-play")
        played = true
        wr(LEVEL_CLOCK, 39)       -- the boss is a moment away
      end
    end
    -- The boss: down, hit a few times, its bombs falling.
    if played and not bossShot and fighting() and rd(BOSS_Y) >= 32 and rd(BOSS_HP) <= 20 then
      shot("game-boss")
      bossShot = true
    end
    if bossShot then wr(LIVES, 0) end
  end
  if rd(STATE) == 2 and overFrame == nil then overFrame = frame end
  if overFrame and frame == overFrame + 30 then
    shot("game-over")
    emu.stop(0)
  end
  if frame >= 30000 then emu.stop(1) end
end, emu.eventType.endFrame)
