using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WinASM65.Monitor.Abstractions.Tests
{
    /// <summary>
    /// The data shapes a plugin fills in, exercised through a fake that declares the
    /// same memory-only profile MesenCE and MAME measured.
    ///
    /// The point of <see cref="MemoryRegion.IsWritable"/> is that one
    /// <see cref="ExecutionCapability.MemoryWrite"/> bit cannot describe what
    /// MesenCE 2.2.1 actually does: <c>emu.write</c> exists, RAM takes the byte, and
    /// PRG ROM drops it without a word. A machine therefore has both a writable and a
    /// read-only region, and the host needs to tell them apart before it offers to
    /// edit either.
    /// </summary>
    [TestClass]
    public class ContractDataTests
    {
        private const int PrgRegion = 0;
        private const int RamRegion = 1;

        private static RegionAddress Ram(int offset)
        {
            return new RegionAddress(RamRegion, 0, offset);
        }

        private static RegionAddress Prg(int offset)
        {
            return new RegionAddress(PrgRegion, 0, offset);
        }

        [TestMethod]
        public void ARegionStatesWhereItSitsHowLongItIsAndWhetherItTakesWrites()
        {
            MemoryRegion prg = new FakePlugin().Regions[PrgRegion];

            Assert.AreEqual("PRG", prg.Name);
            Assert.AreEqual(0x8000, prg.StartAddress);
            Assert.AreEqual(0x4000, prg.Length);
            Assert.IsFalse(prg.IsWritable);
            Assert.AreEqual(1, prg.BankCount);
        }

        [TestMethod]
        public void AWriteIntoAReadOnlyRegionIsRefusedRatherThanAccepted()
        {
            using (var plugin = new FakePlugin())
            {
                // MesenCE's measured behaviour, refused by name. An adapter that took
                // the byte and dropped it would be reporting a write that never landed,
                // which is the one failure this project refuses to perform.
                Assert.ThrowsException<System.NotSupportedException>(
                    () => plugin.Write(Prg(0), new byte[] { 0xA9, 0x42 }));
            }
        }

        [TestMethod]
        public void AWriteIntoAWritableRegionIsReadableBack()
        {
            using (var plugin = new FakePlugin())
            {
                plugin.Write(Ram(0x0000), new byte[] { 0x01, 0x02, 0x03 });
                CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, 0x03 }, plugin.Read(Ram(0x0000), 3));

                Assert.AreEqual(1, plugin.WrittenAddresses.Count);
                Assert.AreEqual(0x0000, plugin.WrittenAddresses[0]);
            }
        }

        [TestMethod]
        public void AnAddressCarriesRegionBankAndOffsetWithoutFlatteningThem()
        {
            // Whether $8000 is bank 3 of PRG or the start of CHR is the mapper's
            // business. The contract carries all three numbers and decides none of it.
            var where = new RegionAddress(2, 3, 0x0100);

            Assert.AreEqual(2, where.Region);
            Assert.AreEqual(3, where.Bank);
            Assert.AreEqual(0x0100, where.Offset);
        }

        [TestMethod]
        public void ACycleCounterThatOutgrewThirtyTwoBitsIsStillCarried()
        {
            // Mesen2 passes 43 million cycles while paused, and a real session runs to
            // tens of millions more. A 32-bit counter would wrap long before the user
            // stopped for the night.
            var cpu = new CpuState(0xC01B, 0x42, 0x00, 0x00, 0xFD, 0x24, 4294967296L);

            Assert.AreEqual(4294967296L, cpu.CycleCount);
            Assert.AreEqual(0x42, cpu.A);
            Assert.AreEqual(0xC01B, cpu.Pc);
        }
    }
}