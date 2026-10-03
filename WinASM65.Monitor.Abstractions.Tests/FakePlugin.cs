using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace WinASM65.Monitor.Abstractions.Tests
{
    /// <summary>
    /// A plugin, compiled against the contract exactly as a real one would be.
    ///
    /// It implements all four contract interfaces and nothing more, and it declares
    /// what it measured: memory and the processor's registers, no execution control.
    /// That is MAME 0.289 and MesenCE 2.2.1 without their emulators — memory only,
    /// where <c>debug:step</c> returns without moving the program counter and
    /// <c>debug:bpset</c> blocks.
    ///
    /// It exists as a separate compilation because that is the only way to test the
    /// constraint that matters. A contract used by both sides from one compilation is
    /// one <see cref="System.Type"/> and there is nothing to prove; the plugin loading
    /// test loads a second copy of this assembly into its own load context so the
    /// resolution of the contract is a real decision made by a real resolver.
    ///
    /// Public with a parameterless constructor because the test instantiates it by
    /// reflection, from an assembly it did not load.
    /// </summary>
    public sealed class FakePlugin : IExecutionAdapter, IVideoProvider, ISystemProfile, IAssistantProvider
    {
        private readonly byte[] _memory = new byte[0x10000];
        private readonly List<MemoryRegion> _regions = new List<MemoryRegion>
        {
            // PRG ROM first and read-only, RAM second and writable: the same
            // asymmetry the NES has, and the reason the ROM pane and the RAM pane
            // are not one pane.
            new MemoryRegion("PRG", 0x8000, 0x4000, false, false, 1),
            new MemoryRegion("RAM", 0x0000, 0x0800, true, false, 1)
        };

        /// <summary>Addresses passed to <see cref="Write"/>, so a refusal can be checked to have refused everything.</summary>
        public List<int> WrittenAddresses { get; private set; }

        /// <summary>Disposal was observed, which is the one lifecycle thing a plugin must be able to report.</summary>
        public bool Disposed { get; private set; }

        public string DisplayName
        {
            get { return "FakePlugin 1.0.0"; }
        }

        public ExecutionCapability Capabilities
        {
            get { return ExecutionCapability.Memory | ExecutionCapability.CpuState; }
        }

        public IReadOnlyList<MemoryRegion> Regions
        {
            get { return _regions; }
        }

        public byte[] Read(RegionAddress where, int length)
        {
            byte[] result = new byte[length];
            System.Array.Copy(_memory, _regions[where.Region].StartAddress + where.Offset, result, 0, length);
            return result;
        }

        public void Write(RegionAddress where, byte[] bytes)
        {
            // A read-only region refuses by name. A fake that accepted the write and
            // dropped it would be modelling the exact failure the contract exists to
            // keep visible.
            if (!_regions[where.Region].IsWritable)
                throw new System.NotSupportedException("region " + _regions[where.Region].Name + " is read-only");

            if (WrittenAddresses == null)
                WrittenAddresses = new List<int>();

            int at = _regions[where.Region].StartAddress + where.Offset;
            WrittenAddresses.Add(at);
            System.Array.Copy(bytes, 0, _memory, at, bytes.Length);
        }

        public CpuState ReadCpuState()
        {
            // The reading Mesen2 2.1.1 gave while paused, twice and identically:
            // PC=$CBFD A=01 X=00 Y=5E SP=FD PS=05 cycles=43358022. A real value, so
            // the test that reads this back through the host's interface is reading
            // something rather than asserting a field is non-null.
            return new CpuState(0xCBFD, 0x01, 0x00, 0x5E, 0xFD, 0x05, 43358022L);
        }

        public void Pause() { throw new System.NotSupportedException("no execution control was measured"); }

        public void Resume() { throw new System.NotSupportedException("no execution control was measured"); }

        public void Reset() { throw new System.NotSupportedException("no execution control was measured"); }

        public void Step() { throw new System.NotSupportedException("no execution control was measured"); }

        public void SetBreakpoint(int address) { throw new System.NotSupportedException("no execution control was measured"); }

        public void ClearBreakpoints() { throw new System.NotSupportedException("no execution control was measured"); }

        public void SetWatchpoint(RegionAddress where, MemoryAccessKind kind) { throw new System.NotSupportedException("no watchpoints were measured"); }

        public void ClearWatchpoints() { throw new System.NotSupportedException("no watchpoints were measured"); }

        public byte[] SaveState() { throw new System.NotSupportedException("no snapshots were measured"); }

        public void LoadState(byte[] state) { throw new System.NotSupportedException("no snapshots were measured"); }

        public void Dispose()
        {
            Disposed = true;
        }

        public int Width
        {
            get { return 256; }
        }

        public int Height
        {
            get { return 240; }
        }

        public IReadOnlyList<Color> Palette
        {
            get { return new Color[0]; }
        }

        public Color[,] CaptureFrame()
        {
            return new Color[Width, Height];
        }

        public string SystemId
        {
            get { return "fake"; }
        }

        public CpuDescription Cpu
        {
            get { return new CpuDescription("6502", "6502", 1789773); }
        }

        public IReadOnlyList<MemoryRegion> AddressSpace
        {
            get { return _regions; }
        }

        public IReadOnlyList<TileLayout> TileLayouts
        {
            get { return new TileLayout[0]; }
        }

        public IReadOnlyList<NameTableLayout> NameTables
        {
            get { return new NameTableLayout[0]; }
        }

        public IReadOnlyList<SystemAsset> Assets
        {
            get { return new SystemAsset[0]; }
        }

        public string ProviderId
        {
            get { return "fake"; }
        }

        public IReadOnlyList<AssistantModel> Models
        {
            get { return new[] { new AssistantModel("fake-1", "Fake 1", 4096) }; }
        }

        public int ContextBudget
        {
            get { return 2048; }
        }

        public async IAsyncEnumerable<AssistantChunk> CompleteAsync(AssistantRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new AssistantChunk(request.Prompt, false);
            yield return new AssistantChunk(string.Empty, true);
        }
    }
}