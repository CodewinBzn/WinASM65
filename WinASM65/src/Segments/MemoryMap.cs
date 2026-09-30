// Abdelghani BOUZIANE
// WinASM65 - Configurable multi-region memory (T6)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using WinASM65.Core;

namespace WinASM65.Segments
{
    /// <summary>What a region is allowed to contain.</summary>
    public enum RegionType
    {
        /// <summary>Read-only: ROM content, contributes bytes to the output file.</summary>
        Ro,

        /// <summary>Read-write: initialised RAM, contributes bytes to the output file.</summary>
        Rw,

        /// <summary>Reserved space that is never part of the output file.</summary>
        Bss
    }

    public static class RegionTypes
    {
        public const string RO = "ro";
        public const string RW = "rw";
        public const string BSS = "bss";

        public const string Accepted = RO + "/" + RW + "/" + BSS;

        public static bool TryParse(string text, out RegionType type)
        {
            type = RegionType.Ro;
            if (string.IsNullOrWhiteSpace(text))
                return true;

            switch (text.Trim().ToLowerInvariant())
            {
                case RO:
                case "r":
                    type = RegionType.Ro;
                    return true;
                case RW:
                case "r/w":
                    type = RegionType.Rw;
                    return true;
                case BSS:
                    type = RegionType.Bss;
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// One declared region, with every address and size already resolved. The
    /// window a region covers is the <b>run</b> window, because a <c>.org</c>
    /// places code where it executes, not where it is stored.
    /// </summary>
    public sealed class MemoryRegion
    {
        public string Name { get; private set; }

        /// <summary>First address of the run window, inclusive.</summary>
        public long Start { get; private set; }

        /// <summary>First address after the run window, exclusive. 0x10000 when unbounded.</summary>
        public long End { get; private set; }

        public bool HasSize { get; private set; }

        public int? Bank { get; private set; }

        public RegionType Type { get; private set; }

        /// <summary>Where the bytes execute, which is the region window itself.</summary>
        public long RunAddress { get { return Start; } }

        /// <summary>
        /// Where the bytes are stored. It equals the region base unless the
        /// configuration declares a <c>Load</c>, which is the load/run split a
        /// relocating target needs.
        /// </summary>
        public ushort LoadAddress { get; private set; }

        public long Size { get { return End - Start; } }

        public bool IsBss { get { return Type == RegionType.Bss; } }

        public bool Contains(long address)
        {
            return address >= Start && address < End;
        }

        public MemoryRegion(string name, long start, long end, bool hasSize, int? bank, RegionType type,
            ushort loadAddress)
        {
            Name = name;
            Start = start;
            End = end;
            HasSize = hasSize;
            Bank = bank;
            Type = type;
            LoadAddress = loadAddress;
        }

        public string DescribeWindow()
        {
            if (!HasSize)
                return string.Format(CultureInfo.InvariantCulture, "{0}-{1}", FormatAddress(Start), "$FFFF");
            if (Size == 0)
                return FormatAddress(Start);
            return string.Format(CultureInfo.InvariantCulture, "{0}-{1}", FormatAddress(Start), FormatAddress(End - 1));
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append(Name).Append(' ').Append(DescribeWindow());
            if (Bank.HasValue)
                text.Append(" bank ").Append(Bank.Value.ToString(CultureInfo.InvariantCulture));
            text.Append(" (").Append(RegionTypeName(Type)).Append(')');
            return text.ToString();
        }

        public static string FormatAddress(long address)
        {
            return "$" + address.ToString("X4", CultureInfo.InvariantCulture);
        }

        public static string RegionTypeName(RegionType type)
        {
            switch (type)
            {
                case RegionType.Ro: return RegionTypes.RO;
                case RegionType.Bss: return RegionTypes.BSS;
                default: return RegionTypes.RW;
            }
        }
    }

    /// <summary>
    /// The set of regions a build declares, built from <see cref="TargetConf.Regions"/>.
    ///
    /// <para>
    /// This is the T6 memory model: a description of what the target is expected to
    /// look like. It adds <b>no</b> placement mechanism. A <c>.org</c> still places
    /// the bytes, direct-burn output is byte for byte the same with or without a
    /// region declared, and a configuration with no <c>Regions</c> section has an
    /// empty map that validates nothing.
    /// </para>
    /// </summary>
    public sealed class MemoryMap
    {
        public const long AddressSpaceSize = 0x10000;

        public const string OUTSIDE_ANY_REGION =
            "{0} lands outside every declared region. Expected region: {1}. Declared regions: {2}.";

        public const string INSIDE_BSS_REGION =
            "{0} lands in region '{1}', declared 'bss': that space is reserved and contributes no file bytes, so no code can be placed there.";

        private readonly List<MemoryRegion> _regions;
        private readonly List<MemoryRegion> _byStart;

        public IReadOnlyList<MemoryRegion> Regions { get { return _regions; } }

        public bool IsEmpty { get { return _regions.Count == 0; } }

        /// <summary>Total number of bytes the bss regions reserve, which never reach the file.</summary>
        public long TotalBssSize
        {
            get
            {
                long total = 0;
                foreach (MemoryRegion region in _regions)
                    if (region.IsBss && region.HasSize)
                        total += region.Size;
                return total;
            }
        }

        public MemoryMap(IEnumerable<MemoryRegion> regions)
        {
            _regions = new List<MemoryRegion>(regions ?? new MemoryRegion[0]);
            _byStart = new List<MemoryRegion>(_regions);
            _byStart.Sort(delegate (MemoryRegion a, MemoryRegion b)
            {
                int compare = a.Start.CompareTo(b.Start);
                return compare != 0 ? compare : _regions.IndexOf(a).CompareTo(_regions.IndexOf(b));
            });
        }

        /// <summary>The region a <c>.org</c> falls into, or null when it falls outside all of them.</summary>
        public bool TryFind(long address, out MemoryRegion region)
        {
            foreach (MemoryRegion candidate in _regions)
            {
                if (candidate.Contains(address))
                {
                    region = candidate;
                    return true;
                }
            }
            region = null;
            return false;
        }

        /// <summary>The region a load address falls into, which is not the run window when the two differ.</summary>
        public bool TryFindByLoad(long address, out MemoryRegion region)
        {
            foreach (MemoryRegion candidate in _regions)
            {
                if (candidate.LoadAddress == address)
                {
                    region = candidate;
                    return true;
                }
            }
            region = null;
            return false;
        }

        /// <summary>
        /// The region the address was expected to fall into, which is the region a
        /// diagnostic names. The rule is the only one that can be stated without
        /// guessing: the declared regions are ordered by start address, and the
        /// expected region is the last one that starts at or below the address. So
        /// an address below every region expects the first one, and an address inside
        /// a gap expects the region the gap follows.
        /// </summary>
        public MemoryRegion ExpectedFor(long address)
        {
            if (_byStart.Count == 0)
                return null;

            MemoryRegion expected = _byStart[0];
            foreach (MemoryRegion candidate in _byStart)
            {
                if (candidate.Start <= address)
                    expected = candidate;
                else
                    break;
            }
            return expected;
        }

        /// <summary>Number of bytes reserved at an address, which is non-zero only inside a bss region.</summary>
        public long ReservedAt(long address)
        {
            MemoryRegion region;
            if (!TryFind(address, out region) || !region.IsBss || !region.HasSize)
                return 0;
            return region.End - address;
        }

        /// <summary>
        /// Cross-validates a <c>.org</c> against the declared regions. An empty map
        /// validates nothing, which is what keeps every pre-regions configuration
        /// behaving exactly as it did.
        /// </summary>
        public void ValidateOrigin(long address, SourceLocation location, IDiagnosticReporter diagnostics)
        {
            if (IsEmpty || diagnostics == null)
                return;

            MemoryRegion region;
            if (TryFind(address, out region))
            {
                if (region.IsBss)
                {
                    diagnostics.ReportError(location, string.Format(CultureInfo.InvariantCulture,
                        INSIDE_BSS_REGION, MemoryRegion.FormatAddress(address), region.Name));
                }
                return;
            }

            MemoryRegion expected = ExpectedFor(address);
            diagnostics.ReportError(location, string.Format(CultureInfo.InvariantCulture,
                OUTSIDE_ANY_REGION,
                MemoryRegion.FormatAddress(address),
                expected == null ? "none" : expected.Describe(),
                DescribeAll()));
        }

        public string DescribeAll()
        {
            if (_regions.Count == 0)
                return "none";
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < _regions.Count; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(_regions[i].Describe());
            }
            return text.ToString();
        }

        /// <summary>
        /// Region bounds as symbols usable in source, so a source can place a
        /// <c>.res</c> block in a declared bss region instead of hardcoding its
        /// address. Only names that are already valid identifiers produce symbols,
        /// and a region without a declared size produces no <c>END</c> or
        /// <c>SIZE</c> because neither would mean anything.
        /// </summary>
        public IDictionary<string, long> BuildPredefinedSymbols()
        {
            Dictionary<string, long> symbols = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (MemoryRegion region in _regions)
            {
                if (!IsIdentifier(region.Name))
                    continue;
                string prefix = region.Name.ToUpperInvariant();
                symbols[prefix + "_START"] = region.Start;
                symbols[prefix + "_LOAD"] = region.LoadAddress;
                symbols[prefix + "_RUN"] = region.RunAddress;
                if (region.HasSize)
                {
                    symbols[prefix + "_END"] = region.End - 1;
                    symbols[prefix + "_SIZE"] = region.Size;
                }
            }
            return symbols;
        }

        private static bool IsIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (!char.IsLetter(name[0]) && name[0] != '_')
                return false;
            for (int i = 1; i < name.Length; i++)
            {
                if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Builds the map from the configuration, reporting a diagnostic per
        /// incoherent declaration rather than throwing: a bad region is a
        /// configuration error, not a crash.
        /// </summary>
        public static bool TryBuild(RegionConf[] declarations, out MemoryMap map, out IReadOnlyList<Diagnostic> diagnostics)
        {
            List<Diagnostic> errors = new List<Diagnostic>();
            List<MemoryRegion> regions = new List<MemoryRegion>();
            SourceLocation origin = new SourceLocation(string.Empty, 0);

            if (declarations != null)
            {
                HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < declarations.Length; i++)
                {
                    RegionConf conf = declarations[i];
                    if (conf == null)
                    {
                        errors.Add(new Diagnostic(origin, "Region declaration is null."));
                        continue;
                    }

                    string name = string.IsNullOrWhiteSpace(conf.Name) ? "#" + i : conf.Name.Trim();
                    if (!names.Add(name))
                    {
                        errors.Add(new Diagnostic(origin, "Duplicate region name '" + name + "'."));
                        continue;
                    }

                    RegionType type;
                    if (!RegionTypes.TryParse(conf.Type, out type))
                    {
                        errors.Add(new Diagnostic(origin, "Region '" + name + "' has unknown type '" + conf.Type +
                            "'. Accepted types: " + RegionTypes.Accepted + "."));
                        continue;
                    }

                    if (conf.Bank.HasValue && conf.Bank.Value < 0)
                    {
                        errors.Add(new Diagnostic(origin, "Region '" + name + "' has a negative bank number."));
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(conf.Address))
                    {
                        errors.Add(new Diagnostic(origin, "Region '" + name + "' declares no Address."));
                        continue;
                    }

                    ushort? address = ParseAddress(conf.Address, name, "Address", errors);
                    if (address == null)
                        continue;

                    ushort? load = ParseAddress(conf.Load, name, "Load", errors);

                    long start = address.Value;
                    long end = AddressSpaceSize;
                    bool hasSize = !string.IsNullOrWhiteSpace(conf.Size);
                    if (hasSize)
                    {
                        int size;
                        if (!NumericLiteral.TryParseInteger(conf.Size, out size) || size < 0)
                        {
                            errors.Add(new Diagnostic(origin, "Region '" + name + "' has an unreadable Size '" + conf.Size + "'."));
                            continue;
                        }
                        end = start + size;
                        if (end > AddressSpaceSize)
                        {
                            errors.Add(new Diagnostic(origin, "Region '" + name + "' runs past the end of the address space: " +
                                MemoryRegion.FormatAddress(start) + " + " + size + " > $FFFF."));
                            continue;
                        }
                    }

                    regions.Add(new MemoryRegion(name, start, end, hasSize, conf.Bank, type, load ?? address.Value));
                }

                CheckOverlaps(regions, errors, origin);
            }

            map = new MemoryMap(regions);
            diagnostics = errors;
            return errors.Count == 0;
        }

        private static void CheckOverlaps(List<MemoryRegion> regions, List<Diagnostic> errors, SourceLocation origin)
        {
            for (int i = 0; i < regions.Count; i++)
            {
                for (int j = i + 1; j < regions.Count; j++)
                {
                    MemoryRegion a = regions[i];
                    MemoryRegion b = regions[j];
                    if (!a.HasSize || !b.HasSize)
                        continue;
                    if (a.Start < b.End && b.Start < a.End)
                    {
                        errors.Add(new Diagnostic(origin, "Regions '" + a.Name + "' and '" + b.Name +
                            "' overlap: " + a.DescribeWindow() + " and " + b.DescribeWindow() + "."));
                    }
                }
            }
        }

        private static ushort? ParseAddress(string text, string name, string field, List<Diagnostic> errors)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            ushort value;
            if (!NumericLiteral.TryParseUInt16(text, out value))
            {
                errors.Add(new Diagnostic(new SourceLocation(string.Empty, 0), "Region '" + name +
                    "' has an unreadable " + field + " '" + text + "'."));
                return null;
            }
            return value;
        }
    }

    /// <summary>
    /// Makes the map built for the current build visible to the directives.
    ///
    /// <para>
    /// A <c>.org</c> is a directive, and the directive dispatcher is created inside
    /// the shared <c>AssemblerEngine</c>, so the map cannot be injected through the
    /// assembly context without a change to that file. The alternative chosen here
    /// is a scope object: the build installs the map for the duration of the
    /// assembly, and the directive reads <see cref="Current"/>. A null map means
    /// "no regions declared", which is the pre-T6 behaviour.
    /// </para>
    /// </summary>
    public static class MemoryMapScope
    {
        [ThreadStatic]
        private static MemoryMap _current;

        public static MemoryMap Current { get { return _current; } }

        public static IDisposable Activate(MemoryMap map)
        {
            MemoryMap previous = _current;
            _current = map;
            return new Restore(previous);
        }

        private sealed class Restore : IDisposable
        {
            private readonly MemoryMap _previous;

            public Restore(MemoryMap previous) { _previous = previous; }

            public void Dispose() { _current = _previous; }
        }
    }
}
