using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Graphics.Assets;
using WinASM65.Graphics.Systems;

namespace WinASM65.Graphics.Tests
{
    [TestClass]
    public class TileTests
    {
        private static byte[] Draw(params string[] rows)
        {
            byte[] pixels = new byte[64];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                    pixels[y * 8 + x] = (byte)(rows[y][x] == '.' ? 0 : rows[y][x] - '0');
            }
            return pixels;
        }

        [TestMethod]
        public void AKnownTileHasTheBytesOfTheTwoPlanes()
        {
            byte[] pixels = Draw(
                "1.......",
                ".2......",
                "........",
                "........",
                "........",
                "........",
                "........",
                ".......3");

            byte[] data = TestKit.Nes.EncodeTile(pixels);

            Assert.AreEqual("80 00 00 00 00 00 00 01 00 40 00 00 00 00 00 01", TestKit.Hex(data),
                "plane 0 (bit 0 of each pixel) then plane 1, the leftmost pixel in the high bit");
        }

        [TestMethod]
        public void EveryTileSurvivesAnEncodeAndADecode()
        {
            Random random = new Random(1234);
            for (int n = 0; n < 50; n++)
            {
                byte[] pixels = new byte[64];
                for (int i = 0; i < 64; i++)
                    pixels[i] = (byte)random.Next(4);
                byte[] data = TestKit.Nes.EncodeTile(pixels);
                CollectionAssert.AreEqual(pixels, TestKit.Nes.DecodeTile(data, 0));
            }
        }

        [TestMethod]
        public void TheStarCatcherTilesDecodeAndEncodeToTheSameFile()
        {
            string chr = Path.Combine(RepositoryRoot(), "example_star_catcher", "chr", "tiles.chr");
            if (!File.Exists(chr))
                Assert.Inconclusive("the example's tile set is not there");
            byte[] original = File.ReadAllBytes(chr);
            ChrFile file = ChrFile.Load(chr);
            Assert.AreEqual(original.Length / 16, file.TileCount);

            ChrFile copy = new ChrFile(TestKit.Nes);
            for (int t = 0; t < file.TileCount; t++)
                copy.AddTile(file.GetTile(t));
            CollectionAssert.AreEqual(original, copy.ToArray());
        }

        [TestMethod]
        public void AChrFileGrowsWhenATileIsSetPastItsEnd()
        {
            ChrFile chr = new ChrFile(TestKit.Nes);
            chr.SetTile(3, TestKit.Solid(2));
            Assert.AreEqual(4, chr.TileCount);
            Assert.AreEqual(64, chr.ToArray().Length);
            CollectionAssert.AreEqual(TestKit.Solid(0), chr.GetTile(0));
            CollectionAssert.AreEqual(TestKit.Solid(2), chr.GetTile(3));
            Assert.AreEqual(3, chr.FindTile(TestKit.Solid(2)));
            chr.Grow(2);
            Assert.AreEqual(6, chr.TileCount);
        }

        [TestMethod]
        public void UnknownSystemsAreNull()
        {
            Assert.IsNull(GraphicsSystems.For("c64"));
            Assert.AreSame(GraphicsSystems.For("nes"), GraphicsSystems.For("NES"));
            Assert.AreEqual(64, GraphicsSystems.For("famicom").MasterPalette.Count);
        }

        internal static string RepositoryRoot()
        {
            string directory = AppContext.BaseDirectory;
            while (directory != null && !File.Exists(Path.Combine(directory, "WinASM65.sln")))
                directory = Path.GetDirectoryName(directory);
            return directory ?? throw new InvalidOperationException("WinASM65.sln not found above " + AppContext.BaseDirectory);
        }
    }
}
