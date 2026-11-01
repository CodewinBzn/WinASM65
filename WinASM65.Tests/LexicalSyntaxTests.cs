using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using static WinASM65.Tests.SyntaxTestKit;

namespace WinASM65.Tests
{
    /// <summary>
    /// Strings, character literals, comments and the expression tokenizer: the parts
    /// of a line that must be read character by character rather than by regex.
    /// </summary>
    [TestClass]
    public class LexicalSyntaxTests
    {
        [TestMethod]
        public void ASemicolonInsideAStringIsNotAComment()
        {
            AreBytes("61 3B 62", Bytes(".org $8000\n .byte \"a;b\" ; comment"));
        }

        [TestMethod]
        public void AnApostropheInACommentIsHarmless()
        {
            AreBytes("EA", Bytes(".org $8000\n nop ; wait for 2 VBlank's"));
        }

        [TestMethod]
        public void ACharacterLiteralCanBeASemicolon()
        {
            AreBytes("C9 3B", Bytes(".org $8000\n cmp #';' ; compare"));
        }

        [TestMethod]
        public void ACharacterLiteralCanBeASpace()
        {
            AreBytes("A9 20", Bytes(".org $8000\n lda #' '"));
        }

        [TestMethod]
        public void BothQuoteStylesMakeACharacter()
        {
            AreBytes("A9 41 A9 41", Bytes(".org $8000\n lda #'A'\n lda #\"A\""));
        }

        [TestMethod]
        public void CharacterEscapesAreDecoded()
        {
            AreBytes("A9 0A A9 27 A9 5C A9 41", Bytes(".org $8000\n lda #'\\n'\n lda #'\\''\n lda #'\\\\'\n lda #'\\x41'"));
        }

        [TestMethod]
        public void AStringCanHoldACommaAndEscapes()
        {
            AreBytes("61 2C 62 0A 00 22 5C 41", Bytes(".org $8000\n .byte \"a,b\\n\\0\\\"\\\\\\x41\""));
        }

        [TestMethod]
        public void StringsAndValuesMixInOneByteLine()
        {
            AreBytes("4E 45 53 1A", Bytes(".org $8000\n .byte \"NES\", $1A"));
        }

        [TestMethod]
        public void AnUnterminatedStringIsReported()
        {
            StringAssert.Contains(Errors(".org $8000\n .byte \"abc"), ErrorCodes.UNTERMINATED_STRING);
        }

        [TestMethod]
        public void AnUnknownEscapeIsReported()
        {
            StringAssert.Contains(Errors(".org $8000\n .byte \"a\\qb\""), "Invalid escape sequence");
        }

        [TestMethod]
        public void ANameStartingWithAKeywordIsOneName()
        {
            AreBytes("A9 05 A9 07", Bytes("origin = 5\nand_mask = 7\n.org $8000\n lda #origin\n lda #and_mask"));
        }

        [TestMethod]
        public void PercentAfterAValueIsModulo()
        {
            AreBytes("A9 07 A9 02", Bytes(".org $8000\n lda #7%10\n lda #%10"));
        }

        [TestMethod]
        public void KeywordsStillWork()
        {
            AreBytes("A9 01", Bytes(".org $8000\n lda #1 && TRUE or false"));
        }

        [TestMethod]
        public void AnInvalidTokenIsAnErrorNotAZero()
        {
            StringAssert.Contains(Errors(".org $8000\n lda #$12#"), "Invalid token");
        }

        [TestMethod]
        public void CStyleHexIsAnErrorNotAZero()
        {
            StringAssert.Contains(Errors(".org $8000\n lda #0x10"), "Malformed expression");
        }

        [TestMethod]
        public void AnUnbalancedParenthesisIsReported()
        {
            StringAssert.Contains(Errors(".org $8000\n lda #(1+2"), "Malformed expression");
        }

        [TestMethod]
        public void DivisionByZeroIsReported()
        {
            StringAssert.Contains(Errors(".org $8000\n lda #4/0"), "Division by zero");
        }

        [TestMethod]
        public void AHugeNumberIsReported()
        {
            StringAssert.Contains(Errors(".org $8000\n .word 99999999999999999999"), "too large");
        }

        [TestMethod]
        public void SplitArgumentsRespectsLiteralsAndParentheses()
        {
            CollectionAssert.AreEqual(new[] { "\"a,b\"", "','", "(1,2)", "x" },
                SourceText.SplitArguments("\"a,b\", ',' , (1,2),x"));
        }

        [TestMethod]
        public void StripCommentKeepsLiterals()
        {
            Assert.AreEqual(" .byte \"a;b\", ';' ", SourceText.StripComment(" .byte \"a;b\", ';' ; tail"));
            Assert.AreEqual(" nop ", SourceText.StripComment(" nop ; it's fine"));
        }
    }
}
