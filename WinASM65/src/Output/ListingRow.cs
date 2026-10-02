// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Listing row model

using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WinASM65.Output
{
    /// <summary>
    /// What a listing row reports, named after the <see cref="LineType"/> the
    /// assembler announced for it.
    /// </summary>
    public enum ListingRowKind
    {
        /// <summary>A source line that emitted nothing.</summary>
        Text,
        /// <summary>A <c>.org</c>.</summary>
        Origin,
        /// <summary>An instruction or a data directive, and its bytes.</summary>
        Instruction,
        /// <summary>A label definition.</summary>
        Label,
        /// <summary>A <c>.res</c> reservation.</summary>
        Reserve,
        /// <summary>A constant definition.</summary>
        Constant
    }

    /// <summary>
    /// One row of a listing: a display row, not a source line. An instruction
    /// emitting more than four bytes spans several rows, and the extra rows
    /// carry bytes and no text; that is why a row and a source line number are
    /// two different things and both are kept here.
    /// </summary>
    public sealed class ListingRow
    {
        /// <summary>
        /// The source line this row came from, or 0 when the row has no source
        /// line behind it -- a byte continuation row, for instance. With
        /// <c>.include</c> it is the line in the file being read at that moment,
        /// so an included file's lines interleave with the including file's.
        /// </summary>
        public int LineNumber { get; private set; }

        public ListingRowKind Kind { get; private set; }

        /// <summary>
        /// The address this row starts at, or null when it has none: a blank
        /// source line has no address.
        /// </summary>
        public int? Address { get; private set; }

        /// <summary>
        /// The reserved or constant value, for the row kinds that carry one.
        /// </summary>
        public int? Value { get; private set; }

        /// <summary>
        /// The bytes this row displays, in emission order. Never more than four,
        /// which is what keeps the text column aligned however long the
        /// instruction is.
        /// </summary>
        public IReadOnlyList<byte> Bytes { get; private set; }

        /// <summary>
        /// The source text, or null on a byte continuation row, which has bytes
        /// and no text of its own.
        /// </summary>
        public string Text { get; private set; }

        public ListingRow(ListingRowKind kind, int lineNumber, int? address, int? value,
            IReadOnlyList<byte> bytes, string text)
        {
            Kind = kind;
            LineNumber = lineNumber;
            Address = address;
            Value = value;
            Bytes = bytes ?? new byte[0];
            Text = text;
        }

        /// <summary>
        /// Renders this row exactly as the <c>.lst</c> file renders it. Both
        /// writers go through here, so the file and the in-memory listing can
        /// never drift apart.
        /// </summary>
        public string Render()
        {
            StringBuilder text = new StringBuilder();

            switch (Kind)
            {
                case ListingRowKind.Origin:
                case ListingRowKind.Label:
                    text.Append(FormatAddress(Address.GetValueOrDefault()))
                        .Append("".PadLeft(14))
                        .Append(Text);
                    return text.ToString();

                case ListingRowKind.Reserve:
                case ListingRowKind.Constant:
                    return string.Format("{0:X} =   ", Value.GetValueOrDefault()).PadLeft(18) + Text;

                case ListingRowKind.Instruction:
                    text.Append(FormatAddress(Address.GetValueOrDefault()));
                    text.Append(' ');

                    int written = 0;
                    for (int i = 0; i < Bytes.Count; i++)
                    {
                        text.Append(string.Format("{0:X2} ", Bytes[i]));
                        written++;
                    }

                    if (Text != null)
                    {
                        // The column the text starts in is fixed by construction:
                        // a full group of four bytes lands on it directly, a short
                        // one is padded to it. The two are a character apart in the
                        // original listing and that spacing is preserved here on
                        // purpose -- the file is the artefact users already read.
                        if (written < 4)
                        {
                            int left = 4 - written;
                            int leftSpace = (left - 1) > 0 ? left - 1 : 0;
                            leftSpace = leftSpace + (left * 2);
                            text.Append("".PadLeft(leftSpace));
                        }

                        text.Append(' ').Append(Text);
                    }

                    return text.ToString();

                default:
                    return "".PadLeft(18) + Text;
            }
        }

        private static string FormatAddress(int address)
        {
            return ((ushort)address).ToString("X4", CultureInfo.InvariantCulture);
        }
    }
}