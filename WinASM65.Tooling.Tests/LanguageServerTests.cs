using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Tooling.Lsp;

namespace WinASM65.Tooling.Tests
{
    [TestClass]
    public class LanguageServerTests
    {
        private sealed class Harness : IDisposable
        {
            private readonly BlockingCollection<JsonObject> _messages = new BlockingCollection<JsonObject>();
            private readonly List<JsonObject> _backlog = new List<JsonObject>();
            private readonly Thread _loop;
            private int _id;

            public Harness()
            {
                Directory = Path.Combine(Path.GetTempPath(), "winasm65-lsp-test-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(Directory);
                Server = new LanguageServer(m => _messages.Add(m));
                _loop = new Thread(Server.Run) { IsBackground = true };
                _loop.Start();
                Request("initialize", new JsonObject { ["rootUri"] = new Uri(Directory + Path.DirectorySeparatorChar).AbsoluteUri });
            }

            public string Directory { get; }
            public LanguageServer Server { get; }

            public string Write(string name, string text)
            {
                string path = Path.Combine(Directory, name);
                File.WriteAllText(path, text);
                return path;
            }

            public static string Uri(string path) => new Uri(path).AbsoluteUri;

            public JsonObject Request(string method, JsonObject parameters)
            {
                int id = Interlocked.Increment(ref _id);
                Server.Dispatch(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
                return Take(m => m["id"] != null && (int)m["id"] == id, "no reply to " + method);
            }

            public void Notify(string method, JsonObject parameters)
            {
                Server.Dispatch(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });
            }

            public void Open(string path)
            {
                Notify("textDocument/didOpen", new JsonObject
                {
                    ["textDocument"] = new JsonObject { ["uri"] = Uri(path), ["languageId"] = "winasm65", ["version"] = 1, ["text"] = File.ReadAllText(path) }
                });
            }

            public void Change(string path, string text)
            {
                Notify("textDocument/didChange", new JsonObject
                {
                    ["textDocument"] = new JsonObject { ["uri"] = Uri(path), ["version"] = 2 },
                    ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = text })
                });
            }

            /// <summary>The next diagnostics published for <paramref name="path"/>.</summary>
            public JsonArray Diagnostics(string path)
            {
                string uri = Uri(path);
                JsonObject published = Take(m => (string)m["method"] == "textDocument/publishDiagnostics"
                    && (string)m["params"]["uri"] == uri, "no diagnostics for " + Path.GetFileName(path));
                return (JsonArray)published["params"]["diagnostics"];
            }

            private JsonObject Take(Func<JsonObject, bool> match, string failure)
            {
                int index = _backlog.FindIndex(m => match(m));
                if (index >= 0)
                {
                    JsonObject kept = _backlog[index];
                    _backlog.RemoveAt(index);
                    return kept;
                }
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                while (true)
                {
                    int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                    JsonObject message = null;
                    Assert.IsTrue(left > 0 && _messages.TryTake(out message, left),
                        failure + "; received: " + string.Join(" | ", _backlog.Select(m => m.ToJsonString())));
                    if (match(message))
                        return message;
                    _backlog.Add(message);
                }
            }

            public void Dispose()
            {
                Server.Dispose();
                try { System.IO.Directory.Delete(Directory, true); } catch (IOException) { }
            }
        }

        [TestMethod]
        public void AWrongMnemonicIsReportedOnItsLineAndClearedWhenFixed()
        {
            using (Harness h = new Harness())
            {
                string source = h.Write("alone.asm", ".org $8000\n  lda #$01\n  bogus $10\n");
                h.Open(source);

                JsonArray diagnostics = h.Diagnostics(source);
                Assert.AreEqual(1, diagnostics.Count);
                Assert.AreEqual(2, (int)diagnostics[0]["range"]["start"]["line"], "0-based: the third line");
                Assert.AreEqual(1, (int)diagnostics[0]["severity"]);

                h.Change(source, ".org $8000\n  lda #$01\n  rts\n");
                Assert.AreEqual(0, h.Diagnostics(source).Count, "the fix clears the error without a save");
            }
        }

        [TestMethod]
        public void ANameAnotherUnitDefinesIsNotAnError()
        {
            using (Harness h = new Harness())
            {
                string main = h.Write("main.asm", ".org $8000\n  jsr helper\n  rts\n");
                h.Write("lib.asm", ".org $9000\nhelper:\n  rts\n");
                h.Write("config.json", "{ \"Input\": [ { \"FileName\": \"lib.asm\", \"Dependencies\": [] },"
                    + " { \"FileName\": \"main.asm\", \"Dependencies\": [\"lib.asm\"] } ],"
                    + " \"Output\": { \"ObjectFile\": \"out.bin\", \"Files\": [ { \"FileName\": \"main.o\" } ] } }");

                h.Open(main);
                JsonArray first = h.Diagnostics(main);
                Assert.AreEqual(0, first.Count, "helper is defined in lib.asm: " + first.ToJsonString());

                h.Change(main, ".org $8000\n  jsr helpr\n  rts\n");
                JsonArray typo = h.Diagnostics(main);
                Assert.AreEqual(1, typo.Count);
                Assert.AreEqual(1, (int)typo[0]["range"]["start"]["line"]);
            }
        }

        [TestMethod]
        public void AnErrorInAnIncludedFileIsReportedOnThatFile()
        {
            using (Harness h = new Harness())
            {
                string main = h.Write("main.asm", ".org $8000\n  .include \"inc.asm\"\n  rts\n");
                string include = h.Write("inc.asm", "  lda #$01\n  bogus\n");
                h.Write("config.json", "{ \"Input\": [ { \"FileName\": \"main.asm\", \"Dependencies\": [] } ],"
                    + " \"Output\": { \"ObjectFile\": \"out.bin\", \"Files\": [ { \"FileName\": \"main.o\" } ] } }");

                h.Open(main);
                JsonArray onInclude = h.Diagnostics(include);
                Assert.AreEqual(1, onInclude.Count);
                Assert.AreEqual(1, (int)onInclude[0]["range"]["start"]["line"]);
            }
        }

        [TestMethod]
        public void HoverShowsTheAssembledBytesAndTheInstructionForms()
        {
            using (Harness h = new Harness())
            {
                string source = h.Write("alone.asm", ".org $8000\n  lda #$5A\n  rts\n");
                h.Open(source);
                h.Diagnostics(source);

                JsonObject hover = h.Request("textDocument/hover", new JsonObject
                {
                    ["textDocument"] = new JsonObject { ["uri"] = Harness.Uri(source) },
                    ["position"] = new JsonObject { ["line"] = 1, ["character"] = 3 }
                });
                string text = (string)hover["result"]["contents"]["value"];
                StringAssert.Contains(text, "**LDA**");
                StringAssert.Contains(text, "$A9");
                StringAssert.Contains(text, "`$8000`  A9 5A");
            }
        }

        private static JsonArray Listing(Harness h, string path)
        {
            JsonObject reply = h.Request("winasm65/listing", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = Harness.Uri(path) }
            });
            return (JsonArray)reply["result"]["rows"];
        }

        [TestMethod]
        public void TheListingColumnFollowsTheUnsavedText()
        {
            using (Harness h = new Harness())
            {
                string source = h.Write("alone.asm", ".org $8000\n  lda #$5A\n  rts\n");
                h.Open(source);
                h.Diagnostics(source);

                JsonArray rows = Listing(h, source);
                Assert.AreEqual(2, rows.Count);
                Assert.AreEqual(1, (int)rows[0]["line"]);
                Assert.AreEqual(0x8000, (int)rows[0]["address"]);
                Assert.AreEqual("A9 5A", (string)rows[0]["bytes"]);
                Assert.AreEqual("60", (string)rows[1]["bytes"]);

                h.Change(source, ".org $8000\n  nop\n  lda #$5A\n  rts\n");
                rows = Listing(h, source);
                Assert.AreEqual(3, rows.Count);
                Assert.AreEqual(0x8001, (int)rows[1]["address"], "the edit moved lda one byte on");
            }
        }

        [TestMethod]
        public void AnIncludedFileIsListedFromTheBuild()
        {
            using (Harness h = new Harness())
            {
                string main = h.Write("main.asm", ".org $8000\n  nop\n  .include \"inc.asm\"\n  rts\n");
                string include = h.Write("inc.asm", "; helper\n  lda #$01\n");
                h.Write("config.json", "{ \"Input\": [ { \"FileName\": \"main.asm\", \"Dependencies\": [] } ],"
                    + " \"Output\": { \"ObjectFile\": \"out.bin\", \"Files\": [ { \"FileName\": \"main.o\" } ] } }");
                h.Open(main);
                h.Diagnostics(main);

                JsonArray rows = Listing(h, include);
                Assert.AreEqual(1, rows.Count);
                Assert.AreEqual(1, (int)rows[0]["line"]);
                Assert.AreEqual(0x8001, (int)rows[0]["address"]);
                Assert.AreEqual("A9 01", (string)rows[0]["bytes"]);

                JsonArray mine = Listing(h, main);
                CollectionAssert.AreEqual(new[] { 1, 3 }, mine.Select(r => (int)r["line"]).ToArray(),
                    "the unit's column skips the lines its include emitted");
            }
        }

        [TestMethod]
        public void UrisFromVsCodeBecomePathsAndBack()
        {
            string path = LanguageServer.PathOf("file:///c%3A/Projects/x/prog.asm");
            Assert.AreEqual(@"c:\Projects\x\prog.asm", path, true);
            Assert.AreEqual(LanguageServer.PathOf(LanguageServer.UriOf(path)), path, true);
        }

        [TestMethod]
        public void TheWordUnderTheCursorIgnoresPunctuation()
        {
            Assert.AreEqual("LDA", LanguageServer.WordAt("  LDA #$01", 3));
            Assert.AreEqual("helper", LanguageServer.WordAt("  jsr helper ; call", 8));
            Assert.IsNull(LanguageServer.WordAt("  lda #$01", 8));
        }
    }
}
