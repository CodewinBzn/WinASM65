using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using WinASM65.Core;
using WinASM65.Cpu;

namespace WinASM65.Monitor.Protocol
{
    /// <summary>
    /// Protocol server: listens for an incoming connection (the Lua bridge) and
    /// replies on the strength of an <see cref="IMemoryBackend"/>.
    ///
    /// This server holds no intelligence: it turns a line into a backend call, and
    /// the result into text. All the logic stays on the .NET side, which keeps the
    /// bridge trivial and lets the protocol be tested without an emulator.
    /// </summary>
    public sealed class ProtocolServer : IDisposable
    {
        /// <summary>
        /// Largest 6502/65C02 instruction in bytes: only 16-bit absolute and
        /// indirect modes take 3 bytes.
        /// </summary>
        private const int MaxInstructionLength = 3;

        private readonly IMemoryBackend _backend;
        private readonly Disassembler _disassembler;
        private readonly BreakpointSet _breakpoints;
        private readonly UnitLibrary _library;
        private TcpListener _listener;
        private Thread _thread;
        private volatile bool _running;

        public ProtocolServer(IMemoryBackend backend, ICpuInstructionSet cpu)
            : this(backend, cpu, null)
        {
        }

        /// <summary>
        /// <paramref name="assemblerDirectory"/> is where source files are looked up
        /// when a relative path arrives. It defaults to the current directory, so the
        /// monitor can be started from the project it watches.
        /// </summary>
        public ProtocolServer(IMemoryBackend backend, ICpuInstructionSet cpu, string assemblerDirectory)
        {
            if (backend == null)
                throw new ArgumentNullException("backend");
            if (cpu == null)
                throw new ArgumentNullException("cpu");
            _backend = backend;
            _disassembler = new Disassembler(cpu);
            _breakpoints = new BreakpointSet(backend);
            _library = new UnitLibrary(backend, assemblerDirectory);
        }

        public int Port { get; private set; }

        /// <summary>Breakpoints the monitor knows about, as they were set.</summary>
        public BreakpointSet Breakpoints
        {
            get { return _breakpoints; }
        }

        /// <summary>Units assembled and kept, ready to be placed anywhere.</summary>
        public IReadOnlyList<RelocatableUnit> Units
        {
            get { return _library.Units; }
        }

        /// <summary>Starts listening on 127.0.0.1. Returns the effective port.</summary>
        public int Start(int requestedPort = 0)
        {
            _listener = new TcpListener(IPAddress.Loopback, requestedPort);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            _running = true;
            _thread = new Thread(Listen) { IsBackground = true };
            _thread.Start();
            return Port;
        }

        private void Listen()
        {
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    return; // listener stopped
                }
                catch (InvalidOperationException)
                {
                    return;
                }

                // One client at a time: the Lua bridge is synchronous and opens only
                // one. Serving several clients at once would make no sense and would
                // complicate arbitration over the machine.
                using (client)
                {
                    try
                    {
                        Serve(client);
                    }
                    catch (IOException)
                    {
                        // Bridge disconnected: normal.
                    }
                    catch (SocketException)
                    {
                    }
                }
            }
        }

        private void Serve(TcpClient client)
        {
            client.NoDelay = true;
            using (NetworkStream stream = client.GetStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.ASCII))
            using (StreamWriter writer = new StreamWriter(stream, Encoding.ASCII))
            {
                writer.AutoFlush = true;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Trim().Length == 0)
                        continue;
                    writer.WriteLine(Handle(line));
                }
            }
        }

        /// <summary>Executes a command and returns the response line.</summary>
        public string Handle(string line)
        {
            List<string> tokens = new List<string>(line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            if (tokens.Count == 0)
                return MonitorProtocol.ErrPrefix + " empty command";

            string command = tokens[0].ToUpperInvariant();
            try
            {
                switch (command)
                {
                    case "PING":
                        return MonitorProtocol.OkPrefix + " " + _backend.EmulatorName + " " + _backend.EmulatorVersion;

                    case "READ":
                        return Read(tokens);

                    case "WRITE":
                        return Write(tokens);

                    case "DISASM":
                        return Disassemble(tokens);

                    case "PAUSE":
                        _backend.Pause();
                        return MonitorProtocol.OkPrefix;

                    case "RESUME":
                        _backend.Resume();
                        return MonitorProtocol.OkPrefix;

                    case "STEP":
                        _backend.Step();
                        return MonitorProtocol.OkPrefix;

                    case "RESET":
                        _backend.Reset();
                        return MonitorProtocol.OkPrefix;

                    case "BREAK":
                        return Break(tokens);

                    case "STATE":
                        return State(tokens);

                    case "ASSEMBLE":
                        return Assemble(tokens);

                    case "LOAD":
                        return Load(tokens);

                    case "UNITS":
                        return ListUnits();

                    default:
                        return MonitorProtocol.ErrPrefix + " unknown command: " + command;
                }
            }
            catch (MonitorException ex)
            {
                // A refusal from the backend must surface as named: it is the backend
                // that knows why (range, snapshot, breakpoint).
                return MonitorProtocol.ErrPrefix + " " + ex.Message;
            }
            catch (FormatException ex)
            {
                return MonitorProtocol.ErrPrefix + " invalid parameter: " + ex.Message;
            }
        }

        /// <summary>
        /// Assembles a source file once and keeps it. The bytes are not written
        /// anywhere yet: what is kept is the set of modules, which is what makes the
        /// second placement possible without assembling again.
        /// </summary>
        private string Assemble(List<string> tokens)
        {
            if (tokens.Count != 2)
                return MonitorProtocol.ErrPrefix + " ASSEMBLE expects <source file>";

            return _library.Assemble(tokens[1]);
        }

        /// <summary>
        /// Relinks a kept unit at an address, writes it, and reads it back.
        ///
        /// The read-back is the point. A bridge that writes less than it says, or
        /// writes somewhere else, still returns OK; comparing what the machine holds
        /// with what the linker produced is the only evidence available that the
        /// routine really is at the address the caller named.
        /// </summary>
        private string Load(List<string> tokens)
        {
            if (tokens.Count != 3)
                return MonitorProtocol.ErrPrefix + " LOAD expects <unit> <address>";

            int address;
            if (!MonitorProtocol.TryParseAddress(tokens[2], out address))
                return MonitorProtocol.ErrPrefix + " invalid address: " + tokens[2];

            return _library.Load(tokens[1], address);
        }

        private string ListUnits()
        {
            return _library.Describe();
        }

        private string Read(List<string> tokens)
        {
            if (tokens.Count != 3)
                return MonitorProtocol.ErrPrefix + " READ expects <address> <length>";

            int address;
            if (!MonitorProtocol.TryParseAddress(tokens[1], out address))
                return MonitorProtocol.ErrPrefix + " invalid address: " + tokens[1];

            int length;
            if (!int.TryParse(tokens[2], out length))
                return MonitorProtocol.ErrPrefix + " invalid length: " + tokens[2];

            // Bound before the call: the backend cannot protect the bridge from a
            // request that would freeze the emulator.
            if (length < 0 || length > MonitorProtocol.MaxReadLength)
                return MonitorProtocol.ErrPrefix + " length out of bounds (0.."
                    + MonitorProtocol.MaxReadLength + "): " + length;

            return MonitorProtocol.OkPrefix + " " + MonitorProtocol.ToHex(_backend.Read(address, length));
        }

        private string Write(List<string> tokens)
        {
            if (tokens.Count != 3)
                return MonitorProtocol.ErrPrefix + " WRITE expects <address> <hex>";

            int address;
            if (!MonitorProtocol.TryParseAddress(tokens[1], out address))
                return MonitorProtocol.ErrPrefix + " invalid address: " + tokens[1];

            byte[] bytes;
            if (!MonitorProtocol.TryParseHex(tokens[2], out bytes))
                return MonitorProtocol.ErrPrefix + " invalid hex";

            if (bytes.Length > MonitorProtocol.MaxWriteLength)
                return MonitorProtocol.ErrPrefix + " write out of bounds (max "
                    + MonitorProtocol.MaxWriteLength + "): " + bytes.Length;

            _backend.Write(address, bytes);
            return MonitorProtocol.OkPrefix;
        }

        private string Disassemble(List<string> tokens)
        {
            if (tokens.Count != 3)
                return MonitorProtocol.ErrPrefix + " DISASM expects <address> <count>";

            int address;
            if (!MonitorProtocol.TryParseAddress(tokens[1], out address))
                return MonitorProtocol.ErrPrefix + " invalid address: " + tokens[1];

            int count;
            if (!int.TryParse(tokens[2], out count))
                return MonitorProtocol.ErrPrefix + " invalid count: " + tokens[2];

            if (count < 1 || count > MonitorProtocol.MaxDisasmCount)
                return MonitorProtocol.ErrPrefix + " count out of bounds (1.."
                    + MonitorProtocol.MaxDisasmCount + "): " + count;

            // The longest 6502/65C02 instruction is 3 bytes, so N bytes of output fit
            // in at most 3*N bytes. Reading more would be waste on a running machine
            // -- every read is a round trip over the socket while the emulator is
            // frozen. Reading less would truncate the decode, hence the exact bound
            // rather than an approximation.
            int remaining = AddressSpace.Size - address;
            int span = Math.Min(count * MaxInstructionLength, remaining);
            if (span <= 0)
                return MonitorProtocol.OkPrefix + " 0";

            // The backend refuses any overflow, so it cannot truncate silently, and
            // `count` is already capped by MaxDisasmCount, so `span` stays under
            // MaxReadLength without needing another bound here.
            byte[] memory = _backend.Read(address, span);

            StringBuilder text = new StringBuilder();
            int offset = 0;
            int emitted = 0;
            while (emitted < count && offset < memory.Length)
            {
                DisassembledInstruction ins =
                    _disassembler.Disassemble((ushort)(address + offset), memory, offset);
                if (text.Length > 0)
                    text.Append(' ');
                text.Append("$").Append((address + offset).ToString("X4"))
                    .Append(": ").Append(MonitorProtocol.ToHex(ins.Bytes)).Append(' ')
                    .Append(ins.Text);
                offset += ins.Length;
                emitted++;
            }

            return MonitorProtocol.OkPrefix + " " + emitted + " " + text;
        }

        private string Break(List<string> tokens)
        {
            if (tokens.Count < 2)
                return MonitorProtocol.ErrPrefix + " BREAK expects SET, REMOVE, LIST or CLEAR";

            string action = tokens[1].ToUpperInvariant();

            if (action == "CLEAR")
            {
                _breakpoints.Clear();
                return MonitorProtocol.OkPrefix;
            }

            if (action == "LIST")
            {
                if (tokens.Count != 2)
                    return MonitorProtocol.ErrPrefix + " BREAK LIST takes nothing else";
                return MonitorProtocol.OkPrefix + " " + _breakpoints.Describe();
            }

            if (action != "SET" && action != "REMOVE")
                return MonitorProtocol.ErrPrefix + " BREAK expects SET, REMOVE, LIST or CLEAR";

            if (tokens.Count != 4)
                return MonitorProtocol.ErrPrefix + " BREAK " + action + " expects <kind> <address>";

            string kind = tokens[2].ToLowerInvariant();

            int address;
            if (!MonitorProtocol.TryParseAddress(tokens[3], out address))
                return MonitorProtocol.ErrPrefix + " invalid address: " + tokens[3];

            // The kind is validated after the address: a user typing
            // "BREAK SET foob $C000" must learn they got the kind wrong, not that
            // they supplied a strange address.
            if (kind != BreakpointKind.Read && kind != BreakpointKind.Write && kind != BreakpointKind.Exec)
                return MonitorProtocol.ErrPrefix + " unknown kind: " + tokens[2];

            if (action == "SET")
            {
                bool added = _breakpoints.Add(address, kind);

                // A duplicate is a reached state, not an error. Saying so avoids an
                // "ERR" when the requested breakpoint already exists, and above all
                // stops the REPL from raising an alarm for nothing.
                return MonitorProtocol.OkPrefix + (added ? string.Empty : " already set");
            }

            bool removed = _breakpoints.Remove(address, kind);
            if (removed)
                return MonitorProtocol.OkPrefix;
            return MonitorProtocol.ErrPrefix + " no such breakpoint: " + kind + " $" + address.ToString("X4");
        }

        private string State(List<string> tokens)
        {
            if (tokens.Count == 2 && tokens[1].Equals("SAVE", StringComparison.OrdinalIgnoreCase))
            {
                byte[] state = _backend.SaveState();
                if (state.Length > MonitorProtocol.MaxStateLength)
                    return MonitorProtocol.ErrPrefix + " snapshot too large: " + state.Length;
                return MonitorProtocol.OkPrefix + " " + MonitorProtocol.ToHex(state);
            }

            if (tokens.Count == 3 && tokens[1].Equals("LOAD", StringComparison.OrdinalIgnoreCase))
            {
                byte[] state;
                if (!MonitorProtocol.TryParseHex(tokens[2], out state))
                    return MonitorProtocol.ErrPrefix + " invalid hex";
                if (state.Length > MonitorProtocol.MaxStateLength)
                    return MonitorProtocol.ErrPrefix + " snapshot too large: " + state.Length;
                _backend.LoadState(state);
                return MonitorProtocol.OkPrefix;
            }

            return MonitorProtocol.ErrPrefix + " STATE expects SAVE or LOAD <hex>";
        }

        public void Dispose()
        {
            _running = false;
            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                }
                catch (SocketException)
                {
                }
                _listener = null;
            }
        }
    }
}