using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Cpu;
using static WinASM65.Tests.SyntaxTestKit;

namespace WinASM65.Tests
{
    /// <summary>
    /// The instruction sets beyond the documented NMOS one: the stable illegal opcodes
    /// (6502x) and the Rockwell/WDC bit instructions (r65c02, w65c02).
    /// </summary>
    [TestClass]
    public class CpuVariantTests
    {
        [TestMethod]
        public void TheCpuNamesAreKnown()
        {
            Assert.IsInstanceOfType(CpuFactory.Create("6502x"), typeof(Cpu6502X));
            Assert.IsInstanceOfType(CpuFactory.Create("r65c02"), typeof(CpuR65C02));
            Assert.IsInstanceOfType(CpuFactory.Create("W65C02S"), typeof(CpuW65C02));
            Assert.IsInstanceOfType(CpuFactory.Create("65c02"), typeof(Cpu65C02));
            Assert.AreEqual(5, CpuFactory.All.Count);
        }

        [TestMethod]
        public void IllegalOpcodesAssembleOnThe6502X()
        {
            AreBytes("A7 10 B7 10 BF 34 12 87 10 97 10 CB 05 0B 01 4B 02 6B 03 BB 34 12",
                Bytes(".org $8000\n lax $10\n lax $10,y\n lax $1234,y\n sax $10\n sax $10,y\n axs #5\n anc #1\n alr #2\n arr #3\n las $1234,y", "6502x"));
        }

        [TestMethod]
        public void IscAbsoluteXIsTheOpcodeFF()
        {
            AreBytes("FF 34 12 C7 10 03 10 13 10", Bytes(".org $8000\n isc $1234,x\n dcp $10\n slo ($10,x)\n slo ($10),y", "6502x"));
        }

        [TestMethod]
        public void NopTakesOperandsAndJamHalts()
        {
            AreBytes("EA 80 01 04 10 14 10 0C 34 12 1C 34 12 02",
                Bytes(".org $8000\n nop\n nop #1\n nop $10\n nop $10,x\n nop $1234\n nop $1234,x\n jam", "6502x"));
        }

        [TestMethod]
        public void IllegalOpcodesAreRejectedOnTheDocumented6502()
        {
            StringAssert.Contains(Errors(".org $8000\n lax $10"), "Unknown instruction or macro 'lax'");
            StringAssert.Contains(Errors(".org $8000\n nop #1"), "NOP does not support immediate");
        }

        [TestMethod]
        public void ABitBranchBackwardEncodesItsDisplacementFromTheEnd()
        {
            AreBytes("0F 10 FD", Bytes(".org $8000\nLoop: bbr0 $10,Loop", "r65c02"));
        }

        [TestMethod]
        public void ABitBranchForwardIsPatched()
        {
            AreBytes("FF 20 01 EA", Bytes(".org $8000\n bbs7 zp,Done\n nop\nDone:\nzp = $20", "r65c02"));
        }

        [TestMethod]
        public void ABitBranchTooFarIsReported()
        {
            StringAssert.Contains(Errors(".org $8000\n bbr1 $10,far\n .rep 200\n nop\n .endrep\nfar: rts", "r65c02"),
                "Relative jump is too big");
        }

        [TestMethod]
        public void ABitBranchOnAWideAddressIsReported()
        {
            StringAssert.Contains(Errors(".org $8000\nL: bbr2 $1234,L", "r65c02"), "out of range for a 1-byte operand");
        }

        [TestMethod]
        public void BitSetAndResetAreZeroPage()
        {
            AreBytes("07 10 F7 10 37 20", Bytes(".org $8000\n rmb0 $10\n smb7 $10\n rmb3 later\nlater = $20", "r65c02"));
        }

        [TestMethod]
        public void StopAndWaitAreWdcOnly()
        {
            AreBytes("DB CB", Bytes(".org $8000\n stp\n wai", "w65c02"));
            StringAssert.Contains(Errors(".org $8000\n stp", "r65c02"), "Unknown instruction or macro 'stp'");
        }

        [TestMethod]
        public void BitInstructionsAreRejectedOnThePlain65C02()
        {
            StringAssert.Contains(Errors(".org $8000\n rmb0 $10", "65c02"), "Unknown instruction or macro 'rmb0'");
        }

        [TestMethod]
        public void TheDisassemblerShowsTheBitBranchTarget()
        {
            Disassembler dis = new Disassembler(new CpuR65C02());
            DisassembledInstruction instruction = dis.Disassemble(0x8000, new byte[] { 0x0F, 0x10, 0xFD }, 0);
            Assert.AreEqual("BBR0 $10,$8000", instruction.Text);
            Assert.AreEqual((ushort)0x8000, instruction.TargetAddress);
        }

        [TestMethod]
        public void FFIsAnOpcodeAgainOnBothNewCpus()
        {
            Assert.AreEqual("ISC $1234,X", new Disassembler(new Cpu6502X()).Disassemble(0x8000, new byte[] { 0xFF, 0x34, 0x12 }, 0).Text.ToUpperInvariant());
            StringAssert.StartsWith(new Disassembler(new CpuW65C02()).Disassemble(0x8000, new byte[] { 0xFF, 0x10, 0x00 }, 0).Text, "BBS7");
        }
    }
}
