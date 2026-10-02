-- Is the callback function unfindable, or do only user written functions fail?
--
-- "print", "type" and "ipairs" are native Lua functions: they cannot be collected,
-- and they live in the standard library. If they fail too, the resolution mechanism
-- is broken for everything and the only possible cause is the headless environment.
-- If they succeed, only the garbage collector hypothesis remains.
--
-- MesenCE 2.2.1, --testRunner mode.
--
-- Absolute rule for this file: any table construction containing a call must be
-- protected. On the previous round "load" was absent from the sandbox and killed
-- the script during construction, therefore before any output, and without ever
-- reaching emu.stop: the emulator stayed blocked. An unprotected probe is one way
-- to reproduce that trap.

local done = false
local function finish(code)
  if done then return end
  done = true
  pcall(function() emu.stop(code) end)
end

local function note(name, value)
  print(string.format("%-24s %s", name, tostring(value)))
end

function try_shape(name, value, target)
  local ok, res = pcall(target.addEventCallback, target.eventType.startFrame, value)
  note(name, ok and ("ACCEPTED handle=" .. tostring(res)) or ("REFUSED: " .. tostring(res)))
end

note("eventType_startFrame", tostring(emu.eventType and emu.eventType.startFrame))
note("load_available", tostring(type(load)))
note("print_type", type(print))
note("type_type", type(type))

try_shape("native_print", print, emu)
try_shape("native_type", type, emu)
try_shape("native_ipairs", ipairs, emu)

local okLoad, dynamic = pcall(function()
  if type(load) ~= "function" then return nil end
  return load("return function() end")()
end)
if okLoad and type(dynamic) == "function" then
  try_shape("loaded_dynamically", dynamic, emu)
else
  note("loaded_dynamically", "UNAVAILABLE")
end

local holder = {}
holder.f = function() end
try_shape("stored_in_table", holder.f, emu)

note("end", "all shapes tested")
finish(0)