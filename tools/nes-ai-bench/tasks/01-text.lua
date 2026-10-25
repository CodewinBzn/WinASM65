-- 01-text: "GAME ON" at row 10, column 5; HELLO NES gone.
at(60, function()
  local text = textAt(10, 5, 7)
  if text ~= "GAME ON" then return fail("row 10 col 5 reads '" .. text .. "', expected 'GAME ON'") end
  if textAt(4, 11, 9) == "HELLO NES" then return fail("HELLO NES is still shown at row 4") end
  pass("GAME ON at row 10, column 5")
end)
