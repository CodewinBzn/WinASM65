using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Cpu;
using WinASM65.Directives;
using WinASM65.Expressions;

namespace WinASM65.Tests
{
    [TestClass]
    public class CoreTests
    {
        [TestMethod]
        public void Evaluator_RespectsArithmeticPrecedence()
        {
            ExpressionResult result = new ExpressionEvaluator(new Tokenizer()).Evaluate("2 + 3 * 4");
            Assert.IsTrue(result.IsResolved);
            Assert.AreEqual(14L, result.Value.AsInteger);
        }

        [TestMethod]
        public void ConditionalState_RequiresAllParentsToBeTrue()
        {
            ConditionalState state = new ConditionalState();
            state.Push(false);
            state.Push(true);
            Assert.IsFalse(state.ShouldAssembleCurrentLine());
            state.Pop();
            state.FlipTop();
            Assert.IsTrue(state.ShouldAssembleCurrentLine());
        }

        [TestMethod]
        public void Cpu_ParsesRelativeAndZeroPageCandidates()
        {
            Cpu6502 cpu = new Cpu6502();
            Assert.AreEqual(AddressingMode.Relative, cpu.ParseOperand("bne", "target").Mode);
            AddressingMode mode;
            byte opcode;
            byte length;
            Assert.IsTrue(cpu.TryOptimizeZeroPage("lda", AddressingMode.Absolute, 0x10, out mode, out opcode, out length));
            Assert.AreEqual(AddressingMode.ZeroPage, mode);
            Assert.AreEqual(2, length);
            Assert.AreEqual(0xA5, opcode);
        }
    }
}
