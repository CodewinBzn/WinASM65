// WinASM65 - Resolves modules against each other and produces a flat image

using System;
using System.Collections.Generic;
using WinASM65.Core;
using WinASM65.Modules;
using WinASM65.Output;

namespace WinASM65.Linking
{
    /// <summary>One segment once the linker has decided where it goes.</summary>
    public sealed class PlacedSegment
    {
        public string Name { get; internal set; }
        public int ModuleIndex { get; internal set; }
        public int SegmentIndex { get; internal set; }
        public SegmentKind Kind { get; internal set; }
        public byte Bank { get; internal set; }
        public ushort Address { get; internal set; }
        public uint Size { get; internal set; }
        public string SourceModule { get; internal set; }

        public override string ToString()
        {
            return string.Format("{0} @ ${1:X4} ({2}, {3} octets)", Name, Address, Kind, Size);
        }
    }

    /// <summary>
    /// An absolute reference the linker has already resolved. The value in the
    /// image is correct for the address the image was linked at; a target that
    /// moves the image has to add its own load bias to every one of these, so the
    /// sites are kept rather than discarded with the relocation records.
    /// </summary>
    public sealed class LinkedReference
    {
        public ushort Address { get; internal set; }

        /// <summary>1 for a single byte reference, 2 for a word.</summary>
        public int Width { get; internal set; }

        public override string ToString()
        {
            return "$" + Address.ToString("X4") + " (" + Width + ")";
        }
    }

    /// <summary>
    /// The flat, burnable image. This is mode A produced from modules; it does not
    /// replace the direct-burn path, it consumes what that path produces.
    /// </summary>
    public sealed class LinkedImage
    {
        public ushort OriginAddress { get; internal set; }
        public byte[] Data { get; internal set; }
        public IReadOnlyList<PlacedSegment> Segments { get; internal set; }
        public IReadOnlyDictionary<string, ushort> Symbols { get; internal set; }

        /// <summary>
        /// Where the absolute references ended up, in link order. Relative
        /// branches are absent: they are already correct at any address, since
        /// moving the code moves both ends.
        /// </summary>
        public IReadOnlyList<LinkedReference> References { get; internal set; }

        public LinkedImage()
        {
            Data = new byte[0];
            Segments = new List<PlacedSegment>();
            Symbols = new Dictionary<string, ushort>(StringComparer.Ordinal);
            References = new List<LinkedReference>();
        }
    }

    public class LinkerOptions
    {
        /// <summary>
        /// Shifts every segment by this amount. Placing one shared routine at a second
        /// address is a shift of the whole image, not a move of one segment: the module
        /// does not know it is being reused.
        /// </summary>
        public int AddressShift { get; set; }

        /// <summary>Forces the image origin instead of deriving it from the lowest segment.</summary>
        public ushort? BaseAddress { get; set; }

        public LinkerOptions()
        {
            AddressShift = 0;
            BaseAddress = null;
        }
    }

    /// <summary>
    /// Pairs imports with exports by name, places segments, applies relocations and
    /// emits a flat image.
    /// <para>
    /// Resolution is by name, the way Windows resolves imports. The module name on an
    /// import makes a diagnostic precise but does not constrain the search. A module
    /// that asks for Helper may get it from any module that exports it; a wrong module
    /// name is reported rather than silently accepted, because pretending a mismatch
    /// is harmless is how a link becomes a mystery.
    /// </para>
    /// </summary>
    public class Linker
    {
        public OperationResult Link(IReadOnlyList<ModuleImage> modules, out LinkedImage image)
        {
            return Link(modules, null, out image);
        }

        public OperationResult Link(IReadOnlyList<ModuleImage> modules, LinkerOptions options, out LinkedImage image)
        {
            image = null;
            LinkerOptions effective = options ?? new LinkerOptions();
            List<Diagnostic> diagnostics = new List<Diagnostic>();

            if (modules == null || modules.Count == 0)
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0),
                    "Nothing to link: no module was given."));
                return new OperationResult(false, diagnostics);
            }

            List<PlacedSegment> placed;
            if (!Place(modules, effective, out placed, diagnostics))
                return new OperationResult(false, diagnostics);

            if (!CheckOverlap(placed, diagnostics))
                return new OperationResult(false, diagnostics);

            Dictionary<string, ushort> symbols;
            if (!BuildSymbolTable(modules, placed, out symbols, diagnostics))
                return new OperationResult(false, diagnostics);

            if (!ResolveImports(modules, symbols, diagnostics))
                return new OperationResult(false, diagnostics);

            ushort origin, end;
            if (!ComputeExtent(placed, effective, out origin, out end, diagnostics))
                return new OperationResult(false, diagnostics);

            byte[] data = new byte[end - origin];
            if (!CopySegments(modules, placed, data, origin, diagnostics))
                return new OperationResult(false, diagnostics);
            List<LinkedReference> references = new List<LinkedReference>();
            if (!ApplyRelocations(modules, placed, symbols, data, origin, references, diagnostics))
                return new OperationResult(false, diagnostics);

            LinkedImage result = new LinkedImage();
            result.OriginAddress = origin;
            result.Data = data;
            result.Segments = placed;
            result.Symbols = symbols;
            result.References = references;
            image = result;
            return new OperationResult(true, diagnostics);
        }

        // ------------------------------------------------------------ placement

        private bool Place(IReadOnlyList<ModuleImage> modules, LinkerOptions options,
            out List<PlacedSegment> placed, List<Diagnostic> diagnostics)
        {
            placed = new List<PlacedSegment>();
            ushort cursor = 0;

            for (int m = 0; m < modules.Count; m++)
            {
                ModuleImage module = modules[m];
                string owner = NameOf(module, m);
                for (int s = 0; s < module.Segments.Count; s++)
                {
                    ModuleSegment segment = module.Segments[s];
                    if (segment.OccupiedSize == 0)
                        continue;

                    // A segment keeps the origin its unit was written against. A unit
                    // with no origin goes after everything placed so far, so a link
                    // stays deterministic instead of refusing.
                    ushort address = segment.OriginAddress != 0
                        ? segment.OriginAddress
                        : (cursor != 0 ? cursor : (ushort)0x0200);
                    if (options.AddressShift != 0)
                        address = (ushort)(address + options.AddressShift);
                    address = Align(address, segment.Alignment);

                    long finish = (long)address + segment.OccupiedSize;
                    if (finish > 0x10000)
                    {
                        diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0),
                            "Segment '" + segment.Name + "' of " + owner + " does not fit: $" +
                            address.ToString("X4") + " + " + segment.OccupiedSize + " exceeds $FFFF."));
                        return false;
                    }

                    placed.Add(new PlacedSegment
                    {
                        Name = segment.Name,
                        ModuleIndex = m,
                        SegmentIndex = s,
                        Kind = segment.Kind,
                        Bank = segment.Bank,
                        Address = address,
                        Size = segment.OccupiedSize,
                        SourceModule = owner
                    });
                    if (cursor < (ushort)finish)
                        cursor = (ushort)finish;
                }
            }
            return true;
        }

        private static bool CheckOverlap(List<PlacedSegment> placed, List<Diagnostic> diagnostics)
        {
            List<PlacedSegment> sorted = new List<PlacedSegment>(placed);
            sorted.Sort(delegate (PlacedSegment a, PlacedSegment b) { return a.Address.CompareTo(b.Address); });
            for (int i = 1; i < sorted.Count; i++)
            {
                PlacedSegment previous = sorted[i - 1];
                PlacedSegment current = sorted[i];
                if ((long)previous.Address + previous.Size > current.Address)
                {
                    diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0),
                        "Segments overlap: '" + previous.Name + "' of " + previous.SourceModule + " at $" +
                        previous.Address.ToString("X4") + " and '" + current.Name + "' of " +
                        current.SourceModule + " at $" + current.Address.ToString("X4") + "."));
                    return false;
                }
            }
            return true;
        }

        private static ushort Align(ushort address, uint alignment)
        {
            if (alignment < 2)
                return address;
            uint value = address;
            uint remainder = value % alignment;
            if (remainder == 0)
                return address;
            uint target = value + (alignment - remainder);
            return target > 0xFFFF ? address : (ushort)target;
        }

        private static string NameOf(ModuleImage module, int index)
        {
            return string.IsNullOrEmpty(module.ModuleName) ? "module " + index : module.ModuleName;
        }

        // --------------------------------------------------------------- symbols

        private bool BuildSymbolTable(IReadOnlyList<ModuleImage> modules, List<PlacedSegment> placed,
            out Dictionary<string, ushort> symbols, List<Diagnostic> diagnostics)
        {
            symbols = new Dictionary<string, ushort>(StringComparer.Ordinal);
            Dictionary<string, string> origin = new Dictionary<string, string>(StringComparer.Ordinal);

            for (int m = 0; m < modules.Count; m++)
            {
                ModuleImage module = modules[m];
                string owner = NameOf(module, m);
                for (int e = 0; e < module.Exports.Count; e++)
                {
                    ModuleExport export = module.Exports[e];
                    PlacedSegment host;
                    if (!TrySegment(placed, m, export.SegmentIndex, out host))
                    {
                        diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0),
                            "Export '" + export.Name + "' of " + owner + " names segment " +
                            export.SegmentIndex + ", which does not exist."));
                        return false;
                    }
                    long value = (long)host.Address + export.Offset;
                    if (value > 0xFFFF)
                    {
                        diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0),
                            "Export '" + export.Name + "' of " + owner + " falls outside the address space at $" +
                            value.ToString("X") + "."));
                        return false;
                    }
                    if (origin.ContainsKey(export.Name))
                    {
                        diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0),
                            "Symbol '" + export.Name + "' is exported by both " + origin[export.Name] +
                            " and " + owner + "."));
                        return false;
                    }
                    origin[export.Name] = owner;
                    symbols[export.Name] = (ushort)value;
                }
            }
            return true;
        }

        // --------------------------------------------------------------- imports

        private bool ResolveImports(IReadOnlyList<ModuleImage> modules, Dictionary<string, ushort> symbols,
            List<Diagnostic> diagnostics)
        {
            for (int m = 0; m < modules.Count; m++)
            {
                ModuleImage module = modules[m];
                string owner = NameOf(module, m);
                for (int i = 0; i < module.Imports.Count; i++)
                {
                    ModuleImport import = module.Imports[i];
                    if (symbols.ContainsKey(import.Name))
                        continue;
                    string expected = string.IsNullOrEmpty(import.ModuleName)
                        ? ""
                        : ": expected from module '" + import.ModuleName + "'";
                    diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0),
                        "Unsatisfied import '" + import.Name + "' in " + owner + expected +
                        ". No module on the link line exports that name."));
                    return false;
                }
            }
            return true;
        }

        // ---------------------------------------------------------------- extent

        private bool ComputeExtent(List<PlacedSegment> placed, LinkerOptions options,
            out ushort origin, out ushort end, List<Diagnostic> diagnostics)
        {
            origin = 0;
            end = 0;
            if (placed.Count == 0)
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0),
                    "Nothing to link: every segment is empty."));
                return false;
            }
            origin = options.BaseAddress ?? placed[0].Address;
            end = origin;
            foreach (PlacedSegment segment in placed)
            {
                if (segment.Address < origin && !options.BaseAddress.HasValue)
                    origin = segment.Address;
                long finish = (long)segment.Address + segment.Size;
                if (finish > end)
                    end = (ushort)finish;
            }
            return true;
        }

        // --------------------------------------------------------------- payload

        private bool CopySegments(IReadOnlyList<ModuleImage> modules, List<PlacedSegment> placed,
            byte[] data, ushort origin, List<Diagnostic> diagnostics)
        {
            foreach (PlacedSegment segment in placed)
            {
                if (segment.Kind == SegmentKind.Bss)
                    continue;
                ModuleSegment source = modules[segment.ModuleIndex].Segments[segment.SegmentIndex];
                int destination = segment.Address - origin;
                if (destination < 0 || destination + source.Data.Length > data.Length)
                {
                    diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0),
                        "Segment '" + segment.Name + "' falls outside the linked image."));
                    return false;
                }
                Array.Copy(source.Data, 0, data, destination, source.Data.Length);
            }
            return true;
        }

        // ------------------------------------------------------------ relocation

        private bool ApplyRelocations(IReadOnlyList<ModuleImage> modules, List<PlacedSegment> placed,
            Dictionary<string, ushort> symbols, byte[] data, ushort origin,
            List<LinkedReference> references, List<Diagnostic> diagnostics)
        {
            for (int m = 0; m < modules.Count; m++)
            {
                ModuleImage module = modules[m];
                string owner = NameOf(module, m);
                for (int r = 0; r < module.Relocations.Count; r++)
                {
                    RelocationRecord record = module.Relocations[r];
                    if (record.Type == RelocationType.None)
                        continue;

                    PlacedSegment host;
                    if (!TrySegment(placed, m, record.SegmentIndex, out host))
                    {
                        diagnostics.Add(new Diagnostic(new SourceLocation(record.SourceFile, record.SourceLine),
                            "Relocation in " + owner + " names segment " + record.SegmentIndex + ", which does not exist."));
                        return false;
                    }

                    long site = (long)host.Address + record.Offset;
                    if (site < origin || site + record.Width > origin + data.Length)
                    {
                        diagnostics.Add(new Diagnostic(new SourceLocation(record.SourceFile, record.SourceLine),
                            "Relocation site $" + site.ToString("X4") + " falls outside the linked image."));
                        return false;
                    }

                    ushort value;
                    if (!string.IsNullOrEmpty(record.TargetSymbol))
                    {
                        if (!symbols.TryGetValue(record.TargetSymbol, out value))
                        {
                            diagnostics.Add(new Diagnostic(new SourceLocation(record.SourceFile, record.SourceLine),
                                "Relocation in " + owner + " needs '" + record.TargetSymbol +
                                "', which no linked module exports."));
                            return false;
                        }
                    }
                    else
                    {
                        value = (ushort)record.Value;
                    }

                    if (!Write(data, (int)(site - origin), origin, value, record, owner, diagnostics))
                        return false;

                    // A site two records name has to be biased once, not twice:
                    // the stub adds the bias to what it finds in memory, and
                    // finding it twice is a corrupted address rather than a
                    // doubled one.
                    //
                    // Only the word types qualify. Zp8 names zero page, which is
                    // zero page wherever the program is loaded, and Imm8 and
                    // Data8 carry a value rather than an address; biasing any of
                    // them would corrupt something that is already right.
                    if (record.Type == RelocationType.Abs16 || record.Type == RelocationType.Data16)
                    {
                        if (!Holds(references, (ushort)site, record.Width))
                            references.Add(new LinkedReference
                            {
                                Address = (ushort)site,
                                Width = record.Width == 1 ? 1 : 2
                            });
                    }
                }
            }
            return true;
        }

        private static bool Holds(List<LinkedReference> references, ushort address, int width)
        {
            for (int i = 0; i < references.Count; i++)
            {
                if (references[i].Address == address && references[i].Width == width)
                    return true;
            }
            return false;
        }

        private bool Write(byte[] data, int offset, ushort origin, ushort value, RelocationRecord record,
            string owner, List<Diagnostic> diagnostics)
        {
            switch (record.Type)
            {
                case RelocationType.Rel8:
                {
                    // The distance runs from the instruction after the field, so the
                    // site must be resolved back to an absolute address first.
                    // Working on the image offset alone drops the origin and biases
                    // every branch by it, which looks correct only when origin is 0.
                    int site = offset + origin;
                    int delta = (int)value - (site + record.Width);
                    if (delta < -128 || delta > 127)
                    {
                        diagnostics.Add(new Diagnostic(new SourceLocation(record.SourceFile, record.SourceLine),
                            "Relative branch in " + owner + " is too big: " + delta + " octets."));
                        return false;
                    }
                    data[offset] = (byte)(delta & 0xFF);
                    return true;
                }
                case RelocationType.Abs16:
                case RelocationType.Data16:
                case RelocationType.Seg:
                    data[offset] = (byte)(value & 0xFF);
                    data[offset + 1] = (byte)((value >> 8) & 0xFF);
                    return true;
                case RelocationType.Zp8:
                case RelocationType.Imm8:
                case RelocationType.Data8:
                    if (value > 0xFF)
                    {
                        diagnostics.Add(new Diagnostic(new SourceLocation(record.SourceFile, record.SourceLine),
                            "Value $" + value.ToString("X4") + " in " + owner + " does not fit on one octet."));
                        return false;
                    }
                    data[offset] = (byte)(value & 0xFF);
                    return true;
                default:
                    diagnostics.Add(new Diagnostic(new SourceLocation(record.SourceFile, record.SourceLine),
                        "Unknown relocation type " + (int)record.Type + " in " + owner + "."));
                    return false;
            }
        }

        private static bool TrySegment(List<PlacedSegment> placed, int moduleIndex, int segmentIndex,
            out PlacedSegment found)
        {
            foreach (PlacedSegment segment in placed)
            {
                if (segment.ModuleIndex == moduleIndex && segment.SegmentIndex == segmentIndex)
                {
                    found = segment;
                    return true;
                }
            }
            found = null;
            return false;
        }
    }
}
