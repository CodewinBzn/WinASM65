-- 02-bg-color: the universal background colour ($3F00) is a blue.
local blues = { [0x01] = true, [0x02] = true, [0x11] = true, [0x12] = true, [0x21] = true, [0x22] = true,
                [0x31] = true, [0x32] = true, [0x1C] = true, [0x2C] = true }
at(60, function()
  local c = paletteAt(0)
  if not blues[c] then return fail(string.format("background colour $3F00 = $%02X, not a blue", c)) end
  if not state()["ppu.mask.backgroundEnabled"] then return fail("rendering is off") end
  pass(string.format("background colour $%02X", c))
end)
