using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Graphics.Assets;
using WinASM65.Graphics.Import;
using WinASM65.Graphics.Imaging;

namespace WinASM65.Graphics.Tests
{
    [TestClass]
    public class ImporterTests
    {
        private static int Master(int index)
        {
            return TestKit.Nes.MasterPalette[index];
        }

        private static RgbaImage Filled(int width, int height, int rgb, byte alpha = 255)
        {
            RgbaImage image = new RgbaImage(width, height);
            image.Fill(0, 0, width, height, rgb, alpha);
            return image;
        }

        private static ImportResult Import(RgbaImage image, ImportMode mode)
        {
            return PngImporter.Import(image, new ImportOptions { Mode = mode, Name = "pic" }, "pic.png");
        }

        [TestMethod]
        public void ColoursMatchTheNearestSelectableMasterColour()
        {
            Assert.AreEqual(0x0F, PngImporter.NearestColor(TestKit.Nes, 0x000000), "black is $0F, never $0D");
            Assert.AreEqual(0x16, PngImporter.NearestColor(TestKit.Nes, 0xF83802));
            Assert.AreEqual(0x30, PngImporter.NearestColor(TestKit.Nes, 0xFFFFFF));
        }

        [TestMethod]
        public void ABlockWithTooManyColoursIsReducedWithAWarning()
        {
            RgbaImage image = Filled(16, 16, 0x000000);
            int[] colours = { 0x16, 0x28, 0x11, 0x1A, 0x2C };
            for (int i = 0; i < colours.Length; i++)
                image.Fill(i * 3, 4, 2, 2, Master(colours[i]));

            ImportResult result = Import(image, ImportMode.Background);

            Assert.IsFalse(result.HasErrors, TestKit.Describe(result.Diagnostics));
            AssetDiagnostic warning = result.Diagnostics.Single();
            StringAssert.Contains(warning.Message, "block 0,0 (x=0, y=0) has 5 colours besides the background; reduced to the 3 most frequent");
            CollectionAssert.AreEqual(new[] { 0x0F, 0x11, 0x16, 0x1A }, result.Palettes.Palettes[0].Colors,
                "the background, then the three kept colours (equal counts: lowest index first)");
            Assert.AreEqual(0, result.Screen.Attributes[0]);
            Assert.AreEqual(1, result.Statistics.Palettes);
            Assert.AreEqual(1, result.Statistics.Warnings);
            Assert.AreEqual(8, result.Palettes.Palettes.Count);
            Assert.AreEqual(0, result.Palettes.Validate("pic.pal.json").Count);
            Assert.AreEqual(0, result.Screen.Validate("pic.screen.json").Count);
        }

        [TestMethod]
        public void BlocksBeyondFourPalettesUseTheNearestWithAWarning()
        {
            RgbaImage image = Filled(80, 16, 0x000000);
            int[] colours = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x11, 0x12, 0x13 };
            for (int block = 0; block < 5; block++)
            {
                for (int c = 0; c < 3; c++)
                    image.Fill(block * 16 + c * 4, 0, 4, 4, Master(colours[block * 3 + c]));
            }

            ImportResult result = Import(image, ImportMode.Background);

            Assert.AreEqual(4, result.Statistics.Palettes);
            AssetDiagnostic warning = result.Diagnostics.Single();
            StringAssert.Contains(warning.Message, "block 4,0 (x=64, y=0) needs colours no palette has room for");
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, result.Screen.Attributes.Take(4).ToArray());
        }

        [TestMethod]
        public void RepeatedBackgroundTilesAreStoredOnce()
        {
            RgbaImage image = Filled(32, 16, 0x000000);
            int[][] cells = { new[] { 0, 0 }, new[] { 1, 0 }, new[] { 2, 0 }, new[] { 3, 0 }, new[] { 0, 1 } };
            foreach (int[] cell in cells)
            {
                image.SetPixel(cell[0] * 8, cell[1] * 8, Master(0x16));
                image.SetPixel(cell[0] * 8 + 3, cell[1] * 8 + 5, Master(0x16));
            }

            ImportResult result = Import(image, ImportMode.Background);

            Assert.AreEqual(2, result.Chr.TileCount, "the pattern and the blank tile");
            Assert.AreEqual(8, result.Statistics.Cells);
            Assert.AreEqual(2, result.Statistics.NewTiles);
            Assert.AreEqual(7, result.Statistics.ReusedTiles,
                "the blank tile is first made for the screen right of the picture, so every blank cell of the picture reuses it");
            Assert.AreEqual(result.Screen.Tiles[0], result.Screen.Tiles[3]);
            Assert.AreEqual(result.Screen.Tiles[0], result.Screen.Tiles[32]);
            Assert.AreEqual(result.Screen.Tiles[33], result.Screen.Tiles[959], "the screen beyond the picture is blank");
        }

        [TestMethod]
        public void TilesAlreadyInTheTileSetAreReused()
        {
            ChrFile chr = new ChrFile(TestKit.Nes);
            chr.AddTile(TestKit.Solid(3));
            chr.AddTile(TestKit.Solid(0));

            ImportResult result = PngImporter.Import(Filled(8, 8, 0x000000), new ImportOptions { Name = "blank", Chr = chr });

            Assert.AreSame(chr, result.Chr);
            Assert.AreEqual(2, chr.TileCount, "the blank tile was there");
            Assert.AreEqual(1, result.Screen.Tiles[0]);
            Assert.AreEqual(0, result.Statistics.NewTiles);
        }

        [TestMethod]
        public void SpriteTilesAreDeduplicatedWithTheirFlips()
        {
            RgbaImage image = Filled(16, 16, 0, 0);
            int red = Master(0x16);
            int[][] shape = { new[] { 0, 0 }, new[] { 1, 0 }, new[] { 0, 1 } };
            foreach (int[] p in shape)
            {
                image.SetPixel(p[0], p[1], red);                 // as drawn
                image.SetPixel(15 - p[0], p[1], red);            // mirrored left to right
                image.SetPixel(p[0], 15 - p[1], red);            // upside down
                image.SetPixel(15 - p[0], 15 - p[1], red);       // both
            }

            ImportResult result = Import(image, ImportMode.Sprites);

            Assert.IsFalse(result.HasErrors, TestKit.Describe(result.Diagnostics));
            Assert.AreEqual(1, result.Chr.TileCount);
            Assert.AreEqual(1, result.Statistics.NewTiles);
            Assert.AreEqual(3, result.Statistics.ReusedTiles);
            Metasprite sprite = result.Metasprites.Metasprites.Single();
            Assert.AreEqual("pic", sprite.Name);
            Assert.AreEqual(8, sprite.OriginX);
            Assert.AreEqual(8, sprite.OriginY);
            string flips = string.Join(" ", sprite.Tiles.Select(t => t.X + "," + t.Y + (t.FlipH ? "H" : "") + (t.FlipV ? "V" : "")));
            Assert.AreEqual("0,0 8,0H 0,8V 8,8HV", flips);
            Assert.IsTrue(sprite.Tiles.All(t => t.Tile == 0 && t.Palette == 0));
            CollectionAssert.AreEqual(new[] { 0x0F, 0x16, 0x0F, 0x0F }, result.Palettes.Palettes[4].Colors, "sprite palette 0");
            Assert.AreEqual(0, result.Metasprites.Validate("pic.msprite.json").Count);
        }

        [TestMethod]
        public void EmptySpriteCellsAreLeftOut()
        {
            RgbaImage image = Filled(16, 8, 0, 0);
            image.SetPixel(9, 2, Master(0x28));

            ImportResult result = Import(image, ImportMode.Sprites);

            Assert.AreEqual(1, result.Metasprites.Metasprites[0].Tiles.Count);
            Assert.AreEqual(8, result.Metasprites.Metasprites[0].Tiles[0].X);
        }

        [TestMethod]
        public void ABackgroundImageOfTheWrongSizeIsAnError()
        {
            ImportResult result = Import(Filled(12, 8, 0), ImportMode.Background);
            Assert.IsTrue(result.HasErrors);
            Assert.IsNull(result.Screen);
            StringAssert.Contains(result.Diagnostics[0].Message, "multiple of 8x8");
        }
    }
}
