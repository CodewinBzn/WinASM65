using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Monitor;
using WinASM65.Tooling.Mcp;

namespace WinASM65.Tooling.Tests
{
    /// <summary>
    /// The MCP server for AI assistants: the protocol, the code tools on real
    /// projects, and the debug tools against the fake machine.
    /// </summary>
    [TestClass]
    public class McpServerTests
    {
        private const string Program =
            ".org $C000\n" +     // 1
            "RESET:\n" +         // 2
            "  lda #$01\n" +     // 3  $C000
            "  jsr SUB\n" +      // 4  $C002
            "  sta $10\n" +      // 5  $C005
            "loop:\n" +          // 6
            "  jmp loop\n" +     // 7  $C007
            "SUB:\n" +           // 8
            "  ldx #$02\n" +     // 9  $C00A
            "  rts\n";           // 10 $C00C

        private sealed class Harness : IDisposable
        {
            private readonly List<JsonObject> _sent = new List<JsonObject>();
            private int _id;

            public Harness()
            {
                Directory = Path.Combine(Path.GetTempPath(), "winasm65-mcp-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(Directory);
                Environment = new FakeEnvironment();
                Debug = new DebugTools(() => Environment);
                Server = new McpServer(CodeTools.All().Concat(Debug.All()).ToList(), m => { lock (_sent) _sent.Add(m); });
            }

            public string Directory { get; }
            public FakeEnvironment Environment { get; }
            public DebugTools Debug { get; }
            public McpServer Server { get; }

            public string Write(string name, string text)
            {
                string path = Path.Combine(Directory, name);
                File.WriteAllText(path, text);
                return path;
            }

            public string Project(string target = null)
            {
                Write("prog.asm", Program);
                return Write("config.json", "{ " + (target != null ? "\"Target\": " + target + ", " : "")
                    + "\"Input\": [ { \"FileName\": \"prog.asm\" } ], \"Output\": { \"ObjectFile\": \"out.bin\","
                    + " \"Files\": [ { \"FileName\": \"prog.o\" } ] } }");
            }

            public JsonObject Send(string method, JsonObject parameters = null)
            {
                int id = ++_id;
                Server.Handle(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters ?? new JsonObject() });
                lock (_sent)
                    return _sent.Single(m => (int?)m["id"] == id);
            }

            /// <summary>Calls a tool; returns its text and whether it failed.</summary>
            public (string Text, bool Error) Call(string tool, JsonObject arguments = null)
            {
                JsonObject result = (JsonObject)Send("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = arguments ?? new JsonObject() })["result"];
                string text = string.Join("\n", ((JsonArray)result["content"]).Where(c => (string)c["type"] == "text").Select(c => (string)c["text"]));
                return (text, (bool)result["isError"]);
            }

            public void Dispose()
            {
                Debug.Dispose();
                try { System.IO.Directory.Delete(Directory, true); } catch (IOException) { }
            }
        }

        [TestMethod]
        public void InitializeNegotiatesTheVersionAndListsTools()
        {
            using (Harness h = new Harness())
            {
                JsonObject result = (JsonObject)h.Send("initialize", new JsonObject { ["protocolVersion"] = "2025-03-26" })["result"];
                Assert.AreEqual("2025-03-26", (string)result["protocolVersion"], "a version the server speaks is kept");
                Assert.AreEqual("winasm65", (string)result["serverInfo"]["name"]);
                Assert.IsNotNull(result["capabilities"]["tools"]);

                JsonObject unknown = (JsonObject)h.Send("initialize", new JsonObject { ["protocolVersion"] = "1999-01-01" })["result"];
                Assert.AreEqual(McpServer.ProtocolVersions[0], (string)unknown["protocolVersion"]);

                JsonArray tools = (JsonArray)h.Send("tools/list")["result"]["tools"];
                string[] names = tools.Select(t => (string)t["name"]).ToArray();
                CollectionAssert.IsSubsetOf(new[] { "build_project", "assemble", "get_listing", "find_symbol", "describe_instruction",
                    "debug_launch", "set_breakpoints", "continue", "step", "get_state", "read_memory", "write_memory", "debug_stop" }, names);
                JsonObject build = (JsonObject)tools.First(t => (string)t["name"] == "build_project");
                Assert.AreEqual("object", (string)build["inputSchema"]["type"]);
                CollectionAssert.Contains(((JsonArray)build["inputSchema"]["required"]).Select(r => (string)r).ToArray(), "config");
                Assert.IsFalse((bool)tools.First(t => (string)t["name"] == "write_memory")["annotations"]["readOnlyHint"]);
                Assert.IsTrue((bool)tools.First(t => (string)t["name"] == "write_memory")["annotations"]["destructiveHint"]);
                Assert.IsTrue((bool)tools.First(t => (string)t["name"] == "get_listing")["annotations"]["readOnlyHint"]);
            }
        }

        [TestMethod]
        public void NotificationsAreNotAnsweredAndUnknownMethodsAreErrors()
        {
            using (Harness h = new Harness())
            {
                h.Server.Handle(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" });
                JsonObject reply = h.Send("resources/subscribe");
                Assert.AreEqual(-32601, (int)reply["error"]["code"]);
                Assert.IsNotNull(h.Send("ping")["result"]);
            }
        }

        [TestMethod]
        public void BuildProjectReportsTheImageOrTheErrors()
        {
            using (Harness h = new Harness())
            {
                string config = h.Project();
                (string text, bool error) = h.Call("build_project", new JsonObject { ["config"] = config });
                Assert.IsFalse(error, text);
                StringAssert.Contains(text, "Build succeeded.");
                StringAssert.Contains(text, "out.bin");

                h.Write("prog.asm", "  lda #$01\n  bne nowhere\n");
                (text, error) = h.Call("build_project", new JsonObject { ["config"] = config });
                Assert.IsTrue(error);
                StringAssert.Contains(text, "prog.asm:2");
                StringAssert.Contains(text, "nowhere");
            }
        }

        [TestMethod]
        public void AssembleShowsTheBytesOfAFragment()
        {
            using (Harness h = new Harness())
            {
                (string text, bool error) = h.Call("assemble", new JsonObject { ["source"] = "  .org $8000\n  lda #$01\n  sta $2000\n" });
                Assert.IsFalse(error, text);
                StringAssert.Contains(text, "$8000  A9 01");
                StringAssert.Contains(text, "8D 00 20");
                StringAssert.Contains(text, "Assembled 5 byte(s).");

                (text, error) = h.Call("assemble", new JsonObject { ["source"] = "  stz $10\n", ["cpu"] = "65c02" });
                StringAssert.Contains(text, "64 10", "STZ is a 65C02 instruction");
            }
        }

        [TestMethod]
        public void ListingSymbolsAndInstructionsAreLookedUp()
        {
            using (Harness h = new Harness())
            {
                string config = h.Project();
                (string listing, bool error) = h.Call("get_listing", new JsonObject { ["config"] = config, ["file"] = "prog.asm", ["fromLine"] = 9, ["toLine"] = 9 });
                Assert.IsFalse(error, listing);
                StringAssert.Contains(listing, "$C00A  A2 02");
                Assert.IsFalse(listing.Contains("$C000"), "only the lines asked for");

                (string symbols, _) = h.Call("find_symbol", new JsonObject { ["config"] = config, ["pattern"] = "^SUB$" });
                StringAssert.Contains(symbols, "SUB = $C00A (49162)");

                (string lda, _) = h.Call("describe_instruction", new JsonObject { ["mnemonic"] = "lda" });
                StringAssert.Contains(lda, "Load A");
                StringAssert.Contains(lda, "$A9");
                (string wrong, bool failed) = h.Call("describe_instruction", new JsonObject { ["mnemonic"] = "STZ" });
                Assert.IsTrue(failed, "STZ is not a 6502 instruction");
                StringAssert.Contains(wrong, "LDA");
            }
        }

        [TestMethod]
        public void AToolGivenWrongArgumentsSaysWhich()
        {
            using (Harness h = new Harness())
            {
                (string text, bool error) = h.Call("build_project");
                Assert.IsTrue(error);
                StringAssert.Contains(text, "\"config\"");
                (text, error) = h.Call("get_state");
                Assert.IsTrue(error);
                StringAssert.Contains(text, "debug_launch");
            }
        }

        [TestMethod]
        public void TheDebuggerIsDrivenFromLaunchToAStop()
        {
            using (Harness h = new Harness())
            {
                string config = h.Project("{ \"System\": \"nes\", \"Format\": \"bin\" }");
                (string launched, bool error) = h.Call("debug_launch", new JsonObject
                {
                    ["config"] = config,
                    ["breakpoints"] = new JsonArray("prog.asm:9"),
                    ["waitSeconds"] = 0
                });
                Assert.IsFalse(error, launched);
                StringAssert.Contains(launched, "Breakpoint prog.asm:9 -> set at line 9");
                CollectionAssert.Contains(h.Environment.Machine.Breakpoints.ToArray(), 0xC00A);

                h.Environment.Machine.SimulateStop(StopReason.Breakpoint, 0xC00A, 0x0A);
                (string state, _) = h.Call("continue", new JsonObject { ["waitSeconds"] = 5 });
                StringAssert.Contains(state, "Stopped in SUB");
                StringAssert.Contains(state, "prog.asm:9");
                StringAssert.Contains(state, "ldx #$02");
                StringAssert.Contains(state, "Registers:");

                (string written, _) = h.Call("write_memory", new JsonObject { ["space"] = "PAL", ["address"] = "$01", ["bytes"] = "16 27" });
                StringAssert.Contains(written, "Wrote 2 byte(s) at PAL $0001");
                (string memory, _) = h.Call("read_memory", new JsonObject { ["space"] = "PAL", ["address"] = 0, ["length"] = 4 });
                StringAssert.Contains(memory, "$0000  00 16 27 00");

                (string machine, _) = h.Call("describe_machine");
                StringAssert.Contains(machine, "Nintendo Entertainment System (nes), CPU 2A03");
                StringAssert.Contains(machine, "PAL (32 bytes)");

                (string ended, _) = h.Call("debug_stop");
                StringAssert.Contains(ended, "ended");
            }
        }
    }
}
