-- Probe 2: does any memory type observe RAM the CPU wrote on its own?
--
-- Probe 1 established that emu.read requires a memtype (there is no default
-- read), that only types 8 and 264 return PRG ROM at $8000, and that types 8, 9,
-- 264, 265 round trip a RAM write. A round trip is not proof of liveness, so this
-- probe waits for the game to actually run and then reads RAM back.
--
-- The wait is a blocking accept on a port nobody connects to, because a busy Lua
-- loop would starve the emulated CPU and defeat the point of the probe.

local HOST = "127.0.0.1"
local PORT = 45680
local RAM = { 8, 9, 264, 265 }
local report = {}

local function sleep_seconds(seconds)
  local ok, socket = pcall(require, "socket.core")
  if not ok then return end
  local server = socket.tcp()
  server:settimeout(1.0)
  pcall(function() server:bind(HOST, PORT + 1) end)
  pcall(function() server:listen(1) end)
  local deadline = os.time() + seconds
  while os.time() < deadline do
    pcall(function() server:accept() end)
  end
  pcall(function() server:close() end)
end

local function nonzero_in(address, length, memtype)
  local count = 0
  local sample = ""
  for i = 0, length - 1 do
    local ok, value = pcall(emu.read, address + i, memtype)
    if ok and value ~= 0 then
      count = count + 1
      if #sample < 16 then sample = sample .. string.format("%02X", value) end
    end
  end
  return count, sample
end

sleep_seconds(4)
report[#report + 1] = "--- after 4s of play ---"

for _, t in ipairs(RAM) do
  local count, sample = nonzero_in(0x0000, 2048, t)
  report[#report + 1] = string.format("RAM 2048 type=%d nonZero=%d sample=%s", t, count, sample)
end

for _, t in ipairs(RAM) do
  local rom = pcall(emu.read, 0x8000, t)
  local romValue = rom and string.format("%02X", rom) or "refused"
  report[#report + 1] = string.format("ROM $8000 type=%d = %s", t, romValue)
end

-- Regions worth isolating: the stack at $0100-$01FF and the OAM shadow at $0200.
for _, t in ipairs(RAM) do
  local stack = nonzero_in(0x0100, 256, t)
  local oam = nonzero_in(0x0200, 256, t)
  report[#report + 1] = string.format(
    "type=%d pile$0100=%d oam$0200=%d", t, stack, oam)
end

for _, line in ipairs(report) do print("PROBE " .. line) end
emu.stop(0)