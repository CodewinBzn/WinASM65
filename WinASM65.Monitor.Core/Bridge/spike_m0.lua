-- M0 spike: empirically verify the MesenCE 2.2.1 capabilities the whole plan
-- depends on, before writing the bridge for real.
--
-- Nothing here is carried over verbatim into the final bridge. This script tests,
-- it does not work. The report goes to disk and not to stdout: capturing an
-- emulator's standard output is unreliable, and a lost result would look like a
-- capability failure.
--
-- Every capability is recorded explicitly, including the missing ones: a silent
-- absence would suggest the bridge is written when it does not work.

local report = {}

local function note(key, value)
  local line = key .. " = " .. tostring(value)
  report[#report + 1] = line
  -- stdout first: file writing requires the "Allow access to I/O and OS
  -- functions" permission, which can be refused. If it is, the spike must stay
  -- readable, otherwise one cannot tell "capability absent" from
  -- "permission missing".
  print(line)
end

local function note_table(prefix, t)
  if type(t) ~= "table" then
    note(prefix, "(not a table)")
    return
  end
  local keys = {}
  for k, _ in pairs(t) do keys[#keys + 1] = tostring(k) end
  table.sort(keys)
  for _, k in ipairs(keys) do
    note(prefix .. "." .. k, t[k])
  end
end

-- 1. Interpreter.
note("lua", _VERSION)

-- 2. I/O access.
-- Distinguishes "io absent" from "require present but refused": that distinction
-- decides whether LuaSocket is an option or a dead end.
probe = function(name, fn)
  local ok, value = pcall(fn)
  if not ok then
    note(name, "ERROR: " .. tostring(value))
    return nil
  end
  note(name, value)
  return value
end

probe("io_available", function()
  if type(io) ~= "table" then return "io ABSENT" end
  return "table"
end)
probe("os_available", function() return type(os) end)
probe("require_callable", function() return type(require) end)
probe("io_open_result", function()
  local f, err = io.open("C:/Users/ghani/AppData/Local/Temp/kilo/spike-m0-io.txt", "w")
  if f then f:close() return "WRITING ALLOWED" end
  return "REFUSED: " .. tostring(err)
end)
probe("script_folder", function() return tostring(emu.getScriptDataFolder()) end)

-- 3. LuaSocket, the gateway. Without it the whole protocol falls.
for _, name in ipairs({ "socket.core", "socket", "socket.http" }) do
  probe("require_" .. string.gsub(name, "[.]", "_"), function()
    local ok, mod = pcall(require, name)
    if not ok then return "ERROR: " .. tostring(mod) end
    return "OK type=" .. type(mod) .. " tcp=" .. tostring(type(mod) == "table" and mod.tcp)
  end)
end

-- 4. Memory, under the real names.
local nesDebug
probe("emu_memType", function()
  if type(emu.memType) ~= "table" then return "table absent, type=" .. type(emu.memType) end
  local names = {}
  for k, v in pairs(emu.memType) do names[#names + 1] = tostring(k) .. "=" .. tostring(v) end
  table.sort(names)
  return table.concat(names, " ")
end)

-- Measured 2026-10-01: there is no "cpuDebug". Each system has its own "Debug"
-- type, and for NES that is nesDebug. The documentation announces
-- "memType.cpuDebug" for a different product.
note("cpuDebug_exists", tostring(type(emu.memType) == "table" and emu.memType.cpuDebug))
nesDebug = type(emu.memType) == "table" and emu.memType.nesDebug or nil
note("nesDebug", tostring(nesDebug))

if nesDebug then
  probe("read_8000_nesDebug", function() return tostring(emu.read(0x8000, nesDebug)) end)
  probe("readWord_8000_nesDebug", function() return tostring(emu.readWord(0x8000, nesDebug)) end)
  probe("write_then_readback", function()
    local ok = pcall(emu.write, 0x0800, 0x5A, nesDebug)
    if not ok then return "write refused" end
    return "wrote 5A, read back " .. tostring(emu.read(0x0800, nesDebug))
  end)
end

-- Breakpoints: the real names, and what the return value is worth.
probe("callbackType", function()
  if type(emu.callbackType) ~= "table" then return "type=" .. type(emu.callbackType) end
  local names = {}
  for k, v in pairs(emu.callbackType) do names[#names + 1] = tostring(k) .. "=" .. tostring(v) end
  table.sort(names)
  return table.concat(names, " ")
end)

-- Mesen refuses an anonymous callback: "callback function could not be found". A
-- named global is refused too, and the designation is just "not found" even for an
-- event callback, which does not touch memory. So the "global" here is deliberate:
-- a local function is not visible from the C# side.
function spike_write_callback()
  return 0x5A
end

probe("addMemoryCallback_anonymous", function()
  local t = emu.callbackType and emu.callbackType.write
  local ok, res = pcall(emu.addMemoryCallback, 0x0800, 1, t, function() return 0x5A end)
  return ok and ("ACCEPTED handle=" .. tostring(res)) or ("REFUSED: " .. tostring(res))
end)

probe("addMemoryCallback_named_global", function()
  local t = emu.callbackType and emu.callbackType.write
  if t == nil then return "callbackType.write absent" end
  local ok, handle = pcall(emu.addMemoryCallback, 0x0800, 1, t, spike_write_callback)
  if not ok then return "REFUSED: " .. tostring(handle) end
  return "handle=" .. tostring(handle)
end)

-- 5. Snapshots.
note("emu_getState", type(emu and emu.getState))
note("emu_setState", type(emu and emu.setState))

-- Report to disk.
local path = "C:/Users/ghani/AppData/Local/Temp/kilo/spike-m0-rapport.txt"
local file, err = io.open(path, "w")
if file then
  file:write(table.concat(report, "\n"))
  file:close()
end
note("report_written", file ~= nil)

-- 6. Exit code: what makes the spike usable in CI. A report written but a null exit
-- code would be a test that always passes.
if emu and emu.stop then
  emu.stop(0)
else
  note("emu_stop", "ABSENT: no exit code, the spike is judged on the report")
end