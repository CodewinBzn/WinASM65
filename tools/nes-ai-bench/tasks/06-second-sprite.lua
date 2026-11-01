-- 06-second-sprite: ship still there, star tile $03 at (60,150) with palette 1.
at(60, function()
  if firstSpriteWithTile(0x01) == nil then return fail("the ship (tile $01) is gone") end
  local s = firstSpriteWithTile(0x03)
  if s == nil then return fail("no visible sprite with tile $03") end
  if math.abs(s.x - 60) > 2 or math.abs(s.y - 150) > 2 then return fail("star is " .. describe(s)) end
  if (s.attr & 3) ~= 1 then return fail("star uses palette " .. (s.attr & 3) .. ": " .. describe(s)) end
  pass(describe(s))
end)
