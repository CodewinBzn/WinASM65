using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using static WinASM65.Tests.SyntaxTestKit;

namespace WinASM65.Tests
{
    /// <summary>Data, layout and diagnostic directives.</summary>
    [TestClass]
    public class DataDirectiveTests
    {
        [TestMethod]
        public void AsciizEndsWithAZero()
        {
            AreBytes("48 49 21 00", Bytes(".org $8000\n .asciiz \"HI\", '!'"));
        }

        [TestMethod]
        public void LoBytesAndHiBytesSplitAnAddressTable()
        {
            AreBytes("34 78 12 56", Bytes(".org $8000\n .lobytes $1234, $5678\n .hibytes $1234, $5678"));
        }

        [TestMethod]
        public void LoBytesAcceptAForwardReference()
        {
            AreBytes("02 80 60", Bytes(".org $8000\n .lobytes target\n .hibytes target\ntarget: rts"));
        }

        [TestMethod]
        public void DsbFillsWithZeroOrAValue()
        {
            AreBytes("00 00 00 EA EA FF", Bytes(".org $8000\n .dsb 3\n .ds 2, $EA\n .fill 1, $FF"));
        }

        [TestMethod]
        public void DswFillsWords()
        {
            AreBytes("34 12 34 12", Bytes(".org $8000\n .dsw 2, $1234"));
        }

        [TestMethod]
        public void AnUnlabelledResAdvancesTheReservationArea()
        {
            AreBytes("A5 12", Bytes(".memarea $10\n .res 2\nvar .res 1\n.org $8000\n lda var"));
        }

        [TestMethod]
        public void PadFillsUpToAnAddress()
        {
            AreBytes("EA FF FF 60", Bytes(".org $8000\n nop\n .pad $8003, $FF\n rts"));
        }

        [TestMethod]
        public void PadBackwardsIsReported()
        {
            StringAssert.Contains(Errors(".org $8000\n nop\n nop\n .pad $8001"), ".pad target $8001 is below the current address $8002");
        }

        [TestMethod]
        public void AlignReachesThePowerOfTwo()
        {
            AreBytes("EA 00 00 00 60", Bytes(".org $8000\n nop\n .align 4\n rts"));
            AreBytes("60", Bytes(".org $8000\n .align 256\n rts"));
        }

        [TestMethod]
        public void AlignRefusesAnythingButAPowerOfTwo()
        {
            StringAssert.Contains(Errors(".org $8000\n .align 3"), ".align requires a positive power of two, got 3");
        }

        [TestMethod]
        public void AFillCountMustBeKnownNow()
        {
            StringAssert.Contains(Errors(".org $8000\n .dsb later\nlater = 2"), "a count known at this point");
        }

        [TestMethod]
        public void IncbinTakesAnOffsetAndALength()
        {
            string dir = Path.Combine(Path.GetTempPath(), "winasm65-incbin-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllBytes(Path.Combine(dir, "data.bin"), new byte[] { 1, 2, 3, 4, 5 });
                string source = Path.Combine(dir, "main.asm");
                File.WriteAllText(source, ".org $8000\n .incbin \"data.bin\", 1, 2\n .incbin \"data.bin\", 3\n .incbin \"data.bin\", 4, 9\n");
                AssemblyResult result = new AssemblerEngine().Assemble(source, Path.Combine(dir, "main.o"));
                AreBytes("02 03 04 05", result.OutputBytes);
                StringAssert.Contains(Describe(result), ".incbin offset/length outside 'data.bin' (5 bytes)");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void ErrorStopsTheBuildAndWarningDoesNot()
        {
            StringAssert.Contains(Errors(".org $8000\n .error \"not on this target\""), "not on this target");

            AssemblyResult warned = Assemble(".org $8000\n .warning \"slow path\"\n nop");
            Assert.IsTrue(warned.Success, Describe(warned));
            StringAssert.Contains(Describe(warned), "slow path");
        }

        [TestMethod]
        public void AnErrorInADeadBranchIsNotReported()
        {
            AreBytes("EA", Bytes(".org $8000\n.if 0\n .error \"never\"\n.endif\n nop"));
        }

        [TestMethod]
        public void AssertChecksNowOrOnceTheSymbolIsKnown()
        {
            AreBytes("EA", Bytes(".org $8000\n .assert 1 == 1\n .assert size < 10, \"too big\"\n nop\nsize = 4"));
            StringAssert.Contains(Errors(".org $8000\n .assert size < 10, \"too big\"\n nop\nsize = 40"), "Assertion failed: too big");
            StringAssert.Contains(Errors(".org $8000\n .assert 2 > 3"), "Assertion failed: 2 > 3");
        }
    }
}
