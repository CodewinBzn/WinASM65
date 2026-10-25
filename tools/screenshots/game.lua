-- Plays Star Catcher in Mesen2's test runner and saves five pictures: the
-- title, a moment of play, the boss, the 3D stage and the game over screen.
-- shoot-game.ps1 replaces __OUT__ with the folder to write to, and each
-- __SYM_<name>__ with the address of that variable in src/game.symb.
--
-- The player is a small autopilot: it presses START, fires all the time and
-- steers under the lowest enemy, or under the boss; on the 3D stage, at the
-- nearest enemy. To show the later enemies it starts on level 5, and to reach
-- the boss in seconds it moves the level's clock on; it takes the boss's life
-- down to its last phase, and keeps its own lives until the 3D stage's
-- picture is taken. Then it lets them go.

local out = "__OUT__"
local frame = 0
local mem = emu.memType.nesDebug

local STATE, SHIP_X, SHIP_Y, LIVES = __SYM_state__, __SYM_ship_x__, __SYM_ship_y__, __SYM_lives__
local PHASE, PHASE_TIMER, LEVEL_CLOCK = __SYM_phase__, __SYM_phase_timer__, __SYM_level_clock__
local LEVEL, TIER, MODE3D = __SYM_level__, __SYM_tier__, __SYM_mode3d__
local OBJ_TYPE, OBJ_X, OBJ_Y = __SYM_obj_type__, __SYM_obj_x__, __SYM_obj_y__
local OBJ_Z, OBJ_SX, OBJ_SY, OBJ_SIZE = __SYM_obj_base__, __SYM_obj_sx__, __SYM_obj_sy__, __SYM_obj_size__
local BOSS_X, BOSS_Y, BOSS_HP, BOSS_T2 = __SYM_boss_x__, __SYM_boss_y__, __SYM_boss_hp__, __SYM_boss_t2__
local BOMB_Y = __SYM_bomb_y__

local played, bossAt, bossShot, stageAt, stageShot, overFrame = false, nil, false, nil, false, nil

local function rd(address) return emu.read(address, mem) end
local function wr(address, value) emu.write(address, value, mem) end

local function shot(name)
  local f = io.open(out .. "/" .. name .. ".png", "wb")
  f:write(emu.takeScreenshot())
  f:close()
end

local function fighting() return rd(PHASE) == 1 and rd(PHASE_TIMER) == 0 end

local function steer(input, targetX, targetY, shipX, shipY)
  if targetX < shipX - 2 then input.left = true end
  if targetX > shipX + 2 then input.right = true end
  if targetY then
    if targetY < shipY - 2 then input.up = true end
    if targetY > shipY + 2 then input.down = true end
  end
end

emu.addEventCallback(function()
  local input = {}
  if frame >= 60 and frame < 64 then input.start = true end
  if rd(STATE) == 1 then
    input.a = (frame % 2 == 0)
    if rd(MODE3D) == 1 then
      local nearest, best = nil, 999
      for i = 0, 9 do
        local kind, z = rd(OBJ_TYPE + i), rd(OBJ_Z + i)
        if kind >= 1 and kind <= 3 and rd(OBJ_SIZE + i) ~= 255 and z < best then nearest, best = i, z end
      end
      if nearest then
        steer(input, rd(OBJ_SX + nearest), rd(OBJ_SY + nearest), rd(SHIP_X) + 12, rd(SHIP_Y) + 8)
      end
    else
      local target = nil
      if fighting() then
        target = rd(BOSS_X) + 12
      else
        local lowest = -1
        for i = 0, 9 do
          local kind, y = rd(OBJ_TYPE + i), rd(OBJ_Y + i)
          if kind >= 2 and kind ~= 5 and y > lowest then target, lowest = rd(OBJ_X + i) + 4, y end
        end
      end
      if target then steer(input, target, nil, rd(SHIP_X) + 4, nil) end
      if rd(SHIP_Y) > 176 then input.up = true end
    end
  end
  emu.setInput(input, 0)
end, emu.eventType.inputPolled)

local function count(from, to, test)
  local n = 0
  for i = from, to do if test(i) then n = n + 1 end end
  return n
end

emu.addEventCallback(function()
  frame = frame + 1
  if frame == 40 then shot("game-title") end
  if rd(STATE) == 1 then
    if not stageShot then wr(LIVES, 3) end
    if frame < 70 then wr(LEVEL, 5); wr(TIER, 3) end
    -- The moment of play: four enemies, two of the later kinds, bullets flying.
    if not played and frame > 400 and rd(MODE3D) == 0 then
      local enemies = count(0, 9, function(i)
        local kind, y = rd(OBJ_TYPE + i), rd(OBJ_Y + i)
        return kind >= 2 and kind ~= 5 and y > 24 and y < 180 end)
      local later = count(0, 9, function(i) return rd(OBJ_TYPE + i) >= 6 end)
      local bullets = count(0, 11, function(i) return rd(BOMB_Y + i) ~= 0 end)
      if (enemies >= 4 and later >= 2 and bullets >= 2) or frame > 3000 then
        shot("game-play")
        played = true
        wr(LEVEL_CLOCK, 39)       -- the boss is a moment away
      end
    end
    -- The boss, in its last phase: its spiral and rings.
    if played and not bossShot and fighting() and rd(BOSS_Y) >= 32 then
      if bossAt == nil then bossAt = frame; wr(BOSS_HP, rd(BOSS_T2) - 1) end
      local bullets = count(0, 11, function(i) return rd(BOMB_Y + i) ~= 0 end)
      if frame > bossAt + 200 and bullets >= 9 then
        shot("game-boss")
        bossShot = true
        wr(BOSS_HP, 1)
      end
    end
    -- The 3D stage: an enemy close, others coming.
    if rd(MODE3D) == 1 and not stageShot then
      if stageAt == nil then stageAt = frame end
      local big = count(0, 9, function(i)
        local kind = rd(OBJ_TYPE + i)
        return kind >= 1 and kind <= 4 and rd(OBJ_SIZE + i) == 2 end)
      local far = count(0, 9, function(i)
        local kind, size = rd(OBJ_TYPE + i), rd(OBJ_SIZE + i)
        return kind >= 1 and kind <= 4 and size ~= 255 and size < 2 end)
      if frame > stageAt + 300 and big >= 1 and far >= 3 then
        shot("game-3d")
        stageShot = true
      end
    end
    if stageShot then wr(LIVES, 0) end
  end
  if rd(STATE) == 2 and overFrame == nil then overFrame = frame end
  if overFrame and frame == overFrame + 30 then
    shot("game-over")
    emu.stop(0)
  end
  if frame >= 30000 then emu.stop(1) end
end, emu.eventType.endFrame)
