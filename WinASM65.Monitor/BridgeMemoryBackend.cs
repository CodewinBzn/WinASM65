using System;
using System.Collections.Generic;
using System.Globalization;
using WinASM65.Monitor.Abstractions;
using WinASM65.Monitor.Protocol;

namespace WinASM65.Monitor
{
    /// <summary>
    /// The real backend: the emulated machine seen through the Lua bridge.
    ///
    /// This is the class that makes the rest of the monitor mean anything. Until it
    /// existed, the protocol had a server side, a client side and a bridge script,
    /// and nothing that joined them to <see cref="IMemoryBackend"/> — so the whole
    /// tool was a library nobody could run.
    ///
    /// Every refusal from the bridge is turned into a named exception. The bridge
    /// answers <c>ERR &lt;reason&gt;</c> precisely so the reason survives the trip; a
    /// backend that swallowed it would turn "this host has no emu.pause" into a
    /// generic failure, which is the one thing the protocol forbids.
    /// </summary>
    public sealed class BridgeMemoryBackend : IMemoryBackend, ICpuStateSource, IRomInfoSource, IExecutionAdapter
    {
        private readonly ProtocolClient _client;

        // What this host was measured able to do, and the address space it exposes.
        // Both are decided in the constructor from the handshake, because both are
        // facts about the build on the other end rather than about this process.
        private readonly ExecutionCapability _capabilities;
        private readonly IReadOnlyList<MemoryRegion> _regions;

        // Reads the registers through the bridge's CPU command.
//
// Implemented as part of ICpuStateSource rather than IMemoryBackend: memory access
// and processor state are different capabilities, and a backend able to do the
// first is not thereby able to do the second.
public CpuSnapshot ReadCpuState()
        {
            string response = _client.Send("CPU");
            if (!_client.IsOk(response))
                throw new MonitorException(ProtocolClient.ErrorOf(response));

            string payload = ProtocolClient.PayloadOf(response);
            int pc = 0, a = 0, x = 0, y = 0, sp = 0, ps = 0;
            long cycles = 0;

            foreach (string field in payload.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = field.IndexOf('=');
                if (equals <= 0)
                    continue;

                string key = field.Substring(0, equals);
                string value = field.Substring(equals + 1);

                if (key == "pc" || key == "a" || key == "x" || key == "y" || key == "sp" || key == "ps")
                    value = value.TrimStart('$');

                if (key == "pc") pc = ParseHex(value);
                else if (key == "a") a = ParseHex(value);
                else if (key == "x") x = ParseHex(value);
                else if (key == "y") y = ParseHex(value);
                else if (key == "sp") sp = ParseHex(value);
                else if (key == "ps") ps = ParseHex(value);
                else if (key == "cycles") cycles = ParseDecimal(value);
            }

            return new CpuSnapshot(pc, a, x, y, sp, ps, cycles);
        }

        // Registers are hex, the cycle counter is decimal. Parsing the counter as hex
        // would report cycle 16 as 22, and a cycle count that silently disagrees with
        // the emulator is worse than one that is not read at all.
        // Cartridge description, as reported by the emulator itself.
        public string ReadRomInfo()
        {
            string response = _client.Send("ROM");
            if (!_client.IsOk(response))
                throw new MonitorException(ProtocolClient.ErrorOf(response));
            return ProtocolClient.PayloadOf(response);
        }

        private static int ParseHex(string text)
        {
            int value;
            return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
                ? value
                : 0;
        }

        private static int ParseDecimal(string text)
        {
            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : 0;
        }

        public BridgeMemoryBackend(ProtocolClient client)
        {
            if (client == null)
                throw new ArgumentNullException("client");
            _client = client;

            // The bridge answers PING with its name and version, which is where both
            // come from: hard-coding them would let the monitor run against a host it
            // was never measured against.
            string response = _client.Send("PING");
            if (!client.IsOk(response))
                throw new MonitorException("the bridge refused the handshake: " + ProtocolClient.ErrorOf(response));

            string banner = ProtocolClient.PayloadOf(response);
            string[] parts = banner.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1)
                EmulatorName = parts[0];
            if (parts.Length >= 2)
                EmulatorVersion = parts[1];
            if (parts.Length == 0)
                throw new MonitorException("the bridge sent an empty handshake: '" + banner + "'");

            // The answer to PING is the only statement this side has about what the
            // host can do, and it is enough: the three builds the host has measured
            // are named exactly here, and anything else declares nothing. Reading
            // it is not probing — nothing is sent to find out, so no capability is
            // learned by making the user wait for a refusal.
            _capabilities = ExecutionCapabilities.For(EmulatorName, EmulatorVersion);

            // One flat region, because that is what the bridge exposes and what the
            // monitor already speaks: a 64 KiB CPU space addressed by a single
            // number. Splitting it into RAM and PRG would be inventing a region map
            // this protocol has never had, and the one asymmetry inside the flat
            // space — MesenCE drops a write into PRG ROM — is enforced by the
            // bridge's read-back rather than by a boundary here.
            _regions = new List<MemoryRegion>
            {
                new MemoryRegion(FlatRegionName, 0x0000, FlatRegionLength,
                    _capabilities.HasFlag(ExecutionCapability.MemoryWrite), false, 1)
            };
        }

        /// <summary>Connects to a bridge listening on 127.0.0.1.</summary>
        public static BridgeMemoryBackend Connect(int port, int timeoutMs = 5000)
        {
            return new BridgeMemoryBackend(ProtocolClient.Connect(port, timeoutMs));
        }

        /// <summary>
        /// The one region the contract's <see cref="Regions"/> exposes, named for
        /// what it is rather than for a machine it does not describe: this bridge
        /// speaks a flat 64 KiB CPU space and has no region or bank of its own.
        /// </summary>
        private const string FlatRegionName = "flat";

        private const int FlatRegionLength = 0x10000;

        public string EmulatorName { get; private set; }
        public string EmulatorVersion { get; private set; }

        public bool IsRunning
        {
            get { return true; }
        }

        /// <summary>
        /// Bytes per request. MesenCE kills a Lua script that stays too long in a
        /// single pass, and a large read is the only unbounded loop on the bridge
        /// side. The assembled result is identical either way; the bridge simply does
        /// less per request. A 4 KiB read in one message would be answered correctly
        /// on paper and would hang the emulator in practice.
        /// </summary>
        private const int ChunkSize = 256;

        public byte[] Read(int address, int length)
        {
            // Checked here as well as on the server: the backend must not depend on
            // every caller having gone through the protocol to be bounded.
            CheckRange(address, length);
            if (length > MonitorProtocol.MaxReadLength)
                throw new MonitorException("read of " + length + " bytes exceeds the protocol bound ("
                    + MonitorProtocol.MaxReadLength + ")");

            byte[] result = new byte[length];
            int done = 0;
            while (done < length)
            {
                int take = Math.Min(ChunkSize, length - done);
                byte[] chunk = ReadChunk(address + done, take);

                // A short answer would silently shift every following byte, so it is
                // refused rather than padded.
                if (chunk.Length != take)
                    throw new MonitorException("the bridge answered a read of " + take
                        + " bytes with " + chunk.Length);

                Buffer.BlockCopy(chunk, 0, result, done, take);
                done += take;
            }
            return result;
        }

        private byte[] ReadChunk(int address, int length)
        {
            string response = _client.Send("READ $" + address.ToString("X4") + " " + length);
            Expect(response, "READ");

            byte[] bytes;
            if (!MonitorProtocol.TryParseHex(ProtocolClient.PayloadOf(response), out bytes))
                throw new MonitorException("the bridge answered READ with something that is not hex");
            return bytes;
        }

        public void Write(int address, byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException("bytes");
            CheckRange(address, bytes.Length);
            if (bytes.Length > MonitorProtocol.MaxWriteLength)
                throw new MonitorException("write of " + bytes.Length + " bytes exceeds the protocol bound ("
                    + MonitorProtocol.MaxWriteLength + ")");

            // Same reason as the read: the bridge writes byte by byte inside one Lua
            // pass, and a long pass is what gets the script killed.
            int done = 0;
            while (done < bytes.Length)
            {
                int take = Math.Min(ChunkSize, bytes.Length - done);
                byte[] chunk = new byte[take];
                Buffer.BlockCopy(bytes, done, chunk, 0, take);

                Expect(_client.Send("WRITE $" + (address + done).ToString("X4")
                    + " " + MonitorProtocol.ToHex(chunk)), "WRITE");

                done += take;
            }
        }

        public void Pause()
        {
            Expect(_client.Send("PAUSE"), "PAUSE");
        }

        public void Resume()
        {
            Expect(_client.Send("RESUME"), "RESUME");
        }

        public void Step()
        {
            Expect(_client.Send("STEP"), "STEP");
        }

        public void Reset()
        {
            Expect(_client.Send("RESET"), "RESET");
        }

        public void AddBreakpoint(int address, string kind)
        {
            if (string.IsNullOrEmpty(kind))
                throw new MonitorException("a breakpoint needs a kind: read, write or exec");
            CheckRange(address, 1);

            Expect(_client.Send("BREAK SET " + kind + " $" + address.ToString("X4")), "BREAK SET");
        }

        public void ClearBreakpoints()
        {
            Expect(_client.Send("BREAK CLEAR"), "BREAK CLEAR");
        }

        public byte[] SaveState()
        {
            string response = _client.Send("STATE SAVE");
            Expect(response, "STATE SAVE");

            byte[] state;
            if (!MonitorProtocol.TryParseHex(ProtocolClient.PayloadOf(response), out state))
                throw new MonitorException("the bridge answered STATE SAVE with something that is not hex");
            if (state.Length > MonitorProtocol.MaxStateLength)
                throw new MonitorException("the bridge returned a state of " + state.Length
                    + " bytes, over the bound (" + MonitorProtocol.MaxStateLength + ")");
            return state;
        }

        public void LoadState(byte[] state)
        {
            if (state == null)
                throw new ArgumentNullException("state");
            if (state.Length > MonitorProtocol.MaxStateLength)
                throw new MonitorException("state of " + state.Length + " bytes exceeds the protocol bound");

            Expect(_client.Send("STATE LOAD " + MonitorProtocol.ToHex(state)), "STATE LOAD");
        }

        private void Expect(string response, string what)
        {
            if (_client.IsOk(response))
                return;

            // The bridge's own words, kept whole. "PAUSE unavailable on this host" is
            // a fact about the host the user can act on; "backend failure" is not.
            throw new MonitorException(what + ": " + ProtocolClient.ErrorOf(response));
        }

        private static void CheckRange(int address, int length)
        {
            if (address < 0 || address > AddressSpace.MaxAddress)
                throw new AddressRangeException(address, length);
            if (length < 0 || address + length > AddressSpace.MaxAddress + 1)
                throw new AddressRangeException(address, length);
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        // ---------------------------------------------------------------- the contract
        //
        // The host <-> plugin contract, on the same object the session already holds.
        //
        // One adapter rather than two backends on purpose: the session, the RAM
        // pane, the status line and the key bindings all talk to this object
        // already, and an adapter beside it would be a second answer to "what can
        // this machine do", free to disagree with the first.
        //
        // The two interfaces genuinely overlap, so the contract's members are
        // implemented explicitly. Read(RegionAddress, int) and Read(int, int) are
        // different questions — one names a region and a bank, the other is the flat
        // address this protocol has always used — and silently widening one into the
        // other would let a caller pass a bank this bridge has no notion of.

        /// <summary>Name and version of the host, as its handshake spelled them.</summary>
        public string DisplayName
        {
            get { return EmulatorName + " " + EmulatorVersion; }
        }

        /// <summary>
        /// What this build was measured able to do. Never widened: a host outside
        /// <see cref="ExecutionCapabilities"/> declares nothing at all.
        /// </summary>
        public ExecutionCapability Capabilities
        {
            get { return _capabilities; }
        }

        public IReadOnlyList<MemoryRegion> Regions
        {
            get { return _regions; }
        }

        byte[] IExecutionAdapter.Read(RegionAddress where, int length)
        {
            return Read(Flatten(where), length);
        }

        void IExecutionAdapter.Write(RegionAddress where, byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException("bytes");

            Write(Flatten(where), bytes);
        }

        /// <summary>
        /// The registers, as the contract's type. A named refusal rather than zeros
        /// when the bit is clear: a frozen machine and a CPU that cannot be observed
        /// produce the same empty reading, and only one of them is a fault.
        /// </summary>
        CpuState IExecutionAdapter.ReadCpuState()
        {
            Require(ExecutionCapability.CpuState, "report the registers");

            CpuSnapshot snapshot = ReadCpuState();
            return new CpuState(snapshot.Pc, snapshot.A, snapshot.X, snapshot.Y,
                snapshot.Sp, snapshot.Ps, snapshot.CycleCount);
        }

        void IExecutionAdapter.Pause()
        {
            Require(ExecutionCapability.Pause, "pause");
            Pause();
        }

        void IExecutionAdapter.Resume()
        {
            Require(ExecutionCapability.Resume, "resume");
            Resume();
        }

        void IExecutionAdapter.Reset()
        {
            Require(ExecutionCapability.Reset, "reset");
            Reset();
        }

        void IExecutionAdapter.Step()
        {
            Require(ExecutionCapability.StepInstruction, "step");
            Step();
        }

        void IExecutionAdapter.SetBreakpoint(int address)
        {
            Require(ExecutionCapability.BreakpointExecution, "break on an address");
            AddBreakpoint(address, BreakpointKind.Exec);
        }

        void IExecutionAdapter.ClearBreakpoints()
        {
            Require(ExecutionCapability.BreakpointExecution, "release a breakpoint");
            ClearBreakpoints();
        }

        void IExecutionAdapter.SetWatchpoint(RegionAddress where, MemoryAccessKind kind)
        {
            int address = Flatten(where);

            // Two callbacks, not one combined: the bridge registers a named callback
            // type per kind and has no combined form, so ReadWrite is exactly the
            // case the contract says means the machine cannot separate them — and
            // watching both is still watching without changing anything.
            if (kind == MemoryAccessKind.Read || kind == MemoryAccessKind.ReadWrite)
            {
                Require(ExecutionCapability.WatchpointRead, "watch a read");
                AddBreakpoint(address, BreakpointKind.Read);
            }

            if (kind == MemoryAccessKind.Write || kind == MemoryAccessKind.ReadWrite)
            {
                Require(ExecutionCapability.WatchpointWrite, "watch a write");
                AddBreakpoint(address, BreakpointKind.Write);
            }
        }

        void IExecutionAdapter.ClearWatchpoints()
        {
            Require(ExecutionCapability.WatchpointRead | ExecutionCapability.WatchpointWrite,
                "release a watchpoint");
            ClearBreakpoints();
        }

        byte[] IExecutionAdapter.SaveState()
        {
            Require(ExecutionCapability.StateSaveLoad, "save or restore a snapshot");
            return SaveState();
        }

        void IExecutionAdapter.LoadState(byte[] state)
        {
            Require(ExecutionCapability.StateSaveLoad, "save or restore a snapshot");
            LoadState(state);
        }

        /// <summary>
        /// The refusal a bit the host has not been given produces.
        ///
        /// Reached only when a caller ignores <see cref="Capabilities"/>, which is
        /// why the host gates first and this is second: the contract says an adapter
        /// must not make a missing capability something the user learns by pressing
        /// a key, and it cannot be, because the shell greys the key out. What is
        /// left here is the backstop, and it names the host, the action and the
        /// measurement — never a bare <see cref="NotSupportedException"/>, which
        /// would tell the user nothing at all.
        /// </summary>
        private void Require(ExecutionCapability capability, string action)
        {
            if (_capabilities.HasFlag(capability))
                return;

            throw new MonitorException(DisplayName + " cannot " + action
                + ": " + ExecutionCapabilities.Measurement(capability));
        }

        /// <summary>
        /// A contract address as the flat address this bridge speaks.
        ///
        /// Only one region and one bank exist, and an address naming anything else
        /// is refused rather than folded into the nearest thing that exists: a
        /// request for bank 3 of a region this machine does not have is a wrong
        /// request, and answering it from bank 0 would show false data.
        /// </summary>
        private static int Flatten(RegionAddress where)
        {
            if (where.Region != 0)
                throw new MonitorException("no region " + where.Region + ": this host exposes one flat region");

            if (where.Bank != 0)
                throw new MonitorException("no bank " + where.Bank + ": this host's space is not banked");

            return where.Offset;
        }
    }
}
