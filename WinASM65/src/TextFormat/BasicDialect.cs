// WinASM65 - the language side of a tokenised BASIC
//
// These systems do not run 6502 code, they run an interpreter that reads a file
// of tokens. So a source line here is not a line of assembly: it is a line
// number followed by a stream in which every keyword has become one byte, every
// string has become a length and some bytes, and every number is still text.
//
// The stream is not the same as the source, and the difference is the whole
// difficulty. The keywords collide with ordinary words, so `ATN` is the arc
// tangent and `AT N` is the AT statement, and a parser that matches the first
// keyword it finds gets one of the two wrong. The operators collide with
// punctuation: `+` is a token, `:` is not. And `?` and `(N)` have no token of
// their own, they are shorthands for whole statements.
//
// The dialect carries all of that. A second BASIC is a second table, not a
// second parser.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using WinASM65.Core;

namespace WinASM65.TextFormat
{
    /// <summary>One line record of a tokenised program, as it sits in the file.</summary>
    public sealed class BasicRecord
    {
        public int Number { get; private set; }
        public int From { get; private set; }
        public int To { get; private set; }
        public int End { get; private set; }

        public BasicRecord(int number, int from, int to, int end)
        {
            Number = number;
            From = from;
            To = to;
            End = end;
        }
    }

    /// <summary>
    /// One BASIC: where its program starts, and which byte stands for which
    /// keyword. A second BASIC is a second table, not a second parser.
    /// </summary>
    public class BasicDialect
    {
        private readonly Dictionary<string, byte> _keywords = new Dictionary<string, byte>(StringComparer.Ordinal);
        private readonly Dictionary<string, byte> _escapes = new Dictionary<string, byte>(StringComparer.Ordinal);
        private readonly Dictionary<byte, Dictionary<byte, string>> _extended =
            new Dictionary<byte, Dictionary<byte, string>>();
        private readonly Dictionary<byte, string> _text = new Dictionary<byte, string>();
        private readonly List<string> _byLength = new List<string>();

        public string Name { get; private set; }

        /// <summary>
        /// The address the first line claims to be at. Applesoft puts programs
        /// at $0801 and writes that address into the file, so the interpreter's
        /// own notion of where the program is matches the file's. A dialect
        /// whose container says nothing about addresses leaves it at zero.
        /// </summary>
        public int ProgramBase { get; private set; }

        /// <summary>The highest line number the dialect accepts.</summary>
        public int MaxLineNumber { get; private set; }

        /// <summary>Characters that may appear in a variable name after the first letter.</summary>
        public string NameCharacters { get; private set; }

        /// <summary>
        /// Whether a string is stored as its length followed by its bytes.
        /// Applesoft reads the length first and so has to write it; BBC BASIC
        /// keeps the quotes and reads to the closing one.
        /// </summary>
        public bool StringsCarryLength { get; set; }

        public BasicDialect(string name, int programBase, int maxLineNumber, string nameCharacters)
        {
            Name = name;
            ProgramBase = programBase;
            MaxLineNumber = maxLineNumber;
            NameCharacters = nameCharacters ?? "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.$%";
            StringsCarryLength = true;
        }

        /// <summary>Defines one token. The first definition of a byte wins, so a clash is visible here.</summary>
        public void Define(byte token, string keyword)
        {
            if (!_text.ContainsKey(token))
                _text.Add(token, keyword);
            if (!_keywords.ContainsKey(keyword))
            {
                _keywords.Add(keyword, token);
                _byLength.Add(keyword);
            }
            // Longest first, so ONERR is tried before ON and the parser never
            // has to think about prefixes.
            _byLength.Sort(delegate (string a, string b) { return b.Length.CompareTo(a.Length); });
        }

        /// <summary>
        /// Defines a token that costs two bytes: an escape, then the token. BBC
        /// BASIC spends three of its 128 slots on escapes, so the keywords that
        /// follow them live above $8D and are named here.
        /// </summary>
        public void Define(byte token, string keyword, byte escape)
        {
            Define(token, keyword);
            if (!_escapes.ContainsKey(keyword))
                _escapes.Add(keyword, escape);

            Dictionary<byte, string> tokens;
            if (!_extended.TryGetValue(escape, out tokens))
            {
                tokens = new Dictionary<byte, string>();
                _extended.Add(escape, tokens);
            }
            if (!tokens.ContainsKey(token))
                tokens.Add(token, keyword);
        }

        /// <summary>The escape a two byte token has to be preceded by, if any.</summary>
        public bool TryEscape(string keyword, out byte escape)
        {
            return _escapes.TryGetValue(keyword, out escape);
        }

        /// <summary>
        /// Names an extended token, given the escape it follows. The same byte
        /// is a different keyword under a different escape — $99 is ATN on its
        /// own and RENUMBER after $C7 — so the escape is part of the name.
        /// </summary>
        public bool TryKeyword(byte escape, byte token, out string keyword)
        {
            keyword = null;
            Dictionary<byte, string> tokens;
            if (!_extended.TryGetValue(escape, out tokens))
                return false;
            return tokens.TryGetValue(token, out keyword);
        }

        public bool IsKeyword(string keyword)
        {
            return _keywords.ContainsKey(keyword);
        }

        public bool TryToken(string keyword, out byte token)
        {
            return _keywords.TryGetValue(keyword, out token);
        }

        public bool TryKeyword(byte token, out string keyword)
        {
            return _text.TryGetValue(token, out keyword);
        }

        /// <summary>The token of a single character, when the dialect spends a byte on it.</summary>
        public bool TryPunctuation(char c, out byte token)
        {
            return _keywords.TryGetValue(c.ToString(), out token);
        }

        /// <summary>Keywords ordered longest first, so a match is always the longest one.</summary>
        public IEnumerable<string> KeywordsByLength
        {
            get { return _byLength; }
        }

        /// <summary>
        /// How many bytes a token takes. Most take one; a BBC BASIC escape takes
        /// two, and its line reference takes four.
        /// </summary>
        public virtual int TokenLength(byte token)
        {
            return 1;
        }

        /// <summary>
        /// Whether a token has to be handed back as the bytes it is rather than
        /// as a keyword. A BBC BASIC line reference is three bytes of arithmetic
        /// on a number, and there is no keyword to call it.
        /// </summary>
        public virtual bool IsOpaqueToken(byte token)
        {
            return false;
        }

        /// <summary>
        /// Whether a number written just after this keyword is a reference to
        /// another line rather than a value. Applesoft says no: it stores every
        /// number as text. BBC BASIC says yes after GOTO and friends, because a
        /// reference there is stored in three bytes instead of digits.
        /// </summary>
        public virtual bool IsLineReferenceKeyword(string keyword)
        {
            return false;
        }

        /// <summary>
        /// Whether a line opens with the reference flag already set. A dialect
        /// that crunches its leading line number in reference form leaves the
        /// flag standing, so a number that opens the line is a reference too.
        /// </summary>
        public virtual bool ArmsReferenceAtLineStart
        {
            get { return false; }
        }

        /// <summary>
        /// Whether a keyword leaves the reference flag as it found it. A value
        /// keyword — a function, an operator, a pseudo-variable — is part of the
        /// expression that was already being parsed, so it does not decide
        /// whether the next number is a target; a statement keyword does.
        /// </summary>
        public virtual bool KeepsReferenceArmed(string keyword)
        {
            return false;
        }

        /// <summary>
        /// Stores a reference to another line. Dialects that write references as
        /// digits return false and the digits are written out as they are.
        /// </summary>
        public virtual bool TryEncodeReference(int number, List<byte> body)
        {
            return false;
        }

        /// <summary>Reads back a reference written by <see cref="TryEncodeReference"/>.</summary>
        public virtual bool TryDecodeReference(byte[] data, int at, out int number)
        {
            number = 0;
            return false;
        }

        /// <summary>
        /// Puts the line records together. What a container owes the interpreter
        /// is a different matter per family: Applesoft needs a chain of
        /// addresses, and BBC BASIC needs a length and a carriage return.
        /// </summary>
        public virtual byte[] Frame(IReadOnlyList<int> numbers, IReadOnlyList<byte[]> bodies)
        {
            // A record is two bytes of address, two of number, the body, and the $00
            // that ends it. The addresses go in afterwards, because a record's
            // length is only known once the ones before it have been measured.
            List<int> starts = new List<int>();
            int total = 0;
            for (int i = 0; i < bodies.Count; i++)
            {
                starts.Add(total);
                total += 5 + bodies[i].Length;
            }

            byte[] file = new byte[total + 2];
            for (int i = 0; i < bodies.Count; i++)
            {
                int at = starts[i];
                int next = i + 1 < bodies.Count
                    ? ProgramBase + starts[i + 1]
                    : ProgramBase + total;
                WriteWord(file, at, (ushort)next);
                WriteWord(file, at + 2, (ushort)numbers[i]);
                Array.Copy(bodies[i], 0, file, at + 4, bodies[i].Length);
                file[at + 4 + bodies[i].Length] = 0x00;
            }
            return file;
        }

        /// <summary>
        /// Reads the record that starts at an offset, which is what the reader
        /// needs to walk a container it did not write. A false return with no
        /// problem means the offset is where the container says the program
        /// ends; a false return with a problem means the file is wrong.
        /// </summary>
        public virtual bool TryReadRecord(byte[] data, int offset, out BasicRecord record, out string problem)
        {
            record = null;
            problem = null;

            if (offset + 1 < data.Length && data[offset] == 0x00 && data[offset + 1] == 0x00)
                return false;

            if (offset + 4 > data.Length)
            {
                problem = "A line header runs past the end of the file.";
                return false;
            }

            int number = data[offset + 2] | (data[offset + 3] << 8);
            int body = offset + 4;

            // The file says where each line ends: the next address in the header
            // is the start of the line that follows, so the $00 that ends this
            // one sits just before it. Looking for a $00 instead would cut a
            // line of data short, because a line of data is allowed to contain
            // one.
            int next = data[offset] | (data[offset + 1] << 8);
            int stop = next - ProgramBase - 1;
            if (next <= ProgramBase || stop < body || stop >= data.Length)
            {
                problem = "Line " + number + " points at $" + next.ToString("X4")
                    + ", which is not inside the file.";
                return false;
            }

            if (data[stop] != 0x00)
            {
                problem = "Line " + number + " is not terminated by a $00.";
                return false;
            }

            record = new BasicRecord(number, body, stop, stop + 1);
            return true;
        }

        /// <summary>
        /// The most bytes one record may take in this container, or zero when the
        /// container lets a line run as long as it needs.
        /// </summary>
        public virtual int MaxRecordLength
        {
            get { return 0; }
        }

        protected static void WriteWord(byte[] data, int at, ushort value)
        {
            data[at] = (byte)(value & 0xFF);
            data[at + 1] = (byte)((value >> 8) & 0xFF);
        }
    }

    /// <summary>The Applesoft dialect, as the ROM's table has it.</summary>
    public static class ApplesoftDialect
    {
        public static BasicDialect Create()
        {
            BasicDialect dialect = new BasicDialect("Applesoft", 0x0801, 63999, "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.");

            // $80 to $EA, in order, from the token table the ROM carries. $EB to
            // $FF are left undefined: the encoder refuses to produce them and the
            // decoder says so, rather than the two inventing names for bytes
            // nobody has given them.
            string[] names = new string[]
            {
                "END", "FOR", "NEXT", "DATA", "INPUT", "DEL", "DIM", "READ",
                "GR", "TEXT", "PR#", "IN#", "CALL", "PLOT", "HLIN", "VLIN",
                "HGR2", "HGR", "HCOLOR=", "HPLOT", "DRAW", "XDRAW", "HTAB", "HOME",
                "ROT=", "SCALE=", "SHLOAD", "TRACE", "NOTRACE", "NORMAL", "INVERSE", "FLASH",
                "COLOR=", "POP", "VTAB", "HIMEM:", "LOMEM:", "ONERR", "RESUME", "RECALL",
                "STORE", "SPEED=", "LET", "GOTO", "RUN", "IF", "RESTORE", "&",
                "GOSUB", "RETURN", "REM", "STOP", "ON", "WAIT", "LOAD", "SAVE",
                "DEF FN", "POKE", "PRINT", "CONT", "LIST", "CLEAR", "GET", "NEW",
                "TAB", "TO", "FN", "SPC(", "THEN", "AT", "NOT", "STEP",
                "+", "-", "*", "/", ";", "AND", "OR", ">", "=", "<",
                "SGN", "INT", "ABS", "USR", "FRE", "SCRN(", "PDL", "POS",
                "SQR", "RND", "LOG", "EXP", "COS", "SIN", "TAN", "ATN",
                "PEEK", "LEN", "STR$", "VAL", "ASC", "CHR$", "LEFT$", "RIGHT$",
                "MID$"
            };

            // "DEF FN" is one token, $B8, while "FN" on its own is $C2. Both are
            // in the list above; the second definition of $B8 would be ignored,
            // which is the point of letting the first one win.
            for (int i = 0; i < names.Length; i++)
                dialect.Define((byte)(0x80 + i), names[i]);

            return dialect;
        }

        /// <summary>The token that opens a line of raw bytes.</summary>
        public const byte BinaryLine = 0x96;
    }
}
