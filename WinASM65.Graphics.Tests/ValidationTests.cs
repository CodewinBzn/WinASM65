using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Graphics.Assets;

namespace WinASM65.Graphics.Tests
{
    [TestClass]
    public class ValidationTests
    {
        private static string[] Paths(AssetLoadResult<PaletteAsset> result)
        {
            return result.Diagnostics.Select(d => d.Path).ToArray();
        }

        [TestMethod]
        public void PaletteProblemsAreReportedAtTheirPath()
        {
            using (TempFolder folder = new TempFolder())
            {
                string path = folder.File("bad.pal.json");
                File.WriteAllText(path, "{ \"version\": 1, \"system\": \"nes\", \"name\": \"bad\", \"palettes\": ["
                    + "{ \"name\": \"a\", \"colors\": [15, 64, 0, 0] },"
                    + "{ \"name\": \"A\", \"colors\": [15, 1, 2] } ] }");

                AssetLoadResult<PaletteAsset> result = PaletteAsset.Load(path);

                Assert.IsNotNull(result.Asset);
                Assert.IsTrue(result.HasErrors);
                CollectionAssert.AreEqual(new[] { "$.palettes", "$.palettes[0].colors[1]", "$.palettes[1].name", "$.palettes[1].colors" }, Paths(result),
                    TestKit.Describe(result.Diagnostics));
                StringAssert.Contains(result.Diagnostics[1].Message, "colour 64 is out of range 0..63");
            }
        }

        [TestMethod]
        public void BrokenJsonAndUnknownSystemsAreDiagnosticsNotExceptions()
        {
            using (TempFolder folder = new TempFolder())
            {
                string broken = folder.File("broken.pal.json");
                File.WriteAllText(broken, "{ \"version\": 1, \"palettes\": [ { \"colors\": \"red\" } ] }");
                AssetLoadResult<PaletteAsset> parsed = PaletteAsset.Load(broken);
                Assert.IsNull(parsed.Asset);
                Assert.AreEqual("$.palettes[0].colors", parsed.Diagnostics.Single().Path);

                string other = folder.File("other.screen.json");
                File.WriteAllText(other, "{ \"version\": 2, \"system\": \"c64\", \"name\": \"x\" }");
                AssetLoadResult<ScreenAsset> screen = ScreenAsset.Load(other);
                CollectionAssert.AreEqual(new[] { "$.version", "$.system" }, screen.Diagnostics.Select(d => d.Path).ToArray());

                AssetDocument missing = AssetDocument.Load(folder.File("none.map.json"));
                Assert.IsTrue(missing.HasErrors);
                Assert.IsNull(missing.Asset);
            }
        }

        [TestMethod]
        public void TilesAreCheckedAgainstTheTileSetTheAssetNames()
        {
            using (TempFolder folder = new TempFolder())
            {
                ChrFile chr = new ChrFile(TestKit.Nes);
                chr.Grow(2);
                chr.Save(folder.File("tiles.chr"));
                MetaspriteAsset asset = new MetaspriteAsset { Chr = "tiles.chr", Palettes = "missing.pal.json" };
                asset.Metasprites.Add(new Metasprite
                {
                    Name = "hero",
                    OriginY = 200,
                    Tiles = { new MetaspriteTile { Tile = 5, Palette = 4 }, new MetaspriteTile { Tile = 1, Y = 72 } },
                });
                asset.Animations.Add(new MetaspriteAnimation { Name = "walk", Frames = { new AnimationFrame { Metasprite = "villain", Duration = 0 } } });
                string path = folder.File("hero.msprite.json");
                asset.Save(path);

                AssetLoadResult<MetaspriteAsset> result = MetaspriteAsset.Load(path);

                string described = TestKit.Describe(result.Diagnostics);
                CollectionAssert.AreEqual(new[]
                {
                    "$.palettes",
                    "$.metasprites[0].tiles[0].tile",
                    "$.metasprites[0].tiles[0].palette",
                    "$.metasprites[0].tiles[0].y",
                    "$.metasprites[0].tiles[1].y",
                    "$.animations[0].frames[0].metasprite",
                    "$.animations[0].frames[0].duration",
                }, result.Diagnostics.Select(d => d.Path).ToArray(), described);
                Assert.IsFalse(result.Diagnostics[0].IsError, "a missing palette file is a warning");
                StringAssert.Contains(described, "tile 5 is beyond the tile set (2 tiles)");
                StringAssert.Contains(described, "reads as the end marker $80");
            }
        }

        [TestMethod]
        public void ScreenAndMapLengthsAreChecked()
        {
            ScreenAsset screen = ScreenAsset.Blank(TestKit.Nes, "s");
            screen.Tiles.RemoveAt(0);
            screen.Attributes[3] = 4;
            screen.Compression = "zip";
            CollectionAssert.AreEqual(new[] { "$.compression", "$.tiles", "$.attributes[3]" },
                screen.Validate("s.screen.json").Select(d => d.Path).ToArray());

            MapAsset map = new MapAsset { Name = "m", Width = 2, Height = 1, Metatiles = { new Metatile { Tiles = { 0, 0, 0 } } }, Cells = { 0, 1 } };
            CollectionAssert.AreEqual(new[] { "$.metatiles[0].tiles", "$.cells[1]" },
                map.Validate("m.map.json").Select(d => d.Path).ToArray());
        }

        [TestMethod]
        public void SavedJsonIsStableAndReadsBack()
        {
            using (TempFolder folder = new TempFolder())
            {
                string path = folder.File("game.pal.json");
                TestKit.Palettes().Save(path);
                string text = File.ReadAllText(path);

                StringAssert.StartsWith(text, "{\n  \"version\": 1,\n  \"system\": \"nes\",\n  \"name\": \"game\",\n  \"prefix\": \"\",\n  \"palettes\": [\n");
                StringAssert.Contains(text, "    { \"name\": \"bg0\", \"colors\": [33, 48, 22, 40] },\n");
                Assert.IsTrue(text.EndsWith("}\n") && !text.Contains("\r"));

                PaletteAsset.Load(path).Asset.Save(path);
                Assert.AreEqual(text, File.ReadAllText(path));

                string screen = folder.File("s.screen.json");
                ScreenAsset.Blank(TestKit.Nes, "s").Save(screen);
                string[] lines = File.ReadAllLines(screen);
                Assert.AreEqual(30, lines.Count(l => l.StartsWith("    0, 0") && l.Split(',').Length >= 32), "one line per row of 32 tiles");
            }
        }
    }
}
