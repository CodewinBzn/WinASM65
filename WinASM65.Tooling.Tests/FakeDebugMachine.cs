using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WinASM65.Monitor;
using WinASM65.Monitor.Emulators;
using WinASM65.Projects;
using WinASM65.Tooling.Dap;

namespace WinASM65.Tooling.Tests
{
    /// <summary>
    /// A machine the test drives by hand: it records what the adapter asked for and
    /// stops when the test says so. Stops and resumes are raised on another thread,
    /// as the bridge's receiver would.
    /// </summary>
    public sealed class FakeDebugMachine : IMemoryBackend, ICpuStateSource, IExecutionEvents, IBreakpointRemoval, IVideoMemory
    {
        private readonly byte[] _memory = new byte[0x10000];

        // The NES memories besides the CPU's, as a version 2 bridge offers them.
        private readonly Dictionary<string, byte[]> _spaces = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "PPU", new byte[0x4000] }, { "PAL", new byte[0x20] }, { "OAM", new byte[0x100] }, { "CHR", new byte[0x2000] }
        };

        public IReadOnlyList<string> MemorySpaces { get; set; } = new[] { "PPU", "PAL", "OAM", "CHR" };

        public byte[] ReadSpace(string space, int address, int length)
        {
            Record("ReadSpace " + space);
            byte[] memory = SpaceOf(space);
            byte[] result = new byte[length];
            Buffer.BlockCopy(memory, address, result, 0, length);
            return result;
        }

        public void WriteSpace(string space, int address, byte[] bytes)
        {
            Record("WriteSpace " + space);
            Buffer.BlockCopy(bytes, 0, SpaceOf(space), address, bytes.Length);
        }

        public byte PeekSpace(string space, int address)
        {
            return _spaces[space][address];
        }

        private byte[] SpaceOf(string space)
        {
            byte[] memory;
            if (!_spaces.TryGetValue(space, out memory) || !System.Linq.Enumerable.Contains(MemorySpaces, space, StringComparer.OrdinalIgnoreCase))
                throw new MonitorException("no memory space " + space);
            return memory;
        }

        // Events leave on one thread, in the order they happened, as the bridge's
        // receiver delivers them: a resume must never overtake the stop after it.
        private readonly System.Collections.Concurrent.BlockingCollection<Action> _events =
            new System.Collections.Concurrent.BlockingCollection<Action>();

        public FakeDebugMachine()
        {
            new System.Threading.Thread(() =>
            {
                foreach (Action raise in _events.GetConsumingEnumerable())
                    raise();
            }) { IsBackground = true, Name = "fake-receiver" }.Start();
        }

        private void Raise(Action raise)
        {
            if (!_events.IsAddingCompleted)
                _events.Add(raise);
        }

        public List<string> Calls { get; } = new List<string>();
        public HashSet<int> Breakpoints { get; } = new HashSet<int>();
        public int Pc { get; set; }
        public int Sp { get; set; } = 0xFD;
        public bool Disposed { get; private set; }
        public bool Connected { get; set; } = true;

        public void Load(int address, byte[] bytes)
        {
            Buffer.BlockCopy(bytes, 0, _memory, address, bytes.Length);
        }

        public byte Peek(int address)
        {
            return _memory[address];
        }

        /// <summary>Stops the machine as the bridge would report it.</summary>
        public void SimulateStop(StopReason reason, int pc, int? prg = null)
        {
            Pc = pc;
            StopInfo stop = new StopInfo(reason, pc, prg);
            LastStop = stop;
            IsStopped = true;
            Raise(() => Stopped?.Invoke(stop));
        }

        public string EmulatorName => "Mesen2";
        public string EmulatorVersion => "2.1.1";
        public bool IsRunning => !IsStopped;
        public bool IsConnected => Connected;
        public string RamRefreshMode => null;
        public bool ReportsExecutionState => true;
        public bool IsStopped { get; private set; }
        public StopInfo LastStop { get; private set; }

        public event Action<IReadOnlyList<(int Address, byte Value)>> RamChanged { add { } remove { } }
        public event Action<StopInfo> Stopped;
        public event Action Resumed;

        public byte[] Read(int address, int length)
        {
            lock (Calls) Calls.Add("READ $" + address.ToString("X4") + " " + length);
            byte[] result = new byte[length];
            Buffer.BlockCopy(_memory, address, result, 0, length);
            return result;
        }

        public void Write(int address, byte[] bytes)
        {
            if (address >= 0x8000)
                throw new MonitorException("write refused: $" + address.ToString("X4") + " is ROM");
            lock (Calls) Calls.Add("WRITE $" + address.ToString("X4"));
            Buffer.BlockCopy(bytes, 0, _memory, address, bytes.Length);
        }

        public void Pause() { Record("PAUSE"); SimulateStop(StopReason.Pause, Pc); }
        public void Resume() { Record("RESUME"); Run(); }
        public void Step() { Record("STEP"); SimulateStop(StopReason.Step, Pc + 1); }
        public void Reset() { Record("RESET"); Run(); }

        private void Run()
        {
            IsStopped = false;
            LastStop = null;
            Raise(() => Resumed?.Invoke());
        }

        public void AddBreakpoint(int address, string kind)
        {
            Record("BREAK SET " + kind + " $" + address.ToString("X4"));
            Breakpoints.Add(address);
        }

        public void RemoveBreakpoint(int address, string kind)
        {
            Record("BREAK REMOVE " + kind + " $" + address.ToString("X4"));
            if (!Breakpoints.Remove(address))
                throw new MonitorException("no such breakpoint");
        }

        public void ClearBreakpoints()
        {
            Record("BREAK CLEAR");
            Breakpoints.Clear();
        }

        public byte[] SaveState() => new byte[0];
        public void LoadState(byte[] state) { }

        public CpuSnapshot ReadCpuState()
        {
            return new CpuSnapshot(Pc, 0x01, 0x02, 0x03, Sp, 0xA5, 1234);
        }

        public void Dispose()
        {
            Record("DISPOSE");
            Disposed = true;
        }

        private void Record(string call)
        {
            lock (Calls) Calls.Add(call);
        }
    }

    /// <summary>The real assembler, a fake emulator and the fake machine.</summary>
    public sealed class FakeEnvironment : IDebugEnvironment
    {
        public FakeDebugMachine Machine { get; } = new FakeDebugMachine();
        public FakeEmulator Emulator { get; } = new FakeEmulator();
        public bool EmulatorExitsAtOnce { get; set; }
        public string LaunchedRom { get; private set; }

        public ProjectBuildResult Build(string configurationPath, bool writeImage)
        {
            ProjectBuildResult result = new DebugEnvironment().Build(configurationPath, writeImage);
            if (result.Success)
                Machine.Load(0xC000, result.Image);
            return result;
        }

        public EmulatorConfiguration ConfiguredEmulator(string configurationPath) => EmulatorConfiguration.Of(@"C:\fake\Mesen.exe", null);

        public EmulatorConfiguration LaunchedEmulator { get; private set; }

        public int FreePort() => 12345;

        public IEmulatorProcess Launch(EmulatorConfiguration emulator, string romPath, int port)
        {
            LaunchedEmulator = emulator;
            LaunchedRom = romPath;
            if (EmulatorExitsAtOnce)
                Emulator.HasExited = true;
            return Emulator;
        }

        public IMemoryBackend Connect(int port, int timeoutMs)
        {
            if (EmulatorExitsAtOnce)
                throw new MonitorException("refused");
            return Machine;
        }
    }

    public sealed class FakeEmulator : IEmulatorProcess
    {
        public bool HasExited { get; set; }
        public bool Killed { get; private set; }
        public event Action Exited;

        public void Kill()
        {
            Killed = true;
            HasExited = true;
        }

        public void Exit()
        {
            HasExited = true;
            Exited?.Invoke();
        }
    }
}
