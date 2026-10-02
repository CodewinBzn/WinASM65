-- Bridge for Mesen2 (github.com/SourMesen/Mesen2), the modern Mesen.
--
-- Measured on Mesen2 2.1.1, Windows x64, against a WinASM65-built homebrew ROM:
--
--   headless        Mesen.exe --testRunner <script> <rom> -novideo -noaudio
--                   -noinput -enablestdout -donotsavesettings
--                   Boots the cartridge, so the reference workload is NES native.
--
--   memory          emu.read / emu.write on emu.memType.nesDebug, side effect free
--                   and covering PRG ROM, RAM and the register mirrors.
--
--   cpu state       emu.getState() returns a FLAT table keyed "cpu.pc", "cpu.a",
--                   "cpu.x", "cpu.y", "cpu.sp", "cpu.ps", "cpu.cycleCount",
--                   alongside PPU, APU and mapper state.
--
--   breakpoints     emu.addMemoryCallback with callbackType.exec / .read / .write.
--
--   execution       emu.breakExecution / emu.resume / emu.step exist but refuse
--                   to be called from the script body: "This function cannot be
--                   called outside a callback". All three work inside an
--                   event callback. Every command below is therefore served from
--                   inside one, which is why nothing here blocks while the machine
--                   is running.
--
-- Two consequences shape the whole file.
--
-- First: inputPolled only fires while the machine runs. Once execution is broken
-- no further callback arrives, so a bridge that paused and waited for the next
-- tick would wait forever. Pause is therefore served inside the very tick that
-- pauses: break, answer, then block in the receive loop until RESUME. Blocking
-- there is not the MesenCE mistake the header warns about, because the machine
-- is frozen and nothing is being starved.
--
-- Second: ScriptingContext::ExecutionCountHook aborts a script whose pass runs
-- longer than Debug.ScriptWindow.ScriptTimeout seconds, 1 by default, so a long
-- pause needs that setting raised. The launcher writes a settings.json with
-- AllowIoOsAccess, AllowNetworkAccess and ScriptTimeout beside Mesen.exe; io,
-- require and os are nil otherwise and no socket can be opened at all.
--
-- The protocol is the monitor's existing one, so this is a second backend rather
-- than a second protocol. Where MesenCE refused by name because the API was
-- missing, nothing is refused here except what is genuinely impossible: a write
-- into read-only space, which is detected by reading the bytes back.

local NAME = "Mesen2"
local VERSION = "2.1.1"

local DEFAULT_PORT = 45678

-- Measured bound of the protocol, mirrored from MonitorProtocol.cs. The bridge
-- checks it too: a request that would freeze the emulator must be refused by
-- whoever received it, not by whoever sent it.
local MAX_READ = 4096
local MAX_WRITE = 4096
local MAX_STATE = 131072

-- Commands drained per tick while the machine runs. One tick is about 16 ms, and
-- every command in the drain freezes the machine for the act, so the drain is
-- bounded: a 4 KiB read arrives as 16 chunks and must not hold the machine for
-- longer than it takes to answer it.
local MAX_DRAIN = 64

-- Seconds between progress notes while paused, so a pause that is not a hang is
-- visible in the log.
local PAUSE_REPORT_SECONDS = 8

local ADDRESS_MAX = 0xFFFF

local function fail(text)
  return "ERR " .. text
end

local function log(text)
  pcall(function() emu.log("[mesen2] " .. text) end)
end

---------------------------------------------------------------------- helpers

local DIGITS = "0123456789ABCDEF"

local function to_hex(bytes)
  local out = {}
  for i = 1, #bytes do
    local value = bytes[i]
    out[#out + 1] = DIGITS:sub(math.floor(value / 16) + 1, math.floor(value / 16) + 1)
    out[#out + 1] = DIGITS:sub(value % 16 + 1, value % 16 + 1)
  end
  return table.concat(out)
end

local function from_hex(text)
  local cleaned = text:gsub("%s", "")
  if #cleaned % 2 ~= 0 or #cleaned == 0 then return nil end
  local bytes = {}
  for i = 1, #cleaned, 2 do
    local value = tonumber(cleaned:sub(i, i + 1), 16)
    if value == nil then return nil end
    bytes[#bytes + 1] = value
  end
  return bytes
end

-- The monitor writes "$8000", "0x8000", "d32768" or a bare hex value, and
-- addresses its bridge with the same grammar. A prefix is always authoritative:
-- "$FFF0" means 65520, not 8000.
local function parse_address(text)
  if text == nil then return nil end
  local value = text
  local first = value:sub(1, 1)
  if first == "$" then
    value = value:sub(2)
    return tonumber(value, 16)
  end
  if value:sub(1, 2):lower() == "0x" then
    return tonumber(value:sub(3), 16)
  end
  if first == "d" or first == "D" then
    return tonumber(value:sub(2), 10)
  end
  return tonumber(value, 16)
end

local function in_range(address, length)
  return address ~= nil and length ~= nil and address >= 0 and length >= 0
    and address <= ADDRESS_MAX and address + length <= ADDRESS_MAX + 1
end

------------------------------------------------------------------- memory

local function read_memory(address, length)
  local bytes = {}
  for i = 0, length - 1 do
    bytes[i + 1] = emu.read(address + i, emu.memType.nesDebug)
  end
  return bytes
end

local function write_memory(address, bytes)
  for i = 1, #bytes do
    emu.write(address + i - 1, bytes[i], emu.memType.nesDebug)
  end
end

-------------------------------------------------------------------- state

-- emu.getState() is a flat table keyed by dotted names, with numbers, booleans
-- and strings among the values. The protocol carries an opaque byte string, so
-- the table is rendered to text, sorted by key, and hex encoded. Sorting is not
-- cosmetic: two round trips must produce identical bytes, or a snapshot compared
-- against itself would differ for no reason.
local function state_to_bytes()
  local state = emu.getState()
  if type(state) ~= "table" then return nil, "the emulator returned no state table" end

  local keys = {}
  for key in pairs(state) do keys[#keys + 1] = tostring(key) end
  table.sort(keys)

  local lines = {}
  for _, key in ipairs(keys) do
    local value = state[key]
    local rendered
    if type(value) == "boolean" then
      rendered = value and "b1" or "b0"
    elseif type(value) == "number" then
      if value == math.floor(value) and math.abs(value) < 2 ^ 53 then
        rendered = "n" .. string.format("%d", value)
      else
        rendered = "n" .. string.format("%.17g", value)
      end
    elseif type(value) == "string" then
      -- Escaped because the separator is a newline and a key is free text.
      rendered = "s" .. value:gsub("\\", "\\\\"):gsub("\n", "\\n"):gsub(";", "\\;")
    else
      return nil, "state value for " .. key .. " is a " .. type(value)
    end
    lines[#lines + 1] = key .. "=" .. rendered
  end

  local text = table.concat(lines, ";")
  local bytes = {}
  for i = 1, #text do bytes[i] = text:byte(i) end
  return bytes
end

local function bytes_to_state(bytes)
  local text = {}
  for i = 1, #bytes do text[i] = string.char(bytes[i]) end
  local joined = table.concat(text)

  local state = {}
  for _, entry in ipairs(joined:gmatch("[^;]+")) do
    local equals = entry:find("=", 1, true)
    if equals == nil then return nil, "malformed state entry: " .. entry end
    local key = entry:sub(1, equals - 1)
    local value = entry:sub(equals + 1)
    local kind = value:sub(1, 1)
    local body = value:sub(2)
    if kind == "b" then
      state[key] = (body == "1")
    elseif kind == "n" then
      state[key] = tonumber(body)
    elseif kind == "s" then
      state[key] = body:gsub("\\;", ";"):gsub("\\n", "\n"):gsub("\\\\", "\\")
    else
      return nil, "unknown state value kind: " .. kind
    end
  end
  return state
end

-------------------------------------------------------------- breakpoints

local KINDS = {
  exec = { id = emu.callbackType.exec, action = "SET" },
  read = { id = emu.callbackType.read, action = "READ" },
  write = { id = emu.callbackType.write, action = "WRITE" },
}

-- address -> kind -> callback id. Two kinds on one address are two callbacks, and
-- both fire: an exec breakpoint on an address that is also written to is not a
-- contradiction, it is two things the user asked for.
local breakpoints = {}

local function install_breakpoint(address, kind)
  local definition = KINDS[kind]
  if definition == nil then return nil, "unknown kind: " .. kind end

  local id = emu.addMemoryCallback(function(accessed, value)
    if definition.action == "SET" then
      pcall(function() emu.breakExecution() end)
      log(string.format("breakpoint hit: %s $%04X", kind, accessed))
      local outcome = serve_blocked("breakpoint " .. kind .. " $" .. string.format("%04X", accessed))
      if outcome == "resume" then
        emu.resume()
      else
        emu.stop(0)
      end
    else
      -- A watchpoint answers by refusing to let the access change anything:
      -- returning a value from the callback replaces the result of the
      -- read or write, so the original value goes back untouched.
      return value
    end
  end, definition.id, address, address, emu.cpuType.nes, emu.memType.nesDebug)

  if id == nil then return nil, "the emulator refused the breakpoint" end
  breakpoints[address] = breakpoints[address] or {}
  breakpoints[address][kind] = id
  return id
end

local function remove_breakpoint(address, kind)
  local byKind = breakpoints[address]
  if byKind == nil or byKind[kind] == nil then
    return false
  end
  emu.removeMemoryCallback(byKind[kind])
  byKind[kind] = nil
  if next(byKind) == nil then breakpoints[address] = nil end
  return true
end

local function describe_breakpoints()
  local parts = {}
  local addresses = {}
  for address in pairs(breakpoints) do addresses[#addresses + 1] = address end
  table.sort(addresses)
  for _, address in ipairs(addresses) do
    local byKind = breakpoints[address]
    local kinds = {}
    for kind in pairs(byKind) do kinds[#kinds + 1] = kind end
    table.sort(kinds)
    for _, kind in ipairs(kinds) do
      parts[#parts + 1] = kind .. " $" .. string.format("%04X", address)
    end
  end
  if #parts == 0 then return "none" end
  return table.concat(parts, ", ")
end

--------------------------------------------------------------- commands

-- Each handler returns the response line. Handlers may set the two flags below
-- to change what happens to the machine after the answer.
local paused_requested = false

local function cpu_line()
  local state = emu.getState()
  if type(state) ~= "table" then return fail("the emulator returned no state table") end
  local cpu = state["cpu.pc"] and state or nil
  if cpu == nil then return fail("the emulator returned no cpu state") end
  return string.format("OK pc=$%04X a=%02X x=%02X y=%02X sp=%02X ps=%02X cycles=%d",
    state["cpu.pc"] or 0, state["cpu.a"] or 0, state["cpu.x"] or 0,
    state["cpu.y"] or 0, state["cpu.sp"] or 0, state["cpu.ps"] or 0,
    state["cpu.cycleCount"] or 0)
end

local function rom_line()
  local info = emu.getRomInfo()
  if type(info) ~= "table" then
    return fail("the emulator described no cartridge")
  end
  local keys = {}
  for key in pairs(info) do keys[#keys + 1] = tostring(key) end
  table.sort(keys)
  local parts = {}
  for _, key in ipairs(keys) do
    local value = info[key]
    if type(value) ~= "table" and type(value) ~= "function" then
      parts[#parts + 1] = key .. "=" .. tostring(value)
    end
  end
  if #parts == 0 then return fail("the cartridge description was empty") end
  return "OK " .. table.concat(parts, " ")
end

local function dispatch(line)
  local tokens = {}
  for token in line:gmatch("%S+") do tokens[#tokens + 1] = token end
  if #tokens == 0 then return fail("empty command") end

  local command = tokens[1]:upper()

  if command == "PING" then
    return "OK " .. NAME .. " " .. VERSION
  end

  if command == "READ" then
    if #tokens ~= 3 then return fail("READ expects <address> <length>") end
    local address = parse_address(tokens[2])
    local length = tonumber(tokens[3], 10)
    if address == nil then return fail("invalid address: " .. tokens[2]) end
    if length == nil then return fail("invalid length: " .. tokens[3]) end
    if length < 0 or length > MAX_READ then
      return fail("length out of bounds (0.." .. MAX_READ .. "): " .. length)
    end
    if not in_range(address, length) then
      return fail(string.format("range $%04X+%d exceeds the address space", address, length))
    end
    return "OK " .. to_hex(read_memory(address, length))
  end

  if command == "WRITE" then
    if #tokens ~= 3 then return fail("WRITE expects <address> <hex>") end
    local address = parse_address(tokens[2])
    if address == nil then return fail("invalid address: " .. tokens[2]) end
    local bytes = from_hex(tokens[3])
    if bytes == nil then return fail("invalid hex") end
    if #bytes > MAX_WRITE then
      return fail("write out of bounds (max " .. MAX_WRITE .. "): " .. #bytes)
    end
    if not in_range(address, #bytes) then
      return fail(string.format("range $%04X+%d exceeds the address space", address, #bytes))
    end

    local ok, message = pcall(write_memory, address, bytes)
    if not ok then return fail("write refused by the emulator: " .. tostring(message)) end

    -- Verified, not assumed: PRG ROM is read-only and the emulator accepts a
    -- write there without complaint. An unverified OK would report a poke that
    -- never landed, which is the one failure this tool refuses to perform.
    local back = read_memory(address, #bytes)
    for i = 1, #bytes do
      if back[i] ~= bytes[i] then
        return fail(string.format(
          "write not observable at $%04X (offset %d): wrote %02X, read %02X -- read-only space?",
          address + i - 1, i - 1, bytes[i], back[i]))
      end
    end
    return "OK"
  end

  if command == "CPU" then
    return cpu_line()
  end

  if command == "ROM" then
    return rom_line()
  end

  if command == "PAUSE" then
    paused_requested = true
    return "OK"
  end

  if command == "RESUME" then
    return "OK" -- answered by the blocked loop, which owns the machine
  end

  if command == "STEP" then
    -- One instruction, then broken again: a step is a pause of one instruction.
    local ok, message = pcall(function() emu.step(1, emu.stepType.step, emu.cpuType.nes) end)
    if not ok then return fail("step refused by the emulator: " .. tostring(message)) end
    return "OK"
  end

  if command == "RESET" then
    local ok, message = pcall(function() emu.reset() end)
    if not ok then return fail("reset refused by the emulator: " .. tostring(message)) end
    return "OK"
  end

  if command == "BREAK" then
    if #tokens < 2 then return fail("BREAK expects SET, REMOVE, LIST or CLEAR") end
    local action = tokens[2]:upper()

    if action == "CLEAR" then
      for address, byKind in pairs(breakpoints) do
        for kind, id in pairs(byKind) do
          emu.removeMemoryCallback(id)
          byKind[kind] = nil
        end
        breakpoints[address] = nil
      end
      return "OK"
    end

    if action == "LIST" then
      if #tokens ~= 2 then return fail("BREAK LIST takes nothing else") end
      return "OK " .. describe_breakpoints()
    end

    if action ~= "SET" and action ~= "REMOVE" then
      return fail("BREAK expects SET, REMOVE, LIST or CLEAR")
    end
    if #tokens ~= 4 then return fail("BREAK " .. action .. " expects <kind> <address>") end

    local kind = tokens[3]:lower()
    local address = parse_address(tokens[4])
    if address == nil then return fail("invalid address: " .. tokens[4]) end

    -- The kind is checked after the address, as the monitor does: a user who
    -- mistyped the kind should be told about the kind.
    if KINDS[kind] == nil then return fail("unknown kind: " .. tokens[3]) end

    if action == "SET" then
      local byKind = breakpoints[address]
      if byKind ~= nil and byKind[kind] ~= nil then return "OK already set" end
      local id, reason = install_breakpoint(address, kind)
      if id == nil then return fail(reason) end
      return "OK"
    end

    if remove_breakpoint(address, kind) then return "OK" end
    return fail(string.format("no such breakpoint: %s $%04X", kind, address))
  end

  if command == "STATE" then
    if #tokens == 2 and tokens[2]:upper() == "SAVE" then
      local bytes, reason = state_to_bytes()
      if bytes == nil then return fail(reason) end
      if #bytes > MAX_STATE then return fail("snapshot too large: " .. #bytes) end
      return "OK " .. to_hex(bytes)
    end

    if #tokens == 3 and tokens[2]:upper() == "LOAD" then
      local bytes = from_hex(tokens[3])
      if bytes == nil then return fail("invalid hex") end
      if #bytes > MAX_STATE then return fail("snapshot too large: " .. #bytes) end
      local state, reason = bytes_to_state(bytes)
      if state == nil then return fail(reason) end
      local ok, message = pcall(function() emu.setState(state) end)
      if not ok then return fail("snapshot refused by the emulator: " .. tostring(message)) end
      return "OK"
    end

    return fail("STATE expects SAVE or LOAD <hex>")
  end

  return fail("unknown command: " .. command)
end

------------------------------------------------------------------ serving

local client = nil
local server = nil
local stopped = false

-- Served while the machine is broken, which is where a debugger spends its life.
-- Blocking here is deliberate and safe: the emulator is frozen, so nothing is
-- being starved, and this is the only way commands reach a paused machine,
-- because no event callback fires while execution is broken.
--
-- The receive timeout is bookkeeping only. A timeout is not a reason to give up,
-- so the loop keeps waiting until RESUME or the client goes away.
local function serve_blocked(reason)
  client:settimeout(0.5)
  local waited = 0.0
  local reported = 0.0
  log("paused (" .. tostring(reason) .. "), waiting for RESUME")

  while true do
    local line, err = client:receive("*l")
    if line ~= nil then
      local upper = line:upper()
      if upper == "RESUME" then
        client:send("OK\n")
        log(string.format("resumed after %.1fs", waited))
        client:settimeout(0)
        return "resume"
      end
      if upper == "QUIT" then
        client:send("OK\n")
        return "closed"
      end

      paused_requested = false
      local response = dispatch(line)
      if paused_requested then paused_requested = false end
      client:send(response .. "\n")
      waited = 0.0
      reported = 0.0
    elseif err == "timeout" then
      waited = waited + 0.5
      if waited - reported >= PAUSE_REPORT_SECONDS then
        reported = waited
        log(string.format("still paused after %.0fs", waited))
      end
    else
      log("client gone while paused")
      return "closed"
    end
  end
end

-- One tick of the running machine. Anything received is answered with the machine
-- frozen: freeze first, act second, so no read races the program under it.
local function on_tick()
  if stopped then return end

  if client == nil then
    local candidate = server:accept()
    if candidate == nil then return end
    candidate:settimeout(0)
    client = candidate
    log("monitor connected")
    return
  end

  local line = client:receive("*l")
  if line == nil then return end

  pcall(function() emu.breakExecution() end)

  local handled = 0
  local pause = false
  repeat
    local response = dispatch(line)
    if paused_requested then pause = true end
    paused_requested = false
    pcall(function() client:send(response .. "\n") end)
    handled = handled + 1
    line = (handled < MAX_DRAIN) and client:receive("*l") or nil
  until line == nil or line == "timeout"

  if pause then
    -- PAUSE arrived while running: answer, then keep serving from here, because
    -- the next tick will never come.
    local outcome = serve_blocked("PAUSE")
    if outcome == "resume" then
      emu.resume()
    else
      emu.stop(0)
    end
    return
  end

  emu.resume()
end

-------------------------------------------------------------------- start

local function start()
  local port = DEFAULT_PORT
  local ok, from_env = pcall(function() return os.getenv("MACHINE_PORT") end)
  if ok and from_env ~= nil and tonumber(from_env) ~= nil then
    port = tonumber(from_env)
  end

  local loaded, socket = pcall(require, "socket.core")
  if not loaded then
    log("socket.core is unavailable: " .. tostring(socket))
    log("allow network access in settings.json, under Debug.ScriptWindow")
    emu.stop(1)
    return
  end

  server = socket.tcp()
  server:settimeout(0)
  local bound, reason = pcall(function()
    server:bind("127.0.0.1", port)
    server:listen(1)
  end)
  if not bound then
    log("cannot listen on 127.0.0.1:" .. port .. " -- " .. tostring(reason))
    emu.stop(1)
    return
  end

  emu.addEventCallback(on_tick, emu.eventType.inputPolled)
  log(NAME .. " " .. VERSION .. " bridge listening on 127.0.0.1:" .. port)
end

start()
