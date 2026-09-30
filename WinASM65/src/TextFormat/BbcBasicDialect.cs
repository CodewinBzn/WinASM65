// WinASM65 - BBC BASIC V
//
// BBC BASIC is the same idea as Applesoft and a different container. The
// interpreter reads a list of lines, each one a carriage return, a line number, a
// length, and then tokens and literals. There is no chain of addresses and no $00
// to close a line, because the length says where the next one starts.
//
// Three things here have no Applesoft equivalent:
//
//   Strings keep their quotes. Applesoft writes a length and the bytes; BBC
//   BASIC writes the text and reads to the closing quote, so a quote inside a
//   string is written twice and means one.
//
//   A line reference is three bytes, not digits. GOTO and its friends store the
//   target spread over three bytes that all stay inside ASCII, because the
//   interpreter scans forward for the ELSE token and would trip over a line
//   number whose high byte happened to be $8B. The encoding splits the top two
//   bits off each byte of the number, combines them, exclusive-ORs that with
//   $54, and sets bit 6 of each of the two remaining bytes.
//
//   Keywords run from $7F, and three of them are escapes: $C6, $C7 and $C8 are
//   each followed by a second byte that names the real token, so CASE and
//   RENUMBER can be tokens without giving up 128 slots.
//
// The tables come from RISC OS Open, via the work of Matt Godbolt on the
// RISC OS sources; the tables in the ROM interpreter itself were not available
// to check against, so the two extended tables are held to what that
// derivation gives.

using System;
using System.Collections.Generic;
using System.Text;

namespace WinASM65.TextFormat
{
    /// <summary>
    /// The same program as plain text. A BBC BASIC program does not have to
    /// be tokenised on disk: the machine tokenises a text file when it loads
    /// it, and the text is the form anybody reads. It is also the only form
    /// in which a comment and a keyword are told apart by the person rather
    /// than by the interpreter.
    /// </summary>
    public static class BbcBasicText
    {
        public static string Render(IReadOnlyList<BasicLine> lines)
        {
            return BasicSource.Render(lines);
        }
    }

    public static class BbcBasicDialect
    {
        /// <summary>The byte that ends a program, after the carriage return.</summary>
        public const byte EndOfProgram = 0xFF;

        /// <summary>The token that introduces a line reference.</summary>
        public const byte LineReference = 0x8D;

        public const byte EscapeFunction = 0xC6;
        public const byte EscapeCommand = 0xC7;
        public const byte EscapeStatement = 0xC8;

        /// <summary>The first byte of an extended token, whichever escape it follows.</summary>
        public const byte ExtendedFirst = 0x8E;

        /// <summary>The longest a line may be, header included.</summary>
        public const int MaxLineLength = 251;

        /// <summary>The highest line number, because $FF in the high byte ends the program.</summary>
        public const int MaxLineNumber = 0xFEFF;

        /// <summary>The keywords after which a number is a reference to another line.</summary>
        private static readonly string[] References = { "GOTO", "GOSUB", "THEN", "ELSE", "RESTORE", "ON" };

        /// <summary>
        /// The keywords that are part of an expression rather than a statement.
        /// They do not decide whether the next number is a target, so they leave
        /// the flag as they found it: that is what makes the flag the ROM leaves
        /// set at the start of a line still set after one of these.
        /// </summary>
        private static readonly string[] Values =
        {
            "AND", "DIV", "EOR", "MOD", "OR", "NOT", "FALSE", "TRUE", "PI",
            "ABS", "ACS", "ADVAL", "ASN", "ATN", "COS", "DEG", "EVAL", "EXP",
            "INT", "LN", "LOG", "RAD", "RND", "SGN", "SIN", "SQR", "TAN",
            "ASC", "CHR$", "GET$", "INKEY", "INKEY$", "INSTR(", "LEFT$(",
            "LEN", "MID$(", "RIGHT$(", "STR$", "STRING$(", "VAL",
            "BGET", "POINT(", "POS", "EOF", "EXT", "USR", "FN", "VPOS",
            "PTR", "PAGE", "TIME", "LOMEM", "HIMEM", "ERROR", "LINE", "OFF",
            "STEP", "TO", "SPC", "TAB("
        };

        public static BasicDialect Create()
        {
            Dialect dialect = new Dialect();

            // The main table. It starts at $7F rather than $80, and the three
            // escapes take the bytes that would have held an ordinary keyword.
            string[] names = new string[]
            {
                "OTHERWISE",                                  // $7F
                "AND", "DIV", "EOR", "MOD", "OR", "ERROR", "LINE", "OFF",
                "STEP", "SPC", "TAB(", "ELSE", "THEN", null,   // $8D is the line reference
                "OPENIN", "PTR",

                "PAGE", "TIME", "LOMEM", "HIMEM", "ABS", "ACS", "ADVAL", "ASC",
                "ASN", "ATN", "BGET", "COS", "COUNT", "DEG", "ERL", "ERR",

                "EVAL", "EXP", "EXT", "FALSE", "FN", "GET", "INKEY", "INSTR(",
                "INT", "LEN", "LN", "LOG", "NOT", "OPENUP", "OPENOUT", "PI",

                "POINT(", "POS", "RAD", "RND", "SGN", "SIN", "SQR", "TAN",
                "TO", "TRUE", "USR", "VAL", "VPOS", "CHR$", "GET$", "INKEY$",

                "LEFT$(", "MID$(", "RIGHT$(", "STR$", "STRING$(", "EOF",
                null, null, null,                              // $C6, $C7, $C8 are escapes
                "WHEN", "OF", "ENDCASE", "ELSE2", "ENDIF", "ENDWHILE", "PTR",

                "PAGE", "TIME", "LOMEM", "HIMEM", "SOUND", "BPUT", "CALL", "CHAIN",
                "CLEAR", "CLOSE", "CLG", "CLS", "DATA", "DEF", "DIM", "DRAW",

                "END", "ENDPROC", "ENVELOPE", "FOR", "GOSUB", "GOTO", "GCOL", "IF",
                "INPUT", "LET", "LOCAL", "MODE", "MOVE", "NEXT", "ON", "VDU",

                "PLOT", "PRINT", "PROC", "READ", "REM", "REPEAT", "REPORT", "RESTORE",
                "RETURN", "RUN", "STOP", "COLOUR", "TRACE", "UNTIL", "WIDTH", "OSCLI"
            };

            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] != null)
                    dialect.Define((byte)(0x7F + i), names[i]);
            }

            // Two names land on the same word: ELSE is both the $8B of a plain
            // else and the $CC the interpreter looks for while skipping a line.
            // The first definition wins, so the keyword table keeps $8B.
            string[] functions = new string[] { "SUM", "BEAT" };
            string[] commands = new string[]
            {
                "APPEND", "AUTO", "CRUNCH", "DELET", "EDIT", "HELP", "LIST", "LOAD",
                "LVAR", "NEW", "OLD", "RENUMBER", "SAVE", "TEXTLOAD", "TEXTSAVE",
                "TWINTWIN", "TWINO", "INSTALL"
            };
            string[] statements = new string[]
            {
                "CASE", "CIRCLE", "FILL", "ORIGIN", "PSET", "RECT", "SWAP", "WHILE",
                "WAIT", "MOUSE", "QUIT", "SYS", "INSTALL", "LIBRARY", "TINT", "ELLIPSE",
                "BEATS", "TEMPO", "VOICES", "VOICE", "STEREO", "OVERLAY"
            };

Define(dialect, EscapeFunction, functions);
            Define(dialect, EscapeCommand, commands);
            Define(dialect, EscapeStatement, statements);

            dialect.StringsCarryLength = false;
            return dialect;
        }

        /// <summary>The dialect itself, with everything that is not the token table.</summary>
        private sealed class Dialect : BasicDialect
        {
            public Dialect()
                : base("BBC BASIC V", 0, BbcBasicDialect.MaxLineNumber, "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._%")
            {
                StringsCarryLength = false;
            }

            /// <summary>
            /// The ROM crunches the leading line number with the reference flag
            /// already set, and writing a number never clears it, so the arm
            /// carries over into the line. That is why a line that opens with a
            /// value keyword stores its number as a reference.
            /// </summary>
            public override bool ArmsReferenceAtLineStart
            {
                get { return true; }
            }

            public override int MaxRecordLength
            {
                get { return MaxLineLength; }
            }

            public override int TokenLength(byte token)
            {
                if (token == LineReference)
                    return 4;
                if (token == EscapeFunction || token == EscapeCommand || token == EscapeStatement)
                    return 2;
                return 1;
            }

            /// <summary>An escape is named by the byte that follows it.</summary>
            public bool IsEscape(byte token)
            {
                return token == EscapeFunction || token == EscapeCommand || token == EscapeStatement;
            }

            public override bool IsOpaqueToken(byte token)
            {
                return token == LineReference;
            }

            public override bool IsLineReferenceKeyword(string keyword)
            {
                return Named(References, keyword);
            }

            public override bool KeepsReferenceArmed(string keyword)
            {
                return Named(Values, keyword);
            }

            private static bool Named(string[] names, string keyword)
            {
                if (keyword == null)
                    return false;
                foreach (string candidate in names)
                {
                    if (string.Equals(candidate, keyword, StringComparison.Ordinal))
                        return true;
                }
                return false;
            }

            public override bool TryEncodeReference(int number, List<byte> body)
            {
                if (number < 0 || number > BbcBasicDialect.MaxLineNumber)
                    return false;

                EncodeReference(number, body);
                return true;
            }

            public override byte[] Frame(IReadOnlyList<int> numbers, IReadOnlyList<byte[]> bodies)
            {
                return BbcBasicDialect.Frame(numbers, bodies);
            }

            public override bool TryReadRecord(byte[] data, int offset, out BasicRecord record, out string problem)
            {
                record = null;
                problem = null;

                if (offset + 2 <= data.Length && data[offset] == 0x0D
                    && data[offset + 1] == EndOfProgram)
                    return false;

                if (offset + 4 > data.Length || data[offset] != 0x0D)
                {
                    problem = "A line does not start with a carriage return.";
                    return false;
                }

                int number = (data[offset + 1] << 8) | data[offset + 2];
                int length = data[offset + 3];
                if (length < 4 || offset + length > data.Length)
                {
                    problem = "Line " + number + " declares a length that runs past"
                        + " the end of the file.";
                    return false;
                }

                record = new BasicRecord(number, offset + 4, offset + length, offset + length);
                return true;
            }
        }

        private static void Define(BasicDialect dialect, byte escape, string[] names)
        {
            for (int i = 0; i < names.Length; i++)
                dialect.Define((byte)(ExtendedFirst + i), names[i], escape);
        }

        /// <summary>
        /// Three bytes that all stay inside ASCII. The top two bits of each byte
        /// of the number are taken off and packed into the first byte, and each
        /// of the two bytes that remain keeps bit 6 set, so no byte of a line
        /// reference can be mistaken for a token.
        /// </summary>
        public static void EncodeReference(int number, List<byte> body)
        {
            int hi = (number >> 8) & 0xFF;
            int lo = number & 0xFF;

            int packed = ((lo >> 6) << 4) | ((hi >> 6) << 2);
            body.Add(LineReference);
            body.Add((byte)(packed ^ 0x54));
            body.Add((byte)((lo & 0x3F) | 0x40));
            body.Add((byte)((hi & 0x3F) | 0x40));
        }

        /// <summary>
        /// The number a reference stands for. The reader does not use this: it
        /// hands the four bytes back as they are, because a reference may hold
        /// more than the encoding of one number and the round trip has to hold
        /// for every file rather than for the ones a machine wrote. This is here
        /// for a caller that wants the number anyway — a listing, a trace.
        /// </summary>
        public static int DecodeReference(byte b0, byte b1, byte b2)
        {
            int packed = b0 ^ 0x54;
            int lo = ((b1 & 0x3F) | (((packed >> 4) & 0x03) << 6));
            int hi = ((b2 & 0x3F) | (((packed >> 2) & 0x03) << 6));
            return (hi << 8) | lo;
        }

        /// <summary>
        /// Each line is a carriage return, the number, the length of the whole
        /// record, and then the tokens. The program ends with a carriage return
        /// and $FF, which is why $FF cannot be the high byte of a line number.
        /// </summary>
        private static byte[] Frame(IReadOnlyList<int> numbers, IReadOnlyList<byte[]> bodies)
        {
            List<byte> file = new List<byte>();
            for (int i = 0; i < numbers.Count; i++)
            {
                byte[] body = bodies[i];
                file.Add(0x0D);
                file.Add((byte)((numbers[i] >> 8) & 0xFF));
                file.Add((byte)(numbers[i] & 0xFF));
                file.Add((byte)(4 + body.Length));
                file.AddRange(body);
            }

            file.Add(0x0D);
            file.Add(EndOfProgram);
            return file.ToArray();
        }
    }
}