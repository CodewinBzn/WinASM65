using System;
using System.Collections.Generic;
using System.IO;
using WinASM65.Output;
using WinASM65.Projects;

namespace WinASM65.Monitor.Debugging
{
    /// <summary>
    /// Builds a <see cref="SourceMap"/> from a project build run with
    /// <see cref="ProjectBuildOptions.CaptureListing"/>.
    ///
    /// The PRG offset of a line is computed the way the bytes travelled: its offset
    /// in the unit's object file (from the unit's origins), plus where that object
    /// file sits in the image, minus the iNES header when the image has one.
    /// </summary>
    public static class SourceMapBuilder
    {
        public static SourceMap Build(ProjectBuildResult result)
        {
            if (result == null)
                return SourceMap.Empty;

            int header = PrgStart(result.Image);
            List<MappedLine> lines = new List<MappedLine>();

            foreach (ProjectUnitResult unit in result.Units)
            {
                ImagePlacement placement = PlacementOf(unit, result.Placements);
                string unitDirectory = Path.GetDirectoryName(unit.SourceFile) ?? string.Empty;
                int originIndex = 0;

                foreach (ListingRow row in unit.Listing)
                {
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

            return new SourceMap(lines);
        }

        /// <summary>Where PRG-ROM starts in the image: after an iNES header and trainer, or at 0.</summary>
        public static int PrgStart(byte[] image)
        {
            if (image != null && image.Length >= 16
                && image[0] == (byte)'N' && image[1] == (byte)'E' && image[2] == (byte)'S' && image[3] == 0x1A)
                return 16 + ((image[6] & 0x04) != 0 ? 512 : 0);
            return 0;
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
