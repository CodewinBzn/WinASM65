using System;
using System.IO;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Designer;
using WinASM65.Designer.Rpc;
using WinASM65.Graphics.Assets;
using WinASM65.Graphics.Imaging;

namespace WinASM65.Graphics.Tests
{
    /// <summary>The designer executable: its command line and its JSON-RPC server, both in memory.</summary>
    [TestClass]
    public class DesignerTests
    {
        private static int Run(out string output, out string error, params string[] args)
        {
            StringWriter outWriter = new StringWriter();
            StringWriter errWriter = new StringWriter();
            int code = Cli.Run(args, outWriter, errWriter);
            output = outWriter.ToString();
            error = errWriter.ToString();
            return code;
        }

        private static void WriteScreenProject(TempFolder folder)
        {
            ChrFile chr = new ChrFile(TestKit.Nes);
            chr.AddTile(TestKit.Solid(0));
            chr.AddTile(TestKit.Solid(1));
            chr.Save(folder.File("tiles.chr"));
            TestKit.Palettes().Save(folder.File("game.pal.json"));
            ScreenAsset screen = ScreenAsset.Blank(TestKit.Nes, "title");
            screen.Chr = "tiles.chr";
            screen.Palettes = "game.pal.json";
            screen.Tiles[0] = 1;
            Directory.CreateDirectory(folder.File("screens"));
            screen.Chr = "../tiles.chr";
            screen.Palettes = "../game.pal.json";
            screen.Save(folder.File(Path.Combine("screens", "title.screen.json")));
        }

        [TestMethod]
        public void ExportOfAFolderWritesEveryAsset()
        {
            using (TempFolder folder = new TempFolder())
            {
                WriteScreenProject(folder);

                int code = Run(out string output, out string error, "export", folder.Path);

                Assert.AreEqual(0, code, output + error);
                Assert.IsTrue(File.Exists(folder.File("game.pal.bin")));
                Assert.IsTrue(File.Exists(folder.File("game.pal.asm")));
                Assert.IsTrue(File.Exists(folder.File(Path.Combine("screens", "title.nam"))));
                Assert.IsTrue(File.Exists(folder.File(Path.Combine("screens", "title.screen.asm"))));
                StringAssert.Contains(output, "wrote " + folder.File("game.pal.asm"));
            }
        }

        [TestMethod]
        public void ExitCodesTellErrorsFromBadUsage()
        {
            using (TempFolder folder = new TempFolder())
            {
                File.WriteAllText(folder.File("bad.pal.json"), "{ \"version\": 1, \"system\": \"nes\", \"palettes\": [] }");

                Assert.AreEqual(1, Run(out string output, out _, "validate", folder.File("bad.pal.json")));
                StringAssert.Contains(output, "bad.pal.json: error: $.palettes: 0 palettes, expected 8");
                Assert.AreEqual(1, Run(out _, out _, "export", folder.File("bad.pal.json")));
                Assert.AreEqual(2, Run(out _, out _));
                Assert.AreEqual(2, Run(out _, out _, "paint"));
                Assert.AreEqual(2, Run(out _, out _, "render", folder.File("bad.pal.json")), "render needs -o");
                Assert.AreEqual(2, Run(out _, out _, "import", "x.png", "--mode", "tiles", "--out", folder.Path, "--name", "x"));
                Assert.AreEqual(0, Run(out string systems, out _, "systems"));
                Assert.AreEqual("nes", (string)JsonNode.Parse(systems)[0]["id"]);
            }
        }

        [TestMethod]
        public void RenderAndImportFromTheCommandLine()
        {
            using (TempFolder folder = new TempFolder())
            {
                WriteScreenProject(folder);
                string screen = folder.File(Path.Combine("screens", "title.screen.json"));

                Assert.AreEqual(0, Run(out string output, out string error, "render", screen, "-o", folder.File("title.png"), "--scale", "2"), output + error);
                RgbaImage image = PngReader.Load(folder.File("title.png"));
                Assert.AreEqual(512, image.Width);
                Assert.AreEqual(TestKit.Nes.MasterPalette[0x30], image.GetRgb(0, 0), "tile 1 in colour 1 of palette 0");

                // The render, imported back, gives the same two tiles in the tile set it came from.
                RgbaImage single = PngReader.Load(folder.File("title.png"));
                RgbaImage original = new RgbaImage(256, 240);
                for (int y = 0; y < 240; y++)
                {
                    for (int x = 0; x < 256; x++)
                        original.SetPixel(x, y, single.GetRgb(x * 2, y * 2));
                }
                PngWriter.Save(original, folder.File("original.png"));
                File.Copy(folder.File("tiles.chr"), folder.File("copy.chr"));

                int code = Run(out output, out error, "import", folder.File("original.png"), "--mode", "background",
                    "--out", folder.File("imported"), "--name", "title", "--chr", folder.File("copy.chr"));

                Assert.AreEqual(0, code, output + error);
                StringAssert.Contains(output, "0 new");
                AssetLoadResult<ScreenAsset> imported = ScreenAsset.Load(folder.File(Path.Combine("imported", "title.screen.json")));
                Assert.IsFalse(imported.HasErrors, TestKit.Describe(imported.Diagnostics));
                Assert.AreEqual("../copy.chr", imported.Asset.Chr);
                Assert.AreEqual(2, ChrFile.Load(folder.File("copy.chr")).TileCount);
            }
        }

        private static JsonObject Request(int id, string method, JsonObject parameters)
        {
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters };
        }

        [TestMethod]
        public void TheServerDescribesSystemsAndRendersAssets()
        {
            using (TempFolder folder = new TempFolder())
            {
                WriteScreenProject(folder);
                MemoryStream requests = new MemoryStream();
                MessageStream writer = new MessageStream(new MemoryStream(), requests);
                writer.Write(Request(1, "describe", new JsonObject { ["system"] = "nes" }));
                writer.Write(Request(2, "render", new JsonObject { ["path"] = folder.File("game.pal.json"), ["scale"] = 2 }));
                writer.Write(Request(3, "render", new JsonObject { ["path"] = folder.File("nothing.screen.json") }));
                writer.Write(Request(4, "frobnicate", new JsonObject()));
                writer.Write(Request(5, "shutdown", null));
                writer.Write(Request(6, "describe", new JsonObject()));
                requests.Position = 0;

                MemoryStream responses = new MemoryStream();
                DesignerServer server = new DesignerServer(new MessageStream(requests, responses));
                server.Run();
                responses.Position = 0;
                MessageStream reader = new MessageStream(responses, new MemoryStream());

                JsonObject describe = reader.Read();
                Assert.AreEqual(1, (int)describe["id"]);
                Assert.AreEqual("nes", (string)describe["result"]["id"]);
                Assert.AreEqual(8, (int)describe["result"]["tileWidth"]);
                Assert.AreEqual(4, (int)describe["result"]["spritePaletteCount"]);
                Assert.AreEqual(64, describe["result"]["masterPalette"].AsArray().Count);
                Assert.AreEqual("#7C7C7C", (string)describe["result"]["masterPalette"][0]);

                JsonObject render = reader.Read();
                Assert.AreEqual(2, (int)render["id"]);
                Assert.AreEqual(128, (int)render["result"]["width"]);
                Assert.AreEqual(256, (int)render["result"]["height"]);
                RgbaImage swatch = PngReader.Decode(Convert.FromBase64String((string)render["result"]["png"]));
                Assert.AreEqual(TestKit.Nes.MasterPalette[0x21], swatch.GetRgb(0, 0));

                JsonObject missing = reader.Read();
                Assert.AreEqual(DesignerServer.AssetError, (int)missing["error"]["code"]);
                Assert.IsTrue(missing["error"]["data"]["diagnostics"].AsArray().Count > 0);

                Assert.AreEqual(DesignerServer.MethodNotFound, (int)reader.Read()["error"]["code"]);
                JsonObject shutdown = reader.Read();
                Assert.AreEqual(5, (int)shutdown["id"]);
                Assert.IsTrue(shutdown.ContainsKey("result"));
                Assert.IsNull(reader.Read(), "nothing is answered after shutdown");
                Assert.IsTrue(server.ShutdownRequested);
            }
        }

        [TestMethod]
        public void TheServerLoadsValidatesAndExports()
        {
            using (TempFolder folder = new TempFolder())
            {
                WriteScreenProject(folder);
                DesignerServer server = new DesignerServer(new MessageStream(new MemoryStream(), new MemoryStream()));
                string screen = folder.File(Path.Combine("screens", "title.screen.json"));

                JsonObject load = server.Handle(Request(1, "load", new JsonObject { ["path"] = screen }));
                Assert.AreEqual("screen", (string)load["result"]["kind"]);
                Assert.AreEqual(960, load["result"]["asset"]["tiles"].AsArray().Count);
                Assert.AreEqual(0, load["result"]["diagnostics"].AsArray().Count);

                JsonObject validate = server.Handle(Request(2, "validate", new JsonObject { ["path"] = folder.File("tiles.chr") }));
                Assert.AreEqual(0, (int)validate["result"]["errors"]);

                JsonObject export = server.Handle(Request(3, "export", new JsonObject { ["path"] = screen }));
                Assert.AreEqual(2, export["result"]["written"].AsArray().Count);

                JsonObject bad = server.Handle(Request(4, "render", new JsonObject { ["path"] = 5 }));
                Assert.AreEqual(DesignerServer.InvalidParams, (int)bad["error"]["code"]);
            }
        }

        [TestMethod]
        public void TheServerFormatsAnAssetAsTheLibrarySavesIt()
        {
            using (TempFolder folder = new TempFolder())
            {
                WriteScreenProject(folder);
                DesignerServer server = new DesignerServer(new MessageStream(new MemoryStream(), new MemoryStream()));
                string screenPath = folder.File(Path.Combine("screens", "title.screen.json"));
                string saved = File.ReadAllText(screenPath);

                // An editor's copy of the asset, compact and with a change, comes back as the file the library writes.
                JsonNode asset = JsonNode.Parse(saved);
                asset["tiles"][1] = 1;
                JsonObject format = server.Handle(Request(1, "format", new JsonObject
                {
                    ["kind"] = "screen", ["asset"] = JsonNode.Parse(asset.ToJsonString()), ["path"] = screenPath,
                }));
                ScreenAsset expected = ScreenAsset.Load(screenPath).Asset;
                expected.Tiles[1] = 1;
                expected.Save(folder.File("expected.screen.json"));
                Assert.AreEqual(File.ReadAllText(folder.File("expected.screen.json")), (string)format["result"]["text"]);
                Assert.AreEqual(0, format["result"]["diagnostics"].AsArray().Count);

                JsonObject unchanged = server.Handle(Request(2, "format", new JsonObject { ["kind"] = "screen", ["asset"] = JsonNode.Parse(saved) }));
                Assert.AreEqual(saved, (string)unchanged["result"]["text"]);

                string palette = File.ReadAllText(folder.File("game.pal.json"));
                JsonObject pal = server.Handle(Request(3, "format", new JsonObject { ["kind"] = "palette", ["asset"] = JsonNode.Parse(palette) }));
                Assert.AreEqual(palette, (string)pal["result"]["text"]);

                MapAsset map = new MapAsset { Name = "level", Width = 3, Height = 2, Chr = "tiles.chr" };
                map.Metatiles.Add(new Metatile { Name = "sky", Tiles = { 0, 0, 0, 0 } });
                for (int i = 0; i < 6; i++)
                    map.Cells.Add(0);
                map.Save(folder.File("level.map.json"));
                string mapText = File.ReadAllText(folder.File("level.map.json"));
                JsonObject mapFormat = server.Handle(Request(4, "format", new JsonObject { ["kind"] = "map", ["asset"] = JsonNode.Parse(mapText) }));
                Assert.AreEqual(mapText, (string)mapFormat["result"]["text"]);

                JsonObject wrongKind = server.Handle(Request(5, "format", new JsonObject { ["kind"] = "chr", ["asset"] = new JsonObject() }));
                Assert.AreEqual(DesignerServer.InvalidParams, (int)wrongKind["error"]["code"]);
                JsonObject wrongShape = server.Handle(Request(6, "format", new JsonObject
                {
                    ["kind"] = "palette", ["asset"] = new JsonObject { ["palettes"] = "nope" },
                }));
                Assert.AreEqual(DesignerServer.InvalidParams, (int)wrongShape["error"]["code"]);
            }
        }
    }
}
