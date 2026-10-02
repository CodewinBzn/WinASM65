-- Probe 5: what does the emu table actually expose?
--
-- The bridge currently reads memory correctly and writes it correctly. The
-- problem is upstream: with the script holding the execution slot, the emulated
-- CPU never runs, so the memory being inspected never changes. cycleCount sits
-- at 7 and the program counter never leaves $8000.
--
-- If MesenCE exposes anything that advances emulation -- run, execute, frames,
-- tick -- the bridge can pump the machine between commands and the whole tool
-- comes alive. This probe enumerates the table so that question is answered by
-- evidence instead of by another guess.

local names = {}
for key, value in pairs(emu) do
  names[#names + 1] = string.format("%s(%s)", tostring(key), type(value))
end
table.sort(names)

print("PROBE emu contient " .. #names .. " entrees")
for _, entry in ipairs(names) do
  print("PROBE   " .. entry)
end

local function try_call(label, fn, ...)
  local ok, err = pcall(fn, ...)
  if ok then
    print("PROBE " .. label .. " OK")
  else
    print("PROBE " .. label .. " refused: " .. tostring(err))
  end
end

-- Candidate names for advancing emulation, called only if they exist, so that a
-- missing one reports itself instead of crashing the script.
for _, candidate in ipairs({ "run", "execute", "tick", "advanceFrames", "step",
                            "runFrames", "update", "refresh", "frame" }) do
  if type(emu[candidate]) == "function" then
    print("PROBE emu." .. candidate .. " existe")
    try_call("emu." .. candidate .. "(1)", emu[candidate], 1)
  end
end

print("PROBE end")
emu.stop(0)