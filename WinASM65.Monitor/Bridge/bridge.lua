-- Lua bridge for MesenCE 2.2.1.
--
-- The script holds no intelligence: it turns one protocol line into one emu.*
-- call and returns the answer. All the logic stays on the .NET side, which keeps
-- this file short and the monitor testable without an emulator.
--
-- What was measured on MesenCE 2.2.1, and what dictates every choice below:
--
--   emu.read / emu.readWord / emu.write / emu.writeWord  -> available
--   emu.memType.nesDebug                                  -> the only side effect free read
--   emu.getState / emu.setState / emu.loadSavestate       -> available
--   emu.stop(code)                                        -> hands control back to the shell
--
--   emu.addMemoryCallback / emu.addEventCallback          -> UNUSABLE
--   emu.breakExecution                                    -> "cannot be called outside a callback"
--   emu.pause                                             -> DOES NOT EXIST
--   emu.step / emu.resume                                 -> refused outside a callback
--
-- Consequence: this bridge serves memory and snapshots, and explicitly refuses
-- everything else. It does not pretend a missing capability exists: an "OK" on a
-- breakpoint that was never set would be exactly the silent failure the whole
-- project refuses.

local PORT = 45678
local HOST = "127.0.0.1"

-- Socket timeout, used as a keep-alive poll and not as a shutdown signal. The
-- .NET side is an interactive REPL and sits idle between commands by design, so
-- an expired timeout must be retried rather than treated as a lost connection.
local TIMEOUT = 1.0

-- Work per reply, in bytes. MesenCE kills a script that runs too long in a
-- single pass, and a large read is the only unbounded loop here. The .NET side
-- splits large reads into chunks of this size, so this is a safety net, not the
-- normal path.
local CHUNK = 256

-- Bounds. The bridge runs inside the emulator process: an unbounded request
-- freezes the machine, and the user thinks it crashed.
local READ_MAX = 4096
local WRITE_MAX = 4096

local MEMORY_SPACE = 0x10000

function reply(socket, line)
  socket:send(line .. "\n")
end

function fail(socket, reason)
  reply(socket, "ERR " .. reason)
end

-- Checks that a range fits in the machine's 16-bit space. Overflow must be
-- refused explicitly: truncating in silence would display false data.
function range_valid(address, length)
  return address >= 0 and length >= 0 and (address + length) <= MEMORY_SPACE
end

function read_memory(address, length)
  local data = {}
  for i = 0, length - 1 do
    data[i + 1] = emu.read(address + i, emu.memType.nesDebug)
  end
  return data
end

function write_memory(address, bytes)
  for i = 1, #bytes do
    emu.write(address + (i - 1), bytes[i], emu.memType.nesDebug)
  end
end

function to_hex(bytes)
  local pieces = {}
  for i = 1, #bytes do
    pieces[i] = string.format("%02X", bytes[i])
  end
  return table.concat(pieces)
end

function parse_address(text)
  if text == nil or text == "" then return nil end
  local rest = text
  local base = 16
  if string.sub(rest, 1, 1) == "$" then
    rest = string.sub(rest, 2)
  elseif string.lower(string.sub(rest, 1, 2)) == "0x" then
    rest = string.sub(rest, 3)
  elseif string.sub(rest, 1, 1) == "d" then
    rest = string.sub(rest, 2)
    base = 10
  end
  if rest == "" then return nil end
  local value = tonumber(rest, base)
  if value == nil then return nil end
  if value < 0 or value > 0xFFFF then return nil end
  return value
end

function parse_hex(text)
  if text == nil or text == "" then return nil end
  local clean = string.gsub(text, " ", "")
  if #clean % 2 ~= 0 then return nil end
  local bytes = {}
  for i = 1, #clean, 2 do
    local value = tonumber(string.sub(clean, i, i + 1), 16)
    if value == nil then return nil end
    bytes[#bytes + 1] = value
  end
  return bytes
end

function handle(socket, line)
  local words = {}
  for word in string.gmatch(line, "%S+") do words[#words + 1] = word end

  local command = string.upper(words[1] or "")

  if command == "PING" then
    reply(socket, "OK MesenCE 2.2.1 nesDebug")
    return
  end

  if command == "READ" then
    local address = parse_address(words[2])
    local length = tonumber(words[3])
    if address == nil then fail(socket, "invalid address: " .. tostring(words[2])); return end
    if length == nil then fail(socket, "invalid length: " .. tostring(words[3])); return end
    if length < 0 or length > READ_MAX then
      fail(socket, "length out of bounds (0.." .. READ_MAX .. "): " .. length)
      return
    end
    if not range_valid(address, length) then
      fail(socket, "access outside the memory space: " .. address .. " + " .. length)
      return
    end
    reply(socket, "OK " .. to_hex(read_memory(address, length)))
    return
  end

  if command == "WRITE" then
    local address = parse_address(words[2])
    local bytes = parse_hex(words[3])
    if address == nil then fail(socket, "invalid address: " .. tostring(words[2])); return end
    if bytes == nil then fail(socket, "invalid hex"); return end
    if #bytes > WRITE_MAX then
      fail(socket, "write out of bounds (max " .. WRITE_MAX .. "): " .. #bytes)
      return
    end
    if not range_valid(address, #bytes) then
      fail(socket, "access outside the memory space: " .. address .. " + " .. #bytes)
      return
    end
    local ok, message = pcall(write_memory, address, bytes)
    if not ok then fail(socket, "write refused by the emulator: " .. tostring(message)); return end
    reply(socket, "OK")
    return
  end

  if command == "CPU" then
    -- Registers and the cycle counter, so the monitor can tell a cold machine from
    -- a broken read. MesenCE exposes emu.getCpuState with short keys: pc, a, x, y,
    -- sp, ps, cycleCount.
    --
    -- This matters because the CPU cannot be advanced from a script: no run, no
    -- execute, no tick exists in the 64 entry emu table. A machine sitting on
    -- cycleCount 7 with pc at the reset vector is the expected state here, and the
    -- monitor has to be able to say so rather than display empty RAM and let the
    -- user conclude the bridge is broken.
    local ok, state = pcall(emu.getCpuState)
    if not ok or state == nil then
      fail(socket, "CPU state refused by the emulator: " .. tostring(state))
      return
    end
    local function field(name, fallback)
      local value = state[name]
      if value == nil then return fallback end
      return value
    end
    reply(socket, string.format(
      "OK pc=$%04X a=%02X x=%02X y=%02X sp=%02X ps=%02X cycles=%d",
      field("pc", 0), field("a", 0), field("x", 0), field("y", 0),
      field("sp", 0), field("ps", 0), field("cycleCount", 0)))
    return
  end

  if command == "ROM" then
    local ok, info = pcall(emu.getRomInfo)
    if not ok or type(info) ~= "table" then
      fail(socket, "ROM info refused by the emulator: " .. tostring(info))
      return
    end
    local pieces = {}
    -- Every scalar field, not a guessed list of names. The first attempt at this
    -- command named six fields and returned an empty answer, because MesenCE
    -- names none of them that way. Dumping what is actually there cannot go stale
    -- the way a hard coded list does.
    for key, value in pairs(info) do
      if type(value) ~= "table" and type(value) ~= "function" then
        pieces[#pieces + 1] = tostring(key) .. "=" .. tostring(value)
      end
    end
    table.sort(pieces)
    reply(socket, "OK " .. table.concat(pieces, " "))
    return
  end

  if command == "STATE" then
    local action = string.upper(words[2] or "")
    if action == "SAVE" then
      local state = emu.getState()
      if state == nil then
        fail(socket, "STATE SAVE: emu.getState returned nil")
        return
      end
      -- emu.getState returns a table, not necessarily a contiguous array of
      -- bytes. Walking it as if every key were an index guarantees nothing, and
      -- answering "OK " with an empty payload would make the caller believe a
      -- snapshot was saved when it was not. A named refusal beats an empty
      -- success.
      local bytes = {}
      local i = 1
      while state[i] ~= nil do
        bytes[i] = state[i]
        i = i + 1
      end
      if #bytes == 0 then
        fail(socket, "STATE SAVE: emu.getState does not return a contiguous array of bytes (type "
          .. type(state) .. "); the snapshot is not serialized rather than returned empty")
        return
      end
      reply(socket, "OK " .. to_hex(bytes))
      return
    end
    if action == "LOAD" then
      local bytes = parse_hex(words[3])
      if bytes == nil then fail(socket, "invalid hex"); return end
      local ok, message = pcall(emu.setState, bytes)
      if not ok then fail(socket, "snapshot refused: " .. tostring(message)); return end
      reply(socket, "OK")
      return
    end
    fail(socket, "STATE expects SAVE or LOAD <hex>")
    return
  end

  -- Capabilities measured as unavailable. The refusal is named and explained:
  -- that is what distinguishes a known limitation from a bug.
  local limits = {
    PAUSE = "MesenCE 2.2.1 does not expose emu.pause",
    RESUME = "emu.resume refuses calls outside a callback, unavailable in testrunner mode",
    STEP = "emu.step expects parameters and refuses calls outside a callback",
    BREAK = "emu.addMemoryCallback refuses every function, named ones included: breakpoints cannot be set on this build",
    RESET = "emu.reset changes global state without callback context: reserved for manual validation",
  }
  if limits[command] then
    fail(socket, unavailable_message(command, limits[command]))
    return
  end

  fail(socket, "unknown command: " .. command)
end

function unavailable_message(command, reason)
  return command .. " unavailable: " .. reason
end

-- Direction of the connection: the .NET side must initiate, because it is the
-- interactive one. The REPL starts when the user feels like it, not when the
-- emulator starts. So the bridge listens and waits for a single connection rather
-- than dialing the monitor.
--
-- One client at a time: two simultaneous clients would make no sense against a
-- single machine.
function main_loop()
  local ok, socket = pcall(require, "socket.core")
  if not ok then
    print("BRIDGE IMPOSSIBLE: socket.core is missing. " .. tostring(socket))
    emu.stop(1)
    return
  end

  local server = socket.tcp()
  server:settimeout(TIMEOUT)

  local bound, message = pcall(function()
    server:bind(HOST, PORT)
    server:listen(1)
  end)
  if not bound then
    print("BRIDGE IMPOSSIBLE: cannot listen on " .. HOST .. ":" .. PORT .. ". " .. tostring(message))
    emu.stop(2)
    return
  end

  print("BRIDGE WAITING: " .. HOST .. ":" .. PORT)

  -- Wait for the client, tolerating an arbitrary delay. The monitor retries on
  -- its side too, but a bridge that gives up after one timeout would report an
  -- impossible "no connection received" for a client that is merely slow.
  local client
  while true do
    local accepted, refusal = server:accept()
    if accepted ~= nil then
      client = accepted
      break
    end
    if refusal ~= "timeout" then
      print("BRIDGE IMPOSSIBLE: cannot accept a connection. " .. tostring(refusal))
      emu.stop(3)
      return
    end
  end

  client:settimeout(TIMEOUT)
  print("BRIDGE CONNECTED")

  -- Serve until the .NET side genuinely closes.
  --
  -- Only a real disconnection ends the loop. An expired timeout means the user is
  -- typing, nothing more: ending there would kill the session on every pause, and
  -- emu.stop would take the emulator down with it. Looping on a socket that is
  -- actually dead is not a risk either, because a closed socket reports "closed"
  -- rather than timing out.
  while true do
    local line, received, err = client:receive("*l")
    if line ~= nil then
      if line ~= "" then
        local handled, error_message = pcall(handle, client, line)
        if not handled then
          fail(client, "internal bridge error: " .. tostring(error_message))
        end
      end
    elseif received ~= "timeout" then
      print("BRIDGE: connection closed (" .. tostring(received or err) .. ")")
      break
    end
  end

  client:close()
  server:close()
  emu.stop(0)
end

main_loop()