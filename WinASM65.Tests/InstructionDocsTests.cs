using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.Output;

namespace WinASM65.Tests
{
    [TestClass]
    public class InstructionDocsTests
    {
        // Every documented form has to be writable in a source line, or the
        // agreement test could only check the forms that happen to have an easy
        // spelling. The zero-page forms take a one-byte operand and the absolute
        // ones a two-byte operand, which is also what keeps the assembler from
        // shrinking an absolute form to zero page behind the test's back.
        private static string SourceFor(InstructionDocumentation doc)
        {
            switch (doc.Mode)
            {
                case AddressingMode.Implicit:
                    return doc.Mnemonic;
                case AddressingMode.Accumulator:
                    return doc.Mnemonic;
                case AddressingMode.Immediate:
                    return doc.Mnemonic + " #$01";
                case AddressingMode.Absolute:
                    return doc.Mnemonic + " $1234";
                case AddressingMode.AbsoluteX:
                    return doc.Mnemonic + " $1234,x";
                case AddressingMode.AbsoluteY:
                    return doc.Mnemonic + " $1234,y";
                case AddressingMode.ZeroPage:
                    return doc.Mnemonic + " $10";
                case AddressingMode.ZeroPageX:
                    return doc.Mnemonic + " $10,x";
                case AddressingMode.ZeroPageY:
                    return doc.Mnemonic + " $10,y";
                case AddressingMode.Indirect:
                    return doc.Mnemonic + " ($1234)";
                case AddressingMode.IndirectX:
                    return doc.Mnemonic + " ($10,x)";
                case AddressingMode.IndirectY:
                    return doc.Mnemonic + " ($10),y";
                case AddressingMode.Relative:
                    return "Start:\n" + doc.Mnemonic + " Start";
                case AddressingMode.ZeroPageIndirect:
                    return doc.Mnemonic + " ($10)";
                case AddressingMode.AbsoluteIndexedIndirect:
                    return doc.Mnemonic + " ($1234,x)";
                default:
                    return null;
            }
        }

        private static void AssertAgreesWithTheAssembler(ICpuInstructionSet cpu, string cpuName)
        {
            IReadOnlyList<InstructionDocumentation> docs = InstructionDocs.ForCpu(cpu);
            Assert.IsTrue(docs.Count > 0, cpuName + " has documented forms");

            foreach (InstructionDocumentation doc in docs)
            {
                string line = SourceFor(doc);
                Assert.IsNotNull(line, cpuName + ": no spelling for " + doc.Mnemonic + " in " + doc.Mode);

                AssemblyResult result = new AssemblerEngine(cpu: cpu).AssembleSource(".org $8000\n" + line + "\n", null);
                Assert.IsTrue(result.Success, cpuName + ": " + line + " assembles (" + Describe(result) + ")");

                Assert.AreEqual(doc.Length, result.OutputBytes.Length,
                    cpuName + ": " + line + " occupies the documented " + doc.Length + " bytes");
                Assert.AreEqual(doc.Opcode, result.OutputBytes[0],
                    cpuName + ": " + line + " emits the documented opcode");
            }
        }

        [TestMethod]
        public void The65C02ParserAlwaysPicksTheZeroPageForm()
        {
            // Pinned because it decides what a 65C02 source can say: the parser
            // returns the (zp) form whenever the table has one, whatever the
            // operand value is, so a two-byte operand inside parentheses is then
            // out of range rather than silently encoded as a word.
            Assert.IsTrue(InstructionDocs.TryGet("LDA", AddressingMode.ZeroPageIndirect, out InstructionDocumentation zp, new Cpu65C02()));

            AssemblyResult narrow = new AssemblerEngine(cpu: new Cpu65C02()).AssembleSource(".org $8000\nLDA ($12)\n", null);
            Assert.IsTrue(narrow.Success);
            Assert.AreEqual(2, narrow.OutputBytes.Length);
            Assert.AreEqual(zp.Opcode, narrow.OutputBytes[0], "a 65C02 encodes (zp) where it has one");

            AssemblyResult tooWide = new AssemblerEngine(cpu: new Cpu65C02()).AssembleSource(".org $8000\nLDA ($1234)\n", null);
            Assert.IsFalse(tooWide.Success,
                "the parser picks (zp) whatever the operand is, so a two-byte operand is then out of range");

            // The only mnemonic with a real (nnnn) form is JMP, and it is
            // parsed as an ordinary jump on both CPUs.
            AssemblyResult jump65 = new AssemblerEngine(cpu: new Cpu65C02()).AssembleSource(".org $8000\nJMP ($1234)\n", null);
            Assert.IsTrue(jump65.Success);
            Assert.AreEqual(3, jump65.OutputBytes.Length);

            AssemblyResult jumpNmos = new AssemblerEngine(cpu: new Cpu6502()).AssembleSource(".org $8000\nJMP ($1234)\n", null);
            Assert.IsTrue(jumpNmos.Success);
            Assert.AreEqual(3, jumpNmos.OutputBytes.Length);
        }

        [TestMethod]
        public void EveryDocumented6502FormAgreesWithTheAssembler()
        {
            AssertAgreesWithTheAssembler(new Cpu6502(), "6502");
        }

        [TestMethod]
        public void EveryDocumented65C02FormAgreesWithTheAssembler()
        {
            AssertAgreesWithTheAssembler(new Cpu65C02(), "65C02");
        }

        [TestMethod]
        public void EveryMnemonicTheAssemblerAcceptsIsDocumented()
        {
            foreach (ICpuInstructionSet cpu in new ICpuInstructionSet[] { new Cpu6502(), new Cpu65C02() })
            {
                foreach (string mnemonic in cpu.InstructionTable.Keys)
                {
                    IReadOnlyList<InstructionDocumentation> forms = InstructionDocs.ForMnemonic(mnemonic, cpu);
                    Assert.IsTrue(forms.Count > 0, mnemonic + " is accepted by the assembler but documented nowhere");
                }
            }
        }

        [TestMethod]
        public void EveryDocumentedFormIsOneTheAssemblerAccepts()
        {
            foreach (ICpuInstructionSet cpu in new ICpuInstructionSet[] { new Cpu6502(), new Cpu65C02() })
            {
                foreach (InstructionDocumentation doc in InstructionDocs.ForCpu(cpu))
                {
                    byte opcode;
                    Assert.IsTrue(cpu.TryGetOpcode(doc.Mnemonic, doc.Mode, out opcode),
                        doc.Mnemonic + " in " + doc.Mode + " is documented but the assembler refuses it");
                    Assert.AreEqual(opcode, doc.Opcode, doc.Mnemonic + " in " + doc.Mode);
                }
            }
        }

        [TestMethod]
        public void LengthsAreTheOnesTheDisassemblerWalksAnImageWith()
        {
            // Feed each documented opcode back through the disassembler: the
            // mnemonic and the length it reports have to be the ones documented,
            // or an editor stepping over an instruction would skip the wrong
            // number of bytes.
            Disassembler disassembler = new Disassembler(new Cpu65C02());

            foreach (InstructionDocumentation doc in InstructionDocs.For65C02)
            {
                byte[] image = new byte[8];
                image[0] = doc.Opcode;

                DisassembledInstruction decoded = disassembler.Disassemble(0x8000, image, 0);

                Assert.IsTrue(decoded.IsKnown, doc.Opcode.ToString("X2") + " decodes");
                Assert.AreEqual(doc.Mnemonic, decoded.Mnemonic,
                    doc.Opcode.ToString("X2") + " decodes back to " + doc.Mnemonic);
                Assert.AreEqual(doc.Length, decoded.Length,
                    doc.Mnemonic + " in " + doc.Mode + " walks the documented length");
            }
        }

        [TestMethod]
        public void TheCmosExtensionsAreDocumentedAndTheNmosTableIsNot()
        {
            Assert.IsTrue(InstructionDocs.TryGet("BRA", AddressingMode.Relative, out InstructionDocumentation bra, new Cpu65C02()));
            Assert.AreEqual(0x80, bra.Opcode);
            Assert.AreEqual(2, bra.Length);

            Assert.IsTrue(InstructionDocs.TryGet("STZ", AddressingMode.ZeroPage, out InstructionDocumentation stz, new Cpu65C02()));
            Assert.AreEqual(0x64, stz.Opcode);

            InstructionDocumentation unknown;
            Assert.IsFalse(InstructionDocs.TryGet("NOPE", AddressingMode.Absolute, out unknown));
            Assert.IsNull(unknown);

            InstructionDocumentation braOnNmos;
            Assert.IsFalse(InstructionDocs.TryGet("BRA", AddressingMode.Relative, out braOnNmos, new Cpu6502()),
                "BRA is a 65C02 addition, so an NMOS lookup must not answer it");
        }

        [TestMethod]
        public void TheMnemonicSetIsTheOneTheAssemblerAccepts()
        {
            IReadOnlyList<string> mnemonics = InstructionDocs.Mnemonics;
            HashSet<string> names = new HashSet<string>(mnemonics, StringComparer.Ordinal);

            Assert.IsTrue(names.Contains("LDA"));
            Assert.IsTrue(names.Contains("STZ"), "a 65C02 mnemonic is in the set an editor highlights with");
            Assert.IsFalse(names.Contains("lda"), "the set is normalised to upper case");
            Assert.IsFalse(names.Contains("NOPE"));

            Assert.IsTrue(InstructionDocs.IsMnemonic("lda"));
            Assert.IsFalse(InstructionDocs.IsMnemonic("ldax"));
            Assert.IsFalse(InstructionDocs.IsMnemonic(null));

            HashSet<string> unique = new HashSet<string>(mnemonics);
            Assert.AreEqual(unique.Count, mnemonics.Count, "the mnemonic set has no duplicate");
        }

        [TestMethod]
        public void DocumentationIsCachedPerCpuAndNotSharedBetweenCpus()
        {
            Cpu6502 first = new Cpu6502();
            IReadOnlyList<InstructionDocumentation> again = InstructionDocs.ForCpu(first);
            Assert.AreSame(InstructionDocs.ForCpu(first), again, "one pass per CPU instance");

            Assert.IsTrue(InstructionDocs.For6502.Count < InstructionDocs.For65C02.Count,
                "the 65C02 table is the NMOS one plus its extensions");
            Assert.IsTrue(InstructionDocs.ForCpu(null).Count == 0, "no CPU, no documentation");
        }

        [TestMethod]
        public void TheSyntaxIsWrittenPerAddressingMode()
        {
            Assert.AreEqual(string.Empty, InstructionDocs.SyntaxFor(AddressingMode.Implicit));
            Assert.AreEqual("A", InstructionDocs.SyntaxFor(AddressingMode.Accumulator));
            Assert.AreEqual("#$nn", InstructionDocs.SyntaxFor(AddressingMode.Immediate));
            Assert.AreEqual("$nnnn", InstructionDocs.SyntaxFor(AddressingMode.Absolute));
            Assert.AreEqual("($nn),y", InstructionDocs.SyntaxFor(AddressingMode.IndirectY));
            Assert.AreEqual("($nnnn,x)", InstructionDocs.SyntaxFor(AddressingMode.AbsoluteIndexedIndirect));
        }

        private static string Describe(AssemblyResult result)
        {
            if (result.Diagnostics.Count == 0)
                return "no diagnostic";

            return result.Diagnostics[0].Message;
        }
    }
}