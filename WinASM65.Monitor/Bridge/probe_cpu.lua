-- Probe 4: is the emulated CPU actually executing while the script runs?
--
-- Every anomaly so far has one explanation that fits all of them: in
-- --testrunner mode with a script loaded, the game never runs. RAM is untouched,
-- so it reads as zero, while ROM is static and reads correctly. The bridge would
-- then be blameless and the invocation would be the defect.
--
-- The registry tells us directly: if the program counter moves, the CPU runs.

local HOST = "127.0.0.1"

local function sleep_seconds(seconds)
  local ok, socket = pcall(require, "socket.core")
  if not ok then return end
  local server = socket.tcp()
  server:settimeout(seconds)
  pcall(function() server:bind(HOST, 45682) end)
  pcall(function() server:listen(1) end)
  pcall(function() server:accept() end)
  pcall(function() server:close() end)
end

local function snapshot(label)
  local ok, state = pcall(emu.getCpuState)
  if not ok then
    print("PROBE " .. label .. " getCpuState refused: " .. tostring(state))
    return nil
  end
  local pieces = {}
  for key, value in pairs(state) do
    pieces[#pieces + 1] = string.format("%s=%s", tostring(key), tostring(value))
  end
  table.sort(pieces)
  print("PROBE " .. label .. " " .. table.concat(pieces, " "))
  return state
end

local function pc_of(state)
  if type(state) ~= "table" then return nil end
  for key, value in pairs(state) do
    if string.find(tostring(key), "programCounter", 1, true) then return value end
  end
  return nil
end

print("PROBE start")
local first = snapshot("t0")
sleep_seconds(2)
local second = snapshot("t2")
sleep_seconds(2)
local third = snapshot("t4")

local a, b, c = pc_of(first), pc_of(second), pc_of(third)
print(string.format("PROBE PC t0=%s t2=%s t4=%s", tostring(a), tostring(b), tostring(c)))
if a ~= nil and b ~= nil and c ~= nil then
  if a == b and b == c then
    print("PROBE VERDICT: PC FROZEN, the CPU does not run")
  else
    print("PROBE VERDICT: the CPU advances")
  end
end

print("PROBE end")
emu.stop(0)