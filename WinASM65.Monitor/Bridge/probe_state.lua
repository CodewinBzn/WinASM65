-- What the bridge can actually do.
--
-- Callbacks are out of reach in MesenCE 2.2.1: seven argument shapes give seven
-- identical refusals, including an anonymous function, a named global one, and the
-- function name as text. That is not a calling mistake but a product limitation, so
-- the bridge must be built on what remains rather than on one more lucky guess.
--
-- This script queries processor state and the control primitives, which are the
-- substitute building blocks for breakpoints.

local done = false
local function finish(code)
  if done then return end
  done = true
  pcall(function() emu.stop(code) end)
end

function note(key, value) print(key .. " = " .. tostring(value)) end

function probe(name, fn)
  local ok, v = pcall(fn)
  if not ok then note(name, "ERROR: " .. tostring(v)); return nil end
  note(name, v)
  return v
end

-- Processor state is the substitute for callbacks: without it, no breakpoint is even
-- conceivable.
local state = probe("getCpuState", function()
  return emu.getCpuState()
end)

if type(state) == "table" then
  local keys = {}
  for k, _ in pairs(state) do keys[#keys + 1] = tostring(k) end
  table.sort(keys)
  note("cpuState_keys", table.concat(keys, ","))
  for _, k in ipairs({ "ProgramCounter", "Pc", "pc", "Registers" }) do
    if state[k] ~= nil then
      note("cpuState." .. k, type(state[k]) == "table" and "(table)" or state[k])
    end
  end
end

-- Control primitives: the protocol announces PAUSE/RESUME/STEP.
for _, name in ipairs({ "pause", "resume", "step", "breakExecution", "reset",
                        "getCpuCycleCount", "getSystemInfo", "getVersion",
                        "getRomInfo", "getMemorySize" }) do
  note("emu." .. name, type(emu[name]))
end

-- step and resume fail without arguments: they probably expect a type (one
-- instruction, one frame, one counter). That is a question of signature rather than
-- of absence, and it is settled by reading the exposed types.
for _, name in ipairs({ "stepType", "counterType" }) do
  probe(name, function()
    if type(emu[name]) ~= "table" then return "type=" .. type(emu[name]) end
    local keys = {}
    for k, v in pairs(emu[name]) do keys[#keys + 1] = tostring(k) .. "=" .. tostring(v) end
    table.sort(keys)
    return table.concat(keys, ",")
  end)
end

-- breakExecution is the only candidate for "pause": emu.pause does not exist.
probe("breakExecution_no_arg", function()
  local ok, e = pcall(emu.breakExecution)
  return tostring(ok) .. " " .. tostring(e)
end)

probe("step_with_type", function()
  local ok, e = pcall(emu.step, emu.stepType and emu.stepType.oneInstruction)
  return tostring(ok) .. " " .. tostring(e)
end)

probe("resume_with_type", function()
  local ok, e = pcall(emu.resume, emu.counterType and emu.counterType.oneInstruction)
  return tostring(ok) .. " " .. tostring(e)
end)

-- After all these attempts the cycle count must have moved if the machine really
-- advances. That is the independent witness of the operation.
probe("cycles_after", function() return tostring(emu.getCpuCycleCount()) end)
probe("pc_after", function() return tostring(emu.getCpuState().pc) end)

note("end", "processor state and controls queried")
finish(0)