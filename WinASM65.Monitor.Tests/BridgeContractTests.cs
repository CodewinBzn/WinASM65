using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Monitor.Abstractions;
using WinASM65.Monitor.Emulators;
using WinASM65.Monitor.Protocol;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// Version 2 of the bridge contract (HELLO, memory spaces) and the emulator
    /// configuration and adapters that start a bridge.
    /// </summary>
    [TestClass]
    public class BridgeContractTests
    {
        private const string Hello =
            "OK protocol=2 name=Mesen2 version=2.1.1 system=nes caps=memory,cpu,pause,resume,reset,step,break,state spaces=PPU,PAL,OAM,CHR";

        private static ScriptedBridge Version2Bridge()
        {
            ScriptedBridge host = new ScriptedBridge();
            Func<string, string> standard = host.Answer;
            host.Answer = line =>
            {
                if (line == "HELLO")
                    return Hello;
                if (line.StartsWith("READ PAL:", StringComparison.Ordinal))
                {
                    int length = int.Parse(line.Split(' ')[2]);
                    return "OK " + string.Concat(Enumerable.Repeat("0F", length));
                }
                if (line.StartsWith("READ NOPE:", StringComparison.Ordinal))
                    return "ERR unknown memory space: NOPE";
                return standard(line);
            };
            return host;
        }

        [TestMethod]
        public void AHelloSaysWhatTheBridgeCanDo()
        {
            BridgeHello hello;
            Assert.IsTrue(BridgeHello.TryParse(Hello.Substring(3), out hello));
            Assert.AreEqual(2, hello.Protocol);
            Assert.AreEqual("Mesen2", hello.Name);
            Assert.AreEqual("2.1.1", hello.Version);
            Assert.AreEqual("nes", hello.System);
            CollectionAssert.AreEqual(new[] { "PPU", "PAL", "OAM", "CHR" }, hello.Spaces.ToArray());
            Assert.IsTrue(hello.Capabilities.HasFlag(ExecutionCapability.StepInstruction));
            Assert.IsTrue(hello.Capabilities.HasFlag(ExecutionCapability.MemoryWrite));
            Assert.IsFalse(hello.Capabilities.HasFlag(ExecutionCapability.WatchpointRead), "not announced, not assumed");
        }

        [TestMethod]
        public void AnAnswerWithoutAProtocolOfTwoOrMoreIsNotAHello()
        {
            BridgeHello hello;
            Assert.IsFalse(BridgeHello.TryParse("", out hello));
            Assert.IsFalse(BridgeHello.TryParse("Mesen2 2.1.1", out hello));
            Assert.IsFalse(BridgeHello.TryParse("protocol=1 name=Old", out hello));
            Assert.AreEqual(ExecutionCapability.None, BridgeHello.CapabilitiesOf("teleport,warp"),
                "unknown words are ignored, not guessed");
        }

        [TestMethod]
        public void AVersion2BridgeIsTakenAtItsWord()
        {
            using (ScriptedBridge host = Version2Bridge())
            using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(host.Port, poll: false))
            {
                Assert.AreEqual(2, bridge.ProtocolVersion);
                Assert.AreEqual("nes", bridge.BridgeSystem);
                CollectionAssert.AreEqual(new[] { "PPU", "PAL", "OAM", "CHR" }, bridge.MemorySpaces.ToArray());
                Assert.IsFalse(bridge.Capabilities.HasFlag(ExecutionCapability.WatchpointRead));

                byte[] palette = bridge.ReadSpace("pal", 0, 32);
                Assert.AreEqual(32, palette.Length);
                Assert.IsTrue(palette.All(b => b == 0x0F));
                Assert.IsTrue(host.Received.Contains("READ PAL:$0000 32"), "the space is named as the bridge spells it");

                bridge.WriteSpace("CHR", 0x10, new byte[] { 0xAA, 0x55 });
                Assert.IsTrue(host.Received.Contains("WRITE CHR:$0010 AA55"));

                MonitorException refused = Assert.ThrowsException<MonitorException>(() => bridge.ReadSpace("VRAM", 0, 1));
                StringAssert.Contains(refused.Message, "PPU, PAL, OAM, CHR", "the refusal says what there is");
            }
        }

        [TestMethod]
        public void AVersion1BridgeKeepsTheMeasuredTableAndHasNoSpaces()
        {
            using (ScriptedBridge host = new ScriptedBridge())
            using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(host.Port, poll: false))
            {
                Assert.AreEqual(1, bridge.ProtocolVersion);
                Assert.IsNull(bridge.BridgeSystem);
                Assert.AreEqual(0, bridge.MemorySpaces.Count);
                Assert.AreEqual(ExecutionCapabilities.Mesen2_2_1_1, bridge.Capabilities);
                MonitorException refused = Assert.ThrowsException<MonitorException>(() => bridge.ReadSpace("PAL", 0, 1));
                StringAssert.Contains(refused.Message, "offers no memory besides the CPU's");
            }
        }

        [TestMethod]
        public void TheEmulatorSectionIsReadWithPathsRelativeToTheProject()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                File.WriteAllText(Path.Combine(temp.Path, "my_bridge.lua"), "-- bridge");
                string config = Path.Combine(temp.Path, "config.json");
                File.WriteAllText(config,
                    "{ \"emulator\": { \"name\": \"MyEmu\", \"path\": \"emu/run.exe\", \"bridge\": \"my_bridge.lua\","
                    + " \"arguments\": \"-rom {rom} -lua {script} -port {port}\", \"port\": \"4000\" } }");

                EmulatorConfiguration emulator = EmulatorConfiguration.Read(config);
                Assert.IsNotNull(emulator);
                Assert.AreEqual("myemu", emulator.Name);
                Assert.AreEqual(Path.Combine(temp.Path, "emu", "run.exe"), emulator.Path);
                Assert.AreEqual(Path.Combine(temp.Path, "my_bridge.lua"), emulator.Bridge);
                Assert.AreEqual(4000, emulator.Port);
                Assert.AreEqual("-rom {rom} -lua {script} -port {port}", emulator.Arguments);
            }
        }

        [TestMethod]
        public void AProjectWithoutAnEmulatorPathHasNoEmulator()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string config = Path.Combine(temp.Path, "config.json");
                File.WriteAllText(config, "{ \"Emulator\": { \"Name\": \"mesen2\" } }");
                Assert.IsNull(EmulatorConfiguration.Read(config));
                File.WriteAllText(config, "{ \"Input\": [] }");
                Assert.IsNull(EmulatorConfiguration.Read(config));
                Assert.IsNull(EmulatorConfiguration.Read(Path.Combine(temp.Path, "missing.json")));
            }
        }

        [TestMethod]
        public void TheAdapterIsChosenByNameOrByTheExecutable()
        {
            Assert.IsInstanceOfType(EmulatorAdapters.For(EmulatorConfiguration.Of(@"C:\x\Mesen.exe", null)), typeof(Mesen2Adapter),
                "a Mesen.exe without a name is Mesen2, as every existing project assumed");
            Assert.IsInstanceOfType(EmulatorAdapters.For(EmulatorConfiguration.Of(@"C:\x\Mesen.exe", "MesenCE")), typeof(MesenCeAdapter));
            IEmulatorAdapter custom = EmulatorAdapters.For(EmulatorConfiguration.Of(@"C:\x\mame.exe", null));
            Assert.IsInstanceOfType(custom, typeof(CustomEmulatorAdapter));
            Assert.AreEqual("mame", custom.Name);
            Assert.IsNull(custom.BuiltInBridge);
        }

        [TestMethod]
        public void ACustomEmulatorGetsTheCommandLineTheProjectWrote()
        {
            EmulatorConfiguration emulator = new EmulatorConfiguration
            {
                Name = "myemu",
                Path = @"C:\emu\run.exe",
                Arguments = "apple2e -flop1 \"{rom}\" -autoboot_script {script} -port={port}"
            };
            ProcessStartInfo start = new ProcessStartInfo(emulator.Path);
            new CustomEmulatorAdapter("myemu").AddArguments(start, emulator, @"C:\my game\disk.dsk", @"C:\b\bridge.lua", 4567);
            CollectionAssert.AreEqual(
                new[] { "apple2e", "-flop1", @"C:\my game\disk.dsk", "-autoboot_script", @"C:\b\bridge.lua", "-port=4567" },
                start.ArgumentList.ToArray());

            ProcessStartInfo plain = new ProcessStartInfo(emulator.Path);
            new CustomEmulatorAdapter("x").AddArguments(plain, new EmulatorConfiguration { Path = emulator.Path }, @"C:\g.nes", null, 1);
            CollectionAssert.AreEqual(new[] { @"C:\g.nes" }, plain.ArgumentList.ToArray(), "without Arguments, the ROM alone");
        }

        [TestMethod]
        public void Mesen2LoadsTheRomThenItsBridge()
        {
            ProcessStartInfo start = new ProcessStartInfo("Mesen.exe");
            new Mesen2Adapter().AddArguments(start, EmulatorConfiguration.Of("Mesen.exe", "mesen2"), "game.nes", "bridge_mesen2.lua", 1);
            CollectionAssert.AreEqual(new[] { "game.nes", "bridge_mesen2.lua" }, start.ArgumentList.ToArray());
            Assert.AreEqual("bridge_mesen2.lua", new Mesen2Adapter().BuiltInBridge);
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }

            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinASM65Bridge_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(Path))
                        Directory.Delete(Path, true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
