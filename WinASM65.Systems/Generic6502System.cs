using System.Collections.Generic;
using WinASM65.Monitor.Abstractions;

namespace WinASM65.Systems
{
    /// <summary>
    /// A 6502 machine nothing more is known about: one flat, writable 64 KB, no
    /// other memory, the program loaded and run.
    ///
    /// It is what a system gets until it has a definition of its own, and what a
    /// project that names no system gets. It never pretends: no video memory, no
    /// palette, no header.
    /// </summary>
    public sealed class Generic6502System : SystemDefinition
    {
        private static readonly MemoryRegion[] Regions =
        {
            new MemoryRegion("Memory", 0x0000, 0x10000, true, false, 1),
        };

        private static readonly MemoryView[] DefaultViews =
        {
            new MemoryView("Memory $0000-$0FFF", CpuSpace, 0x0000, 0x1000, true),
        };

        private readonly string _id;
        private readonly string _name;

        /// <summary>Creates the fallback for a system, keeping its id so it is still reported.</summary>
        /// <param name="systemId">The id the project named, or null.</param>
        public Generic6502System(string systemId)
        {
            _id = string.IsNullOrWhiteSpace(systemId) ? "6502" : systemId;
            _name = string.IsNullOrWhiteSpace(systemId) ? "6502 machine" : systemId + " (generic 6502)";
        }

        public override string SystemId { get { return _id; } }
        public override string DisplayName { get { return _name; } }
        public override CpuDescription Cpu { get { return new CpuDescription("6502", "6502", 1000000); } }
        public override IReadOnlyList<MemoryRegion> AddressSpace { get { return Regions; } }
        public override BootKind Boot { get { return BootKind.LoadAndRun; } }
        public override IReadOnlyList<MemoryView> Views { get { return DefaultViews; } }
    }
}
