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
        private static IReadOnlyList<SourceToken> Lex(string line)
        {
            return new AssemblyLexer().TokenizeLine(line);
        }

        private static SourceToken Single(string line)
        {
            IReadOnlyList<SourceToken> tokens = Lex(line);
            Assert.AreEqual(1, tokens.Count, "one token in '" + line + "', got " + Describe(tokens));
            return tokens[0];
        }

        private static string Describe(IReadOnlyList<SourceToken> tokens)
        {
            string text = string.Empty;
            foreach (SourceToken token in tokens)
                text += "[" + token.Kind + " '" + token.Text + "'] ";
            return text;
        }

        private static void AssertKind(string line, SourceTokenKind kind, string text)
        {
            SourceToken token = Single(line);
            Assert.AreEqual(kind, token.Kind, "'" + line + "'");
            Assert.AreEqual(text, token.Text, "'" + line + "'");
        }

        [TestMethod]
        public void AnInstructionLineIsAMnemonicAnOperatorAndANumber()
        {
            IReadOnlyList<SourceToken> tokens = Lex("  lda #$2A");

            Assert.AreEqual(3, tokens.Count, Describe(tokens));
            Assert.AreEqual(SourceTokenKind.Mnemonic, tokens[0].Kind);
            Assert.AreEqual("lda", tokens[0].Text);
            Assert.AreEqual(SourceTokenKind.Operator, tokens[1].Kind);
            Assert.AreEqual("#", tokens[1].Text);
            Assert.AreEqual(SourceTokenKind.Number, tokens[2].Kind);
            Assert.AreEqual("$2A", tokens[2].Text);
        }

        [TestMethod]
        public void MnemonicsAreMatchedWhateverTheirCase()
        {
            AssertKind("LDA", SourceTokenKind.Mnemonic, "LDA");
            AssertKind("lda", SourceTokenKind.Mnemonic, "lda");
            AssertKind("ClC", SourceTokenKind.Mnemonic, "ClC");
            AssertKind("zzz", SourceTokenKind.Plain, "zzz");
        }

        [TestMethod]
        public void ALabelIsTheNameBeforeTheColon()
        {
            IReadOnlyList<SourceToken> tokens = Lex("Start:");

            Assert.AreEqual(2, tokens.Count, Describe(tokens));
            Assert.AreEqual(SourceTokenKind.Label, tokens[0].Kind);
            Assert.AreEqual("Start", tokens[0].Text);
            Assert.AreEqual(SourceTokenKind.Operator, tokens[1].Kind);
            Assert.AreEqual(":", tokens[1].Text);

            IReadOnlyList<SourceToken> onOneLine = Lex("Start: lda #$01");
            Assert.AreEqual(SourceTokenKind.Label, onOneLine[0].Kind);
            Assert.AreEqual(SourceTokenKind.Mnemonic, onOneLine[2].Kind);

            AssertKind("Start", SourceTokenKind.Plain, "Start");
        }

        [TestMethod]
        public void ACommentRunsToTheEndOfTheLineAndIsNotLexed()
        {
            IReadOnlyList<SourceToken> tokens = Lex("  lda #$01  ; this ; is not code");

            Assert.AreEqual(4, tokens.Count, Describe(tokens));
            Assert.AreEqual(SourceTokenKind.Comment, tokens[3].Kind);
            Assert.AreEqual("; this ; is not code", tokens[3].Text);

            Assert.AreEqual(SourceTokenKind.Comment, Single("; nothing else").Kind);
        }

        [TestMethod]
        public void DirectivesAreKeywordsAndUnknownOnesAreNot()
        {
            AssertKind(".org", SourceTokenKind.Keyword, ".org");
            AssertKind(".BYTE", SourceTokenKind.Keyword, ".BYTE");
            AssertKind(".byte", SourceTokenKind.Keyword, ".byte");
            AssertKind(".nope", SourceTokenKind.Plain, ".nope");
            AssertKind(".", SourceTokenKind.Operator, ".");
        }

        [TestMethod]
        public void ADotWithoutANameIsPunctuationAndNotAnError()
        {
            AssertKind(".", SourceTokenKind.Operator, ".");

            IReadOnlyList<SourceToken> doubled = Lex("..");
            Assert.AreEqual(2, doubled.Count, Describe(doubled));
            Assert.AreEqual(SourceTokenKind.Operator, doubled[0].Kind);
            Assert.AreEqual(SourceTokenKind.Operator, doubled[1].Kind);

            IReadOnlyList<SourceToken> trailing = Lex("5.");
            Assert.AreEqual(2, trailing.Count, Describe(trailing));
            Assert.AreEqual(SourceTokenKind.Number, trailing[0].Kind);
            Assert.AreEqual(SourceTokenKind.Operator, trailing[1].Kind);
        }

        [TestMethod]
        public void NumbersFollowTheExpressionTokenizer()
        {
            AssertKind("$FF", SourceTokenKind.Number, "$FF");
            AssertKind("%1010", SourceTokenKind.Number, "%1010");
            AssertKind("123", SourceTokenKind.Number, "123");

            // The sign is an operator of its own, the way the expression tokenizer
            // reads it, so #-1 colours as a number and a sign.
            IReadOnlyList<SourceToken> negative = Lex("-1");
            Assert.AreEqual(2, negative.Count, Describe(negative));
            Assert.AreEqual(SourceTokenKind.Operator, negative[0].Kind);
            Assert.AreEqual("-", negative[0].Text);
            Assert.AreEqual(SourceTokenKind.Number, negative[1].Kind);
            Assert.AreEqual("1", negative[1].Text);

            IReadOnlyList<SourceToken> negativeHex = Lex("-$10");
            Assert.AreEqual(2, negativeHex.Count, Describe(negativeHex));
            Assert.AreEqual(SourceTokenKind.Number, negativeHex[1].Kind);
            Assert.AreEqual("$10", negativeHex[1].Text);

            // % is binary only when digits of that radix follow it; otherwise it
            // is the modulo operator, exactly as the expression tokenizer reads it.
            AssertKind("%", SourceTokenKind.Operator, "%");

            IReadOnlyList<SourceToken> modulo = Lex("%x");
            Assert.AreEqual(2, modulo.Count, Describe(modulo));
            Assert.AreEqual(SourceTokenKind.Operator, modulo[0].Kind);
            Assert.AreEqual(SourceTokenKind.Plain, modulo[1].Kind);
        }

        [TestMethod]
        public void StringsAreOneTokenEvenWhenTheyHoldPunctuation()
        {
            AssertKind("\"\"", SourceTokenKind.String, "\"\"");
            AssertKind("\"a, b, c\"", SourceTokenKind.String, "\"a, b, c\"");
            AssertKind("\"it's; here\"", SourceTokenKind.String, "\"it's; here\"");

            IReadOnlyList<SourceToken> tokens = Lex("  .byte \"AB\", $0D");
            Assert.AreEqual(4, tokens.Count, Describe(tokens));
            Assert.AreEqual(SourceTokenKind.Keyword, tokens[0].Kind);
            Assert.AreEqual(SourceTokenKind.String, tokens[1].Kind);
            Assert.AreEqual("\"AB\"", tokens[1].Text);
            Assert.AreEqual(SourceTokenKind.Operator, tokens[2].Kind);
            Assert.AreEqual(SourceTokenKind.Number, tokens[3].Kind);
        }

        [TestMethod]
        public void AnUnterminatedStringEndsAtTheLineAndDoesNotThrow()
        {
            SourceToken token = Single("\"unterminated");

            Assert.AreEqual(SourceTokenKind.String, token.Kind);
            Assert.AreEqual("\"unterminated", token.Text);
        }

        [TestMethod]
        public void ExpressionKeywordsAreKeywordsAndSymbolsArePlain()
        {
            AssertKind("or", SourceTokenKind.Keyword, "or");
            AssertKind("TRUE", SourceTokenKind.Keyword, "TRUE");
            AssertKind("false", SourceTokenKind.Keyword, "false");
            AssertKind("PTR", SourceTokenKind.Plain, "PTR");

            // AND is both an expression keyword and a mnemonic. It has to read as
            // a mnemonic: AND #$01 is an instruction.
            AssertKind("AND", SourceTokenKind.Mnemonic, "AND");
        }

        [TestMethod]
        public void OperatorsAreReadLongestFirst()
        {
            AssertKind("<<", SourceTokenKind.Operator, "<<");
            AssertKind(">>", SourceTokenKind.Operator, ">>");
            AssertKind("<=", SourceTokenKind.Operator, "<=");
            AssertKind(">=", SourceTokenKind.Operator, ">=");
            AssertKind("!=", SourceTokenKind.Operator, "!=");
            AssertKind("<>", SourceTokenKind.Operator, "<>");
            AssertKind("==", SourceTokenKind.Operator, "==");
            AssertKind("*", SourceTokenKind.Operator, "*");
            AssertKind("+", SourceTokenKind.Operator, "+");
            AssertKind("{", SourceTokenKind.Operator, "{");
            AssertKind("}", SourceTokenKind.Operator, "}");
        }

        [TestMethod]
        public void TokensIndexTheLineTheyCameFrom()
        {
            string line = "Start: lda (#$10,x),y ; loop";
            IReadOnlyList<SourceToken> tokens = Lex(line);

            int previousEnd = 0;
            foreach (SourceToken token in tokens)
            {
                Assert.IsTrue(token.Start >= previousEnd, "tokens are in source order");
                Assert.IsTrue(token.Length > 0, "no token is empty");
                Assert.AreEqual(token.Text, line.Substring(token.Start, token.Length),
                    "the token text is the line at its own offsets");
                previousEnd = token.Start + token.Length;
            }
        }

        [TestMethod]
        public void MalformedSourceNeverThrows()
        {
            string[] hostile =
            {
                null,
                string.Empty,
                "   ",
                "\t\t",
                "\"",
                "'",
                "$",
                "%",
                "%2",
                "$ZZZZ",
                "lda #",
                "lda ($10,x",
                "lda ,,,,,",
                ":::::",
                "....",
                ";; only a comment",
                "\"\"\"\"",
                "lda #$01\t; tab\tseparated",
                " ",
                "lda #300",
                new string('x', 20000)
            };

            foreach (string line in hostile)
            {
                IReadOnlyList<SourceToken> tokens = Lex(line);
                if (line == null)
                {
                    Assert.AreEqual(0, tokens.Count);
                    continue;
                }

                foreach (SourceToken token in tokens)
                {
                    Assert.IsTrue(token.Start >= 0 && token.Start + token.Length <= line.Length,
                        "no token runs past the end of '" + line.Substring(0, Math.Min(line.Length, 40)) + "'");
                    Assert.AreEqual(token.Text, line.Substring(token.Start, token.Length));
                }
            }
        }

        [TestMethod]
        public void AWholeSourceIsNumberedFromOne()
        {
            string probe = "clc\n  lda #$01\r\nrts\n";
            IReadOnlyList<SourceTokenLine> lines = new AssemblyLexer().Tokenize(probe);

            // A source ending with a newline has no last line. The assembler
            // counts it that way and so does the listing, so the gutter an editor
            // draws from this has to agree with both.
            Assert.AreEqual(3, lines.Count);
            Assert.AreEqual(1, lines[0].LineNumber);
            Assert.AreEqual(2, lines[1].LineNumber);
            Assert.AreEqual(3, lines[2].LineNumber);
            Assert.AreEqual(SourceTokenKind.Mnemonic, lines[1].Tokens[0].Kind);
            Assert.AreEqual("lda", lines[1].Tokens[0].Text);

            Assert.AreEqual(1, new AssemblyLexer().Tokenize("clc\n").Count);
            Assert.AreEqual(0, new AssemblyLexer().Tokenize(null).Count);
        }

        [TestMethod]
        public void TheLexedMnemonicsAreTheOnesTheAssemblerAccepts()
        {
            IReadOnlyList<SourceToken> nmos = Lex("stz $10");
            Assert.AreEqual(SourceTokenKind.Plain, nmos[0].Kind, "an NMOS assembler has no STZ");

            IReadOnlyList<SourceToken> cmos = new AssemblyLexer(new Cpu65C02()).TokenizeLine("stz $10");
            Assert.AreEqual(SourceTokenKind.Mnemonic, cmos[0].Kind, "a 65C02 has it");

            IReadOnlyList<SourceToken> bra = new AssemblyLexer(new Cpu65C02()).TokenizeLine("bra Start");
            Assert.AreEqual(SourceTokenKind.Mnemonic, bra[0].Kind);
        }

        [TestMethod]
        public void EveryDirectiveTheLexerHighlightsIsOneTheAssemblerDispatches()
        {
            HashSet<string> dispatched = new HashSet<string>(AssemblerEngine.DefaultDirectiveNames, StringComparer.OrdinalIgnoreCase);

            foreach (string directive in AssemblyLexer.DefaultDirectives)
            {
                if (directive == ".res")
                {
                    // .res has no handler: it is a pattern the line dispatch
                    // matches before any handler runs. Pinned here so that the
                    // exception stays deliberate.
                    AssemblyResult reserved = new AssemblerEngine()
                        .AssembleSource(".org $8000\nBuffer .res 4\n", null);
                    Assert.IsTrue(reserved.Success, ".res assembles");
                    continue;
                }

                Assert.IsTrue(dispatched.Contains(directive),
                    directive + " is highlighted as a directive but the assembler dispatches no such handler");
            }

            Assert.IsFalse(dispatched.Contains(".res"));
        }
    }
}