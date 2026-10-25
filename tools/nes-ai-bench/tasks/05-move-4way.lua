-- 05-move-4way: each direction held 20 frames moves the ship at least 15 pixels that way.
local moves = {
  { button = "right", axis = "x", sign = 1 },
  { button = "down", axis = "y", sign = 1 },
  { button = "left", axis = "x", sign = -1 },
  { button = "up", axis = "y", sign = -1 },
}
local before = {}
for i, m in ipairs(moves) do
  local start = 40 + i * 30
  hold(start + 1, start + 20, { [m.button] = true })
  at(start, function()
    local s = firstSpriteWithTile(0x01)
    if s == nil then return fail("no visible ship (tile $01) before " .. m.button) end
    before[i] = s
  end)
  at(start + 25, function()
    local s = firstSpriteWithTile(0x01)
    if s == nil then return fail("the ship disappeared after " .. m.button) end
    local moved = (s[m.axis] - before[i][m.axis]) * m.sign
    local other = m.axis == "x" and "y" or "x"
    if moved < 15 then
      return fail(string.format("%s held 20 frames: %s %d -> %d", m.button, m.axis, before[i][m.axis], s[m.axis]))
    end
    if math.abs(s[other] - before[i][other]) > 2 then return fail(m.button .. " also changed " .. other) end
    if i == #moves then pass("all four directions move the ship") end
  end)
end
