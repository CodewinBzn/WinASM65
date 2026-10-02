// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Building listing rows from the fragment stream the assembler emits

using System.Collections.Generic;

namespace WinASM65.Output
{
    /// <summary>
    /// One piece of a listing line, exactly as the assembler handed it over: a
    /// run of source text, an announcement that this line is an origin, a label,
    /// an instruction of N bytes, a reservation or a constant, or the number of
    /// the source line behind this line had. The order matters -- a label on an
    /// instruction line arrives before the instruction.
    /// </summary>
    internal sealed class ListingFragment
    {
        private readonly LineType _type;
        private readonly int _value;
        private readonly string _text;
        private readonly bool _isSourceLine;

        public ListingFragment(string text)
        {
            _type = LineType.NONE;
            _value = 0;
            _text = text;
        }

        public ListingFragment(LineType type, int value)
        {
            _type = type;
            _value = value;
            _text = string.Empty;
        }

        public ListingFragment(int sourceLineNumber)
        {
            _type = LineType.NONE;
            _value = sourceLineNumber;
            _text = string.Empty;
            _isSourceLine = true;
        }

        public LineType Type { get { return _type; } }
        public int Value { get { return _value; } }
        public string Text { get { return _text; } }
        public bool IsSourceLine { get { return _isSourceLine; } }
    }

    /// <summary>
    /// Turns the fragment stream into listing rows, walking the emitted image
    /// in the order the assembler emitted it.
    /// <para>
    /// This is the single place where a line of the stream becomes rows. The
    /// file writer and the in-memory collector both come through here, which is
    /// what makes the two agree by construction instead of by review.
    /// </para>
    /// </summary>
    internal static class ListingRowBuilder
    {
        private const int BytesPerRow = 4;

        public static List<ListingRow> Build(IReadOnlyList<IReadOnlyList<ListingFragment>> lines, byte[] memoryBytes)
        {
            List<ListingRow> rows = new List<ListingRow>();
            byte[] memory = memoryBytes ?? new byte[0];
            int memoryIndex = 0;
            int currentAddress = 0;
            int lineNumber = 0;

            for (int i = 0; i < lines.Count; i++)
            {
                IReadOnlyList<ListingFragment> fragments = lines[i];
                if (fragments == null || fragments.Count == 0)
                    continue;

                lineNumber = SourceLineNumber(fragments, lineNumber);
                string text = ConcatText(fragments);

                if (!HasKind(fragments))
                {
                    rows.Add(new ListingRow(ListingRowKind.Text, lineNumber, null, null, null, text));
                    continue;
                }

                for (int f = 0; f < fragments.Count; f++)
                {
                    ListingFragment fragment = fragments[f];
                    if (fragment.Type == LineType.NONE || fragment.IsSourceLine)
                        continue;

                    switch (fragment.Type)
                    {
                        case LineType.ORG:
                            currentAddress = (ushort)fragment.Value;
                            rows.Add(new ListingRow(ListingRowKind.Origin, lineNumber, currentAddress, null, null, text));
                            break;

                        case LineType.LABEL:
                            rows.Add(new ListingRow(ListingRowKind.Label, lineNumber,
                                (ushort)fragment.Value, null, null, text));
                            break;

                        case LineType.RES:
                        case LineType.CONST:
                            rows.Add(new ListingRow(fragment.Type == LineType.RES ? ListingRowKind.Reserve : ListingRowKind.Constant,
                                lineNumber, null, fragment.Value, null, text));
                            break;

                        case LineType.INST:
                            AppendInstructionRows(rows, fragment.Value, text, lineNumber, ref currentAddress, ref memoryIndex, memory);
                            break;
                    }
                }
            }

            return rows;
        }

        private static void AppendInstructionRows(List<ListingRow> rows, int byteCount, string text, int lineNumber,
            ref int currentAddress, ref int memoryIndex, byte[] memory)
        {
            List<byte> rowBytes = new List<byte>(BytesPerRow);
            bool textPlaced = false;

            for (int i = 0; i < byteCount; i++)
            {
                byte value = memoryIndex < memory.Length ? memory[memoryIndex] : (byte)0;
                memoryIndex++;
                rowBytes.Add(value);
                currentAddress = currentAddress + 1;

                if (rowBytes.Count < BytesPerRow)
                    continue;

                // The source is written once, on the row that closes the first
                // full group. The rows after it carry bytes only: an instruction
                // is one line of source however many cells it occupies.
                string rowText = textPlaced ? null : text;
                textPlaced = true;
                rows.Add(new ListingRow(ListingRowKind.Instruction, lineNumber, currentAddress - rowBytes.Count, null,
                    rowBytes.ToArray(), rowText));
                rowBytes.Clear();
            }

            if (rowBytes.Count == 0 && textPlaced)
                return;

            rows.Add(new ListingRow(ListingRowKind.Instruction, lineNumber, currentAddress - rowBytes.Count, null,
                rowBytes.ToArray(), textPlaced ? null : text));
        }

        private static bool HasKind(IReadOnlyList<ListingFragment> fragments)
        {
            for (int i = 0; i < fragments.Count; i++)
            {
                if (fragments[i].Type != LineType.NONE && !fragments[i].IsSourceLine)
                    return true;
            }
            return false;
        }

        private static string ConcatText(IReadOnlyList<ListingFragment> fragments)
        {
            string text = string.Empty;
            for (int i = 0; i < fragments.Count; i++)
            {
                if (fragments[i].Type == LineType.NONE && !fragments[i].IsSourceLine)
                    text += fragments[i].Text;
            }
            return text;
        }

        private static int SourceLineNumber(IReadOnlyList<ListingFragment> fragments, int fallback)
        {
            for (int i = 0; i < fragments.Count; i++)
            {
                if (fragments[i].IsSourceLine)
                    return fragments[i].Value;
            }
            return fallback;
        }
    }
}