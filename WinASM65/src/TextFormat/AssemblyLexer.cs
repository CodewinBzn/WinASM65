// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Highlighting lexer for assembly source

using System;
using System.Collections.Generic;
using WinASM65.Cpu;

namespace WinASM65.TextFormat
{
    /// <summary>
    /// What a source token is, for a user interface to paint. Deliberately
    /// coarse: an editor needs a colour, not a parse tree, and a lexer that
    /// tried to be a parser would have to agree with the assembler on every
    /// rule -- which is what the assembler's own dispatch already does.
    /// </summary>
    public enum SourceTokenKind
    {
        /// <summary>Anything not classified: an unknown directive, a symbol.</summary>
        Plain,
        /// <summary>A directive the assembler dispatches, or an expression keyword.</summary>
        Keyword,
        /// <summary>An instruction the CPU table knows.</summary>
        Mnemonic,
        /// <summary>
        /// A decimal, hexadecimal or binary literal. The sign is not part of it:
        /// the expression tokenizer reads <c>-1</c> as a minus and a number, and
        /// the lexer follows, so <c>#-$10</c> colours its sign as an operator.
        /// </summary>
        Number,
        /// <summary>A quoted string or character.</summary>
        String,
        /// <summary>A <c>;</c> comment, which runs to the end of the line.</summary>
        Comment,
        /// <summary>Punctuation and arithmetic.</summary>
        Operator,
        /// <summary>A label name written before a colon.</summary>
        Label
    }

    /// <summary>
    /// One token, located in the line it came from rather than carrying its own
    /// text twice: <see cref="Start"/> and <see cref="Length"/> index the string
    /// that was lexed, so a caller never has to slice and compare.
    /// </summary>
    public sealed class SourceToken
    {
        public int Start { get; private set; }
        public int Length { get; private set; }
        public SourceTokenKind Kind { get; private set; }

        /// <summary>The line this token was lexed from.</summary>
        public string Text { get; private set; }

        public SourceToken(int start, int length, SourceTokenKind kind, string text)
        {
            Start = start;
            Length = length;
            Kind = kind;
            Text = text ?? string.Empty;
        }

        public override string ToString()
        {
            return string.Format("{0} '{1}'", Kind, Text);
        }
    }

    /// <summary>Every token of one source line, in source order.</summary>
    public sealed class SourceTokenLine
    {
        /// <summary>Numbered from one.</summary>
        public int LineNumber { get; private set; }

        public IReadOnlyList<SourceToken> Tokens { get; private set; }

        public SourceTokenLine(int lineNumber, IReadOnlyList<SourceToken> tokens)
        {
            LineNumber = lineNumber;
            Tokens = tokens ?? new List<SourceToken>();
        }
    }

    /// <summary>
    /// Splits assembly source into tokens for syntax highlighting.
    /// <para>
    /// Pure and side-effect free: it takes a string, touches no file, and never
    /// throws. Source that does not assemble still lexes, because a line being
    /// typed is exactly the source most worth highlighting, and an editor that
    /// threw on a half-typed line would lose its colours at the moment they
    /// were needed.
    /// </para>
    /// <para>
    /// Mnemonics come from <see cref="InstructionDocs"/>, which reads the CPU
    /// opcode tables, so a mnemonic highlighted here is one the assembler will
    /// accept. Number and string shapes follow the expression tokenizer, for
    /// the same reason: a sign stays an operator next to its literal, because
    /// <c>Table+$10</c> is an addition and <c>Table-1</c> is a subtraction, and
    /// only the assembler knows which is which.
    /// </para>
    /// </summary>
    public sealed class AssemblyLexer
    {
        private static readonly HashSet<string> DirectiveLookup = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".org", ".memarea", ".incbin", ".include", ".byte", ".word", ".res",
            ".macro", ".endmacro", ".if", ".ifdef", ".ifndef", ".else", ".endif",
            ".rep", ".endrep", ".end", ".export", ".import"
        };

        /// <summary>
        /// The words the expression tokenizer reads as operators rather than
        /// identifiers. They are keywords for the same reason the arithmetic
        /// symbols are. <c>AND</c> is not among them: it is also a mnemonic, and
        /// a mnemonic wins, because <c>AND #$01</c> is an instruction and
        /// highlighting it as an operator would be a lie about the code.
        /// </summary>
        private static readonly HashSet<string> ExpressionKeywordLookup =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "OR", "TRUE", "FALSE" };

        private static readonly HashSet<string> DefaultMnemonicLookup =
            new HashSet<string>(InstructionDocs.Mnemonics, StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _mnemonics;
        private readonly HashSet<string> _directives;

        /// <summary>
        /// Lexes for the NMOS 6502 with every directive this assembler
        /// dispatches.
        /// </summary>
        public AssemblyLexer()
            : this(null, null)
        {
        }

        /// <summary>
        /// Lexes for <paramref name="cpu"/>, which decides what counts as a
        /// mnemonic -- a 65C02 source has mnemonics an NMOS one does not.
        /// </summary>
        public AssemblyLexer(ICpuInstructionSet cpu)
            : this(cpu, null)
        {
        }

        /// <summary>
        /// Lexes for <paramref name="cpu"/> with a caller-supplied directive
        /// set, for a target that adds directives of its own. Null for either
        /// argument keeps the default.
        /// </summary>
        public AssemblyLexer(ICpuInstructionSet cpu, ISet<string> directives)
        {
            _mnemonics = new HashSet<string>(cpu == null ? DefaultMnemonicLookup : InstructionDocs.MnemonicsFor(cpu),
                StringComparer.OrdinalIgnoreCase);

            _directives = directives == null
                ? new HashSet<string>(DirectiveLookup, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(directives, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Every directive the default lexer recognises as a keyword, dots
        /// included.
        /// </summary>
        public static IReadOnlyList<string> DefaultDirectives
        {
            get
            {
                List<string> names = new List<string>(DirectiveLookup);
                names.Sort(StringComparer.Ordinal);
                return names;
            }
        }

        /// <summary>Lexes one line, without its terminator.</summary>
        public IReadOnlyList<SourceToken> TokenizeLine(string line)
        {
            List<SourceToken> tokens = new List<SourceToken>();
            if (string.IsNullOrEmpty(line))
                return tokens;

            int index = 0;
            int length = line.Length;
            bool atLabelPosition = true;

            while (index < length)
            {
                char c = line[index];

                if (char.IsWhiteSpace(c))
                {
                    index++;
                    continue;
                }

                if (c == ';')
                {
                    // The assembler strips a comment the same way, and to the end
                    // of the line for the same reason: it has no block comment.
                    Add(tokens, index, length - index, SourceTokenKind.Comment, line);
                    break;
                }

                if (c == '"' || c == '\'')
                {
                    index = ReadString(tokens, line, index, c);
                    atLabelPosition = false;
                    continue;
                }

                if (IsDigit(c))
                {
                    index = ReadNumber(tokens, line, index);
                    atLabelPosition = false;
                    continue;
                }

                if (c == '$' || c == '%')
                {
                    if (c == '$' || HasRadixDigits(line, index))
                    {
                        index = ReadRadixNumber(tokens, line, index, c);
                        atLabelPosition = false;
                        continue;
                    }

                    Add(tokens, index, 1, SourceTokenKind.Operator, line);
                    index++;
                    atLabelPosition = false;
                    continue;
                }

                if (c == '.' && IsNameStart(Peek(line, index + 1)))
                {
                    int start = index;
                    index++;
                    while (index < length && IsNamePart(line[index]))
                        index++;

                    string word = line.Substring(start, index - start);
                    SourceTokenKind kind = _directives.Contains(word) ? SourceTokenKind.Keyword : SourceTokenKind.Plain;
                    tokens.Add(new SourceToken(start, index - start, kind, word));
                    atLabelPosition = false;
                    continue;
                }

                if (IsNameStart(c))
                {
                    int start = index;
                    while (index < length && IsNamePart(line[index]))
                        index++;

                    string word = line.Substring(start, index - start);

                    if (atLabelPosition && IsLabelColon(line, index))
                    {
                        Add(tokens, start, index - start, SourceTokenKind.Label, line);
                        continue;
                    }

                    Add(tokens, start, index - start, ClassifyWord(word), line);
                    atLabelPosition = false;
                    continue;
                }

                int operatorLength = OperatorLength(line, index);
                Add(tokens, index, operatorLength, SourceTokenKind.Operator, line);
                index += operatorLength;
                atLabelPosition = false;
            }

            return tokens;
        }

        /// <summary>
        /// Lexes a whole source, line by line. Accepts either line terminator and
        /// never produces a token that indexes outside its line.
        /// <para>
        /// A source ending with a newline has no last line, which is how the
        /// assembler counts it and how a listing numbers it; the lexer counts the
        /// same way so that a gutter and a listing agree.
        /// </para>
        /// </summary>
        public IReadOnlyList<SourceTokenLine> Tokenize(string text)
        {
            List<SourceTokenLine> lines = new List<SourceTokenLine>();
            if (text == null)
                return lines;

            string[] rawLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            int count = rawLines.Length;
            if (count > 1 && rawLines[count - 1].Length == 0)
                count--;

            for (int i = 0; i < count; i++)
                lines.Add(new SourceTokenLine(i + 1, TokenizeLine(rawLines[i])));

            return lines;
        }

        private SourceTokenKind ClassifyWord(string word)
        {
            if (_mnemonics.Contains(word))
                return SourceTokenKind.Mnemonic;
            if (ExpressionKeywordLookup.Contains(word))
                return SourceTokenKind.Keyword;
            return SourceTokenKind.Plain;
        }

        private int ReadString(List<SourceToken> tokens, string line, int start, char quote)
        {
            int length = line.Length;
            int index = start + 1;

            // No escape and no doubled quote: .byte strips the quotes and emits
            // what is between them, so "" is an empty string here too. An
            // unterminated string ends at the line rather than throwing, because
            // the line is simply still being typed.
            while (index < length && line[index] != quote)
                index++;

            if (index < length)
                index++;

            tokens.Add(new SourceToken(start, index - start, SourceTokenKind.String, line.Substring(start, index - start)));
            return index;
        }

        private static int ReadNumber(List<SourceToken> tokens, string line, int start)
        {
            int length = line.Length;
            int index = start;

            while (index < length && IsDigit(line[index]))
                index++;

            Add(tokens, start, index - start, SourceTokenKind.Number, line);
            return index;
        }

        private static int ReadRadixNumber(List<SourceToken> tokens, string line, int start, char radix)
        {
            int length = line.Length;
            int index = start + 1;

            while (index < length && IsRadixDigit(line[index], radix == '%' ? '1' : 'F'))
                index++;

            Add(tokens, start, index - start, SourceTokenKind.Number, line);
            return index;
        }

        private static bool HasRadixDigits(string line, int start)
        {
            return start + 1 < line.Length && (line[start + 1] == '0' || line[start + 1] == '1');
        }

        private static bool IsRadixDigit(char c, char highest)
        {
            if (c >= '0' && c <= '9')
                return c <= highest;
            if (highest == 'F')
                return (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            return false;
        }

        private static bool IsLabelColon(string line, int index)
        {
            int i = index;
            while (i < line.Length && char.IsWhiteSpace(line[i]))
                i++;

            return i < line.Length && line[i] == ':';
        }

        private static int OperatorLength(string line, int index)
        {
            string rest = index + 2 <= line.Length ? line.Substring(index, 2) : string.Empty;

            if (rest == "<<" || rest == ">>" || rest == "<=" || rest == ">=" || rest == "!=" ||
                rest == "<>" || rest == "==")
                return 2;

            return 1;
        }

        private static char Peek(string line, int index)
        {
            return index < line.Length ? line[index] : '\0';
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        private static bool IsNameStart(char c)
        {
            return c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        private static bool IsNamePart(char c)
        {
            return IsNameStart(c) || IsDigit(c);
        }

        private static void Add(List<SourceToken> tokens, int start, int length, SourceTokenKind kind, string line)
        {
            tokens.Add(new SourceToken(start, length, kind, line.Substring(start, length)));
        }
    }
}