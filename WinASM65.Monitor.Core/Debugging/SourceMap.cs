using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WinASM65.Monitor.Debugging
{
    /// <summary>
    /// One emitted line of source: the CPU address it was assembled for, where its
    /// bytes sit in the cartridge, and the file and line that produced it.
    /// </summary>
    public sealed class MappedLine
    {
        public MappedLine(int address, int length, int? prgOffset, string path, int line)
        {
            Address = address;
            Length = length;
            PrgOffset = prgOffset;
            Path = path;
            Line = line;
        }

        /// <summary>The CPU address of the line's first byte.</summary>
        public int Address { get; }

        /// <summary>Bytes the line emitted.</summary>
        public int Length { get; }

        /// <summary>
        /// Offset of the first byte in PRG-ROM, or null when the build could not
        /// place it (a unit that is not part of the image). Two lines may share a
        /// CPU address in different banks; they never share this.
        /// </summary>
        public int? PrgOffset { get; }

        /// <summary>Full path of the source file, includes included.</summary>
        public string Path { get; }

        /// <summary>1-based line number.</summary>
        public int Line { get; }

        public override string ToString()
        {
            return System.IO.Path.GetFileName(Path) + ":" + Line + " $" + Address.ToString("X4");
        }
    }

    /// <summary>
    /// Source lines to addresses and back, for one build of a project.
    ///
    /// Built from the listing the build itself produced, never from a file
    /// assembled on its own: a unit's addresses depend on the target's load
    /// address, the memory map and the order of the project, and only the build
    /// knows all three.
    /// </summary>
    public sealed class SourceMap
    {
        public static readonly SourceMap Empty = new SourceMap(new List<MappedLine>());

        private readonly SortedList<int, string> _labels = new SortedList<int, string>();
        private readonly Dictionary<string, long> _symbols = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        private readonly List<MappedLine> _lines;
        private readonly Dictionary<string, SortedList<int, List<MappedLine>>> _byFile =
            new Dictionary<string, SortedList<int, List<MappedLine>>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, List<MappedLine>> _byAddress = new Dictionary<int, List<MappedLine>>();
        private readonly Dictionary<int, MappedLine> _byPrg = new Dictionary<int, MappedLine>();

        public SourceMap(IEnumerable<MappedLine> lines)
            : this(lines, null, null)
        {
        }

        /// <param name="labels">Code labels by address; the first one seen at an address wins.</param>
        /// <param name="symbols">Every name the build defined, labels and constants.</param>
        public SourceMap(IEnumerable<MappedLine> lines, IEnumerable<KeyValuePair<int, string>> labels,
            IEnumerable<KeyValuePair<string, long>> symbols)
        {
            if (labels != null)
            {
                foreach (KeyValuePair<int, string> label in labels)
                {
                    if (!_labels.ContainsKey(label.Key))
                        _labels.Add(label.Key, label.Value);
                }
            }
            if (symbols != null)
            {
                foreach (KeyValuePair<string, long> symbol in symbols)
                {
                    if (!_symbols.ContainsKey(symbol.Key))
                        _symbols[symbol.Key] = symbol.Value;
                }
            }

            _lines = new List<MappedLine>(lines ?? Enumerable.Empty<MappedLine>());
            foreach (MappedLine line in _lines)
            {
                SortedList<int, List<MappedLine>> byLine;
                if (!_byFile.TryGetValue(line.Path, out byLine))
                    _byFile[line.Path] = byLine = new SortedList<int, List<MappedLine>>();
                List<MappedLine> atLine;
                if (!byLine.TryGetValue(line.Line, out atLine))
                    byLine[line.Line] = atLine = new List<MappedLine>();
                atLine.Add(line);

                List<MappedLine> atAddress;
                if (!_byAddress.TryGetValue(line.Address, out atAddress))
                    _byAddress[line.Address] = atAddress = new List<MappedLine>();
                atAddress.Add(line);

                if (line.PrgOffset.HasValue)
                    _byPrg[line.PrgOffset.Value] = line;
            }
        }

        public IReadOnlyList<MappedLine> Lines
        {
            get { return _lines; }
        }

        public IEnumerable<string> Files
        {
            get { return _byFile.Keys; }
        }

        /// <summary>Every name the build defined, with its value.</summary>
        public IReadOnlyDictionary<string, long> Symbols
        {
            get { return _symbols; }
        }

        /// <summary>
        /// The closest code label at or below <paramref name="address"/>, within
        /// <paramref name="reach"/> bytes, or null. Names a stack frame the way a
        /// reader would: the routine the program counter is in.
        /// </summary>
        public string NearestLabel(int address, out int offset, int reach = 0x400)
        {
            offset = 0;
            IList<int> keys = _labels.Keys;
            int low = 0, high = keys.Count - 1, found = -1;
            while (low <= high)
            {
                int middle = (low + high) / 2;
                if (keys[middle] <= address) { found = middle; low = middle + 1; }
                else high = middle - 1;
            }
            if (found < 0 || address - keys[found] > reach)
                return null;
            offset = address - keys[found];
            return _labels.Values[found];
        }

        /// <summary>Every emission of a line: one, or several for a macro or a repeat.</summary>
        public IReadOnlyList<MappedLine> AtLine(string path, int line)
        {
            SortedList<int, List<MappedLine>> byLine;
            List<MappedLine> found;
            if (path != null && _byFile.TryGetValue(Normalize(path), out byLine) && byLine.TryGetValue(line, out found))
                return found;
            return new List<MappedLine>();
        }

        /// <summary>Every line assembled at a CPU address: several when banks share it.</summary>
        public IReadOnlyList<MappedLine> AtAddress(int address)
        {
            List<MappedLine> found;
            return _byAddress.TryGetValue(address, out found) ? found : new List<MappedLine>();
        }

        /// <summary>
        /// The line executing at <paramref name="pc"/>. With a PRG offset the answer is
        /// exact; without one, a CPU address several banks share is ambiguous and the
        /// first candidate is returned with <paramref name="ambiguous"/> set.
        /// </summary>
        public MappedLine Resolve(int pc, int? prgOffset, out bool ambiguous)
        {
            ambiguous = false;
            MappedLine exact;
            if (prgOffset.HasValue && _byPrg.TryGetValue(prgOffset.Value, out exact) && exact.Address == pc)
                return exact;

            IReadOnlyList<MappedLine> candidates = AtAddress(pc);
            if (candidates.Count == 0)
                return null;
            ambiguous = candidates.Count > 1;
            return candidates[0];
        }

        /// <summary>
        /// The line a breakpoint on <paramref name="line"/> really lands on: the line
        /// itself when it emits, otherwise the next emitting line within
        /// <paramref name="lookahead"/> lines, which is where execution would first
        /// pass. A comment above a routine thus breaks on its first instruction.
        /// </summary>
        public bool TryNearestEmittingLine(string path, int line, out int emittingLine, int lookahead = 5)
        {
            emittingLine = 0;
            SortedList<int, List<MappedLine>> byLine;
            if (path == null || !_byFile.TryGetValue(Normalize(path), out byLine))
                return false;

            for (int candidate = line; candidate <= line + lookahead; candidate++)
            {
                if (byLine.ContainsKey(candidate))
                {
                    emittingLine = candidate;
                    return true;
                }
            }
            return false;
        }

        public static string Normalize(string path)
        {
            try
            {
                return System.IO.Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return path;
            }
        }
    }
}
