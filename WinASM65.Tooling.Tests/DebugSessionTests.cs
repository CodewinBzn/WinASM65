using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Monitor;
using WinASM65.Tooling.Dap;

namespace WinASM65.Tooling.Tests
{
    /// <summary>
    /// The debug adapter end to end, minus the emulator: real builds, real source
    /// map, a machine the test stops by hand.
    /// </summary>
    [TestClass]
    public class DebugSessionTests
    {
        // Line numbers below are the ones VS Code would send.
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
            private readonly BlockingCollection<JsonObject> _messages = new BlockingCollection<JsonObject>();
            private readonly Thread _loop;
            private int _seq;

            public Harness()
            {
                Directory = Path.Combine(Path.GetTempPath(), "winasm65-dap-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(Directory);
                Environment = new FakeEnvironment();
                Session = new DebugSession(Environment, message => _messages.Add(message));
                _loop = new Thread(Session.Run) { IsBackground = true };
                _loop.Start();
            }

            public string Directory { get; }
            public FakeEnvironment Environment { get; }
            public FakeDebugMachine Machine => Environment.Machine;
            public DebugSession Session { get; }

            public string Write(string name, string text)
            {
                string path = Path.Combine(Directory, name);
                File.WriteAllText(path, text);
                return path;
            }

            public string Project(params string[] units)
            {
                string inputs = string.Join(",", units.Select(u => "{ \"FileName\": \"" + u + "\", \"Dependencies\": [] }"));
                string files = string.Join(",", units.Select((u, i) => "{ \"FileName\": \"" + Path.ChangeExtension(u, ".o") + "\""
                    + (i < units.Length - 1 ? ", \"Size\": \"$10\"" : "") + " }"));
                return Write("config.json", "{ \"Input\": [" + inputs + "], \"Output\": { \"ObjectFile\": \"out.bin\", \"Files\": [" + files + "] } }");
            }

            public JsonObject Request(string command, JsonObject arguments = null)
            {
                int seq = Interlocked.Increment(ref _seq);
                Session.Dispatch(new JsonObject
                {
                    ["seq"] = seq,
                    ["type"] = "request",
                    ["command"] = command,
                    ["arguments"] = arguments ?? new JsonObject()
                });
                return Next(m => (string)m["type"] == "response" && (int)m["request_seq"] == seq,
                    "no response to " + command);
            }

            public JsonObject Event(string name, int timeoutMs = 5000)
            {
                return Next(m => (string)m["type"] == "event" && (string)m["event"] == name, "no " + name + " event", timeoutMs);
            }

            public bool NoEvent(string name, int waitMs = 500)
            {
                return TryNext(m => (string)m["type"] == "event" && (string)m["event"] == name, waitMs) == null;
            }

            private JsonObject Next(Func<JsonObject, bool> match, string failure, int timeoutMs = 5000)
            {
                JsonObject found = TryNext(match, timeoutMs);
                Assert.IsNotNull(found, failure);
                return found;
            }

            // Messages already received but not yet asked for, in arrival order: an
            // output event sent before a response must still be there afterwards.
            private readonly System.Collections.Generic.List<JsonObject> _backlog = new System.Collections.Generic.List<JsonObject>();

            private JsonObject TryNext(Func<JsonObject, bool> match, int timeoutMs)
            {
                int index = _backlog.FindIndex(m => match(m));
                if (index >= 0)
                {
                    JsonObject kept = _backlog[index];
                    _backlog.RemoveAt(index);
                    return kept;
                }

                DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (true)
                {
                    int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                    JsonObject message;
                    if (left <= 0 || !_messages.TryTake(out message, left))
                        return null;
                    if (match(message))
                        return message;
                    _backlog.Add(message);
                }
            }

            /// <summary>initialize, launch, breakpoints, configurationDone.</summary>
            public void Start(string config, string source = null, params int[] lines)
            {
                Assert.IsTrue((bool)Request("initialize")["success"]);
                JsonObject launch = Request("launch", new JsonObject { ["config"] = config });
                Assert.IsTrue((bool)launch["success"], (string)launch["message"]);
                Event("initialized");
                if (source != null)
                    SetBreakpoints(source, lines);
                Assert.IsTrue((bool)Request("configurationDone")["success"]);
            }

            public JsonArray SetBreakpoints(string source, params int[] lines)
            {
                JsonObject response = Request("setBreakpoints", new JsonObject
                {
                    ["source"] = new JsonObject { ["path"] = source },
                    ["breakpoints"] = new JsonArray(lines.Select(l => (JsonNode)new JsonObject { ["line"] = l }).ToArray())
                });
                Assert.IsTrue((bool)response["success"]);
                return (JsonArray)response["body"]["breakpoints"];
            }

            public void Dispose()
            {
                Session.Dispose();
                try { System.IO.Directory.Delete(Directory, true); } catch (IOException) { }
            }
        }

        [TestMethod]
        public void LaunchBuildsConnectsAndHoldsTheMachineUntilConfigured()
        {
            using (Harness h = new Harness())
            {
                h.Write("prog.asm", Program);
                h.Start(h.Project("prog.asm"));

                StringAssert.EndsWith(h.Environment.LaunchedRom, "out.bin");
                Assert.IsTrue(File.Exists(h.Environment.LaunchedRom), "the image was written for the emulator");
                CollectionAssert.IsSubsetOf(new[] { "PAUSE", "RESET" }, h.Machine.Calls);
                Assert.IsTrue(h.Machine.Calls.IndexOf("PAUSE") < h.Machine.Calls.IndexOf("RESET"),
                    "paused at launch, reset only once configured");
            }
        }

        [TestMethod]
        public void ABreakpointOnALabelLandsOnItsFirstInstruction()
        {
            using (Harness h = new Harness())
            {
                string source = h.Write("prog.asm", Program);
                h.Request("initialize");
                h.Request("launch", new JsonObject { ["config"] = h.Project("prog.asm") });

                JsonArray breakpoints = h.SetBreakpoints(source, 8, 30);

                Assert.IsTrue((bool)breakpoints[0]["verified"]);
                Assert.AreEqual(9, (int)breakpoints[0]["line"], "SUB: snaps to ldx");
                Assert.IsFalse((bool)breakpoints[1]["verified"], "nothing is assembled at line 30");
                CollectionAssert.AreEquivalent(new[] { 0xC00A }, h.Machine.Breakpoints.ToArray());
            }
        }

        [TestMethod]
        public void AStopShowsItsLineRegistersAndRoutine()
        {
            using (Harness h = new Harness())
            {
                string source = h.Write("prog.asm", Program);
                h.Start(h.Project("prog.asm"), source, 9);

                h.Machine.SimulateStop(StopReason.Breakpoint, 0xC00A, 0x0A);
                JsonObject stopped = h.Event("stopped");
                Assert.AreEqual("breakpoint", (string)stopped["body"]["reason"]);
                Assert.AreEqual(1, ((JsonArray)stopped["body"]["hitBreakpointIds"]).Count);

                JsonObject frame = (JsonObject)h.Request("stackTrace", new JsonObject { ["threadId"] = 1 })["body"]["stackFrames"][0];
                Assert.AreEqual("SUB", (string)frame["name"]);
                Assert.AreEqual(9, (int)frame["line"]);
                Assert.AreEqual(Path.GetFullPath(source), (string)frame["source"]["path"], true);
                Assert.AreEqual("0xC00A", (string)frame["instructionPointerReference"]);

                JsonArray registers = (JsonArray)h.Request("variables", new JsonObject { ["variablesReference"] = 1 })["body"]["variables"];
                Assert.AreEqual("$C00A", (string)registers.First(v => (string)v["name"] == "PC")["value"]);
                StringAssert.StartsWith((string)registers.First(v => (string)v["name"] == "PS")["value"], "$A5");
            }
        }

        [TestMethod]
        public void NextStepsOverASubroutineCall()
        {
            using (Harness h = new Harness())
            {
                string source = h.Write("prog.asm", Program);
                h.Start(h.Project("prog.asm"), source, 4);
                h.Machine.SimulateStop(StopReason.Breakpoint, 0xC002, 0x02);
                h.Event("stopped");

                Assert.IsTrue((bool)h.Request("next", new JsonObject { ["threadId"] = 1 })["success"]);
                Assert.IsTrue(h.Machine.Breakpoints.Contains(0xC005), "a temporary breakpoint after the JSR");
                Assert.AreEqual("RESUME", h.Machine.Calls.Last());

                h.Machine.SimulateStop(StopReason.Breakpoint, 0xC005, 0x05);
                Assert.AreEqual("step", (string)h.Event("stopped")["body"]["reason"]);
                Assert.IsFalse(h.Machine.Breakpoints.Contains(0xC005), "the temporary breakpoint is gone");
                Assert.IsTrue(h.Machine.Breakpoints.Contains(0xC002), "the user's breakpoint stays");
            }
        }

        [TestMethod]
        public void NextOnAnOrdinaryInstructionIsOneStep()
        {
            using (Harness h = new Harness())
            {
                string source = h.Write("prog.asm", Program);
                h.Start(h.Project("prog.asm"), source, 3);
                h.Machine.SimulateStop(StopReason.Breakpoint, 0xC000, 0x00);
                h.Event("stopped");

                h.Request("next", new JsonObject { ["threadId"] = 1 });
                Assert.AreEqual("STEP", h.Machine.Calls.Last());
                Assert.AreEqual("step", (string)h.Event("stopped")["body"]["reason"]);
            }
        }

        [TestMethod]
        public void AStopInAnotherBankAtTheSameAddressRunsOn()
        {
            using (Harness h = new Harness())
            {
                h.Write("bank0.asm", ".org $C000\n  lda #$01\n  rts\n");
                string bank1 = h.Write("bank1.asm", ".org $C000\n  ldx #$02\n  rts\n");
                h.Start(h.Project("bank0.asm", "bank1.asm"), bank1, 2);

                h.Machine.SimulateStop(StopReason.Breakpoint, 0xC000, 0x00);
                Assert.IsTrue(h.NoEvent("stopped"), "bank 0 is not where the breakpoint is");
                Assert.AreEqual("RESUME", h.Machine.Calls.Last());

                h.Machine.SimulateStop(StopReason.Breakpoint, 0xC000, 0x10);
                Assert.AreEqual("breakpoint", (string)h.Event("stopped")["body"]["reason"]);
            }
        }

        [TestMethod]
        public void ASharedAddressLeavesTheBridgeOnlyWhenNoBreakpointHoldsIt()
        {
            using (Harness h = new Harness())
            {
                string bank0 = h.Write("bank0.asm", ".org $C000\n  lda #$01\n  rts\n");
                string bank1 = h.Write("bank1.asm", ".org $C000\n  ldx #$02\n  rts\n");
                h.Start(h.Project("bank0.asm", "bank1.asm"), bank0, 2);
                h.SetBreakpoints(bank1, 2);
                Assert.AreEqual(1, h.Machine.Calls.Count(c => c == "BREAK SET exec $C000"));

                h.SetBreakpoints(bank0);
                Assert.IsTrue(h.Machine.Breakpoints.Contains(0xC000), "bank 1 still holds it");

                h.SetBreakpoints(bank1);
                Assert.IsFalse(h.Machine.Breakpoints.Contains(0xC000));
            }
        }

        [TestMethod]
        public void MemoryIsReadAndWrittenThroughVsCodesHexView()
        {
            using (Harness h = new Harness())
            {
                h.Write("prog.asm", Program);
                h.Start(h.Project("prog.asm"));
                h.Machine.Load(0x0010, new byte[] { 0xDE, 0xAD });

                JsonObject read = h.Request("readMemory", new JsonObject { ["memoryReference"] = "0x0010", ["count"] = 2 });
                CollectionAssert.AreEqual(new byte[] { 0xDE, 0xAD }, Convert.FromBase64String((string)read["body"]["data"]));

                JsonObject write = h.Request("writeMemory", new JsonObject
                {
                    ["memoryReference"] = "0x0010",
                    ["offset"] = 1,
                    ["data"] = Convert.ToBase64String(new byte[] { 0x42 })
                });
                Assert.IsTrue((bool)write["success"]);
                Assert.AreEqual(0x42, h.Machine.Peek(0x0011));
                h.Event("memory");

                JsonObject refused = h.Request("writeMemory", new JsonObject
                {
                    ["memoryReference"] = "0xC000",
                    ["data"] = Convert.ToBase64String(new byte[] { 0x00 })
                });
                Assert.IsFalse((bool)refused["success"]);
                StringAssert.Contains((string)refused["message"], "ROM");
            }
        }

        [TestMethod]
        public void TheRamPanelReadsAllOfRamAndNamesItsBytes()
        {
            using (Harness h = new Harness())
            {
                h.Write("prog.asm", "FRAMEDONE = $0042\nscore = $0300\n" + Program);
                h.Start(h.Project("prog.asm"));
                h.Machine.Load(0x07FF, new byte[] { 0x99 });

                JsonObject read = h.Request("readMemory", new JsonObject { ["memoryReference"] = "0x0000", ["count"] = 0x800 });
                byte[] ram = Convert.FromBase64String((string)read["body"]["data"]);
                Assert.AreEqual(0x800, ram.Length);
                Assert.AreEqual(0x99, ram[0x7FF]);

                JsonArray symbols = (JsonArray)h.Request("winasm65.symbols", new JsonObject { ["from"] = 0, ["to"] = 0x7FF })["body"]["symbols"];
                CollectionAssert.AreEqual(new[] { "FRAMEDONE", "score" }, symbols.Select(s => (string)s["name"]).ToArray(),
                    "RAM names only, by address; the code labels at $C000 are out of range");
                Assert.AreEqual(0x42L, (long)symbols[0]["value"]);
            }
        }

        [TestMethod]
        public void TheDebugConsoleSpeaksMonitorAndSymbols()
        {
            using (Harness h = new Harness())
            {
                h.Write("prog.asm", Program);
                h.Start(h.Project("prog.asm"));

                JsonObject cpu = h.Request("evaluate", new JsonObject { ["expression"] = "CPU", ["context"] = "repl" });
                StringAssert.Contains((string)cpu["body"]["result"], "PC=$");

                JsonObject symbol = h.Request("evaluate", new JsonObject { ["expression"] = "SUB", ["context"] = "hover" });
                StringAssert.StartsWith((string)symbol["body"]["result"], "$C00A");
                Assert.AreEqual("0xC00A", (string)symbol["body"]["memoryReference"]);

                JsonObject register = h.Request("evaluate", new JsonObject { ["expression"] = "x", ["context"] = "watch" });
                Assert.AreEqual("$02", (string)register["body"]["result"]);

                JsonObject nonsense = h.Request("evaluate", new JsonObject { ["expression"] = "nonsense", ["context"] = "hover" });
                Assert.IsFalse((bool)nonsense["success"]);
            }
        }

        [TestMethod]
        public void ABuildErrorFailsTheLaunchAndNamesTheLine()
        {
            using (Harness h = new Harness())
            {
                h.Write("prog.asm", ".org $C000\n  lda #$01\n  bogus $10\n");
                h.Request("initialize");
                JsonObject launch = h.Request("launch", new JsonObject { ["config"] = h.Project("prog.asm") });
                Assert.IsFalse((bool)launch["success"]);

                JsonObject output = h.Event("output");
                while (!((string)output["body"]["output"]).Contains("prog.asm:3"))
                    output = h.Event("output");
                Assert.AreEqual("stderr", (string)output["body"]["category"]);
            }
        }

        [TestMethod]
        public void AnEmulatorThatExitsAtOnceIsExplained()
        {
            using (Harness h = new Harness())
            {
                h.Write("prog.asm", Program);
                h.Environment.EmulatorExitsAtOnce = true;
                h.Request("initialize");
                JsonObject launch = h.Request("launch", new JsonObject { ["config"] = h.Project("prog.asm"), ["connectTimeoutMs"] = 2000 });
                Assert.IsFalse((bool)launch["success"]);
                StringAssert.Contains((string)launch["message"], "single instance");
            }
        }

        [TestMethod]
        public void DisconnectClearsBreakpointsAndStopsTheEmulatorItStarted()
        {
            using (Harness h = new Harness())
            {
                string source = h.Write("prog.asm", Program);
                h.Start(h.Project("prog.asm"), source, 3);

                Assert.IsTrue((bool)h.Request("disconnect")["success"]);
                CollectionAssert.Contains(h.Machine.Calls, "BREAK CLEAR");
                Assert.IsTrue(h.Machine.Disposed);
                Assert.IsTrue(h.Environment.Emulator.Killed);
            }
        }

        [TestMethod]
        public void TheEmulatorClosingEndsTheSession()
        {
            using (Harness h = new Harness())
            {
                h.Write("prog.asm", Program);
                h.Start(h.Project("prog.asm"));
                h.Environment.Emulator.Exit();
                h.Event("terminated");
            }
        }

        [TestMethod]
        public void FlagsReadTheWayTheyAreWritten()
        {
            // $A5 = 1010 0101: N, the unused bit, I and C set.
            Assert.AreEqual("Nv-bdIzC", DebugSession.FlagString(0xA5));
        }

        // ------------------------------------------------------------ systems

        private static string ConfigFor(string target)
        {
            return "{ \"Target\": " + target + ", \"Input\": [ { \"FileName\": \"prog.asm\" } ],"
                + " \"Output\": { \"ObjectFile\": \"out.bin\", \"Files\": [ { \"FileName\": \"prog.o\" } ] } }";
        }

        [TestMethod]
        public void TheMachineIsDescribedWithItsMemoriesViewsAndColours()
        {
            using (Harness h = new Harness())
            {
                h.Write("prog.asm", Program);
                h.Start(h.Write("config.json", ConfigFor("{ \"System\": \"nes\", \"Format\": \"bin\" }")));

                JsonObject system = (JsonObject)h.Request("winasm65.system")["body"];
                Assert.AreEqual("nes", (string)system["id"]);
                Assert.AreEqual("2A03", (string)system["cpu"]);
                JsonArray spaces = (JsonArray)system["spaces"];
                CollectionAssert.AreEqual(new[] { "CPU", "PPU", "PAL", "OAM", "CHR" },
                    spaces.Select(s => (string)s["name"]).ToArray());
                Assert.IsTrue(spaces.All(s => (bool)s["available"]), "the fake bridge offers every NES space");
                Assert.AreEqual("RAM", (string)system["views"][0]["name"]);
                Assert.AreEqual(0x800, (int)system["views"][0]["length"]);
                Assert.AreEqual(64, ((JsonArray)system["palette"]).Count);
                Assert.AreEqual("Mesen2", (string)system["emulator"]["name"]);
            }
        }

        [TestMethod]
        public void AMemorySpaceIsReadAndWrittenByName()
        {
            using (Harness h = new Harness())
            {
                h.Write("prog.asm", Program);
                h.Start(h.Write("config.json", ConfigFor("{ \"System\": \"nes\", \"Format\": \"bin\" }")));

                JsonObject written = h.Request("winasm65.memory.write", new JsonObject
                {
                    ["space"] = "PAL", ["address"] = 1, ["data"] = Convert.ToBase64String(new byte[] { 0x16, 0x27 })
                });
                Assert.IsTrue((bool)written["success"], (string)written["message"]);
                Assert.AreEqual(0x16, h.Machine.PeekSpace("PAL", 1));

                JsonObject read = h.Request("winasm65.memory.read", new JsonObject { ["space"] = "pal", ["address"] = 0, ["count"] = 3 });
                CollectionAssert.AreEqual(new byte[] { 0x00, 0x16, 0x27 }, Convert.FromBase64String((string)read["body"]["data"]));

                JsonObject cpu = h.Request("winasm65.memory.read", new JsonObject { ["address"] = 0xC000, ["count"] = 2 });
                CollectionAssert.AreEqual(new byte[] { 0xA9, 0x01 }, Convert.FromBase64String((string)cpu["body"]["data"]),
                    "without a space, the CPU's");
            }
        }

        [TestMethod]
        public void ASpaceTheBridgeDoesNotOfferIsRefusedWithItsName()
        {
            using (Harness h = new Harness())
            {
                h.Machine.MemorySpaces = new string[0];
                h.Write("prog.asm", Program);
                h.Start(h.Write("config.json", ConfigFor("{ \"System\": \"nes\", \"Format\": \"bin\" }")));

                JsonObject read = h.Request("winasm65.memory.read", new JsonObject { ["space"] = "PAL", ["address"] = 0, ["count"] = 1 });
                Assert.IsFalse((bool)read["success"]);
                StringAssert.Contains((string)read["message"], "offers no memory besides the CPU's");
                JsonObject system = (JsonObject)h.Request("winasm65.system")["body"];
                Assert.IsFalse((bool)system["spaces"][2]["available"], "described, but not offered by this bridge");
            }
        }

        [TestMethod]
        public void ALoadedProgramRunsFromWhereItWasLoadedInsteadOfAReset()
        {
            using (Harness h = new Harness())
            {
                h.Write("prog.asm", Program);
                string config = h.Write("config.json", ConfigFor("{ \"System\": \"c64\", \"Format\": \"bin\", \"LoadAddress\": \"$C000\" }"));
                Assert.IsTrue((bool)h.Request("initialize")["success"]);
                JsonObject launch = h.Request("launch", new JsonObject { ["config"] = config, ["stopOnEntry"] = true });
                Assert.IsTrue((bool)launch["success"], (string)launch["message"]);
                h.Event("initialized");
                Assert.IsTrue((bool)h.Request("configurationDone")["success"]);

                CollectionAssert.DoesNotContain(h.Machine.Calls, "RESET", "a C64 program is not a cartridge");
                CollectionAssert.Contains(h.Machine.Calls, "RESUME");
                CollectionAssert.Contains(h.Machine.Breakpoints.ToArray(), 0xC000, "stop on entry at the load address");
            }
        }

        [TestMethod]
        public void LaunchJsonChoosesTheEmulatorAndItsBridge()
        {
            using (Harness h = new Harness())
            {
                h.Write("prog.asm", Program);
                Assert.IsTrue((bool)h.Request("initialize")["success"]);
                JsonObject launch = h.Request("launch", new JsonObject
                {
                    ["config"] = h.Project("prog.asm"),
                    ["emulator"] = "MyEmu",
                    ["emulatorPath"] = @"C:\emu\run.exe",
                    ["bridge"] = "bridges/my_bridge.lua"
                });
                Assert.IsTrue((bool)launch["success"], (string)launch["message"]);
                Assert.AreEqual("myemu", h.Environment.LaunchedEmulator.Name);
                Assert.AreEqual(@"C:\emu\run.exe", h.Environment.LaunchedEmulator.Path);
                Assert.AreEqual(Path.Combine(h.Directory, "bridges", "my_bridge.lua"), h.Environment.LaunchedEmulator.Bridge,
                    "a relative bridge is relative to the project");
            }
        }
    }
}
