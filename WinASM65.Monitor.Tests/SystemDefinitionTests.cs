using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Monitor.Abstractions;
using WinASM65.Systems;

namespace WinASM65.Monitor.Tests
{
    [TestClass]
    public class SystemDefinitionTests
    {
        private static byte[] Ines(bool trainer)
        {
            byte[] image = new byte[16 + (trainer ? 512 : 0) + 16];
            image[0] = (byte)'N';
            image[1] = (byte)'E';
            image[2] = (byte)'S';
            image[3] = 0x1A;
            image[6] = (byte)(trainer ? 0x04 : 0x00);
            return image;
        }

        [TestMethod]
        public void TheNesIsKnownByItsIdsAndByItsHeader()
        {
            Assert.IsInstanceOfType(SystemRegistry.For("nes"), typeof(NesSystem));
            Assert.IsInstanceOfType(SystemRegistry.For("FAMICOM"), typeof(NesSystem));
            Assert.IsInstanceOfType(SystemRegistry.Detect(Ines(false)), typeof(NesSystem));
            Assert.IsNull(SystemRegistry.Detect(new byte[16]), "a headerless image names no machine");
        }

        [TestMethod]
        public void TheNesHeaderIsSixteenBytesOrMoreWithATrainer()
        {
            NesSystem nes = new NesSystem();
            Assert.AreEqual(16, nes.ImageHeaderLength(Ines(false)));
            Assert.AreEqual(16 + 512, nes.ImageHeaderLength(Ines(true)));
            Assert.AreEqual(0, nes.ImageHeaderLength(new byte[40]), "no header, nothing to skip");
        }

        [TestMethod]
        public void TheNesDescribesItsVideoMemoriesAndItsColours()
        {
            NesSystem nes = new NesSystem();
            CollectionAssert.AreEqual(new[] { "PPU", "PAL", "OAM", "CHR" }, nes.MemorySpaces.Select(s => s.Name).ToArray());
            Assert.AreEqual(32, nes.SpaceNamed("pal").Length);
            Assert.AreEqual(64, nes.MasterPalette.Count);
            Assert.AreEqual(BootKind.ResetVector, nes.Boot);
            Assert.AreEqual("RAM", nes.Views[0].Name);
            Assert.AreEqual(0x0800, nes.Views[0].Length, "the first view is the 2 KB of work RAM");
            Assert.IsTrue(nes.Views.All(v => v.Space == SystemDefinition.CpuSpace || nes.SpaceNamed(v.Space) != null),
                "every view is on a memory the system has");
        }

        [TestMethod]
        public void AnyOtherSystemIsAGeneric6502ThatKeepsItsId()
        {
            SystemDefinition c64 = SystemRegistry.For("c64");
            Assert.IsInstanceOfType(c64, typeof(Generic6502System));
            Assert.AreEqual("c64", c64.SystemId);
            Assert.AreEqual(0, c64.MemorySpaces.Count, "no video memory is invented");
            Assert.AreEqual(BootKind.LoadAndRun, c64.Boot);
            Assert.AreEqual(0x10000, c64.AddressSpace.Single().Length);
            Assert.IsNull(SystemRegistry.For(null));
            Assert.IsNull(SystemRegistry.For("raw"), "a project that names no system says nothing about the machine");
        }

        [TestMethod]
        public void TheProjectSystemWinsThenTheBridgeThenTheImage()
        {
            Assert.AreEqual("c64", SystemRegistry.Resolve("c64", "nes", Ines(false)).SystemId);
            Assert.AreEqual("nes", SystemRegistry.Resolve(null, "nes", new byte[0]).SystemId);
            Assert.AreEqual("nes", SystemRegistry.Resolve(null, null, Ines(false)).SystemId);
            Assert.IsInstanceOfType(SystemRegistry.Resolve(null, null, new byte[0]), typeof(Generic6502System));
        }

        [TestMethod]
        public void ADefinitionIsAProfileOfTheContract()
        {
            ISystemProfile profile = new NesSystem();
            Assert.AreEqual("nes", profile.SystemId);
            Assert.AreEqual(4, profile.MemorySpaces.Count);
            Assert.AreEqual(4, profile.NameTables.Count);
        }
    }
}
