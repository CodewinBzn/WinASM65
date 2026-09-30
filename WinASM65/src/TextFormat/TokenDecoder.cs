// WinASM65 - reading a tokenised program back
//
// The round trip is the only honest check on an encoder, and it needs a reader
// that was written from the format rather than from the encoder. A reader that
// shares the encoder's idea of what a keyword is proves nothing; a reader that
// walks the bytes and names what it finds proves the file says what it claims.
//
// This reader is byte exact rather than pretty, and that is a decision about
// correctness rather than about taste. A string in the file is a length, then
// that many bytes, then a closing quote: there is no opening quote to find, so a
// reader cannot tell a string from a number followed by letters without
// understanding the grammar, and a grammar aware reader would be guessing on
// exactly the inputs where a guess is least welcome. So a byte that is not a
// token comes back as \xNN, which the encoder turns back into that byte.
//
// Everything that would be read back as syntax is escaped even when it is
// printable, for the same reason. Letters could be the front of a keyword, a
// quote would open a string, a bracket at the start of a statement would be the
// GOTO shorthand, and a question mark would be the PRINT shorthand. Digits,
// operators, colons and commas re-encode to themselves, so they pass through
// and the output still reads.

using System;
using System.Collections.Generic;
using System.Text;
using WinASM65.Core;

namespace WinASM65.TextFormat
{
    /// <summary>Turns a tokenised stream back into a form the encoder accepts.</summary>
    public sealed class TokenDecoder
    {
        private readonly BasicDialect _dialect;
        private readonly List<Diagnostic> _diagnostics;

        public TokenDecoder(BasicDialect dialect, List<Diagnostic> diagnostics)
        {
            if (dialect == null)
                throw new ArgumentNullException("dialect");
            _dialect = dialect;
            _diagnostics = diagnostics ?? new List<Diagnostic>();
        }

        public IReadOnlyList<Diagnostic> Diagnostics
        {
            get { return _diagnostics; }
        }

        public List<BasicLine> Decode(byte[] data)
        {
            List<BasicLine> lines = new List<BasicLine>();
            if (data == null)
                return lines;

            // Each dialect knows how its own container lays a line out, so the
            // walk below is the same for every one of them and the knowledge
            // stays in one place.
            int at = 0;
            while (at < data.Length)
            {
                BasicRecord record;
                string problem;
                if (!_dialect.TryReadRecord(data, at, out record, out problem))
                {
                    if (problem != null)
                        _diagnostics.Add(new Diagnostic(new SourceLocation("program", at), problem));
                    break;
                }

                lines.Add(Read(data, record));
                at = record.End;
            }

            return lines;
        }

        /// <summary>
        /// A line that opens with $96 is a line of data, and every byte up to the
        /// $00 that ends it belongs to that data. There is nothing to parse: the
        /// marker is what says where the bytes are. A text line that opened on
        /// $96 would be an HTAB at the very start of a line, which no statement
        /// is, so the reading here is the format's own.
        /// </summary>
        private BasicLine Read(byte[] data, BasicRecord record)
        {
            if (record.From < record.To && data[record.From] == ApplesoftDialect.BinaryLine)
            {
                List<byte> raw = new List<byte>();
                for (int at = record.From + 1; at < record.To; at++)
                    raw.Add(data[at]);
                return BasicLine.Raw_(record.Number, raw.ToArray());
            }

            return new BasicLine(record.Number, Text(data, record));
        }

        private string Text(byte[] data, BasicRecord record)
        {
            StringBuilder text = new StringBuilder();
            bool literal = false;
            int number = record.Number;

            for (int at = record.From; at < record.To; at++)
            {
                byte b = data[at];

                // Everything after REM or DATA is the statement, byte for byte,
                // escapes included. Reading it as anything else would be a lie
                // about what the line holds: the encoder copies that text out
                // unchanged and would copy the escapes out with it.
                if (literal)
                {
                    text.Append((char)b);
                    continue;
                }

                if (b >= 0x80)
                {
                    int length = _dialect.TokenLength(b);
                    if (length > 1 && at + length > record.To)
                    {
                        _diagnostics.Add(new Diagnostic(new SourceLocation("line " + number, 0),
                            "Token $" + b.ToString("X2") + " on line " + number
                            + " runs past the end of the line."));
                        break;
                    }

                    // A token that takes more than one byte is either an escape,
                    // which the byte after it names, or a number the reader has
                    // no keyword for and hands back as the bytes it is.
                    if (length == 2)
                    {
                        string extended;
                        if (_dialect.TryKeyword(b, data[at + 1], out extended))
                        {
                            Separator(text);
                            text.Append(extended);
                            at++;
                            continue;
                        }
                    }
                    else if (_dialect.IsOpaqueToken(b))
                    {
                        Separator(text);
                        for (int i = 0; i < length; i++)
                            text.Append("\\x").Append(data[at + i].ToString("X2"));
                        at += length - 1;
                        continue;
                    }

                    string keyword;
                    if (length == 1 && _dialect.TryKeyword(b, out keyword))
                    {
                        Separator(text);
                        text.Append(keyword);
                        if (keyword == "REM" || keyword == "DATA")
                            literal = true;
                        continue;
                    }

                    // Naming an unknown token would invent a keyword the dialect
                    // does not have. Hand the byte back instead.
                    _diagnostics.Add(new Diagnostic(new SourceLocation("line " + number, 0),
                        "Token $" + b.ToString("X2") + " on line " + number
                        + " is not one this dialect knows."));
                    Separator(text);
                    text.Append("\\x").Append(b.ToString("X2"));
                    continue;
                }

                Separator(text);
                text.Append(b >= 0x20 && b < 0x7F && IsSafe(b)
                    ? ((char)b).ToString()
                    : "\\x" + b.ToString("X2"));
            }

            return text.ToString();
        }

        private static void Separator(StringBuilder text)
        {
            if (text.Length > 0 && text[text.Length - 1] != ' ')
                text.Append(' ');
        }

        /// <summary>
        /// Whether a printable byte reads back as itself. Everything here would
        /// otherwise be taken for syntax rather than for the byte it is, and the
        /// space is here because the encoder drops it, which would quietly take
        /// a byte out of the middle of a string.
        /// </summary>
        private static bool IsSafe(byte b)
        {
            if (b >= (byte)'A' && b <= (byte)'Z') return false;
            if (b >= (byte)'a' && b <= (byte)'z') return false;
            return b != 0x20 && b != (byte)'"' && b != (byte)'\\' && b != (byte)'(' && b != (byte)'?';
        }

        private static int Hex(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return -1;
        }
    }
}