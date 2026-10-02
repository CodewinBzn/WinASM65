using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Cpu;

namespace WinASM65.Tests
{
    [TestClass]
    public class DisassemblerTests
    {
        // Programme couvrant chaque mode d'adressage du NMOS 6502.
        private const string Programme = ".org $8000\n"
            + "lda #$01\n"
            + "lda $10\n"
            + "lda $1234\n"
            + "lda $1234,x\n"
            + "lda $1234,y\n"
            + "lda ($10,x)\n"
            + "lda ($10),y\n"
            + "sta $1234\n"
            + "jmp $1234\n"
            + "jmp ($1234)\n"
            + "asl\n"
            + "nop\n"
            + "clc\n"
            + "inx\n"
            + "bne loop\n"
            + "loop:\n";

        [TestMethod]
        public void ChaqueEntreeDeTableAUneSortie()
        {
            AssertCoherence(new Cpu6502());
            AssertCoherence(new Cpu65C02());
        }

        [TestMethod]
        public void OpcodeInconnu_EstSignaleEtJamaisDevine()
        {
            Disassembler dis = new Disassembler(new Cpu6502());
            DisassembledInstruction ins = dis.Disassemble(0x8000, new byte[] { 0x02 }, 0);

            Assert.IsFalse(ins.IsKnown);
            Assert.AreEqual(".byte $02", ins.Text);
            Assert.AreEqual(1, ins.Length, "un opcode inconnu occupe un octet.");
        }

        [TestMethod]
        public void FormeZeroPageEstConservee()
        {
            // L'assembleur optimise deja vers la forme zero-page : le desassembleur doit
            // la restituer, sinon idaire puis reassembler ne serait pas idempotent.
            Disassembler dis = new Disassembler(new Cpu6502());
            DisassembledInstruction ins = dis.Disassemble(0x8000, new byte[] { 0xA5, 0x10 }, 0);

            Assert.IsTrue(ins.IsKnown);
            Assert.AreEqual("LDA", ins.Mnemonic);
            Assert.AreEqual(AddressingMode.ZeroPage, ins.Mode);
            Assert.AreEqual("LDA $10", ins.Text);
        }

        [TestMethod]
        public void OperandesRendusCorrespondentALaSyntaxeAcceptee()
        {
            Disassembler dis = new Disassembler(new Cpu6502());
            Assert.AreEqual("LDA #$01", Text(dis, 0x8000, 0xA9, 0x01));
            Assert.AreEqual("LDA $1234", Text(dis, 0x8000, 0xAD, 0x34, 0x12));
            Assert.AreEqual("LDA $1234,x", Text(dis, 0x8000, 0xBD, 0x34, 0x12));
            Assert.AreEqual("LDA $1234,y", Text(dis, 0x8000, 0xB9, 0x34, 0x12));
            Assert.AreEqual("LDA ($10,x)", Text(dis, 0x8000, 0xA1, 0x10));
            Assert.AreEqual("LDA ($10),y", Text(dis, 0x8000, 0xB1, 0x10));
            Assert.AreEqual("JMP ($1234)", Text(dis, 0x8000, 0x6C, 0x34, 0x12));
            Assert.AreEqual("NOP", Text(dis, 0x8000, 0xEA));
            Assert.AreEqual("ASL", Text(dis, 0x8000, 0x0A), "l'accumulateur ne porte pas d'operande.");
        }

        [TestMethod]
        public void BranchRelatif_ZWCalculeLaCible()
        {
            Disassembler dis = new Disassembler(new Cpu6502());

            // BNE +$0A a $8000 -> cible = $8000 + 2 + 10 = $800C
            DisassembledInstruction ins = dis.Disassemble(0x8000, new byte[] { 0xD0, 0x0A }, 0);
            Assert.IsTrue(ins.IsKnown);
            Assert.AreEqual("BNE", ins.Mnemonic);
            Assert.AreEqual(0x800C, ins.TargetAddress.Value);
            Assert.AreEqual("BNE $800C", ins.Text);

            // BNE -$02 a $8000 -> cible = $8000 + 2 - 2 = $8000
            DisassembledInstruction back = dis.Disassemble(0x8000, new byte[] { 0xD0, 0xFE }, 0);
            Assert.AreEqual(0x8000, back.TargetAddress.Value, "le decalage est signe.");
        }

        [TestMethod]
        public void Extensions65C02SontDesassemblees()
        {
            Disassembler dis = new Disassembler(new Cpu65C02());
            Assert.AreEqual("STZ $10", Text(dis, 0x8000, 0x64, 0x10));
            Assert.AreEqual("BRA $800C", Text(dis, 0x8000, 0x80, 0x0A));
            Assert.AreEqual("LDA ($10)", Text(dis, 0x8000, 0xB2, 0x10));
            Assert.AreEqual("JMP ($1234,x)", Text(dis, 0x8000, 0x7C, 0x34, 0x12));
            Assert.AreEqual("BIT #$40", Text(dis, 0x8000, 0x89, 0x40));
            Assert.AreEqual("INC", Text(dis, 0x8000, 0x1A));
            Assert.AreEqual("PHX", Text(dis, 0x8000, 0xDA));
        }

        [TestMethod]
        public void LesOpcodes65C02SontInvisiblesSur6502()
        {
            Disassembler nmos = new Disassembler(new Cpu6502());
            DisassembledInstruction ins = nmos.Disassemble(0x8000, new byte[] { 0x64, 0x10 }, 0);

            // $64 n'existe pas en NMOS : le desassembleur doit le dire, pas inventer STZ.
            Assert.IsFalse(ins.IsKnown);
            Assert.AreEqual(".byte $64", ins.Text);
        }

        [TestMethod]
        public void AllerRetour_OctetsIdentiques()
        {
            byte[] original = Assemble(Programme).OutputBytes;

            Disassembler dis = new Disassembler(new Cpu6502());
            StringBuilder source = new StringBuilder();
            source.Append(".org $8000\n");

            int offset = 0;
            while (offset < original.Length)
            {
                DisassembledInstruction ins = dis.Disassemble((ushort)(0x8000 + offset), original, offset);
                source.Append(ins.Text).Append('\n');
                offset += ins.Length;
            }

            byte[] reassembled = Assemble(source.ToString()).OutputBytes;

            CollectionAssert.AreEqual(
                original, reassembled,
                "desassembler puis reassembler doit redonner exactement les memes octets.\nSource:\n" + source);
        }

        [TestMethod]
        public void AllerRetour_FormesZeroPageRestentStables()
        {
            // Une adresse zero-page reassemblee repasse par Absolute puis est reoptimisee
            // vers ZeroPage : le cycle doit etre fixe, sinon l'ecriture en memoire diverge.
            Disassembler dis = new Disassembler(new Cpu6502());
            byte[] bytes = Assemble(".org $8000\nlda $10\nsta $20\nldx $30\n").OutputBytes;

            DisassembledInstruction ins = dis.Disassemble(0x8000, bytes, 0);
            Assert.AreEqual("LDA $10", ins.Text);

            byte[] again = Assemble(".org $8000\n" + ins.Text + "\n").OutputBytes;
            CollectionAssert.AreEqual(new byte[] { 0xA5, 0x10 }, again);
        }

        [TestMethod]
        public void TamponTronque_NEstPasMasque()
        {
            Disassembler dis = new Disassembler(new Cpu6502());

            // Seuls 2 octets disponibles pour une instruction de 3 : le manque doit etre
            // visible, pas comble par des octets inventes.
            DisassembledInstruction ins = dis.Disassemble(0x8000, new byte[] { 0xAD, 0x34 }, 0);

            Assert.IsTrue(ins.IsKnown);
            Assert.AreEqual(3, ins.Length, "la longueur reelle est conservee.");
            Assert.AreEqual(2, ins.Bytes.Length, "seuls les octets disponibles sont rendus.");
        }

        private static string Text(Disassembler dis, ushort address, params byte[] bytes)
        {
            return dis.Disassemble(address, bytes, 0).Text;
        }

        private static void AssertCoherence(ICpuInstructionSet cpu)
        {
            Disassembler dis = new Disassembler(cpu);
            int checkedEntries = 0;

            foreach (KeyValuePair<string, byte[]> pair in cpu.InstructionTable)
            {
                byte[] row = pair.Value;
                for (int mode = 0; mode < row.Length; mode++)
                {
                    byte opcode = row[mode];
                    if (opcode == 0xFF)
                        continue;

                    OpcodeInfo info;
                    Assert.IsTrue(dis.TryLookup(opcode, out info),
                        "Opcode $" + opcode.ToString("X2") + " absent de l'index inverse.");

                    Assert.AreEqual(pair.Key.ToUpperInvariant(), info.Mnemonic,
                        "opcode $" + opcode.ToString("X2") + " : mnemonique errone.");
                    Assert.AreEqual((AddressingMode)mode, info.Mode,
                        "opcode $" + opcode.ToString("X2") + " : mode errone.");
                    Assert.AreEqual(Disassembler.ModeLength(info.Mode), info.Length);
                    checkedEntries++;
                }
            }

            Assert.IsTrue(checkedEntries > 100,
                "la table de " + cpu.GetType().Name + " ne contient que " + checkedEntries + " opcodes : suspicious.");
        }

        private static AssemblyResult Assemble(string content)
        {
            string dir = Path.Combine(Path.GetTempPath(), "WinASM65Disasm_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string source = Path.Combine(dir, "disasm.asm");
                File.WriteAllText(source, content);
                return new AssemblerEngine().Assemble(source, Path.Combine(dir, "disasm.o"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}