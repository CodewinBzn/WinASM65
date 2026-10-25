-- 04-move-horizontal: Right for 30 frames moves the ship right, Left moves it back.
local x0, x1
hold(61, 90, { right = true })
hold(101, 130, { left = true })
at(60, function()
  local s = firstSpriteWithTile(0x01)
  if s == nil then return fail("no visible ship (tile $01)") end
  x0 = s.x
end)
at(95, function()
  local s = firstSpriteWithTile(0x01)
  if s == nil then return fail("the ship disappeared while moving right") end
  x1 = s.x
  if x1 - x0 < 20 then return fail(string.format("Right held 30 frames: x %d -> %d", x0, x1)) end
end)
at(135, function()
  local s = firstSpriteWithTile(0x01)
  if s == nil then return fail("the ship disappeared while moving left") end
  if x1 - s.x < 20 then return fail(string.format("Left held 30 frames: x %d -> %d", x1, s.x)) end
  pass(string.format("x %d -> %d -> %d", x0, x1, s.x))
end)
