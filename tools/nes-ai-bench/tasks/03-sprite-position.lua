-- 03-sprite-position: the ship (tile $01) at x 200, y 50 (OAM Y may be 49 or 50).
at(60, function()
  local s = firstSpriteWithTile(0x01)
  if s == nil then return fail("no visible sprite with tile $01") end
  if math.abs(s.x - 200) > 2 or math.abs(s.y - 50) > 2 then return fail("ship is " .. describe(s)) end
  pass(describe(s))
end)
