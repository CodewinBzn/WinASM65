-- 07-beep: pulse 1 is silent before A, sounds just after each press of A.
local function sounding()
  local st = state()
  local len = st["apu.square1.envelope.lengthCounter.counter"]
  local constant = st["apu.square1.envelope.constantVolume"]
  local volume = constant and st["apu.square1.envelope.volume"] or st["apu.square1.envelope.counter"]
  local period = st["apu.square1.timer.period"]
  return len > 0 and volume > 0 and period >= 8, string.format("length %d volume %d period %d", len, volume, period)
end
at(55, function()
  local on, why = sounding()
  if on then return fail("pulse 1 already sounds before A is pressed (" .. why .. ")") end
end)
local heard = { false, false }
tap(60, "a")
tap(120, "a")
each(function(f)
  if f >= 61 and f <= 72 and sounding() then heard[1] = true end
  if f >= 121 and f <= 132 and sounding() then heard[2] = true end
  if f == 133 then
    if not heard[1] then return fail("no sound on pulse 1 after the first A press") end
    if not heard[2] then return fail("no sound on pulse 1 after the second A press") end
    pass("pulse 1 sounds after each A press")
  end
end)
