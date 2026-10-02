-- Why "callback function could not be found"?
--
-- The binary contains "could not be found" and "callback function" as separate
-- fragments, plus "GetFunction": the message is concatenated at runtime and Mesen
-- looks the function up through a path that fails. The expected argument shape may
-- therefore be something other than a Lua function.
--
-- Each shape is tried separately and reported. A failure for one must not stop the
-- others: this is an investigation, not a test.

local done = false
local function finish(code)
  if done then return end
  done = true
  pcall(function() emu.stop(code) end)
end

function named_function() end
function named_function_with_return() return nil end

local shapes = {
  { name = "anonymous_function",  value = function() end },
  { name = "named_function",      value = named_function },
  { name = "name_as_text",        value = "named_function" },
  { name = "explicit_global",     value = _G["named_function"] },
  { name = "callable_table",      value = setmetatable({}, { __call = function() end }) },
}

for _, shape in ipairs(shapes) do
  local ok, res = pcall(emu.addEventCallback, emu.eventType.startFrame, shape.value)
  print(string.format("event_%-22s %s -> %s", shape.name,
    ok and "ACCEPTED" or "REFUSED", tostring(res)))
end

-- Memory, using the shape that would have worked for events.
local okM, resM = pcall(emu.addMemoryCallback, 0x0800, 1,
  emu.callbackType.write, named_function)
print("memory_named_function     " .. (okM and "ACCEPTED" or "REFUSED") .. " -> " .. tostring(resM))

local okM2, resM2 = pcall(emu.addMemoryCallback, 0x0800, 1,
  emu.callbackType.write, "named_function")
print("memory_name_as_text       " .. (okM2 and "ACCEPTED" or "REFUSED") .. " -> " .. tostring(resM2))

print("end = shapes tested")
finish(0)