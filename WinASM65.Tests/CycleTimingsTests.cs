using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Cpu;
using WinASM65.Execution;

namespace WinASM65.Tests
{
    /// <summary>The cycle counts an editor shows next to an instruction.</summary>
    [TestClass]
    public class CycleTimingsTests
    {
        private static InstructionDocumentation Form(string cpu, string mnemonic, AddressingMode mode)
        {
            InstructionDocumentation doc;
            Assert.IsTrue(InstructionDocs.TryGet(mnemonic, mode, out doc, CpuFactory.Create(cpu)), mnemonic + " " + mode + " on " + cpu);
            return doc;
        }

        [TestMethod]
        public void TheDocumentedNmosCountsAreTheExecutionCoresOwn()
        {
            // One source of truth: the editor shows what the emulator counts.
            foreach (InstructionDocumentation doc in InstructionDocs.ForCpu(new Cpu6502()))
            {
                Assert.IsTrue(doc.Cycles.HasValue, doc + " has no cycle count");
                Assert.AreEqual(Cpu6502Core.GetBaseCycles(doc.Opcode), doc.Cycles.Value, doc.ToString());
            }
        }

        [TestMethod]
        public void EveryFormOfEveryCpuHasACountExceptTheHalt()
        {
            foreach (ICpuInstructionSet cpu in CpuFactory.All.Values)
            {
                foreach (InstructionDocumentation doc in InstructionDocs.ForCpu(cpu))
                {
                    if (doc.Mnemonic == "JAM")
                        Assert.IsNull(doc.Cycles);
                    else
                        Assert.IsTrue(doc.Cycles > 0, doc + " on " + cpu.GetType().Name + " has no cycle count");
                }
            }
        }

        [TestMethod]
        public void IndexedReadsAndBranchesSayWhenTheyTakeLonger()
        {
            Assert.AreEqual("4 cycles (+1 if a page is crossed)", Form("6502", "LDA", AddressingMode.AbsoluteX).CyclesText);
            Assert.AreEqual("5 cycles", Form("6502", "STA", AddressingMode.AbsoluteX).CyclesText);
            Assert.AreEqual("2 cycles (+1 if taken, +2 if taken across a page)", Form("6502", "BNE", AddressingMode.Relative).CyclesText);
            Assert.AreEqual("5 cycles (+1 if a page is crossed)", Form("6502", "LDA", AddressingMode.IndirectY).CyclesText);
        }

        [TestMethod]
        public void The65C02CountsDifferWhereTheChipDoes()
        {
            Assert.AreEqual(5, Form("6502", "JMP", AddressingMode.Indirect).Cycles);
            Assert.AreEqual(6, Form("65c02", "JMP", AddressingMode.Indirect).Cycles);
            Assert.AreEqual(7, Form("6502", "ROR", AddressingMode.AbsoluteX).Cycles);
            Assert.AreEqual("6 cycles (+1 if a page is crossed)", Form("65c02", "ROR", AddressingMode.AbsoluteX).CyclesText);
            Assert.AreEqual("2 cycles (+1 in decimal mode)", Form("65c02", "ADC", AddressingMode.Immediate).CyclesText);
            Assert.AreEqual(5, Form("65c02", "STZ", AddressingMode.AbsoluteX).Cycles);
            Assert.AreEqual("3 cycles (+1 if it crosses a page)", Form("65c02", "BRA", AddressingMode.Relative).CyclesText);
        }

        [TestMethod]
        public void IllegalAndBitInstructionsHaveTheirPublishedCounts()
        {
            Assert.AreEqual(7, Form("6502x", "ISC", AddressingMode.AbsoluteX).Cycles);
            Assert.AreEqual(8, Form("6502x", "DCP", AddressingMode.IndirectY).Cycles);
            Assert.AreEqual("5 cycles (+1 if a page is crossed)", Form("6502x", "LAX", AddressingMode.IndirectY).CyclesText);
            Assert.AreEqual(3, Form("6502x", "SAX", AddressingMode.ZeroPage).Cycles);
            Assert.AreEqual("halts the processor until reset", Form("6502x", "JAM", AddressingMode.Implicit).CyclesText);
            Assert.AreEqual("5 cycles (+1 if taken, +2 if taken across a page)", Form("r65c02", "BBR0", AddressingMode.ZeroPageRelative).CyclesText);
            Assert.AreEqual(5, Form("r65c02", "SMB3", AddressingMode.ZeroPage).Cycles);
            Assert.AreEqual(3, Form("w65c02", "WAI", AddressingMode.Implicit).Cycles);
        }
    }
}
