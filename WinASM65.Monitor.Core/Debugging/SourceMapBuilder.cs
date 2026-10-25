using System;
using System.Collections.Generic;
using System.IO;
using WinASM65.Output;
using WinASM65.Projects;
using WinASM65.Systems;

namespace WinASM65.Monitor.Debugging
{
    /// <summary>
    /// Builds a <see cref="SourceMap"/> from a project build run with
    /// <see cref="ProjectBuildOptions.CaptureListing"/>.
    ///
    /// The PRG offset of a line is computed the way the bytes travelled: its offset
    /// in the unit's object file (from the unit's origins), plus where that object
    /// file sits in the image, minus the header the machine's images carry (the
    /// iNES header on the NES).
    /// </summary>
    public static class SourceMapBuilder
    {
        /// <summary>Builds the map for the machine the project names, or the one its image is for.</summary>
        public static SourceMap Build(ProjectBuildResult result)
        {
            return Build(result, null);
        }

        public static SourceMap Build(ProjectBuildResult result, SystemDefinition system)
        {
            if (result == null)
                return SourceMap.Empty;

            system = system ?? SystemRegistry.Resolve(result.Target != null ? result.Target.SystemId : null, null, result.Image);
            int header = system.ImageHeaderLength(result.Image);
            List<MappedLine> lines = new List<MappedLine>();
            List<KeyValuePair<int, string>> labels = new List<KeyValuePair<int, string>>();
            List<KeyValuePair<string, long>> symbols = new List<KeyValuePair<string, long>>();

            foreach (ProjectUnitResult unit in result.Units)
            {
                ImagePlacement placement = PlacementOf(unit, result.Placements);
                string unitDirectory = Path.GetDirectoryName(unit.SourceFile) ?? string.Empty;
                int originIndex = 0;
                symbols.AddRange(unit.Symbols);

                foreach (ListingRow row in unit.Listing)
                {
                    if (row.Kind == ListingRowKind.Label && row.Address.HasValue)
                    {
                        string name = LabelName(row.Text);
                        if (name != null)
                            labels.Add(new KeyValuePair<int, string>(row.Address.Value, name));
                        continue;
                    }

                    if (row.Kind != ListingRowKind.Instruction || !row.Address.HasValue || row.Bytes.Count == 0)
                        continue;

                    int address = row.Address.Value;
                    int? prg = null;
                    int objectOffset;
                    if (placement != null && TryPlace(address, unit.Origins, unit.EmittedLength, ref originIndex, out objectOffset))
                    {
                        int imageOffset = placement.Offset + objectOffset;
                        if (imageOffset >= header)
                            prg = imageOffset - header;
                    }

                    lines.Add(new MappedLine(address, row.Bytes.Count, prg,
                        PathOf(row.SourceName, unit.SourceFile, unitDirectory), row.LineNumber));
                }
            }

            return new SourceMap(lines, labels, symbols);
        }

        /// <summary>The label a source line defines: its first word, without the colon.</summary>
        private static string LabelName(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            string trimmed = text.TrimStart();
            int end = 0;
            while (end < trimmed.Length && trimmed[end] != ':' && trimmed[end] != ';' && !char.IsWhiteSpace(trimmed[end]))
                end++;
            return end == 0 ? null : trimmed.Substring(0, end);
        }

        /// <summary>Where the program starts in an image whose machine is recognised by its header, or 0.</summary>
        public static int PrgStart(byte[] image)
        {
            SystemDefinition system = SystemRegistry.Detect(image);
            return system != null ? system.ImageHeaderLength(image) : 0;
        }

        private static ImagePlacement PlacementOf(ProjectUnitResult unit, IReadOnlyList<ImagePlacement> placements)
        {
            if (string.IsNullOrEmpty(unit.ObjectFile))
                return null;
            string objectFile = SourceMap.Normalize(unit.ObjectFile);
            foreach (ImagePlacement placement in placements)
            {
                if (string.Equals(SourceMap.Normalize(placement.FileName), objectFile, StringComparison.OrdinalIgnoreCase))
                    return placement;
            }
            return null;
        }

        /// <summary>
        /// The object-file offset of a byte assembled for <paramref name="address"/>.
        /// Rows come in emission order, so the search starts at the origin the
        /// previous row was found in: two blocks of one unit at the same address are
        /// then told apart by the order they were written in.
        /// </summary>
        private static bool TryPlace(int address, IReadOnlyList<EmitterOrigin> origins, int emittedLength,
            ref int start, out int offset)
        {
            offset = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = pass == 0 ? start : 0; i < origins.Count; i++)
                {
                    int begin = origins[i].BufferOffset;
                    int end = i + 1 < origins.Count ? origins[i + 1].BufferOffset : emittedLength;
                    int relative = address - origins[i].Address;
                    if (relative >= 0 && relative < end - begin)
                    {
                        start = i;
                        offset = begin + relative;
                        return true;
                    }
                }
            }
            return false;
        }

        private static string PathOf(string sourceName, string unitSource, string unitDirectory)
        {
            string name = string.IsNullOrEmpty(sourceName) ? unitSource : sourceName;
            if (!Path.IsPathRooted(name))
                name = Path.Combine(unitDirectory, name);
            return SourceMap.Normalize(name);
        }
    }
}
