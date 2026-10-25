using System.Collections.Generic;
using WinASM65.Monitor.Abstractions;

namespace WinASM65.Systems
{
    /// <summary>
    /// A 6502 machine nothing more is known about: one flat, writable 64 KB, no
    /// other memory.
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
        private readonly BootKind _boot;

        /// <summary>
        /// The fallback when nothing names the machine: a bare 6502 that starts from
        /// its reset vector, as every program debugged before system definitions did.
        /// </summary>
        public Generic6502System(string systemId)
            : this(systemId, BootKind.ResetVector)
        {
        }

        /// <summary>Creates the fallback for a system, keeping its id so it is still reported.</summary>
        /// <param name="systemId">The id the project named, or null.</param>
        /// <param name="boot">How a program starts on it.</param>
        public Generic6502System(string systemId, BootKind boot)
        {
            _id = string.IsNullOrWhiteSpace(systemId) ? "6502" : systemId;
            _name = string.IsNullOrWhiteSpace(systemId) ? "6502 machine" : systemId + " (generic 6502)";
            _boot = boot;
        }

        public override string SystemId { get { return _id; } }
        public override string DisplayName { get { return _name; } }
        public override CpuDescription Cpu { get { return new CpuDescription("6502", "6502", 1000000); } }
        public override IReadOnlyList<MemoryRegion> AddressSpace { get { return Regions; } }
        public override BootKind Boot { get { return _boot; } }
        public override IReadOnlyList<MemoryView> Views { get { return DefaultViews; } }
    }
}
