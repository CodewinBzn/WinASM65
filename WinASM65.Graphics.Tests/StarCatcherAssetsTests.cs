using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Graphics.Assets;
using WinASM65.Graphics.Export;

namespace WinASM65.Graphics.Tests
{
    /// <summary>
    /// The example game's graphics are designer assets whose exports are committed,
    /// so that it builds without the designer. Those files must be what an export
    /// of the assets gives today: an asset edited without exporting would build a
    /// game that does not match its own sources.
    /// </summary>
    [TestClass]
    public class StarCatcherAssetsTests
    {
        private static string Example()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WinASM65.sln")))
                dir = dir.Parent;
            Assert.IsNotNull(dir, "the repository root was not found");
            return Path.Combine(dir.FullName, "example_star_catcher");
        }

        [TestMethod]
        public void EveryAssetIsValid()
        {
            string gfx = Path.Combine(Example(), "gfx");
            string[] assets = Directory.GetFiles(gfx).Where(f => AssetKinds.Of(f) != AssetKind.Unknown).ToArray();
            Assert.AreEqual(5, assets.Length, "palettes, metasprites, two screens and a map");
            foreach (string asset in assets)
            {
                AssetDocument document = AssetDocument.Load(asset);
                Assert.IsFalse(document.Diagnostics.Any(), Path.GetFileName(asset) + ": " + TestKit.Describe(document.Diagnostics));
            }
        }

        [TestMethod]
        public void TheCommittedExportsAreFresh()
        {
            string example = Example();
            using (TempFolder temp = new TempFolder())
            {
                Directory.CreateDirectory(temp.File("gfx"));
                Directory.CreateDirectory(temp.File("chr"));
                File.Copy(Path.Combine(example, "chr", "tiles.chr"), temp.File(Path.Combine("chr", "tiles.chr")));
                foreach (string asset in Directory.GetFiles(Path.Combine(example, "gfx"), "*.json"))
                    File.Copy(asset, temp.File(Path.Combine("gfx", Path.GetFileName(asset))));

                ExportResult result = AssetExporter.ExportAll(temp.File("gfx"));
                Assert.IsFalse(result.HasErrors, TestKit.Describe(result.Diagnostics));
                Assert.AreEqual(8, result.Written.Count);

                foreach (string written in result.Written)
                {
                    string committed = Path.Combine(example, "gfx", Path.GetFileName(written));
                    Assert.IsTrue(File.Exists(committed), Path.GetFileName(written) + " is not committed");
                    if (written.EndsWith(".asm", StringComparison.OrdinalIgnoreCase))
                    {
                        // Text: the same lines, whatever line ends git checked it out with.
                        Assert.AreEqual(File.ReadAllText(written).Replace("\r\n", "\n"),
                            File.ReadAllText(committed).Replace("\r\n", "\n"),
                            Path.GetFileName(written) + " is stale: export the assets of example_star_catcher/gfx");
                    }
                    else
                    {
                        CollectionAssert.AreEqual(File.ReadAllBytes(written), File.ReadAllBytes(committed),
                            Path.GetFileName(written) + " is stale: export the assets of example_star_catcher/gfx");
                    }
                }
            }
        }
    }
}
