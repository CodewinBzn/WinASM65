-- Proof that the bridge really exists: this script opens a socket, connects to the
-- monitor's server, sends a command and reads the answer.
--
-- The previous spike showed that socket.core is loadable. That does not yet prove
-- the transport works: it takes a real connection, a send, and a read. That is
-- what this script does, and it is the only thing separating a present API from a
-- usable bridge.

function note(key, value)
  print(key .. " = " .. tostring(value))
end

local ok, socket = pcall(require, "socket.core")
note("socket_core_loaded", ok)
if not ok then
  note("error", socket)
  emu.stop(1)
  return
end

local client = socket.tcp()

-- Mandatory, without exception: without a timeout, the bridge blocks the emulator
-- and the user thinks it crashed. This is the main trap of this API.
client:settimeout(2.0)

local connected, message = pcall(function()
  client:connect("127.0.0.1", 45678)
end)
note("connected", connected)
if not connected then
  note("error", message)
  client:close()
  emu.stop(2)
  return
end

note("send_PING", client:send("PING\n"))
note("send_READ", client:send("READ $8000 4\n"))

local first = client:receive("*l")
local second = client:receive("*l")
note("reply_1", first)
note("reply_2", second)

client:close()
note("end", "socket closed")
emu.stop(0)