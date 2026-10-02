using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Monitor.Shell;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// The RAM pane's content, without a machine.
    ///
    /// Three things are asserted here that a hex dump gets wrong easily and that a
    /// user notices immediately: the three regions are distinguished, a byte that is
    /// not printable becomes a dot rather than a control character, and a short or
    /// refused read is shown as short or refused rather than padded with zeroes.
    /// </summary>
    [TestClass]
    public class RamDumpTests
    {
        [TestMethod]
        public void TheWindowIsTheFirstTwoKilobytes()
        {
            Assert.AreEqual(0x0000, RamDump.WindowStart);
            Assert.AreEqual(0x07FF, RamDump.WindowEnd);
            Assert.AreEqual(0x800, RamDump.WindowLength);
        }

        [TestMethod]
        public void TheThreeRegionsAreMarked()
        {
            Assert.AreEqual(RamRegion.ZeroPage, RamDump.RegionOf(0x0000));
            Assert.AreEqual(RamRegion.ZeroPage, RamDump.RegionOf(0x00FF));
            Assert.AreEqual(RamRegion.Stack, RamDump.RegionOf(0x0100));
            Assert.AreEqual(RamRegion.Stack, RamDump.RegionOf(0x01FF));
            Assert.AreEqual(RamRegion.Free, RamDump.RegionOf(0x0200));
            Assert.AreEqual(RamRegion.Free, RamDump.RegionOf(0x07FF));
        }

        [TestMethod]
        public void ExactlyThreeAddressesStartARegion()
        {
            int starts = 0;
            for (int address = RamDump.WindowStart; address <= RamDump.WindowEnd; address++)
            {
                if (RamDump.StartsRegion(address))
                    starts++;
            }

            Assert.AreEqual(3, starts);
        }

        [TestMethod]
        public void ARegionNamesItsOwnRange()
        {
            StringAssert.Contains(RamDump.DescribeRegion(RamRegion.ZeroPage), "$0000-$00FF");
            StringAssert.Contains(RamDump.DescribeRegion(RamRegion.Stack), "$0100-$01FF");
            StringAssert.Contains(RamDump.DescribeRegion(RamRegion.Free), "$0200-$07FF");
        }

        [TestMethod]
        public void ARowCarriesItsAddressBytesAndCharacters()
        {
            byte[] memory = new byte[16];
            memory[0] = 0x41; // 'A'
            memory[1] = 0x00; // not printable
            memory[15] = 0x7A; // 'z'

            IReadOnlyList<RamDumpRow> rows = RamDump.Rows(memory, 0x0000);

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(0x0000, rows[0].Address);
            Assert.AreEqual(16, rows[0].Bytes.Count);

            string text = rows[0].Text;
            StringAssert.Contains(text, "$0000");
            StringAssert.Contains(text, "41 00");
            StringAssert.Contains(text, "A..");
            StringAssert.Contains(text, "z");
        }

        [TestMethod]
        public void AControlByteBecomesADotAndNeverReachesTheTerminal()
        {
            byte[] memory = new byte[] { 0x07, 0x1B, 0x7F };

            string text = RamDump.Rows(memory, 0x0000)[0].Text;

            // BEL, ESC and DEL would each change what is on screen if they were
            // written out; the character column exists to be readable, not faithful.
            Assert.AreEqual(1, text.IndexOf("07 1B 7F", StringComparison.Ordinal) >= 0 ? 1 : 0);
            StringAssert.Contains(text, "|...|");
            Assert.AreEqual(-1, text.IndexOf('\u0007'));
            Assert.AreEqual(-1, text.IndexOf('\u001b'));
        }

        [TestMethod]
        public void AShortSnapshotIsDrawnShortRatherThanPaddedWithZeroes()
        {
            // Padding would be indistinguishable from real zeroes, which is exactly
            // the kind of plausible wrong display this project refuses.
            IReadOnlyList<RamDumpRow> rows = RamDump.Rows(new byte[] { 0xAA, 0xBB }, 0x0000);

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(2, rows[0].Bytes.Count);
            StringAssert.Contains(rows[0].Text, "AA BB");
            Assert.AreEqual(-1, rows[0].Text.IndexOf("00 00", StringComparison.Ordinal));
        }

        [TestMethod]
        public void RowsAreSixteenBytesAndStopAtTheEndOfTheWindow()
        {
            byte[] memory = new byte[RamDump.WindowLength + 64];

            IReadOnlyList<RamDumpRow> rows = RamDump.Rows(memory, RamDump.WindowStart);

            Assert.AreEqual(RamDump.WindowLength / RamDump.BytesPerRow, rows.Count);
            Assert.AreEqual(0x07F0, rows[rows.Count - 1].Address);
        }

        [TestMethod]
        public void TheWholeWindowIsOneRowPerSixteenBytes()
        {
            IReadOnlyList<RamDumpRow> rows = RamDump.Window(delegate (int address, int length)
            {
                byte[] memory = new byte[length];
                for (int i = 0; i < length; i++)
                    memory[i] = (byte)(address + i);
                return memory;
            });

            Assert.AreEqual(RamDump.WindowLength / RamDump.BytesPerRow, rows.Count);
            foreach (RamDumpRow row in rows)
                Assert.AreEqual(RamDump.BytesPerRow, row.Bytes.Count);
        }

        [TestMethod]
        public void ARefusedReadBecomesAFaultRatherThanAnException()
        {
            IReadOnlyList<RamDumpRow> rows = RamDump.Window(delegate (int address, int length)
            {
                if (address >= 0x0200)
                    throw new MonitorException("this machine has no RAM there");
                byte[] memory = new byte[length];
                Array.Copy(memory, memory, 0);
                return memory;
            });

            // The rows below $0200 still come back; the refusal does not take the
            // pane, or the rows that were fine, with it.
            Assert.AreEqual(RamDump.WindowLength / RamDump.BytesPerRow, rows.Count);

            int faults = 0;
            foreach (RamDumpRow row in rows)
            {
                if (row.Bytes.Count == 0)
                    faults++;
            }

            Assert.AreEqual((RamDump.WindowEnd - 0x0200 + 1) / RamDump.BytesPerRow, faults);
        }

        [TestMethod]
        public void ANullReaderIsAnEmptyDumpRatherThanAFault()
        {
            Assert.AreEqual(0, RamDump.Window(null).Count);
            Assert.AreEqual(0, RamDump.Rows(null, 0).Count);
        }

        [TestMethod]
        public void AnAddressOutsideTheWindowIsFreeRatherThanGuessed()
        {
            Assert.AreEqual(RamRegion.Free, RamDump.RegionOf(0x8000));
            Assert.AreEqual(RamRegion.Free, RamDump.RegionOf(0xFFFF));
        }
    }
}