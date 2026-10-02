-- Probe 3: liveness, under the 1 second watchdog.
--
-- Probe 2 printed nothing because it was killed before reaching its first print:
-- 8192 emu.read calls is CPU bound Lua, and MesenCE aborts a script that runs
-- longer than a second. Blocking socket calls are not counted, which is why the
-- bridge survives minutes of serving but dies in a flash loop.
--
-- So: wait by blocking, not by computing, and print after every step so a probe
-- killed mid-way still leaves evidence behind.

local HOST = "127.0.0.1"

local function sleep_seconds(seconds)
  local ok, socket = pcall(require, "socket.core")
  if not ok then return end
  local server = socket.tcp()
  server:settimeout(seconds)
  pcall(function() server:bind(HOST, 45681) end)
  pcall(function() server:listen(1) end)
  pcall(function() server:accept() end)
  pcall(function() server:close() end)
end

local function dump(base, length)
  local pieces = {}
  for i = 0, length - 1 do
    local ok, value = pcall(emu.read, base + i)
    pieces[#pieces + 1] = ok and string.format("%02X", value) or "??"
  end
  return table.concat(pieces)
end

local function scan(base, length, memtype)
  local pieces = {}
  local nonzero = 0
  for i = 0, length - 1 do
    local ok, value = pcall(emu.read, base + i, memtype)
    if ok then
      if value ~= 0 then nonzero = nonzero + 1 end
      pieces[#pieces + 1] = string.format("%02X", value)
    end
  end
  return nonzero, table.concat(pieces)
end

print("PROBE depart")
sleep_seconds(5)
print("PROBE 5s ecoules, le jeu a tourne")

for _, t in ipairs({ 8, 264 }) do
  local n, hex = scan(0x0200, 32, t)
  print(string.format("PROBE oam $0200 type=%d nonZero=%d %s", t, n, hex))
end

for _, t in ipairs({ 8, 264 }) do
  local n, hex = scan(0x0000, 32, t)
  print(string.format("PROBE zp  $0000 type=%d nonZero=%d %s", t, n, hex))
end

for _, t in ipairs({ 8, 264 }) do
  local n, hex = scan(0x8000, 16, t)
  print(string.format("PROBE rom $8000 type=%d nonZero=%d %s", t, n, hex))
end

print("PROBE fin")
emu.stop(0)