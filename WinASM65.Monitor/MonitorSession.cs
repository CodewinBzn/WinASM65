using System;
using System.Collections.Generic;
using WinASM65.Cpu;
using WinASM65.Monitor.Protocol;

namespace WinASM65.Monitor
{
    /// <summary>
    /// One monitor session: a machine, the units loaded into it, and the commands
    /// that act on both.
    ///
    /// Deliberately not the shell. Everything here answers in strings and touches
    /// no console, so a session can be driven by a typed line, by a socket, or by a
    /// test — and the three cannot drift apart, because there is only one
    /// implementation of "what does LOAD do".
    ///
    /// READ, WRITE, DISASM, PAUSE, STEP and STATE are answered here rather than
    /// forwarded, even when the machine is a bridge on the other side of a socket.
    /// They are already the <see cref="IMemoryBackend"/> contract, so forwarding
    /// them would only re-encode something already typed. The commands that exist
    /// *because* the monitor is here — ASSEMBLE, LOAD, UNITS — belong to the
    /// <see cref="UnitLibrary"/>, which no bridge can host, because assembling needs
    /// this assembler.
    ///
    /// DISASM is decoded from a single memory snapshot. Reading byte by byte would
    /// let the emulated CPU run between reads and produce a listing that never
    /// existed on the machine.
    /// </summary>
    public sealed class MonitorSession
    {
        private readonly IMemoryBackend _backend;
        private readonly Disassembler _disassembler;
        private readonly UnitLibrary _library;

        // Remembered between CPU commands so the session can tell a machine that
        // advanced from one that stood still. Not persisted: this is a statement
        // about the last two observations, not a durable fact about the emulator.
        private long? _lastCycleCount;

        public MonitorSession(IMemoryBackend backend, ICpuInstructionSet cpu, string assemblerDirectory)
        {
            if (backend == null)
                throw new ArgumentNullException("backend");
            if (cpu == null)
                throw new ArgumentNullException("cpu");

            _backend = backend;
            _disassembler = new Disassembler(cpu);
            _library = new UnitLibrary(backend, assemblerDirectory);
        }

        public IMemoryBackend Backend
        {
            get { return _backend; }
        }

        public UnitLibrary Library
        {
            get { return _library; }
        }

        /// <summary>Set once the user asks to leave, so the caller stops reading.</summary>
        public bool ShouldQuit { get; private set; }

        /// <summary>
        /// Runs one command line and returns what to print. Never throws for a bad
        /// command: a failure is an <c>ERR</c> line, because a monitor that dies on a
        /// typo cannot be used to look up what the typo was.
        /// </summary>
        public IReadOnlyList<string> Execute(string line)
        {
            List<string> output = new List<string>();
            if (line == null)
                return output;

            line = line.Trim();
            if (line.Length == 0)
                return output;

            string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string verb = parts[0].ToUpperInvariant();

            if (verb == "QUIT" || verb == "EXIT")
            {
                ShouldQuit = true;
                output.Add("OK bye");
                return output;
            }

            if (verb == "HELP" || verb == "?")
            {
                output.AddRange(DescribeCommands());
                return output;
            }

            try
            {
                switch (verb)
                {
                    case "PING":
                        output.Add(MonitorProtocol.OkPrefix + " " + _backend.EmulatorName
                            + " " + _backend.EmulatorVersion);
                        break;
                    case "READ":
                        output.Add(Read(ParseAddress(parts, 1, "READ <addr> <len>"),
                            ParseCount(parts, 2, "READ <addr> <len>")));
                        break;
                    case "WRITE":
                        output.AddRange(Write(ParseAddress(parts, 1, "WRITE <addr> <hex bytes>"),
                            ParseHexBytes(parts, 2, "WRITE <addr> <hex bytes>")));
                        break;
                    case "DISASM":
                        output.AddRange(Disassemble(ParseAddress(parts, 1, "DISASM <addr> <count>"),
                            ParseCount(parts, 2, "DISASM <addr> <count>")));
                        break;
                    case "PAUSE":
                        _backend.Pause();
                        output.Add(MonitorProtocol.OkPrefix);
                        break;
                    case "RESUME":
                        _backend.Resume();
                        output.Add(MonitorProtocol.OkPrefix);
                        break;
                    case "STEP":
                        _backend.Step();
                        output.Add(MonitorProtocol.OkPrefix + " stepped");
                        break;
                    case "RESET":
                        _backend.Reset();
                        output.Add(MonitorProtocol.OkPrefix);
                        break;
                    case "BREAK":
                        output.AddRange(Break(parts));
                        break;
                    case "STATE":
                        output.AddRange(State(parts));
                        break;
                    case "ASSEMBLE":
                        Require(parts, 2, "ASSEMBLE <source file>");
                        output.Add(_library.Assemble(parts[1]));
                        break;
                    case "LOAD":
                        Require(parts, 3, "LOAD <unit> <address>");
                        output.Add(_library.Load(parts[1], ParseAddress(parts, 2, "LOAD <unit> <address>")));
                        break;
                    case "UNITS":
                        Require(parts, 1, "UNITS");
                        output.Add(_library.Describe());
                        break;
                    case "CPU":
                        output.AddRange(Cpu());
                        break;
                    case "ROM":
                        output.AddRange(Rom());
                        break;
                    default:
                        output.Add(MonitorProtocol.ErrPrefix + " unknown command: " + parts[0]
                            + ". HELP lists what this session knows.");
                        break;
                }
            }
            catch (MonitorException ex)
            {
                output.Add(MonitorProtocol.ErrPrefix + " " + ex.Message);
            }
            catch (FormatException ex)
            {
                output.Add(MonitorProtocol.ErrPrefix + " bad input: " + ex.Message);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                output.Add(MonitorProtocol.ErrPrefix + " bad input: " + ex.Message);
            }
            catch (OverflowException ex)
            {
                output.Add(MonitorProtocol.ErrPrefix + " bad input: " + ex.Message);
            }

            return output;
        }

        public static IReadOnlyList<string> DescribeCommands()
        {
            return new List<string>
            {
                "PING                          emulator name and version",
                "READ <addr> <len>             memory as hex bytes",
                "WRITE <addr> <hex>            write hex bytes, e.g. WRITE $0800 A9 5A",
                "DISASM <addr> <count>         disassemble from one memory snapshot",
                "PAUSE | RESUME | STEP | RESET machine control",
                "BREAK SET|REMOVE|LIST|CLEAR  breakpoints, kind read/write/exec",
                "STATE SAVE | STATE LOAD <hex> capture and restore the machine",
                "ASSEMBLE <source>             assemble once and keep the unit",
                "LOAD <unit> <addr>            relink, write, and read back",
                "UNITS                         what has been assembled",
                "HELP                          this list",
                "QUIT                          leave the monitor",
            };
        }

        private string Read(int address, int length)
        {
            byte[] bytes = _backend.Read(address, length);
            return "$" + address.ToString("X4") + " " + MonitorProtocol.ToHex(bytes);
        }

        private IReadOnlyList<string> Write(int address, byte[] bytes)
        {
            _backend.Write(address, bytes);
            return new List<string> { MonitorProtocol.OkPrefix + " $" + address.ToString("X4")
                + " " + bytes.Length + " octet(s)" };
        }

        private IReadOnlyList<string> Disassemble(int address, int count)
        {
            if (count <= 0)
                throw new MonitorException("count must be positive");

            // Three bytes per instruction is the longest a 6502 instruction can be,
            // so the window is never short. Reading it once is what keeps the listing
            // a picture of one instant rather than of a running machine.
            int window = Math.Min(count * 3, AddressSpace.MaxAddress + 1 - address);
            if (window <= 0)
                throw new AddressRangeException(address, count);

            byte[] memory = _backend.Read(address, window);

            List<string> lines = new List<string>();
            int offset = 0;
            for (int i = 0; i < count && offset < memory.Length; i++)
            {
                DisassembledInstruction instruction = _disassembler.Disassemble(
                    (ushort)(address + offset), memory, offset);
                lines.Add(instruction.Text);
                if (instruction.Length <= 0)
                    break;
                offset += instruction.Length;
            }
            return lines;
        }

        private IReadOnlyList<string> Break(string[] parts)
        {
            Require(parts, 2, "BREAK SET|REMOVE|LIST|CLEAR ...");
            string action = parts[1].ToUpperInvariant();

            if (action == "CLEAR")
            {
                _backend.ClearBreakpoints();
                return Ok(MonitorProtocol.OkPrefix);
            }

            if (action == "LIST")
            {
                Require(parts, 2, "BREAK LIST");
                return Ok(MonitorProtocol.OkPrefix + " (the bridge owns its breakpoints)");
            }

            if (action != "SET" && action != "REMOVE")
                return Ok(MonitorProtocol.ErrPrefix + " BREAK expects SET, REMOVE, LIST or CLEAR");

            Require(parts, 4, "BREAK " + action + " <read|write|exec> <address>");
            string kind = parts[2].ToLowerInvariant();

            // The address is checked before the kind, so a user who typed
            // "BREAK SET foob $C000" learns the kind is wrong, not that the address
            // looks strange.
            int address = ParseAddress(parts, 3, "BREAK " + action + " <kind> <address>");

            if (kind != BreakpointKind.Read && kind != BreakpointKind.Write && kind != BreakpointKind.Exec)
                return Ok(MonitorProtocol.ErrPrefix + " unknown kind: " + parts[2]
                    + ", expected read, write or exec");

            if (action == "REMOVE")
                return Ok(MonitorProtocol.ErrPrefix + " the bridge owns its breakpoints; use BREAK CLEAR");

            _backend.AddBreakpoint(address, kind);
            return Ok(MonitorProtocol.OkPrefix + " " + kind + " $" + address.ToString("X4"));
        }

        // Shows the registers, and says out loud when the machine is not running.
//
// Without this the user reads a blank RAM and concludes the bridge is broken.
// MesenCE cannot advance the CPU from a script, so a frozen machine is the normal
// state of this backend, and the freeze has to be visible rather than inferred.
private IReadOnlyList<string> Cpu()
        {
            ICpuStateSource source = _backend as ICpuStateSource;
            if (source == null)
                return new[] { MonitorProtocol.ErrPrefix + " this backend cannot observe the CPU" };

            CpuSnapshot snapshot = source.ReadCpuState();

            var lines = new List<string> { snapshot.ToString() };

            if (_lastCycleCount.HasValue && _lastCycleCount.Value == snapshot.CycleCount)
            {
                lines.Add("the cycle counter has not moved since the last CPU command: "
                    + "the emulated CPU is not running. MesenCE 2.2.1 exposes no way to"
                    + " advance it from a script, so RAM stays as loaded and zeroed.");
            }
            else if (snapshot.LooksPoweredDown)
            {
                lines.Add("the machine is at its reset vector: it has not executed a single"
                    + " instruction. This is expected on this host, not a fault.");
            }

            _lastCycleCount = snapshot.CycleCount;
            return lines;
        }

        private IReadOnlyList<string> Rom()
        {
            IRomInfoSource source = _backend as IRomInfoSource;
            if (source == null)
                return new[] { MonitorProtocol.ErrPrefix + " this backend cannot describe the cartridge" };

            return new[] { MonitorProtocol.OkPrefix + " " + source.ReadRomInfo() };
        }

        private IReadOnlyList<string> State(string[] parts)
        {
            if (parts.Length == 2 && parts[1].Equals("SAVE", StringComparison.OrdinalIgnoreCase))
            {
                byte[] state = _backend.SaveState();
                return Ok(MonitorProtocol.OkPrefix + " " + MonitorProtocol.ToHex(state));
            }

            if (parts.Length == 3 && parts[1].Equals("LOAD", StringComparison.OrdinalIgnoreCase))
            {
                _backend.LoadState(ParseHexBytes(parts, 2, "STATE LOAD <hex bytes>"));
                return Ok(MonitorProtocol.OkPrefix);
            }

            return Ok(MonitorProtocol.ErrPrefix + " STATE expects SAVE or LOAD <hex>");
        }

        private static IReadOnlyList<string> Ok(string line)
        {
            return new List<string> { line };
        }

        private static int ParseAddress(string[] parts, int index, string usage)
        {
            Require(parts, index + 1, usage);

            int address;
            if (!MonitorProtocol.TryParseAddress(parts[index], out address))
                throw new MonitorException("not an address: " + parts[index]);
            return address;
        }

        private static int ParseCount(string[] parts, int index, string usage)
        {
            Require(parts, index + 1, usage);

            int value;
            if (!int.TryParse(parts[index], out value) || value < 0)
                throw new MonitorException("not a byte count: " + parts[index]);
            return value;
        }

        /// <summary>
        /// Bytes to write, as they are typed: <c>WRITE $0800 A9 5A 60</c>. Every
        /// remaining word is joined, because requiring one unbroken hex string would
        /// force the user to retype as a single token exactly what the machine is
        /// showing them a byte at a time. A stray word is therefore a bad hex string,
        /// which is the honest reading of a typo.
        /// </summary>
        private static byte[] ParseHexBytes(string[] parts, int index, string usage)
        {
            Require(parts, index + 1, usage);

            string joined = string.Join(" ", parts, index, parts.Length - index);

            byte[] bytes;
            if (!MonitorProtocol.TryParseHex(joined, out bytes))
                throw new MonitorException("not a hex byte string: " + string.Join(" ", parts, index, parts.Length - index));
            return bytes;
        }

        private static void Require(string[] parts, int needed, string usage)
        {
            if (parts.Length < needed)
                throw new MonitorException("usage: " + usage);
        }
    }
}