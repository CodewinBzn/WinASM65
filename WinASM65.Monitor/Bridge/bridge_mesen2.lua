-- Bridge for Mesen2 (github.com/SourMesen/Mesen2), the modern Mesen.
--
-- Measured on Mesen2 2.1.1, Windows x64, against a WinASM65-built homebrew ROM.
-- Everything below was established by probing this build, because the details
-- decide the whole architecture of the script.
--
--   headless        Mesen.exe --testRunner <script> <rom> -novideo -noaudio
--                   -noinput -enablestdout -donotsavesettings. Boots the
--                   cartridge, so the reference workload is NES native.
--
--   memory          emu.read / emu.write on emu.memType.nesDebug: side effect free
--                   and covering PRG ROM, RAM and the register mirrors.
--
--   cpu state       emu.getState() returns a FLAT table keyed "cpu.pc", "cpu.a",
--                   "cpu.x", "cpu.y", "cpu.sp", "cpu.ps", "cpu.cycleCount",
--                   alongside PPU, APU and mapper state.
--
--   execution       emu.breakExecution, emu.resume and emu.step exist, and all
--                   three refuse to be called from the script body: "This
--                   function cannot be called outside a callback". All three work
--                   inside an event callback, so every command here is served
--                   from one.
--
--   breakpoints     emu.addMemoryCallback with callbackType.exec, .read or .write.
--                   The memory type must be a CPU memory type: passing a Debug
--                   type raises "invalid memory type". Debug types belong to
--                   emu.read and emu.write, where the point is to avoid side
--                   effects.
--
-- Four consequences shape this file, and each of them was measured rather than
-- assumed.
--
-- 1. inputPolled only fires while the machine runs. Once execution is broken no
--    callback arrives, so a bridge that paused and waited for the next tick would
--    wait forever. A stopped machine is therefore served from inside the callback
--    that stopped it.
--
-- 2. emu.step cannot be called while the machine is already broken in the same
--    callback invocation: it waits for an instruction to execute, and execution
--    only advances after the callback returns. A step is therefore issued while
--    running and completed later.
--
-- 3. eventType.codeBreak fires "when code execution breaks (e.g breakpoint, step,
--    etc.)". That is the single place where a stopped machine is served, for both
--    a step and a breakpoint, and it is why this file needs no second mechanism.
--
-- 4. A breakpoint set on the address the machine is stopped at does not fire
--    until the CPU comes back to it: Mesen2 treats the instruction as already
--    reached. Break there after a step, not before one.
--
-- Two host settings are required and are written beside Mesen.exe by the
-- launcher, which reports having written them: ScriptingContext::ExecutionCountHook
-- aborts a script whose pass runs longer than Debug.ScriptWindow.ScriptTimeout,
-- 1 second by default, and io, require and os are nil until AllowIoOsAccess and
-- AllowNetworkAccess are set. With the default timeout a long pause kills the
-- script mid-command, and without network access no socket can be opened at all.
--
-- The protocol is the monitor's existing one, so this is a second backend rather
-- than a second protocol. Where MesenCE refuses by name because the API is
-- missing, nothing is refused here except what is genuinely impossible: a write
-- into read-only space, which is caught by reading the bytes back.

local NAME = "Mesen2"
local VERSION = "2.1.1"

local DEFAULT_PORT = 45678

-- Measured bound of the protocol, mirrored from MonitorProtocol.cs. The bridge
-- checks it too: a request that would freeze the emulator must be refused by
-- whoever received it, not by whoever sent it.
local MAX_READ = 4096
local MAX_WRITE = 4096
local MAX_STATE = 131072

-- Commands drained per tick while the machine runs. One tick is about 16 ms and
-- every command in the drain freezes the machine for the act, so the drain is
-- bounded: a 4 KiB read arrives as 16 chunks and must not hold the machine for
-- longer than it takes to answer it.
local MAX_DRAIN = 64

-- Seconds between notes while stopped, so a pause that is not a hang is visible.
local STOP_REPORT_SECONDS = 8

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

-- Bounded before it allocates, not after. Every caller checks the length it got
-- back, but a check that runs after the table is built is no defence against a
-- client that sends one enormous line: the allocation would already have happened.
-- The cap is the largest legal payload in the protocol, so nothing legitimate is
-- refused.
local function from_hex(text, max_bytes)
  local cleaned = text:gsub("%s", "")
  if #cleaned % 2 ~= 0 or #cleaned == 0 then return nil end
  if #cleaned > max_bytes * 2 then return nil end
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
    return tonumber(value:sub(2), 16)
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
      -- Escaped because the separator is a semicolon and a key is free text.
      rendered = "s" .. value:gsub("\\", "\\\\"):gsub(";", "\\;"):gsub("\n", "\\n")
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
  local characters = {}
  for i = 1, #bytes do characters[i] = string.char(bytes[i]) end
  local joined = table.concat(characters)

  local state = {}
  -- Driven by the iterator directly, never through ipairs: on this host gmatch
  -- yields a single closure rather than Lua's (iterator, state, control) triple, so
  -- ipairs would be handed a function and indexing it would raise "attempt to
  -- index a function value".
  for entry in joined:gmatch("[^;]+") do
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
      state[key] = body:gsub("\\n", "\n"):gsub("\\;", ";"):gsub("\\\\", "\\")
    else
      return nil, "unknown state value kind: " .. kind
    end
  end
  return state
end

-------------------------------------------------------------- breakpoints

local KINDS = {
  exec = emu.callbackType.exec,
  read = emu.callbackType.read,
  write = emu.callbackType.write,
}

-- address -> kind -> callback id. Two kinds on one address are two callbacks and
-- both fire: an exec breakpoint on an address that is also written to is not a
-- contradiction, it is two things the user asked for.
local breakpoints = {}

-- Why the machine was stopped, set when a stop is asked for and consumed by the
-- codeBreak handler. Nil means "nothing to serve", which is what a break taken to
-- answer a command looks like from the event's point of view.
--
-- Only an exec breakpoint sets this. The machine cannot execute while a command is
-- being answered, so no other source can raise a stop from inside a command.
local stop_requested = nil

local serve_blocked, serve_stopped

-- One step, from a running machine, then broken again. The resume comes first:
-- emu.step waits for an instruction to execute, and instructions only execute
-- after the current callback returns.
local function step_once()
  emu.resume()
  stop_requested = "step"
  local ok, message = pcall(function() emu.step(1, emu.stepType.step, emu.cpuType.nes) end)
  if not ok then
    log("step refused: " .. tostring(message))
    -- Cleared, or a later break would consume this as a step the user never asked
    -- for and stop the machine for no reason.
    stop_requested = nil
    emu.resume()
  end
end

-- Same rule for a reset: it needs a running machine, so it is issued from the
-- owner rather than from dispatch.
local function reset_now()
  emu.resume()
  local ok, message = pcall(function() emu.reset() end)
  if not ok then log("reset refused: " .. tostring(message)) end
end

-- The single owner of "what does a finished serve_blocked mean". Every caller goes
-- through here, so a new outcome cannot be handled at one call site and forgotten
-- at the other.
local function act_on_outcome(outcome)
  if outcome == "resume" then
    emu.resume()
  elseif outcome == "step" then
    step_once()
  elseif outcome == "reset" then
    reset_now()
  else
    -- "closed": the monitor is gone, so there is nothing left to serve.
    emu.stop(0)
  end
end

local function install_breakpoint(address, kind)
  local callback_type = KINDS[kind]
  if callback_type == nil then return nil, "unknown kind: " .. kind end

  local ok, id = pcall(function()
    return emu.addMemoryCallback(function(accessed, value)
      if kind ~= "exec" then
        -- A watchpoint answers by refusing to let the access change anything:
        -- returning a value from the callback replaces the result of the read or
        -- write, so the original value goes back untouched. Nothing is stopped,
        -- so nothing needs serving.
        log(string.format("watchpoint %s $%04X", kind, accessed))
        return value
      end

      -- Recorded and stopped, never served from here: this callback fires while
      -- the machine runs, and the codeBreak event is the one place that owns a
      -- stopped machine.
      stop_requested = string.format("breakpoint exec $%04X", accessed)
      log("hit: " .. stop_requested)
      emu.breakExecution()
    end, callback_type, address, address, emu.cpuType.nes, emu.memType.nesMemory)
  end)

  if not ok then return nil, "the emulator refused the breakpoint: " .. tostring(id) end
  if id == nil then return nil, "the emulator registered no callback" end

  breakpoints[address] = breakpoints[address] or {}
  breakpoints[address][kind] = id
  return id
end

local function remove_breakpoint(address, kind)
  local byKind = breakpoints[address]
  if byKind == nil or byKind[kind] == nil then return false end
  pcall(function() emu.removeMemoryCallback(byKind[kind]) end)
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

-- Set by PAUSE and by STEP, read by the caller that owns the machine. Neither is
-- acted on inside the handler: both need a decision only the owner can make.
local pause_requested = false
local step_requested = false
local reset_requested = false

local function cpu_line()
  local state = emu.getState()
  if type(state) ~= "table" or state["cpu.pc"] == nil then
    return fail("the emulator returned no cpu state")
  end
  return string.format("OK pc=$%04X a=%02X x=%02X y=%02X sp=%02X ps=%02X cycles=%d",
    state["cpu.pc"] or 0, state["cpu.a"] or 0, state["cpu.x"] or 0,
    state["cpu.y"] or 0, state["cpu.sp"] or 0, state["cpu.ps"] or 0,
    state["cpu.cycleCount"] or 0)
end

local function rom_line()
  local info = emu.getRomInfo()
  if type(info) ~= "table" then return fail("the emulator described no cartridge") end
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

-- Answers one command. Wrapped by every caller: a host API that raises where this
-- build did not expect it must become a named refusal, never a dead socket, and a
-- dead socket looks exactly like a crashed monitor.
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
    if address == nil then return fail("invalid address: " .. tokens[2]) end
    local length = tonumber(tokens[3], 10)
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
    -- Checked before decoding so the refusal names the real reason, and again inside
    -- from_hex so the bound holds no matter which caller is reached.
    if #(tokens[3]:gsub("%s", "")) > MAX_WRITE * 2 then
      return fail("write out of bounds (max " .. MAX_WRITE .. " bytes)")
    end
    local bytes = from_hex(tokens[3], MAX_WRITE)
    if bytes == nil then return fail("invalid hex") end
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
    pause_requested = true
    return "OK"
  end

  if command == "STEP" then
    -- Not executed here: a step needs a running machine, and the machine cannot
    -- run while this callback is on the stack. The owner resumes and steps.
    step_requested = true
    return "OK"
  end

  if command == "RESET" then
    reset_requested = true
    return "OK"
  end

  if command == "RESUME" then
    return "OK" -- answered by the loop that owns the stopped machine
  end

  if command == "BREAK" then
    if #tokens < 2 then return fail("BREAK expects SET, REMOVE, LIST or CLEAR") end
    local action = tokens[2]:upper()

    if action == "CLEAR" then
      for address, byKind in pairs(breakpoints) do
        for kind, id in pairs(byKind) do
          pcall(function() emu.removeMemoryCallback(id) end)
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
      if #(tokens[3]:gsub("%s", "")) > MAX_STATE * 2 then
        return fail("snapshot too large (max " .. MAX_STATE .. " bytes)")
      end
      local bytes = from_hex(tokens[3], MAX_STATE)
      if bytes == nil then return fail("invalid hex") end
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

-- The single entry point every caller uses, so no caller can forget the guard.
local function answer(line)
  local ok, response = pcall(dispatch, line)
  if not ok then
    log("command failed: " .. tostring(line) .. " -> " .. tostring(response))
    return fail("the emulator refused this command: " .. tostring(response))
  end
  return response
end

------------------------------------------------------------------ serving

local client = nil
local server = nil

-- Serves a stopped machine. Called from a callback, so blocking here is not the
-- MesenCE mistake: the emulator is frozen, nothing is being starved, and this is
-- the only route commands can take while stopped, because no callback fires.
--
-- The receive timeout is bookkeeping. A timeout is not a reason to give up.
--
-- Returns "resume" when the machine should run again, "closed" when the monitor
-- is gone, and "step" or "reset" when the user asked for one of those and the
-- owner has to carry it out, because the machine cannot run while this is on the
-- stack.
function serve_blocked(reason)
  client:settimeout(0.5)
  local waited = 0.0
  local reported = 0.0
  log("stopped (" .. tostring(reason) .. "), waiting for RESUME")

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

      -- All three, not just the two this loop acts on: a PAUSE arriving while the
      -- machine is already stopped is a no-op, and leaving its flag set would stop
      -- the machine later over a command that never asked for it.
      pause_requested = false
      step_requested = false
      reset_requested = false
      local response = answer(line)
      local sent = client:send(response .. "\n")
      if not sent then
        log("send failed while the machine was stopped")
        client:settimeout(0)
        return "closed"
      end
      if step_requested then client:settimeout(0) return "step" end
      if reset_requested then client:settimeout(0) return "reset" end
      waited = 0.0
      reported = 0.0
    elseif err == "timeout" then
      waited = waited + 0.5
      if waited - reported >= STOP_REPORT_SECONDS then
        reported = waited
        log(string.format("still stopped after %.0fs", waited))
      end
    else
      log("monitor gone while the machine was stopped")
      return "closed"
    end
  end
end

-- Fires after a step or a breakpoint. This is the only owner of a stopped
-- machine, so a stop never needs a second mechanism to be noticed.
function serve_stopped()
  if stop_requested == nil then return end
  local reason = stop_requested
  stop_requested = nil

  local outcome = serve_blocked(reason)
  act_on_outcome(outcome)
end

-- One tick of the running machine. A command is answered with the machine frozen:
-- freeze first, act second, so no read races the program underneath it.
local function on_tick()
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

  -- A step has to be issued from a running machine, so the break that the drain
  -- normally takes first is skipped for it.
  if line:upper() == "STEP" then
    local response = answer(line)
    -- Cleared here because this branch returns without entering the drain, which
    -- is the only other place the flags are reset. A flag left set would be read
    -- against the next unrelated command and stop the machine for no reason.
    pause_requested = false
    step_requested = false
    reset_requested = false
    if not client:send(response .. "\n") then
      log("send failed on step")
      emu.resume()
      return
    end
    step_once()
    return
  end

  emu.breakExecution()

  local handled = 0
  local pause = false
  local step = false
  local reset = false
  local connected = true
  repeat
    -- Cleared before the answer, read after it: clearing first drops whatever
  -- the previous command left behind, and reading after catches only what this
  -- command asked for, so a command never inherits another one's intent.
    pause_requested = false
    step_requested = false
    reset_requested = false
    local response = answer(line)
    if pause_requested then pause = true end
    if step_requested then step = true end
    if reset_requested then reset = true end
    -- Guarded: the machine is already broken, so a send failure that escaped here
    -- would skip every resume below and leave the emulator frozen with no log.
    if not client:send(response .. "\n") then
      log("send failed while the machine was broken")
      connected = false
      break
    end
    handled = handled + 1
    line = (handled < MAX_DRAIN) and client:receive("*l") or nil
  until line == nil

  if not connected then
    emu.resume()
    return
  end

  if pause then
    -- Answered, and now serving: the next tick will never come.
    act_on_outcome(serve_blocked("PAUSE"))
    return
  end

  if step then
    step_once()
    return
  end

  if reset then
    reset_now()
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
  emu.addEventCallback(serve_stopped, emu.eventType.codeBreak)
  log(NAME .. " " .. VERSION .. " bridge listening on 127.0.0.1:" .. port)
end

start()
