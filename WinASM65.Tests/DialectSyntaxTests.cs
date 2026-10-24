using Microsoft.VisualStudio.TestTools.UnitTesting;
using static WinASM65.Tests.SyntaxTestKit;

namespace WinASM65.Tests
{
    /// <summary>
    /// The forms other 6502 assemblers use: EQU and .set, the program counter '*',
    /// .elseif, directive aliases, and macro arguments that survive substitution.
    /// </summary>
    [TestClass]
    public class DialectSyntaxTests
    {
        [TestMethod]
        public void EquDefinesAConstantWithOrWithoutTheDot()
        {
            AreBytes("A9 05 A9 06", Bytes("five EQU 5\nsix .equ 6\n.org $8000\n lda #five\n lda #six"));
        }

        [TestMethod]
        public void EquRefusesAnotherValue()
        {
            StringAssert.Contains(Errors("n EQU 1\nn EQU 2\n.org $8000\n nop"), "Constant 'n' already defined with a different value");
        }

        [TestMethod]
        public void EquAcceptsTheSameValueAgain()
        {
            AreBytes("EA", Bytes("n EQU 1\nn EQU 1\n.org $8000\n nop"));
        }

        [TestMethod]
        public void SetAndEqualsStayRedefinable()
        {
            AreBytes("A9 02 A9 04", Bytes("n .set 1\nn .set 2\n.org $8000\n lda #n\nm = 3\nm = 4\n lda #m"));
        }

        [TestMethod]
        public void EquAloneIsReported()
        {
            StringAssert.Contains(Errors(".org $8000\n .equ 5"), ".equ requires a name");
        }

        [TestMethod]
        public void StarIsTheProgramCounterInOperandPosition()
        {
            AreBytes("4C 00 80 EA 05 80", Bytes(".org $8000\n jmp *\n nop\n .word *+1"));
        }

        [TestMethod]
        public void StarBetweenValuesStillMultiplies()
        {
            AreBytes("A9 06", Bytes(".org $8000\n lda #2*3"));
        }

        [TestMethod]
        public void StarEqualsSetsTheOrigin()
        {
            Core.AssemblyResult result = Assemble("*= $C000\n jmp *");
            Assert.IsTrue(result.Success, Describe(result));
            AreBytes("4C 00 C0", result.OutputBytes);
        }

        [TestMethod]
        public void APendingExpressionKeepsTheAddressOfItsLine()
        {
            // *+3 is read on the jmp line even though 'later' resolves afterwards.
            AreBytes("4C 05 80 EA EA", Bytes(".org $8000\n jmp *+later\n nop\n nop\nlater = 5"));
        }

        [TestMethod]
        public void ElseIfPicksTheFirstTrueBranch()
        {
            AreBytes("A9 02", Bytes("v = 2\n.org $8000\n.if v == 1\n lda #1\n.elseif v == 2\n lda #2\n.elseif v == 2\n lda #3\n.else\n lda #4\n.endif"));
            AreBytes("A9 04", Bytes("v = 9\n.org $8000\n.if v == 1\n lda #1\n.elseif v == 2\n lda #2\n.else\n lda #4\n.endif"));
        }

        [TestMethod]
        public void ElseIfAfterATakenBranchDoesNotReadItsCondition()
        {
            AreBytes("A9 01", Bytes(".org $8000\n.if 1\n lda #1\n.elseif undefined_name\n lda #2\n.endif"));
        }

        [TestMethod]
        public void ANestedIfInADeadBranchStaysDead()
        {
            AreBytes("A9 09", Bytes(".org $8000\n.if 0\n.if 1\n lda #1\n.else\n lda #2\n.endif\n.else\n lda #9\n.endif"));
        }

        [TestMethod]
        public void ElseAfterElseIsReported()
        {
            StringAssert.Contains(Errors(".org $8000\n.if 1\n nop\n.else\n nop\n.else\n nop\n.endif"), ".else/.elseif after .else");
        }

        [TestMethod]
        public void DataAliasesAreTheSameDirectives()
        {
            AreBytes("01 02 03 34 12 78 56", Bytes(".org $8000\n .db 1\n .byt 2, 3\n .dw $1234\n .addr $5678"));
        }

        [TestMethod]
        public void BlockAliasesCloseTheirBlocks()
        {
            AreBytes("EA EA E8", Bytes(".macro two\n nop\n nop\n.endm\n.org $8000\n two\n.rept 1\n inx\n.endr"));
        }

        [TestMethod]
        public void AMacroParameterIsReplacedAsAWholeWord()
        {
            // A parameter named x must not rewrite "tax" or "x" inside other names.
            AreBytes("AA A9 07", Bytes(".macro load x\n tax\n lda #x\n.endmacro\n.org $8000\n load 7"));
        }

        [TestMethod]
        public void AMacroArgumentCanHoldACommaInParentheses()
        {
            AreBytes("B1 10", Bytes(".macro get where\n lda where\n.endmacro\n.org $8000\n get ($10),y"));
        }
    }
}
