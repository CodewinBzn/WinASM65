using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Tooling.Mcp;

namespace WinASM65.Tooling.Tests
{
    /// <summary>
    /// The NES knowledge tools of the MCP server (nes_reference, nes_search,
    /// nes_snippet, new_nes_project) over the repository's knowledge/nes.
    /// </summary>
    [TestClass]
    public class KnowledgeToolsTests
    {
        private static string KnowledgeRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WinASM65.sln")))
                dir = dir.Parent;
            Assert.IsNotNull(dir, "the repository root was not found");
            return Path.Combine(dir.FullName, "knowledge", "nes");
        }

        private sealed class Harness : IDisposable
        {
            private readonly List<JsonObject> _sent = new List<JsonObject>();
            private int _id;

            public Harness()
            {
                Directory = Path.Combine(Path.GetTempPath(), "winasm65-kt-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(Directory);
                KnowledgeBase knowledge = KnowledgeBase.Load(KnowledgeRoot());
                Server = new McpServer(CodeTools.All().Concat(KnowledgeTools.All(() => knowledge)).ToList(), m => { lock (_sent) _sent.Add(m); });
            }

            public string Directory { get; }
            public McpServer Server { get; }

            public JsonObject Send(string method, JsonObject parameters = null)
            {
                int id = ++_id;
                Server.Handle(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters ?? new JsonObject() });
                lock (_sent)
                    return _sent.Single(m => (int?)m["id"] == id);
            }

            public (string Text, bool Error) Call(string tool, JsonObject arguments)
            {
                JsonObject result = (JsonObject)Send("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = arguments })["result"];
                string text = string.Join("\n", ((JsonArray)result["content"]).Select(c => (string)c["text"]));
                return (text, (bool)result["isError"]);
            }

            public void Dispose()
            {
                try { System.IO.Directory.Delete(Directory, true); } catch (IOException) { }
            }
        }

        [TestMethod]
        public void TheToolsAreListedAndTheInstructionsPointAtThem()
        {
            using (Harness h = new Harness())
            {
                JsonObject init = (JsonObject)h.Send("initialize", new JsonObject { ["protocolVersion"] = "2025-06-18" })["result"];
                StringAssert.Contains((string)init["instructions"], "nes_reference");
                StringAssert.Contains((string)init["instructions"], "build_project after every change");

                JsonArray tools = (JsonArray)h.Send("tools/list")["result"]["tools"];
                string[] names = tools.Select(t => (string)t["name"]).ToArray();
                CollectionAssert.IsSubsetOf(new[] { "nes_reference", "nes_search", "nes_snippet", "new_nes_project" }, names);
                Assert.IsTrue((bool)tools.First(t => (string)t["name"] == "nes_reference")["annotations"]["readOnlyHint"]);
                Assert.IsFalse((bool)tools.First(t => (string)t["name"] == "new_nes_project")["annotations"]["readOnlyHint"]);
            }
        }

        [TestMethod]
        public void ReferenceFindsAPageByNameOrKeyword()
        {
            using (Harness h = new Harness())
            {
                var exact = h.Call("nes_reference", new JsonObject { ["topic"] = "ppu-registers" });
                Assert.IsFalse(exact.Error);
                StringAssert.Contains(exact.Text, "[ppu-registers.md]");
                StringAssert.Contains(exact.Text, "PPUCTRL $2000");

                var spaced = h.Call("nes_reference", new JsonObject { ["topic"] = "PPU registers.md" });
                StringAssert.Contains(spaced.Text, "[ppu-registers.md]", "case, spaces and extension do not matter");

                var keyword = h.Call("nes_reference", new JsonObject { ["topic"] = "sprites" });
                StringAssert.Contains(keyword.Text, "[oam-sprites.md]");

                var joypad = h.Call("nes_reference", new JsonObject { ["topic"] = "joypad buttons" });
                StringAssert.Contains(joypad.Text, "[controllers.md]");

                var unknown = h.Call("nes_reference", new JsonObject { ["topic"] = "zzzz" });
                Assert.IsTrue(unknown.Error);
                StringAssert.Contains(unknown.Text, "apu-sound");
                StringAssert.Contains(unknown.Text, "read_joypad", "the snippets are named too");
            }
        }

        [TestMethod]
        public void SearchReturnsSectionsWithTheirFile()
        {
            using (Harness h = new Harness())
            {
                var attributes = h.Call("nes_search", new JsonObject { ["query"] = "attribute table bits" });
                Assert.IsFalse(attributes.Error);
                StringAssert.Contains(attributes.Text, "=== ppu-memory.md > Attribute table");
                Assert.IsTrue(attributes.Text.Split('\n').Length < 3 * 45, "three excerpts of at most 40 lines");

                var scroll = h.Call("nes_search", new JsonObject { ["query"] = "PPUSCROLL" });
                StringAssert.Contains(scroll.Text, "PPUSCROLL $2005");

                var sound = h.Call("nes_search", new JsonObject { ["query"] = "beep sound" });
                StringAssert.Contains(sound.Text, "pulse");

                var nothing = h.Call("nes_search", new JsonObject { ["query"] = "qqqqq" });
                StringAssert.Contains(nothing.Text, "Nothing matches");
            }
        }

        [TestMethod]
        public void SnippetReturnsTheSourceOrTheList()
        {
            using (Harness h = new Harness())
            {
                var joypad = h.Call("nes_snippet", new JsonObject { ["name"] = "read_joypad" });
                Assert.IsFalse(joypad.Error);
                StringAssert.Contains(joypad.Text, "read_joypad:");
                StringAssert.Contains(joypad.Text, "; vars: buttons 1, prev_buttons 1, pressed 1");

                var partial = h.Call("nes_snippet", new JsonObject { ["name"] = "joypad" });
                StringAssert.Contains(partial.Text, "read_joypad:", "a part of the name is enough when it is unique");

                var dashed = h.Call("nes_snippet", new JsonObject { ["name"] = "Load-Palettes" });
                StringAssert.Contains(dashed.Text, "load_palettes:");

                var unknown = h.Call("nes_snippet", new JsonObject { ["name"] = "fly_to_the_moon" });
                Assert.IsTrue(unknown.Error);
                StringAssert.Contains(unknown.Text, "pulse_blip - ");

                var list = h.Call("nes_snippet", new JsonObject());
                Assert.IsFalse(list.Error);
                StringAssert.Contains(list.Text, "nmi_oam_dma - ");
            }
        }

        [TestMethod]
        public void NewProjectCopiesTheTemplateWhichBuilds()
        {
            using (Harness h = new Harness())
            {
                string folder = Path.Combine(h.Directory, "my game");
                var created = h.Call("new_nes_project", new JsonObject { ["folder"] = folder, ["name"] = "space race" });
                Assert.IsFalse(created.Error, created.Text);
                StringAssert.Contains(created.Text, "build_project");
                Assert.IsTrue(File.Exists(Path.Combine(folder, "src", "game.asm")));
                Assert.IsTrue(File.Exists(Path.Combine(folder, "chr", "tiles.chr")));
                Assert.IsFalse(System.IO.Directory.EnumerateFiles(folder, "*.o", SearchOption.AllDirectories).Any(), "no build outputs are copied");

                var build = h.Call("build_project", new JsonObject { ["config"] = Path.Combine(folder, "config.json") });
                Assert.IsFalse(build.Error, build.Text);
                StringAssert.Contains(build.Text, "space_race.nes (24592 bytes)");

                var again = h.Call("new_nes_project", new JsonObject { ["folder"] = folder });
                Assert.IsTrue(again.Error, "a folder that is not empty is refused");
                StringAssert.Contains(again.Text, "not empty");
            }
        }

        [TestMethod]
        public void TheKnowledgeBaseIsFoundFromTheBuildOutput()
        {
            string found = KnowledgeBase.Find();
            Assert.IsNotNull(found, "copied next to the executable, or reached by walking up to the repository");
            Assert.IsTrue(File.Exists(Path.Combine(found, "index.md")));
            KnowledgeBase knowledge = KnowledgeBase.Load(found);
            Assert.IsTrue(knowledge.Pages.Count >= 13);
            Assert.IsTrue(knowledge.Snippets.Count >= 15);
            Assert.IsTrue(knowledge.Snippets.All(s => s.Purpose.Length > 0), "every snippet has a purpose line");
        }
    }
}
