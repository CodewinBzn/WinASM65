-- Plays Rock Patrol in Mesen2's test runner and saves three pictures: the
-- title, a moment of play and the game over screen. shoot-game.ps1 replaces
-- __OUT__ with the folder to write to, and each __SYM_<name>__ with the
-- address of that variable in src/game.symb.
--
-- The player is a small autopilot: it presses START, fires all the time and
-- steers under the lowest rock. It keeps its lives until the picture of play
-- is taken, then stops and lets the rocks hit the ship.

local out = "__OUT__"
local frame = 0
local mem = emu.memType.nesDebug

local STATE, LIVES, SHIP_X = __SYM_state__, __SYM_lives__, __SYM_ship_x__
local ROCK_X, ROCK_Y, SHOT_Y, SCORE = __SYM_rock_x__, __SYM_rock_y__, __SYM_shot_y__, __SYM_score__

local played, overFrame = false, nil

local function rd(address) return emu.read(address, mem) end
local function wr(address, value) emu.write(address, value, mem) end

local function shot(name)
  local f = io.open(out .. "/" .. name .. ".png", "wb")
  f:write(emu.takeScreenshot())
  f:close()
end

-- The lowest rock on screen, or nil.
local function lowest()
  local best, by = nil, -1
  for i = 0, 5 do
    local y = rd(ROCK_Y + i)
    if y ~= 0 and y < 180 and y > by then best, by = i, y end
  end
  return best
end

emu.addEventCallback(function()
  local input = {}
  if frame >= 60 and frame < 64 then input.start = true end
  if rd(STATE) == 1 and not played then
    input.a = (frame % 2 == 0)
    local n = lowest()
    if n then
      local target = rd(ROCK_X + n)
      if target < rd(SHIP_X) - 2 then input.left = true end
      if target > rd(SHIP_X) + 2 then input.right = true end
    end
  end
  emu.setInput(input, 0)
end, emu.eventType.inputPolled)

emu.addEventCallback(function()
  frame = frame + 1
  if frame == 40 then shot("game-title") end
  if rd(STATE) == 1 and not played then
    wr(LIVES, 3)
    -- The moment of play: four rocks or more, two shots flying, a score.
    local rocks, shots = 0, 0
    for i = 0, 5 do if rd(ROCK_Y + i) ~= 0 then rocks = rocks + 1 end end
    for i = 0, 2 do if rd(SHOT_Y + i) ~= 0 then shots = shots + 1 end end
    if frame > 600 and rocks >= 4 and shots >= 2 and rd(SCORE + 3) ~= 0 then
      shot("game-play")
      played = true
    end
  end
  if rd(STATE) == 2 and overFrame == nil then overFrame = frame end
  if overFrame and frame == overFrame + 30 then
    shot("game-over")
    emu.stop(0)
  end
  if frame >= 30000 then emu.stop(1) end
end, emu.eventType.endFrame)
