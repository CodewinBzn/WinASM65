using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using static WinASM65.Tests.SyntaxTestKit;

namespace WinASM65.Tests
{
    /// <summary>Block comments: /* ... */, inside a line or over several.</summary>
    [TestClass]
    public class BlockCommentTests
    {
        [TestMethod]
        public void ABlockCommentOverSeveralLinesHidesTheirCode()
        {
            AreBytes("A9 02 60", Bytes(".org $8000\n/* lda #1\n   sta $10 ; not this either\n*/\n lda #2\n rts"));
        }

        [TestMethod]
        public void ABlockCommentInsideALineSeparatesWhatIsAroundIt()
        {
            AreBytes("A9 01 85 10", Bytes(".org $8000\n lda/* the count */#1\n sta $10 /* where */ ; and a line comment"));
        }

        [TestMethod]
        public void CodeMayFollowTheCommentOnTheLineThatClosesIt()
        {
            AreBytes("EA 60", Bytes(".org $8000\n nop /* begins here\n ends here */ rts"));
        }

        [TestMethod]
        public void SlashStarInAStringOrACharacterIsData()
        {
            AreBytes("2F 2A 2A 2F 2F", Bytes(".org $8000\n .byte \"/**/\", '/'"));
        }

        [TestMethod]
        public void ASemicolonDoesNotEndABlockComment()
        {
            AreBytes("60", Bytes(".org $8000\n/* a ; inside */ rts"));
        }

        [TestMethod]
        public void AnUnclosedBlockCommentIsReportedWhereItOpens()
        {
            string errors = Errors(".org $8000\n nop\n/* never\n closed");
            StringAssert.Contains(errors, "Line 3");
            StringAssert.Contains(errors, "Unterminated comment");
        }

        [TestMethod]
        public void AMacroBodyMayHoldABlockComment()
        {
            AreBytes("A9 05 A9 05", Bytes(".macro five\n/* load\n five */ lda #5\n.endmacro\n.org $8000\n five\n five"));
        }

        [TestMethod]
        public void StripCommentsCarriesTheStateFromLineToLine()
        {
            bool inBlock = false;
            bool opened;
            Assert.AreEqual(" nop ", SourceText.StripComments(" nop /* a", ref inBlock, out opened));
            Assert.IsTrue(inBlock);
            Assert.IsTrue(opened);
            Assert.AreEqual("", SourceText.StripComments(" still ; in", ref inBlock, out opened));
            Assert.IsFalse(opened);
            Assert.AreEqual("  rts", SourceText.StripComments(" */ rts", ref inBlock, out opened));
            Assert.IsFalse(inBlock);
        }
    }
}
