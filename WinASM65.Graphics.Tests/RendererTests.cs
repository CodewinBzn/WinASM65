using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Graphics.Assets;
using WinASM65.Graphics.Imaging;
using WinASM65.Graphics.Rendering;

namespace WinASM65.Graphics.Tests
{
    [TestClass]
    public class RendererTests
    {
        private static int Master(int index)
        {
            return TestKit.Nes.MasterPalette[index];
        }

        /// <summary>Tile 0 blank; tile 1 colour 2 but its left column colour 0; tile 2 colour 1 at the top left only.</summary>
        private static ChrFile Tiles()
        {
            ChrFile chr = new ChrFile(TestKit.Nes);
            chr.AddTile(TestKit.Solid(0));
            byte[] one = TestKit.Solid(2);
            for (int y = 0; y < 8; y++)
                one[y * 8] = 0;
            chr.AddTile(one);
            byte[] two = TestKit.Solid(0);
            two[0] = 1;
            chr.AddTile(two);
            return chr;
        }

        [TestMethod]
        public void ATileSheetShowsEachTileInThePalette()
        {
            RgbaImage sheet = AssetRenderer.RenderTileSheet(Tiles(), TestKit.Palettes(), 0);

            Assert.AreEqual(128, sheet.Width, "16 tiles per row");
            Assert.AreEqual(8, sheet.Height);
            Assert.AreEqual(Master(0x21), sheet.GetRgb(0, 0), "colour 0 of palette 0");
            Assert.AreEqual(Master(0x21), sheet.GetRgb(8, 3), "the left column of tile 1 is colour 0");
            Assert.AreEqual(Master(0x16), sheet.GetRgb(9, 3), "colour 2 of palette 0");
            Assert.AreEqual(Master(0x30), sheet.GetRgb(16, 0), "colour 1 of palette 0");
            Assert.AreEqual(255, sheet.GetAlpha(16, 0));

            RgbaImage scaled = AssetRenderer.RenderTileSheet(Tiles(), TestKit.Palettes(), 0, 3);
            Assert.AreEqual(384, scaled.Width);
            Assert.AreEqual(Master(0x30), scaled.GetRgb(48, 0));
            Assert.AreEqual(Master(0x30), scaled.GetRgb(50, 2), "a pixel is a 3x3 square");
            Assert.AreEqual(Master(0x21), scaled.GetRgb(51, 0));
        }

        [TestMethod]
        public void AScreenUsesItsAttributesAndTheUniversalBackground()
        {
            ScreenAsset screen = ScreenAsset.Blank(TestKit.Nes, "test");
            screen.Tiles[2] = 1;           // cell 2,0: block 1,0
            screen.Attributes[1] = 1;      // palette 1: 30 16 28 30
            screen.Tiles[32 * 29] = 2;     // the bottom left cell, palette 0

            RgbaImage image = AssetRenderer.RenderScreen(screen, Tiles(), TestKit.Palettes());

            Assert.AreEqual(256, image.Width);
            Assert.AreEqual(240, image.Height);
            Assert.AreEqual(Master(0x21), image.GetRgb(0, 0), "a blank tile shows the universal background");
            Assert.AreEqual(Master(0x21), image.GetRgb(16, 0), "colour 0 of palette 1 is the universal background, not $30");
            Assert.AreEqual(Master(0x28), image.GetRgb(17, 0), "colour 2 of palette 1");
            Assert.AreEqual(Master(0x30), image.GetRgb(0, 232), "colour 1 of palette 0");
        }

        [TestMethod]
        public void AMetaspriteIsDrawnOnTransparencyAroundItsOrigin()
        {
            MetaspriteAsset asset = new MetaspriteAsset();
            asset.Metasprites.Add(new Metasprite
            {
                Name = "ship",
                OriginX = 4,
                OriginY = 8,
                Tiles = { new MetaspriteTile { X = 0, Y = 0, Tile = 2, Palette = 1, FlipH = true } },
            });

            RenderResult result = AssetRenderer.RenderMetasprite(asset, "ship", Tiles(), TestKit.Palettes(), 1, false);

            Assert.AreEqual(8, result.Image.Width);
            Assert.AreEqual(9, result.Image.Height, "the tile and the origin below it");
            Assert.AreEqual(4, result.OriginX);
            Assert.AreEqual(8, result.OriginY);
            Assert.AreEqual(0, result.Image.GetAlpha(0, 0), "colour 0 of a sprite is transparent");
            Assert.AreEqual(Master(0x28), result.Image.GetRgb(7, 0), "flipped: colour 1 of sprite palette 1");
            Assert.AreEqual(255, result.Image.GetAlpha(7, 0));
        }

        [TestMethod]
        public void APaletteSwatchHasASquarePerColour()
        {
            RgbaImage swatch = AssetRenderer.RenderPaletteSwatch(TestKit.Palettes());
            Assert.AreEqual(64, swatch.Width);
            Assert.AreEqual(128, swatch.Height);
            Assert.AreEqual(Master(0x16), swatch.GetRgb(2 * 16 + 5, 5));
            Assert.AreEqual(Master(0x37), swatch.GetRgb(2 * 16, 7 * 16 + 15));
        }
    }
}
