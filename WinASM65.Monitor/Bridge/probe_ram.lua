-- Probe: which emu.* path actually observes NES RAM on MesenCE 2.2.1.
--
-- Why this probe exists. The bridge reads everything through
-- emu.memType.nesDebug (264), a type measured as the only side effect free
-- read. That measurement was taken on $8000, inside PRG ROM. Applied to
-- $0000-$07FF it returns zero for all 2048 bytes while the emulated CPU is
-- demonstrably executing, so nesDebug does not cover RAM.
--
-- A write/read round trip cannot detect this: reading back what was just
-- written only proves the read and the write agree, and a shadow space agrees
-- with itself perfectly. So the identity has to be established against RAM the
-- CPU wrote on its own.
--
-- Matrix, printed as behaviour rather than as names, because the enum labels are
-- not documented and guessing them is what caused the original mistake:
--
--   ROM column  non zero -> this type addresses PRG ROM
--   RAM column  5A       -> writing through this type then reading through it
--                          hits the same live cell
--   MIXED                  -> writes RAM, reads ROM: silent corruption, the
--                          worst case, and the one to look for first

local HOST = "127.0.0.1"
local PORT = 45679

local function hex2(v)
  return string.format("%02X", v)
end

local CANDIDATES = {}
for t = 0, 12 do CANDIDATES[#CANDIDATES + 1] = t end
for t = 255, 275 do CANDIDATES[#CANDIDATES + 1] = t end

local report = {}

local function try_read(address, memtype)
  local ok, value = pcall(emu.read, address, memtype)
  if not ok then return nil, tostring(value) end
  return value, nil
end

local function try_write(address, value, memtype)
  local ok, err = pcall(emu.write, address, value, memtype)
  if not ok then return false, tostring(err) end
  return true, nil
end

-- Section A: what does each type see at $8000, inside PRG ROM.
for _, t in ipairs(CANDIDATES) do
  local value, err = try_read(0x8000, t)
  if value ~= nil then
    report[#report + 1] = string.format("ROM type=%d value=%s", t, hex2(value))
  end
end

-- Section B: default read, no memory type at all.
local def8000, defErr1 = pcall(emu.read, 0x8000)
local def0200, defErr2 = pcall(emu.read, 0x0200)
if def8000 then
  report[#report + 1] = string.format("DEFAULT $8000 = %s", hex2(def8000))
else
  report[#report + 1] = "DEFAULT $8000 refused: " .. tostring(defErr1)
end
if def0200 then
  report[#report + 1] = string.format("DEFAULT $0200 = %s", hex2(def0200))
else
  report[#report + 1] = "DEFAULT $0200 refused: " .. tostring(defErr2)
end

-- Section C: write 5A through each type, read back through the same type and
-- through the default read. Agreement is the signal.
for _, t in ipairs(CANDIDATES) do
  local wrote, werr = try_write(0x0200, 0x5A, t)
  if not wrote then
    report[#report + 1] = string.format("WRITE type=%d refused: %s", t, werr)
  else
    local same = try_read(0x0200, t)
    local viaDefault = pcall(emu.read, 0x0200)
    report[#report + 1] = string.format("WRITE type=%d same=%s default=%s",
      t, same and hex2(same) or "nil", viaDefault and hex2(viaDefault) or "nil")
  end
end

-- Section D: emu.readWord, the other documented reader.
local okw, word = pcall(emu.readWord, 0x0200)
if okw then
  report[#report + 1] = string.format("READWORD $0200 = %04X", word)
else
  report[#report + 1] = "READWORD $0200 refused: " .. tostring(word)
end

local function dump()
  for _, line in ipairs(report) do
    print("PROBE " .. line)
  end
end

local ok, socket = pcall(require, "socket.core")
if ok then
  local server = socket.tcp()
  server:settimeout(2.0)
  local bound = pcall(function() server:bind(HOST, PORT) end)
  if bound then
    pcall(function() server:listen(1) end)
    local client = server:accept()
    if client ~= nil then
      client:settimeout(2.0)
      for _, line in ipairs(report) do
        pcall(function() client:send(line .. "\n") end)
      end
      pcall(function() client:close() end)
    end
    pcall(function() server:close() end)
  end
end

dump()
emu.stop(0)