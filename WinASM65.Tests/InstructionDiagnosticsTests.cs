using Microsoft.VisualStudio.TestTools.UnitTesting;
using static WinASM65.Tests.SyntaxTestKit;

namespace WinASM65.Tests
{
    /// <summary>
    /// What used to fail in silence now fails with a name: an unknown mnemonic or
    /// directive, a mode the CPU does not have, an error found only once a forward
    /// reference resolves.
    /// </summary>
    [TestClass]
    public class InstructionDiagnosticsTests
    {
        [TestMethod]
        public void AnUnknownMnemonicIsAnErrorAndEmitsNothing()
        {
            StringAssert.Contains(Errors(".org $8000\n foo $10"), "Unknown instruction or macro 'foo'");
            Assert.AreEqual(0, Assemble(".org $8000\n foo $10").OutputBytes.Length);
        }

        [TestMethod]
        public void AnUnknownDirectiveIsAnError()
        {
            StringAssert.Contains(Errors(".org $8000\n .bogus 1"), "Unknown directive '.bogus'");
        }

        [TestMethod]
        public void ALabelledUnknownDirectiveIsAnError()
        {
            StringAssert.Contains(Errors(".org $8000\nTable: .bogus 1"), "Unknown directive '.bogus'");
        }

        [TestMethod]
        public void AMissingImpliedFormIsAnError()
        {
            StringAssert.Contains(Errors(".org $8000\n lda"), "LDA does not support implied addressing");
        }

        [TestMethod]
        public void AModeTheInstructionLacksIsAnError()
        {
            StringAssert.Contains(Errors(".org $8000\n stx $1234,x"), "STX does not support absolute,X addressing");
            StringAssert.Contains(Errors(".org $8000\n lda ($12)"), "LDA does not support indirect () addressing");
        }

        [TestMethod]
        public void AccumulatorFormsAcceptTheExplicitA()
        {
            AreBytes("0A 0A 4A 2A 6A", Bytes(".org $8000\n asl\n asl a\n lsr A\n rol a\n ror a"));
            AreBytes("1A 3A", Bytes(".org $8000\n inc a\n dec A", "65c02"));
        }

        [TestMethod]
        public void ASymbolNamedAIsStillASymbolForOtherInstructions()
        {
            AreBytes("A5 10", Bytes("A = $10\n.org $8000\n lda A"));
        }

        [TestMethod]
        public void AZeroPageOnlyFormIsUsedForAForwardReference()
        {
            // STX has no absolute,Y form: the forward reference takes the zero page one.
            AreBytes("96 20", Bytes(".org $8000\n stx later,y\nlater = $20"));
        }

        [TestMethod]
        public void AZeroPageOnlyFormRefusesAWideValue()
        {
            StringAssert.Contains(Errors(".org $8000\n stx $1234,y"), "out of range for a 1-byte operand");
        }

        [TestMethod]
        public void ALateBranchErrorNamesTheBranchLine()
        {
            string errors = Errors(".org $8000\n bne far\n .rep 200\n nop\n .endrep\nfar: rts");
            StringAssert.Contains(errors, "Line 2");
            StringAssert.Contains(errors, "Relative jump is too big");
        }

        [TestMethod]
        public void ALateDivisionByZeroNamesItsLine()
        {
            string errors = Errors(".org $8000\n lda #10/zero\n nop\nzero = 0");
            StringAssert.Contains(errors, "Line 2");
            StringAssert.Contains(errors, "Division by zero");
        }
    }
}
