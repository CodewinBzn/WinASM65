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
        public void TheWindowIsTheFirstSixteenKilobytes()
        {
            Assert.AreEqual(0x0000, RamDump.WindowStart);
            Assert.AreEqual(0x3FFF, RamDump.WindowEnd);
            Assert.AreEqual(0x4000, RamDump.WindowLength);
        }

        [TestMethod]
        public void TheThreeRegionsAreMarked()
        {
            Assert.AreEqual(RamRegion.ZeroPage, RamDump.RegionOf(0x0000));
            Assert.AreEqual(RamRegion.ZeroPage, RamDump.RegionOf(0x00FF));
            Assert.AreEqual(RamRegion.Stack, RamDump.RegionOf(0x0100));
            Assert.AreEqual(RamRegion.Stack, RamDump.RegionOf(0x01FF));
            Assert.AreEqual(RamRegion.Free, RamDump.RegionOf(0x0200));
            Assert.AreEqual(RamRegion.Free, RamDump.RegionOf(0x3FFF));
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
            StringAssert.Contains(RamDump.DescribeRegion(RamRegion.Free), "$0200-$3FFF");
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
            Assert.AreEqual(0x3FF0, rows[rows.Count - 1].Address);
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
                // The bridge refuses by length: a read that would run past the end of
                // the machine's memory is an error, not a short answer. That is the
                // case the window asks about first, and it is what sends it back to
                // asking row by row — which is the behaviour under test here.
                if (address + length > 0x3E00)
                    throw new MonitorException("this machine has no RAM there");
                byte[] memory = new byte[length];
                Array.Copy(memory, memory, 0);
                return memory;
            });

            // The rows below $3E00 still come back; the refusal does not take the
            // pane, or the rows that were fine, with it.
            Assert.AreEqual(RamDump.WindowLength / RamDump.BytesPerRow, rows.Count);

            int faults = 0;
            foreach (RamDumpRow row in rows)
            {
                if (row.Bytes.Count == 0)
                    faults++;
            }

            Assert.AreEqual((RamDump.WindowEnd - 0x3E00 + 1) / RamDump.BytesPerRow, faults);
        }

        /// <summary>
        /// The window is asked for in one piece, not one row at a time.
        ///
        /// A row at a time cost one blocking round trip to the bridge per row — 1024
        /// of them for this window, on the thread that draws — so the shell froze for
        /// the length of the read. The window is 16 KiB and the bridge caps a read at
        /// 4 KiB, so it takes four reads at most: one per chunk, and one more only if
        /// a refusal sends it back to the row-by-row probe.
        /// </summary>
        [TestMethod]
        public void TheWindowIsAskedForInOnePiece()
        {
            int reads = 0;

            IReadOnlyList<RamDumpRow> rows = RamDump.Window(delegate (int address, int length)
            {
                reads++;
                return new byte[length];
            });

            Assert.AreEqual(RamDump.WindowLength / RamDump.BytesPerRow, rows.Count);
            Assert.IsTrue(reads <= 4,
                "the window asked the bridge " + reads + " times for " + rows.Count + " rows");
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

        /// <summary>
        /// A push of byte changes is applied to the rows that hold them, and only
        /// those rows come back. A one-byte change redraws one row, not the
        /// whole window — which is the point of pushing rather than polling.
        /// </summary>
        [TestMethod]
        public void ApplyChangesUpdatesOnlyTheRowsThatChanged()
        {
            IReadOnlyList<RamDumpRow> rows = RamDump.Window(delegate (int address, int length)
            {
                return new byte[length];
            });

            List<(int Address, byte Value)> changes = new List<(int Address, byte Value)>
            {
                (0x0042, 0xA9), // row 4, col 2
                (0x0043, 0xA9), // same row
                (0x0200, 0xEA), // row 32, col 0
            };

            IReadOnlyList<RamDumpRow> changed = RamDump.ApplyChanges(rows, changes);

            Assert.AreEqual(2, changed.Count, "two rows changed, not three");
            Assert.AreEqual(0x0040, changed[0].Address);
            Assert.AreEqual(0x0200, changed[1].Address);
            Assert.AreEqual(0xA9, changed[0].Bytes[2]);
            Assert.AreEqual(0xA9, changed[0].Bytes[3]);
            Assert.AreEqual(0xEA, changed[1].Bytes[0]);
        }

        /// <summary>
        /// A change outside the window is ignored: the pane shows $0000-$3FFF and
        /// has no row for an address above it.
        /// </summary>
        [TestMethod]
        public void ApplyChangesIgnoresChangesOutsideTheWindow()
        {
            IReadOnlyList<RamDumpRow> rows = RamDump.Window(delegate (int address, int length)
            {
                return new byte[length];
            });

            List<(int Address, byte Value)> changes = new List<(int Address, byte Value)>
            {
                (0x4000, 0xFF),
                (0xFFFF, 0xFF),
            };

            IReadOnlyList<RamDumpRow> changed = RamDump.ApplyChanges(rows, changes);

            Assert.AreEqual(0, changed.Count);
        }

        /// <summary>
        /// An empty push changes nothing and returns nothing.
        /// </summary>
        [TestMethod]
        public void ApplyChangesWithNoChangesReturnsNothing()
        {
            IReadOnlyList<RamDumpRow> rows = RamDump.Window(delegate (int address, int length)
            {
                return new byte[length];
            });

            Assert.AreEqual(0, RamDump.ApplyChanges(rows, new List<(int Address, byte Value)>()).Count);
        }
    }
}