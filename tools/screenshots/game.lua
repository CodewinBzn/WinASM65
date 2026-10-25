-- Plays Star Catcher in Mesen2's test runner and saves three pictures: the
-- title, a moment of play and the game over screen. shoot-game.ps1 replaces
-- __OUT__ with the folder to write to.
--
-- The player is a small autopilot: it presses START, then steers under the
-- lowest star. It never dodges meteors, so the game ends on its own.

local out = "__OUT__"
local frame = 0
local overFrame = nil
local played = false
local mem = emu.memType.nesDebug

-- Zero-page variables of src/game.asm (see src/game.symb).
local STATE, SHIP_X, LIVES = 0x05, 0x07, 0x08
local OBJ_TYPE, OBJ_X, OBJ_Y = 0x15, 0x1B, 0x21

local function rd(address) return emu.read(address, mem) end

local function shot(name)
  local f = io.open(out .. "/" .. name .. ".png", "wb")
  f:write(emu.takeScreenshot())
  f:close()
end

emu.addEventCallback(function()
  local input = {}
  if frame >= 60 and frame < 64 then input.start = true end
  if rd(STATE) == 1 then
    local shipX = rd(SHIP_X)
    local target, lowest = nil, -1
    for i = 0, 5 do
      if rd(OBJ_TYPE + i) == 1 and rd(OBJ_Y + i) > lowest then
        target, lowest = rd(OBJ_X + i), rd(OBJ_Y + i)
      end
    end
    if target then
      if target + 4 < shipX + 6 then input.left = true end
      if target + 4 > shipX + 10 then input.right = true end
    end
  end
  emu.setInput(input, 0)
end, emu.eventType.inputPolled)

emu.addEventCallback(function()
  frame = frame + 1
  if frame == 40 then shot("game-title") end
  -- The moment of play: the first frame with two stars and a meteor falling.
  if not played and rd(STATE) == 1 and frame > 200 then
    local stars, meteors = 0, 0
    for i = 0, 5 do
      local kind, y = rd(OBJ_TYPE + i), rd(OBJ_Y + i)
      if kind == 1 and y > 40 then stars = stars + 1 end
      if kind == 2 and y > 60 and y < 180 then meteors = meteors + 1 end
    end
    if stars >= 2 and meteors >= 1 then
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
