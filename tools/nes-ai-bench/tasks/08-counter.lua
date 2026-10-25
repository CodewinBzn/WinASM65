-- 08-counter: two digits at row 2, column 2: 00, then +1 per A press.
for i = 0, 11 do tap(60 + i * 10, "a") end
at(50, function()
  local t = textAt(2, 2, 2)
  if t ~= "00" then return fail("row 2 col 2 reads '" .. t .. "' before any press, expected '00'") end
end)
at(88, function()
  local t = textAt(2, 2, 2)
  if t ~= "03" then return fail("after 3 presses row 2 col 2 reads '" .. t .. "', expected '03'") end
end)
at(190, function()
  local t = textAt(2, 2, 2)
  if t ~= "12" then return fail("after 12 presses row 2 col 2 reads '" .. t .. "', expected '12'") end
  pass("00 -> 03 -> 12")
end)
