-- 10-scroll: the horizontal scroll grows by 1 pixel per frame (the picture moves left).
local last, good, total = nil, 0, 0
each(function(f)
  if f < 60 or f > 160 then return end
  local x = scrollX()
  if last ~= nil then
    total = total + 1
    if ((x - last) & 0xFF) == 1 then good = good + 1 end
  end
  last = x
  if f == 160 then
    if not state()["ppu.mask.backgroundEnabled"] then return fail("background rendering is off") end
    if good < total * 0.9 then return fail(string.format("scroll X grew by 1 on %d of %d frames", good, total)) end
    pass(string.format("scroll X +1 on %d of %d frames", good, total))
  end
end)
