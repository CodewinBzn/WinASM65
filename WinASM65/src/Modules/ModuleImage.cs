// WinASM65 - In-memory model of a module (.w65 object)

using System;
using System.Collections.Generic;
using WinASM65.Output;

namespace WinASM65.Modules
{
    /// <summary>What a segment holds. bss reserves space and occupies no file bytes.</summary>
    public enum SegmentKind
    {
        /// <summary>Read-only code and constants.</summary>
        Ro = 0,

        /// <summary>Read-write initialised data.</summary>
        Rw = 1,

        /// <summary>Reserved space, no file bytes.</summary>
        Bss = 2
    }

    /// <summary>
    /// A named region of a module. The name is what lets the linker place the
    /// same routine at several addresses: it duplicates a segment, it never moves
    /// one.
    /// </summary>
    public sealed class ModuleSegment
    {
        public string Name { get; private set; }
        public byte[] Data { get; private set; }
        public uint Alignment { get; set; }
        public SegmentKind Kind { get; set; }
        public byte Bank { get; set; }

        /// <summary>Byte offset of this segment's data inside the module file. 0 for bss.</summary>
        public uint FileOffset { get; internal set; }

        /// <summary>
        /// The address the unit was assembled at. A segment is stored unplaced, so
        /// this is only the origin the unit was written against, not a placement:
        /// the linker decides where the segment actually goes.
        /// </summary>
        public ushort OriginAddress { get; set; }

        public ModuleSegment(string name, byte[] data, SegmentKind kind, uint alignment, byte bank)
        {
            Name = string.IsNullOrEmpty(name) ? string.Empty : name;
            Data = data ?? new byte[0];
            Kind = kind;
            Alignment = alignment < 1 ? 1u : alignment;
            Bank = bank;
            OriginAddress = 0;
        }

        /// <summary>Size on disk. A bss segment reserves space and stores nothing.</summary>
        public uint StoredSize
        {
            get { return Kind == SegmentKind.Bss ? 0u : (uint)Data.Length; }
        }

        /// <summary>Space the segment occupies in the loaded image, bss included.</summary>
        public uint OccupiedSize
        {
            get { return (uint)Data.Length; }
        }
    }

    /// <summary>
    /// A name made visible outside the module. An export never holds an absolute
    /// address: the address is not known until the linker places the segment.
    /// </summary>
    public sealed class ModuleExport
    {
        public string Name { get; private set; }
        public int SegmentIndex { get; private set; }
        public uint Offset { get; private set; }

        public ModuleExport(string name, int segmentIndex, uint offset)
        {
            Name = name ?? string.Empty;
            SegmentIndex = segmentIndex;
            Offset = offset;
        }
    }

    /// <summary>
    /// A name the module expects someone else to provide. The module name is
    /// advisory: it makes a diagnostic precise, but resolution is by name, the
    /// way Windows resolves imports. A wrong module name is reported rather than
    /// silently ignored.
    /// </summary>
    public sealed class ModuleImport
    {
        public string Name { get; private set; }
        public string ModuleName { get; private set; }

        public ModuleImport(string name, string moduleName)
        {
            Name = name ?? string.Empty;
            ModuleName = moduleName ?? string.Empty;
        }
    }

    /// <summary>
    /// A whole module, in memory: its segments, the names it publishes, the names
    /// it needs, and the sites whose value depends on a name.
    /// </summary>
    public sealed class ModuleImage
    {
        private readonly List<ModuleSegment> _segments = new List<ModuleSegment>();
        private readonly List<ModuleExport> _exports = new List<ModuleExport>();
        private readonly List<ModuleImport> _imports = new List<ModuleImport>();
        private readonly List<RelocationRecord> _relocations = new List<RelocationRecord>();

        public string ModuleName { get; set; }

        public IReadOnlyList<ModuleSegment> Segments { get { return _segments; } }
        public IReadOnlyList<ModuleExport> Exports { get { return _exports; } }
        public IReadOnlyList<ModuleImport> Imports { get { return _imports; } }
        public IReadOnlyList<RelocationRecord> Relocations { get { return _relocations; } }

        public ModuleImage()
        {
            ModuleName = string.Empty;
        }

        public int AddSegment(ModuleSegment segment)
        {
            if (segment == null)
                throw new ArgumentNullException("segment");
            _segments.Add(segment);
            return _segments.Count - 1;
        }

        public void AddExport(ModuleExport export)
        {
            if (export != null)
                _exports.Add(export);
        }

        public void AddImport(ModuleImport import)
        {
            if (import == null)
                return;
            for (int i = 0; i < _imports.Count; i++)
            {
                if (string.Equals(_imports[i].Name, import.Name, StringComparison.Ordinal))
                    return;
            }
            _imports.Add(import);
        }

        public void AddRelocation(RelocationRecord record)
        {
            if (record != null)
                _relocations.Add(record);
        }

        /// <summary>Index of a segment by name, or -1. Names are matched case-sensitively.</summary>
        public int IndexOfSegment(string name)
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                if (string.Equals(_segments[i].Name, name, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        /// <summary>Total bytes this module stores on disk, excluding bss.</summary>
        public long StoredSize
        {
            get
            {
                long total = 0;
                for (int i = 0; i < _segments.Count; i++)
                    total += _segments[i].StoredSize;
                return total;
            }
        }
    }
}
