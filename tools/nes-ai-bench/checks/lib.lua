-- Shared helpers of the benchmark checks, prepended to each task's check script
-- by run-bench.py, which replaces __RESULT__ with the file the verdict goes to.
--
-- A check schedules work by frame number and ends with pass() or fail(reason).
-- It looks only at what the hardware shows (sprite memory, nametables,
-- palettes, APU and PPU state), never at the program's variable names, so any
-- correct program passes.

local RESULT = "__RESULT__"
local frame = 0
local finished = false
local scheduled = {}
local inputs = {}
local everyFrame = {}

local function finish(verdict)
  if finished then return end
  finished = true
  local f = io.open(RESULT, "w")
  f:write(verdict)
  f:close()
  emu.stop(0)
end

function pass(message) finish("PASS " .. (message or "")) end
function fail(message) finish("FAIL " .. (message or "")) end
function currentFrame() return frame end

-- Runs fn at the end of frame n.
function at(n, fn) table.insert(scheduled, { frame = n, fn = fn }) end

-- Runs fn(frame) at the end of every frame.
function each(fn) table.insert(everyFrame, fn) end

-- Holds buttons (e.g. {right=true}) on controller 1 from frame `from` to frame `to` included.
function hold(from, to, buttons) table.insert(inputs, { from = from, to = to, buttons = buttons }) end

-- Presses a button for 4 frames at frame n (then it is released).
function tap(n, button) hold(n, n + 3, { [button] = true }) end

-- Sprite i (0-63) as it is in the PPU's sprite memory.
function sprite(i)
  local m = emu.memType.nesSpriteRam
  return { y = emu.read(i * 4, m), tile = emu.read(i * 4 + 1, m), attr = emu.read(i * 4 + 2, m), x = emu.read(i * 4 + 3, m), index = i }
end

-- The visible sprites (Y below $EF) showing a tile.
function spritesWithTile(tile)
  local found = {}
  for i = 0, 63 do
    local s = sprite(i)
    if s.y < 0xEF and s.tile == tile then table.insert(found, s) end
  end
  return found
end

function firstSpriteWithTile(tile) return spritesWithTile(tile)[1] end

-- Tile number at a row (0-29) and column (0-31) of nametable 0 ($2000).
function tileAt(row, col) return emu.read(row * 32 + col, emu.memType.nesNametableRam) end

-- The characters of a row of nametable 0 from a column (tile = ASCII code).
function textAt(row, col, length)
  local s = ""
  for i = 0, length - 1 do
    local t = tileAt(row, col + i)
    if t >= 32 and t < 127 then s = s .. string.char(t) else s = s .. "?" end
  end
  return s
end

-- Palette memory byte i (0-31, $3F00 + i).
function paletteAt(i) return emu.read(i, emu.memType.nesPaletteRam) end

function state() return emu.getState() end

-- Horizontal scroll in pixels (0-511) from the PPU's temporary address (t) and fine X.
function scrollX()
  local st = emu.getState()
  local t = st["ppu.tmpVideoRamAddr"]
  return ((t >> 10) & 1) * 256 + (t & 31) * 8 + st["ppu.xScroll"]
end

function describe(s)
  if s == nil then return "none" end
  return string.format("sprite %d tile $%02X x=%d y=%d attr=$%02X", s.index, s.tile, s.x, s.y, s.attr)
end

emu.addEventCallback(function()
  local buttons = {}
  for _, h in ipairs(inputs) do
    if frame >= h.from and frame <= h.to then
      for k, v in pairs(h.buttons) do buttons[k] = v end
    end
  end
  emu.setInput(buttons, 0)
end, emu.eventType.inputPolled)

emu.addEventCallback(function()
  frame = frame + 1
  if finished then return end
  for _, fn in ipairs(everyFrame) do
    fn(frame)
    if finished then return end
  end
  for _, s in ipairs(scheduled) do
    if s.frame == frame then
      s.fn()
      if finished then return end
    end
  end
  if frame >= 3600 then fail("the check did not finish within 3600 frames") end
end, emu.eventType.endFrame)

-- ---------------------------------------------------------------- task check
