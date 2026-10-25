-- 09-bounce: a star (tile $03) moving 1 px/frame between 16 and 232, turning at the edges.
local last, minX, maxX, ups, downs = nil, 999, -1, 0, 0
each(function(f)
  if f < 30 or f > 630 then return end
  local s = firstSpriteWithTile(0x03)
  if s == nil then return fail("no visible star (tile $03) at frame " .. f) end
  if math.abs(s.y - 150) > 3 then return fail("star is not at Y=150: " .. describe(s)) end
  if s.x < 12 or s.x > 236 then return fail("star left the 16-232 range: " .. describe(s)) end
  if last ~= nil then
    local d = s.x - last
    if math.abs(d) > 2 then return fail(string.format("star jumped from x=%d to x=%d", last, s.x)) end
    if d > 0 then ups = ups + 1 elseif d < 0 then downs = downs + 1 end
  end
  last = s.x
  if s.x < minX then minX = s.x end
  if s.x > maxX then maxX = s.x end
  if firstSpriteWithTile(0x01) == nil then return fail("the ship (tile $01) is gone") end
  if f == 630 then
    if ups < 100 or downs < 100 then return fail(string.format("moved right %d frames, left %d frames: no bounce", ups, downs)) end
    if minX > 20 or maxX < 228 then return fail(string.format("x range %d-%d, expected 16-232", minX, maxX)) end
    pass(string.format("x range %d-%d, %d frames right, %d left", minX, maxX, ups, downs))
  end
end)
