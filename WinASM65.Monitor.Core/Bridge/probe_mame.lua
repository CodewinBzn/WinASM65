-- Capability probe for the A6 MAME adapter.
--
-- This replaces the four ad-hoc probes that were used while working out how to
-- reach MAME from Lua. It answers one question: which parts of the monitor
-- protocol can an MAME backend actually implement? It is a probe, not an
-- adapter: it reads, writes and reports, and it never drives the machine.
--
-- The contract with the C# side (MameCapabilityTests) is the output format, so
-- keep every line shaped as `MPROBE key=value` and end with `MPROBE end`.
--
-- Measured facts this probe was written against, on MAME 0.289:
--
--   * `emu.MACHINE`, `emu.memory`, `emu.cpu` and `emu.debug` do not exist in this
--     version. Reaching anything at all means going through the global `manager`,
--     as `manager.machine.devices[tag]`.
--   * `emu.pause()` works, so reads can be taken against a stopped machine and
--     will not race the program. It also stops emulated time advancing, so
--     -seconds_to_run will never expire once paused. The machine must be shut
--     down by the caller, which is why this script never ends the process.
--   * `debug:step(n)` returns without error but leaves the program counter where
--     it was, so stepping is reported rather than relied upon.
--   * `debug:bpset()` blocks. It is deliberately not called here: a probe that can
--     hang is not a probe.

-- The report goes to a file as well as stdout, because the probe pauses the
-- machine and MAME therefore never exits: anything the test wants to read has to
-- survive the process being killed. Every line is flushed for the same reason,
-- so the test can watch the file arrive instead of waiting for an exit that will
-- never happen. WINASM65_PROBE_OUT names the file; without it the probe still
-- prints to stdout and can be run by hand.
local reportPath = os.getenv("WINASM65_PROBE_OUT")
local report = nil
if reportPath ~= nil and reportPath ~= "" then
  report = io.open(reportPath, "w")
end

local function say(text)
  local line = "MPROBE " .. tostring(text)
  print(line)
  if report ~= nil then
    report:write(line .. "\n")
    report:flush()
  end
end

-- `attempt` turns a hostile API call into one output line instead of losing the
-- rest of the report. A capability that cannot even be probed is a capability
-- the adapter must not claim.
local function attempt(key, fn)
  local ok, value = pcall(fn)
  if not ok then
    say(key .. "=error")
    return nil
  end
  if value ~= nil then
    say(key .. "=" .. tostring(value))
  end
  return value
end

local function hex(value)
  if type(value) ~= "number" then return "?" end
  return string.format("%02X", value)
end

local function hexAddress(value)
  if type(value) ~= "number" then return "?" end
  return string.format("%04X", value)
end

local function sortedKeys(container)
  local names = {}
  if container == nil then return names end
  for key, _ in pairs(container) do names[#names + 1] = tostring(key) end
  table.sort(names)
  return names
end

say("schema=1")

-- Stop first. Everything below is a read of a machine that is not running, which
-- is the only way the numbers mean anything.
attempt("pause", function()
  emu.pause()
  return "ok"
end)

local cpu = attempt("cpu.tag", function()
  return tostring(manager.machine.devices["maincpu"].tag)
end)

attempt("cpu.device", function()
  return tostring(manager.machine.devices["maincpu"].name)
end)

-- Address spaces and registers, the two things a memory-only backend needs.
local space = nil
attempt("space.names", function()
  local names = sortedKeys(manager.machine.devices["maincpu"].spaces)
  space = manager.machine.devices["maincpu"].spaces["program"]
  return table.concat(names, ",")
end)

local state = nil
attempt("state.tags", function()
  state = manager.machine.devices["maincpu"].state
  return table.concat(sortedKeys(state), ",")
end)

-- Reading PRG at $8000 is the check that proves the address map is live rather
-- than merely present: these bytes come out of the cartridge the monitor shipped.
attempt("mem.read8000", function()
  local out = {}
  for offset = 0, 7 do out[#out + 1] = hex(space:read_u8(0x8000 + offset)) end
  return table.concat(out, "")
end)

attempt("mem.read0000", function()
  return hex(space:read_u8(0x0000))
end)

-- Write, then read back, then restore. A write that reports success but cannot
-- be observed is exactly the failure MesenCE has, and the reason the MesenCE
-- bridge verifies its writes. MAME is checked the same way.
attempt("mem.roundtrip", function()
  local address = 0x0010
  local before = space:read_u8(address)
  space:write_u8(address, 0x5A)
  local after = space:read_u8(address)
  space:write_u8(address, before)
  local restored = space:read_u8(address)
  if after ~= 0x5A then return "unobservable " .. hex(before) .. "->" .. hex(after) end
  if restored ~= before then return "not_restored " .. hex(before) .. "->" .. hex(restored) end
  return "ok"
end)

attempt("mem.range", function()
  local bytes = space:read_range(0x8000, 0x8007, 8)
  return tostring(#bytes)
end)

-- Registers, as the CPU verb needs them.
if state ~= nil then
  for _, name in ipairs({ "PC", "A", "X", "Y", "SP", "P" }) do
    attempt("state." .. name, function()
      return hexAddress(state[name].value)
    end)
  end
end

-- The symbol table (emu.symbol_table) is deliberately not called: it does not
-- accept a machine on this build and hangs the script when called the documented
-- way. The address space above is the read path the adapter will use.

-- Execution control. Reported, not trusted: the plan keeps the MAME adapter
-- non-blocking, and this is the measurement that says why.
local debug = nil
attempt("debug.available", function()
  debug = manager.machine.devices["maincpu"].debug
  return tostring(debug ~= nil)
end)

if debug ~= nil then
  attempt("debug.step", function()
    local before = state["PC"].value
    debug:step(1)
    local after = state["PC"].value
    if after == before then return "nomove" end
    return "moved " .. hexAddress(before) .. "->" .. hexAddress(after)
  end)
end

-- bpset is not called. It blocks indefinitely on this build and a probe that can
-- hang cannot be a test.

say("end")

if report ~= nil then
  report:close()
end