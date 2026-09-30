// WinASM65 - turning BASIC source into the tokenised form an interpreter reads
//
// The line layout, which every Microsoft derived BASIC on these machines shares,
// is fixed by the format rather than by the dialect:
//
//   +$00  2 bytes  the address of the next line, low byte first
//   +$02  2 bytes  the line number, low byte first
//   +$04  n bytes  tokens and literals, ending with $00
//
// and the file ends with a record that is only the two zero bytes of an address.
// That chain is the interpreter's index: it is how LIST walks the program and
// how a GOTO finds its line. A file whose chain is wrong loads and does nothing,
// and looks otherwise perfect.

using System;
using System.Collections.Generic;
using WinASM65.Core;

namespace WinASM65.TextFormat
{
    /// <summary>One line of a tokenised program.</summary>
    public sealed class BasicLine
    {
        public int Number { get; set; }
        public string Body { get; set; }

        /// <summary>
        /// A line of raw bytes rather than tokens, for the data a BASIC program
        /// has to carry. The $96 that opens it is written out in the source too,
        /// because Applesoft's HTAB wears the same value and only the position
        /// tells the two apart.
        /// </summary>
        public byte[] Raw { get; set; }

        public BasicLine()
        {
            Number = 0;
            Body = string.Empty;
        }

        public BasicLine(int number, string body)
        {
            Number = number;
            Body = body ?? string.Empty;
        }

        public static BasicLine Raw_(int number, byte[] data)
        {
            return new BasicLine { Number = number, Raw = data ?? new byte[0] };
        }
    }

    /// <summary>
    /// Encodes BASIC source into the tokenised stream.
    /// </summary>
    public sealed class TokenEncoder
    {
        private readonly BasicDialect _dialect;
        private readonly List<Diagnostic> _diagnostics;

        public TokenEncoder(BasicDialect dialect, List<Diagnostic> diagnostics)
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

        public byte[] Encode(IReadOnlyList<BasicLine> lines)
        {
            List<int> numbers = new List<int>();
            List<byte[]> bodies = new List<byte[]>();

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Number < 0 || lines[i].Number > _dialect.MaxLineNumber)
                {
                    _diagnostics.Add(new Diagnostic(new SourceLocation("line " + lines[i].Number, i + 1),
                        "Line number " + lines[i].Number + " is outside 0.."
                        + _dialect.MaxLineNumber + ", which is what " + _dialect.Name + " accepts."));
                    continue;
                }

                byte[] body = lines[i].Raw != null ? RawBody(lines[i], i) : TextBody(lines[i], i);
                int length = RecordLength(body);
                if (_dialect.MaxRecordLength > 0 && length > _dialect.MaxRecordLength)
                {
                    _diagnostics.Add(new Diagnostic(new SourceLocation("line " + lines[i].Number, i + 1),
                        "Line " + lines[i].Number + " takes " + length
                        + " bytes, over the " + _dialect.MaxRecordLength + " a line may take."));
                    continue;
                }

                numbers.Add(lines[i].Number);
                bodies.Add(body);
            }

            return _dialect.Frame(numbers, bodies);
        }

        /// <summary>
        /// What a body costs in the file. Every family pays four bytes of header
        /// before the body, so this is where a container that caps its line
        /// length is checked against a body the encoder has just built.
        /// </summary>
        private int RecordLength(byte[] body)
        {
            return 4 + body.Length;
        }

        private static byte[] RawBody(BasicLine line, int index)
        {
            List<byte> body = new List<byte>();
            body.Add(ApplesoftDialect.BinaryLine);
            foreach (byte b in line.Raw)
                body.Add(b);
            return body.ToArray();
        }

        private byte[] TextBody(BasicLine line, int index)
        {
            List<byte> body = new List<byte>();
            string text = line.Body ?? string.Empty;
            int at = 0;
            bool literal = false;
            bool statementStart = true;
            bool inGotoParen = false;

            // What the last keyword was, because a dialect may treat the number
            // after it as a reference to another line rather than as a value.
            string lastKeyword = null;

            while (at < text.Length)
            {
                char c = text[at];

                // Inside REM or DATA the line is the statement: no keyword, no
                // string, no operator, no space removed. Only the $00 that ends
                // the line is not text.
                if (literal)
                {
                    body.Add((byte)c);
                    at++;
                    continue;
                }

                // Spaces go everywhere except inside REM and DATA, where the text
                // of the line is the statement and its spacing is part of it.
                if (c == ' ' || c == '\t')
                {
                    at++;
                    continue;
                }

                // A backslash escape is how a reader hands back a byte it could not
                // name. It is not syntax any of these machines have, so it cannot
                // collide with a program, and it is what makes the round trip
                // through the reader exact instead of approximate.
                if (c == '\\')
                {
                    at = Escape(text, at, body, line, index);
                    statementStart = false;
                    continue;
                }

                // A string is opaque: everything up to the closing quote is a
                // byte, and a doubled quote is one quote.
                if (c == '"')
                {
                    at = EncodeString(text, at, body, line, index);
                    statementStart = false;
                    continue;
                }

                if (!literal && IsNameStart(c))                {
                    int matched;
                    byte token;
                    string keyword;
                    if (TryKeyword(text, at, out matched, out token, out keyword))
                    {
                        // A token that costs two bytes is written after the
                        // escape that says so. The keyword that was matched is
                        // the one that decides, not the one the byte is also
                        // known by: $99 is ATN on its own and RENUMBER after $C7.
                        byte escape;
                        if (_dialect.TryEscape(keyword, out escape))
                            body.Add(escape);

                        body.Add(token);
                        at += matched;
                        if (keyword == "REM" || keyword == "DATA")
                            literal = true;
                        lastKeyword = keyword;
                        statementStart = false;
                        continue;
                    }

                    int start = at;
                    while (at < text.Length && IsNamePart(text[at]))
                        at++;
                    Append(text, start, at, body);
                    lastKeyword = null;
                    statementStart = false;
                    continue;
                }

                if (char.IsDigit(c) || (c == '.' && at + 1 < text.Length && char.IsDigit(text[at + 1])))
                {
                    int end = Number(text, at);
                    if (_dialect.IsLineReferenceKeyword(lastKeyword))
                    {
                        int target;
                        if (TryValue(text, at, end, out target)
                            && _dialect.TryEncodeReference(target, body))
                        {
                            at = end;
                            lastKeyword = null;
                            statementStart = false;
                            continue;
                        }
                    }

                    Append(text, at, end, body);
                    at = end;
                    lastKeyword = null;
                    statementStart = false;
                    continue;
                }

                // The two shorthands, and only where a statement can start.
                // Applesoft spends no byte on either, so the parser is the only
                // place that can know they mean something.
                if (statementStart && c == '?')
                {
                    byte print;
                    if (_dialect.TryToken("PRINT", out print))
                        body.Add(print);
                    at++;
                    statementStart = false;
                    continue;
                }
                if (statementStart && c == '(')
                {
                    byte go;
                    if (_dialect.TryToken("GOTO", out go))
                    {
                        body.Add(go);
                        at++;
                        inGotoParen = true;
                        statementStart = false;
                        continue;
                    }
                }

                // The closing parenthesis belongs to the shorthand rather than to
                // the line. Leaving it in would hand the interpreter a bracket it
                // never expects after a GOTO.
                if (c == ')' && inGotoParen)
                {
                    inGotoParen = false;
                    at++;
                    continue;
                }

                byte punctuation;
                if (_dialect.TryPunctuation(c, out punctuation))
                    body.Add(punctuation);
                else
                    body.Add((byte)c);

                if (c == ':')
                    statementStart = true;
                else
                    statementStart = false;
                lastKeyword = null;
                at++;
            }

            return body.ToArray();
        }

        private int Escape(string text, int at, List<byte> body, BasicLine line, int index)
        {
            if (at + 3 < text.Length && (text[at + 1] == 'x' || text[at + 1] == 'X'))
            {
                int high = Hex(text[at + 2]);
                int low = Hex(text[at + 3]);
                if (high >= 0 && low >= 0)
                {
                    body.Add((byte)((high << 4) | low));
                    return at + 4;
                }
            }

            _diagnostics.Add(new Diagnostic(new SourceLocation("line " + line.Number, index + 1),
                "A backslash on line " + line.Number + " is followed by something"
                + " other than \\x and two hexadecimal digits."));
            return at + 1;
        }

        private static int Hex(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return -1;
        }

        private int EncodeString(string text, int at, List<byte> body, BasicLine line, int index)
        {
            int start = at + 1;
            int end = start;
            while (end < text.Length)
            {
                if (text[end] == '"')
                {
                    if (end + 1 < text.Length && text[end + 1] == '"')
                    {
                        end += 2;
                        continue;
                    }
                    break;
                }
                end++;
            }

            if (end >= text.Length)
            {
                _diagnostics.Add(new Diagnostic(new SourceLocation("line " + line.Number, index + 1),
                    "The string on line " + line.Number + " is never closed."));
                end = text.Length;
            }

            // The length is the number of bytes, and a doubled quote counts once
            // because it is stored once. A dialect that keeps the quotes and
            // reads to the closing one wants neither the length nor the doubling.
            if (!_dialect.StringsCarryLength)
            {
                body.Add((byte)'"');
                for (int i = start; i < end; i++)
                {
                    if (text[i] == '"' && i + 1 < end && text[i + 1] == '"')
                        continue;
                    body.Add((byte)text[i]);
                }
                body.Add((byte)'"');
                return end < text.Length ? end + 1 : text.Length;
            }

            List<byte> raw = new List<byte>();
            for (int i = start; i < end; i++)
            {
                if (text[i] == '"' && i + 1 < end && text[i + 1] == '"')
                    continue;
                raw.Add((byte)text[i]);
            }

            body.Add((byte)raw.Count);
            foreach (byte b in raw)
                body.Add(b);
            return end < text.Length ? end + 1 : text.Length;
        }

        private bool TryKeyword(string text, int at, out int matched, out byte token, out string keyword)
        {
            foreach (string candidate in _dialect.KeywordsByLength)
            {
                if (at + candidate.Length > text.Length)
                    continue;
                if (string.CompareOrdinal(text, at, candidate, 0, candidate.Length) != 0)
                    continue;

                // A keyword may not be the front of something longer. This is
                // what tells ATN from AT N and ONERR from ON, without the parser
                // knowing which pairs collide.
                int after = at + candidate.Length;
                if (after < text.Length && IsNamePart(text[after]))
                    continue;

                matched = candidate.Length;
                _dialect.TryToken(candidate, out token);
                keyword = candidate;
                return true;
            }

            matched = 0;
            token = 0;
            keyword = null;
            return false;
        }

        private bool IsNameStart(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }

        private bool IsNamePart(char c)
        {
            if (c >= 'A' && c <= 'Z') return true;
            if (c >= 'a' && c <= 'z') return true;
            if (c >= '0' && c <= '9') return true;
            return _dialect.NameCharacters.IndexOf(c) >= 0;
        }

        /// <summary>
        /// Where a number ends. Numbers stay text in the file, so all this has
        /// to do is find the end of the run; whether the interpreter accepts it
        /// is its business, and refusing here would refuse programs the machine
        /// runs.
        /// </summary>
        private static int Number(string text, int at)
        {
            int end = at;
            bool exponent = false;

            while (end < text.Length)
            {
                char c = text[end];
                if (char.IsDigit(c) || c == '.')
                {
                    end++;
                    continue;
                }
                if (!exponent && (c == 'E' || c == 'e')
                    && end + 1 < text.Length
                    && (char.IsDigit(text[end + 1]) || text[end + 1] == '-' || text[end + 1] == '+'))
                {
                    exponent = true;
                    end += 2;
                    continue;
                }
                break;
            }
            return end;
        }

        private static void Append(string text, int from, int to, List<byte> body)
        {
            for (int i = from; i < to; i++)
                body.Add((byte)text[i]);
        }

        /// <summary>
        /// The value of a number written out in the source. Numbers stay text in
        /// the file, so a dialect only asks for this where it needs the value,
        /// which is for a line reference.
        /// </summary>
        private static bool TryValue(string text, int from, int to, out int value)
        {
            value = 0;
            if (from >= to)
                return false;

            for (int i = from; i < to; i++)
            {
                if (!char.IsDigit(text[i]))
                    return false;
                value = (value * 10) + (text[i] - '0');
                if (value > 0xFFFF)
                    return false;
            }
            return true;
        }

        private static void WriteWord(byte[] data, int at, ushort value)
        {
            data[at] = (byte)(value & 0xFF);
            data[at + 1] = (byte)((value >> 8) & 0xFF);
        }
    }
}
