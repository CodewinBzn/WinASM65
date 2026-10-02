using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.TextFormat;

namespace WinASM65.Tests
{
    [TestClass]
    public class AssemblyLexerTests
    {
        private static SourceTokenKind KindOf(IReadOnlyList<SourceToken> tokens, string text)
        {
            foreach (SourceToken token in tokens)
            {
                if (token.Text == text)
                    return token.Kind;
            }

            Assert.Fail("no token '" + text + "' in " + Dump(tokens));
            return SourceTokenKind.Plain;
        }

        private static int Count(IReadOnlyList<SourceToken> tokens, SourceTokenKind kind)
        {
            int total = 0;
            foreach (SourceToken token in tokens)
            {
                if (token.Kind == kind)
                    total++;
            }

            return total;
        }

        private static string Dump(IReadOnlyList<SourceToken> tokens)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            foreach (SourceToken token in tokens)
                text.Append('[').Append(token.Kind).Append(':').Append(token.Text).Append(']');
            return text.ToString();
        }

        [TestMethod]
        public void AFullSourceLineIsClassifiedTokenByToken()
        {
            IReadOnlyList<SourceToken> tokens = new AssemblyLexer().TokenizeLine("  Start: lda Table+$10,x ; load it");

            Assert.AreEqual(SourceTokenKind.Label, KindOf(tokens, "Start"));
            Assert.AreEqual(SourceTokenKind.Mnemonic, KindOf(tokens, "lda"));
            Assert.AreEqual(SourceTokenKind.Plain, KindOf(tokens, "Table"));
            Assert.AreEqual(SourceTokenKind.Operator, KindOf(tokens, "+"));
            Assert.AreEqual(SourceTokenKind.Number, KindOf(tokens, "$10"));
            Assert.AreEqual(SourceTokenKind.Operator, KindOf(tokens, ","));
            Assert.AreEqual(SourceTokenKind.Comment, KindOf(tokens, "; load it"));

            // Whitespace is not a token: an editor paints cells, not gaps.
            foreach (SourceToken token in tokens)
                Assert.AreEqual(token.Text, token.Text.Trim(), "a token never carries surrounding blanks");
        }

        [TestMethod]
        public void EveryDirectiveTheAssemblerDispatchesIsAKeyword()
        {
            // The lexer's keyword list is a copy, so it is checked against the
            // engine rather than trusted. .res is the one name the engine does
            // not dispatch: it is a pattern the line dispatch matches before any
            // handler is consulted, which is why the engine's own list omits it.
            List<string> engineNames = new List<string>(AssemblerEngine.DefaultDirectiveNames);
            List<string> lexerNames = new List<string>(AssemblyLexer.DefaultDirectives);

            foreach (string name in engineNames)
                Assert.IsTrue(lexerNames.Contains(name), name + " is dispatched but not highlighted");

            Assert.IsTrue(lexerNames.Contains(".res"), ".res is dispatched by pattern, not by a handler");

            foreach (string name in lexerNames)
            {
                if (name == ".res")
                    continue;

                Assert.IsTrue(engineNames.Contains(name), name + " is highlighted but never dispatched");
            }
        }

        [TestMethod]
        public void ADirectiveIsAKeywordOnlyWhenTheAssemblerKnowsIt()
        {
            AssemblyLexer lexer = new AssemblyLexer();

            Assert.AreEqual(SourceTokenKind.Keyword, KindOf(lexer.TokenizeLine(".byte $01"), ".byte"));
            Assert.AreEqual(SourceTokenKind.Plain, KindOf(lexer.TokenizeLine(".nope $01"), ".nope"));
            Assert.AreEqual(SourceTokenKind.Operator, KindOf(lexer.TokenizeLine("5."), "."),
                "a dot that starts nothing is punctuation, not a directive");
        }

        [TestMethod]
        public void MnemonicsFollowTheCpuTables()
        {
            AssemblyLexer nmos = new AssemblyLexer(new Cpu6502());

            Assert.AreEqual(SourceTokenKind.Mnemonic, KindOf(nmos.TokenizeLine("LDA"), "LDA"));
            Assert.AreEqual(SourceTokenKind.Mnemonic, KindOf(nmos.TokenizeLine("asl"), "asl"),
                "the assembler is case-insensitive and so is the lexer");
            Assert.AreEqual(SourceTokenKind.Plain, KindOf(nmos.TokenizeLine("LDQ"), "LDQ"),
                "not a mnemonic of any CPU in this project");
            Assert.AreEqual(SourceTokenKind.Plain, KindOf(nmos.TokenizeLine("STZ"), "STZ"),
                "STZ is a 65C02 addition, so an NMOS source does not call it one");

            AssemblyLexer cmos = new AssemblyLexer(new Cpu65C02());
            Assert.AreEqual(SourceTokenKind.Mnemonic, KindOf(cmos.TokenizeLine("STZ"), "STZ"));
            Assert.AreEqual(SourceTokenKind.Mnemonic, KindOf(cmos.TokenizeLine("BRA"), "BRA"));

            // The default lexer knows both CPUs, because an editor opening a
            // file has not been told which target it is for yet, and calling
            // STZ a mnemonic it may well be is the cheaper mistake to make.
            AssemblyLexer any = new AssemblyLexer();
            Assert.AreEqual(SourceTokenKind.Mnemonic, KindOf(any.TokenizeLine("STZ"), "STZ"));
            Assert.AreEqual(SourceTokenKind.Mnemonic, KindOf(any.TokenizeLine("LDA"), "LDA"));
            Assert.AreEqual(SourceTokenKind.Plain, KindOf(any.TokenizeLine("LDQ"), "LDQ"));
        }

        [TestMethod]
        public void ExpressionKeywordsAreKeywordsNotMnemonics()
        {
            AssemblyLexer lexer = new AssemblyLexer();
            IReadOnlyList<SourceToken> tokens = lexer.TokenizeLine(".if VALUE = TRUE OR OTHER = FALSE");

            Assert.AreEqual(SourceTokenKind.Keyword, KindOf(tokens, "TRUE"));
            Assert.AreEqual(SourceTokenKind.Keyword, KindOf(tokens, "OR"));
            Assert.AreEqual(SourceTokenKind.Keyword, KindOf(tokens, "FALSE"));

            // AND is the one word that is both: $29 really is the AND
            // instruction, and the expression tokenizer reads AND as the logical
            // operator. The mnemonic wins, because that is the instruction a line
            // beginning with it assembles to.
            Assert.AreEqual(SourceTokenKind.Mnemonic, KindOf(lexer.TokenizeLine("AND #$01"), "AND"));
        }

        [TestMethod]
        public void NumbersFollowTheExpressionTokenizerShapes()
        {
            AssemblyLexer lexer = new AssemblyLexer();

            Assert.AreEqual(SourceTokenKind.Number, KindOf(lexer.TokenizeLine("lda #$2A"), "$2A"));
            Assert.AreEqual(SourceTokenKind.Number, KindOf(lexer.TokenizeLine("lda $1234"), "$1234"));
            Assert.AreEqual(SourceTokenKind.Number, KindOf(lexer.TokenizeLine(".byte %1010"), "%1010"));
            Assert.AreEqual(SourceTokenKind.Number, KindOf(lexer.TokenizeLine(".byte 255"), "255"));
            Assert.AreEqual(SourceTokenKind.Number, KindOf(lexer.TokenizeLine(".byte 1"), "1"));
            Assert.AreEqual(SourceTokenKind.Number, KindOf(lexer.TokenizeLine(".byte $2A"), "$2A"));

            // A sign stays an operator next to its literal: the expression
            // tokenizer reads the two apart, and in "Table+$10" the sign is an
            // addition, not a negative.
            Assert.AreEqual(SourceTokenKind.Operator, KindOf(lexer.TokenizeLine(".byte -1"), "-"));
            Assert.AreEqual(SourceTokenKind.Operator, KindOf(lexer.TokenizeLine("lda Table+$10"), "+"));

            // A percent sign with no binary digits is the modulo operator, not a
            // malformed number, exactly as the expression tokenizer reads it.
            Assert.AreEqual(SourceTokenKind.Operator, KindOf(lexer.TokenizeLine("lda #Counter % 2"), "%"));
        }

        [TestMethod]
        public void OperatorsCoverWhatTheExpressionTokenizerReads()
        {
            IReadOnlyList<SourceToken> tokens = new AssemblyLexer().TokenizeLine("lda #$01");

            Assert.AreEqual(SourceTokenKind.Operator, KindOf(tokens, "#"));
            Assert.AreEqual(SourceTokenKind.Number, KindOf(tokens, "$01"));

            IReadOnlyList<SourceToken> bracketed = new AssemblyLexer().TokenizeLine("lda (Table+x)*2");
            Assert.AreEqual(SourceTokenKind.Operator, KindOf(bracketed, "("));
            Assert.AreEqual(SourceTokenKind.Operator, KindOf(bracketed, ")"));
            Assert.AreEqual(SourceTokenKind.Operator, KindOf(bracketed, "+"));
            Assert.AreEqual(SourceTokenKind.Operator, KindOf(bracketed, "*"));
        }

        [TestMethod]
        public void TwoCharacterOperatorsStayWhole()
        {
            IReadOnlyList<SourceToken> tokens = new AssemblyLexer().TokenizeLine(
                ".if (A << 2) >= (B >> 1) or C != D or E <> F or G == H or I <= J");

            foreach (string op in new[] { "<<", ">>", ">=", "!=", "<>", "==", "<=" })
                Assert.AreEqual(SourceTokenKind.Operator, KindOf(tokens, op), op + " is one token");
        }

        [TestMethod]
        public void ACommentRunsToTheEndOfTheLine()
        {
            IReadOnlyList<SourceToken> tokens = new AssemblyLexer().TokenizeLine("  lda #$01  ; this ; is all comment");

            Assert.AreEqual(1, Count(tokens, SourceTokenKind.Comment), "one comment token, however many semicolons");
            Assert.AreEqual("; this ; is all comment", tokens[tokens.Count - 1].Text);
        }

        [TestMethod]
        public void AStringIsOneTokenEvenWhenItHoldsPunctuation()
        {
            IReadOnlyList<SourceToken> tokens = new AssemblyLexer().TokenizeLine(".byte \"Hello, $42 ; world\"");

            Assert.AreEqual(1, Count(tokens, SourceTokenKind.String));
            Assert.AreEqual("\"Hello, $42 ; world\"", tokens[tokens.Count - 1].Text,
                "a semicolon inside a string is not a comment");
        }

        [TestMethod]
        public void AnUnterminatedStringStopsAtTheEndOfTheLine()
        {
            // A half-typed line is the normal state of a buffer being edited. The
            // lexer classifies it rather than refusing it.
            IReadOnlyList<SourceToken> tokens = new AssemblyLexer().TokenizeLine(".byte \"unfinished");

            Assert.AreEqual(1, Count(tokens, SourceTokenKind.String));
            Assert.AreEqual("\"unfinished", tokens[tokens.Count - 1].Text);
        }

        [TestMethod]
        public void AnEmptyStringIsItsOwnToken()
        {
            // The assembler strips the quotes of .byte "" and emits nothing, so
            // "" is a string here too rather than two empty strings.
            IReadOnlyList<SourceToken> tokens = new AssemblyLexer().TokenizeLine(".byte \"\"");

            Assert.AreEqual(1, Count(tokens, SourceTokenKind.String));
            Assert.AreEqual("\"\"", tokens[tokens.Count - 1].Text);
        }

        [TestMethod]
        public void LocalScopeBracesAreOperators()
        {
            AssemblyLexer lexer = new AssemblyLexer();

            Assert.AreEqual(SourceTokenKind.Operator, KindOf(lexer.TokenizeLine("{"), "{"));
            Assert.AreEqual(SourceTokenKind.Operator, KindOf(lexer.TokenizeLine("  }"), "}"));
        }

        [TestMethod]
        public void AMacroNameIsNotAMnemonic()
        {
            // A macro is a plain word: only the mnemonic tables may call a word
            // an instruction, or any name the assembler happens not to know would
            // silently claim to be one.
            Assert.AreEqual(SourceTokenKind.Plain, KindOf(new AssemblyLexer().TokenizeLine("  Print $42"), "Print"));
        }

        [TestMethod]
        public void MalformedSourceLexesWithoutThrowing()
        {
            AssemblyLexer lexer = new AssemblyLexer();
            string[] broken =
            {
                string.Empty,
                "   ",
                "\"",
                "\"\"\"\"",
                ".",
                "$",
                "%",
                "%2",
                "$$",
                "(",
                "()",
                "lda (",
                "lda #$",
                "lda #",
                ":",
                ":::",
                ";",
                "\t\t",
                "lda #$01, ,",
                "\u0000\u0007",
                new string('a', 4096)
            };

            foreach (string line in broken)
            {
                IReadOnlyList<SourceToken> tokens = lexer.TokenizeLine(line);
                foreach (SourceToken token in tokens)
                {
                    Assert.IsTrue(token.Start >= 0, "a token starts inside the line");
                    Assert.IsTrue(token.Start + token.Length <= line.Length,
                        "'" + token.Text + "' is inside '" + line + "'");
                    Assert.AreEqual(line.Substring(token.Start, token.Length), token.Text,
                        "a token's text is the line at its position");
                }
            }
        }

        [TestMethod]
        public void AWholeSourceIsLexedLineByLine()
        {
            IReadOnlyList<SourceTokenLine> lines = new AssemblyLexer().Tokenize("  lda #$01\nStart:\n  rts\n");

            Assert.AreEqual(3, lines.Count);
            Assert.AreEqual(1, lines[0].LineNumber);
            Assert.AreEqual(3, lines[2].LineNumber);
            Assert.AreEqual(SourceTokenKind.Mnemonic, KindOf(lines[0].Tokens, "lda"));
            Assert.AreEqual(SourceTokenKind.Label, KindOf(lines[1].Tokens, "Start"));
            Assert.AreEqual(0, new AssemblyLexer().Tokenize(null).Count, "no source, no lines");
        }

        [TestMethod]
        public void EveryLineTerminatorIsAccepted()
        {
            AssemblyLexer lexer = new AssemblyLexer();

            Assert.AreEqual(3, lexer.Tokenize("a\nb\nc").Count);
            Assert.AreEqual(3, lexer.Tokenize("a\r\nb\r\nc").Count);
            Assert.AreEqual(3, lexer.Tokenize("a\rb\rc").Count);
        }

        [TestMethod]
        public void TheLexerIsPure()
        {
            // Same input, same output, and no state carried between calls: the
            // unit a user interface redraws on every keystroke.
            AssemblyLexer lexer = new AssemblyLexer();

            Assert.AreEqual(Dump(lexer.TokenizeLine("  lda #$01 ; x")), Dump(lexer.TokenizeLine("  lda #$01 ; x")));
            Assert.AreEqual(0, lexer.TokenizeLine(string.Empty).Count);
        }
    }
}