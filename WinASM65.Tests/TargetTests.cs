using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.Segments;
using WinASM65.Output;
using WinASM65.Targets;

namespace WinASM65.Tests
{
    [TestClass]
    public class TargetTests
    {
        [TestMethod]
        public void SystemCatalog_ReturnsNesPreset()
        {
            ResolvedTarget target;
            Assert.IsTrue(SystemCatalog.TryGet("nes", out target));
            Assert.AreEqual("6502", target.CpuName);
            Assert.AreEqual("ines", target.FormatName);
            Assert.IsTrue(target.DefineHardwareSymbols);
            Assert.AreEqual(0x2000L, target.HardwareSymbols["PPUCTRL"]);
        }

        [TestMethod]
        public void SystemCatalog_ReturnsApple2eAs65C02()
        {
            ResolvedTarget target;
            Assert.IsTrue(SystemCatalog.TryGet("apple2e", out target));
            Assert.AreEqual("65c02", target.CpuName);
            Assert.AreEqual("a2bin", target.FormatName);
        }

        [TestMethod]
        public void SystemCatalog_RawDefaultsHaveNoHardwareSymbols()
        {
            ResolvedTarget target;
            Assert.IsTrue(SystemCatalog.TryGet("raw", out target));
            Assert.AreEqual("6502", target.CpuName);
            Assert.AreEqual("bin", target.FormatName);
            Assert.IsFalse(target.DefineHardwareSymbols);
        }

        [TestMethod]
        public void CpuFactory_CreatesExpectedInstructionSets()
        {
            Assert.IsInstanceOfType(CpuFactory.Create("6502"), typeof(Cpu6502));
            Assert.IsInstanceOfType(CpuFactory.Create("65c02"), typeof(Cpu65C02));
        }

        [TestMethod]
        public void CpuFactory_RejectsUnknownCpu()
        {
            try
            {
                CpuFactory.Create("z80");
                Assert.Fail("Expected ArgumentException.");
            }
            catch (ArgumentException)
            {
            }
        }

        [TestMethod]
        public void ResolvedTarget_CloneCopiesOriginAddress()
        {
            ResolvedTarget target = new ResolvedTarget { OriginAddress = 0x1234, LoadAddress = 0x0801 };
            ResolvedTarget clone = target.Clone();
            Assert.AreEqual((ushort)0x1234, clone.OriginAddress.Value);
            Assert.AreEqual((ushort)0x0801, clone.LoadAddress.Value);
        }

        [TestMethod]
        public void TargetResolver_ResolvesC64WithHardwareSymbols()
        {
            ResolvedTarget target = TargetResolver.Resolve(null, "c64", null);
            Assert.AreEqual("c64", target.SystemId);
            Assert.AreEqual("prg", target.FormatName);
            Assert.AreEqual((ushort)0x0801, target.LoadAddress.Value);
            Assert.AreEqual(0xD000L, target.HardwareSymbols["VIC"]);
        }

        [TestMethod]
        public void TargetResolver_ThrowsOnUnknownSystem()
        {
            try
            {
                TargetResolver.Resolve(null, "unknown_sys", null);
                Assert.Fail("Expected ArgumentException.");
            }
            catch (ArgumentException)
            {
            }
        }

        [TestMethod]
        public void TargetResolver_CliSystemOverridesConfigSystem()
        {
            ResolvedTarget target = TargetResolver.Resolve(new TargetConf { System = "vic20" }, "c64", null);
            Assert.AreEqual("c64", target.SystemId);
        }

        [TestMethod]
        public void TargetResolver_CliFormatOverridesConfigAndPreset()
        {
            ResolvedTarget target = TargetResolver.Resolve(new TargetConf { System = "c64", Format = "bin" }, null, null, "o65");
            Assert.AreEqual("o65", target.FormatName);
        }

        [TestMethod]
        public void TargetResolver_DefaultsToRawWhenNothingRequested()
        {
            ResolvedTarget target = TargetResolver.Resolve(null, null, null);
            Assert.AreEqual("bin", target.FormatName);
            Assert.IsFalse(target.LoadAddress.HasValue);
        }

        [TestMethod]
        public void PrgFormat_PrefersOriginAddressOverLoadAddress()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string output = Path.Combine(temp.Path, "game.prg");
                ResolvedTarget target = new ResolvedTarget
                {
                    FormatName = "prg",
                    LoadAddress = 0x0801,
                    OriginAddress = 0x0800
                };
                Assert.IsTrue(new ExecutablePublisher().Publish(output, new byte[] { 0xA9, 0x00, 0x60 }, target).Success);
                byte[] bytes = File.ReadAllBytes(output);
                CollectionAssert.AreEqual(new byte[] { 0x00, 0x08, 0xA9, 0x00, 0x60 }, bytes);
            }
        }

        [TestMethod]
        public void PrgFormat_FallsBackToLoadAddress()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string output = Path.Combine(temp.Path, "game.prg");
                ResolvedTarget target = new ResolvedTarget { FormatName = "prg", LoadAddress = 0x0801 };
                Assert.IsTrue(new ExecutablePublisher().Publish(output, new byte[] { 0x60 }, target).Success);
                byte[] bytes = File.ReadAllBytes(output);
                Assert.AreEqual(0x01, bytes[0]);
                Assert.AreEqual(0x08, bytes[1]);
            }
        }

        [TestMethod]
        public void Publisher_RejectsUnknownFormat()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string output = Path.Combine(temp.Path, "game.bin");
                ResolvedTarget target = new ResolvedTarget { FormatName = "elf" };
                OperationResult result = new ExecutablePublisher().Publish(output, new byte[] { 0x60 }, target);
                Assert.IsFalse(result.Success);
                Assert.IsTrue(result.Diagnostics.Count > 0);
            }
        }

        [TestMethod]
        public void O65Format_WritesHeaderAndSegmentLoadAddress()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string output = Path.Combine(temp.Path, "out.o65");
                ResolvedTarget target = new ResolvedTarget { FormatName = "o65", OriginAddress = 0x2000 };
                Assert.IsTrue(new ExecutablePublisher().Publish(output, new byte[] { 0xA9, 0x00, 0x60 }, target).Success);
                byte[] bytes = File.ReadAllBytes(output);
                Assert.AreEqual(0x6F, bytes[0]);
                Assert.AreEqual(0x36, bytes[1]);
                Assert.AreEqual(0x35, bytes[2]);
                Assert.AreEqual(0x00, bytes[3]);
                Assert.AreEqual(0x01, bytes[4]);
                Assert.AreEqual(0x01, bytes[6]);
                Assert.AreEqual(0x20, bytes[7]);
                Assert.AreEqual(0x00, bytes[8]);
                Assert.AreEqual(0x00, bytes[9]);
                Assert.AreEqual(0x03, bytes[10]);
                Assert.AreEqual(15, bytes.Length);
                CollectionAssert.AreEqual(new byte[] { 0xA9, 0x00, 0x60 },
                    new[] { bytes[12], bytes[13], bytes[14] });
            }
        }

        [TestMethod]
        public void IntelHexFormat_WritesStartDataAndEndRecords()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string output = Path.Combine(temp.Path, "out.ihex");
                ResolvedTarget target = new ResolvedTarget { FormatName = "ihex", OriginAddress = 0x0801 };
                Assert.IsTrue(new ExecutablePublisher().Publish(output, new byte[] { 0xA9, 0x00, 0x60 }, target).Success);
                string[] lines = File.ReadAllLines(output);
                Assert.AreEqual(3, lines.Length);
                Assert.AreEqual(":0400000300000108F0", lines[0]);
                StringAssert.StartsWith(lines[1], ":030801");
                Assert.AreEqual(":00000001FF", lines[2]);
            }
        }

        [TestMethod]
        public void IntelHexFormat_EmitsExtendedAddressRecordWhenPayloadCrosses64K()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string output = Path.Combine(temp.Path, "wrap.ihex");
                ResolvedTarget target = new ResolvedTarget { FormatName = "ihex", OriginAddress = 0xFFF0 };
                Assert.IsTrue(new ExecutablePublisher().Publish(output, new byte[32], target).Success);
                string[] lines = File.ReadAllLines(output);
                Assert.AreEqual(5, lines.Length);
                StringAssert.StartsWith(lines[1], ":10FFF0");
                Assert.AreEqual(":020000040001F9", lines[2]);
                StringAssert.StartsWith(lines[3], ":100000");
                Assert.AreEqual(":00000001FF", lines[4]);
            }
        }

        [TestMethod]
        public void MotorolaSrecFormat_WritesHeaderDataAndTerminationRecords()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string output = Path.Combine(temp.Path, "out.srec");
                ResolvedTarget target = new ResolvedTarget { FormatName = "srec", OriginAddress = 0x0600 };
                Assert.IsTrue(new ExecutablePublisher().Publish(output, new byte[] { 0xA9, 0x00, 0x60 }, target).Success);
                string[] lines = File.ReadAllLines(output);
                Assert.AreEqual(3, lines.Length);
                StringAssert.StartsWith(lines[0], "S0");
                StringAssert.StartsWith(lines[1], "S1");
                StringAssert.Contains(lines[1], "0600");
                StringAssert.StartsWith(lines[2], "S9");
            }
        }

        [TestMethod]
        public void Assembler_UsesDefaultOriginWhenOrgIsAbsent()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "noorg.asm");
                File.WriteAllText(source, "lda #$00\nrts\n");
                AssemblyResult result = new AssemblerFactory().Create(new AssemblerOptions { DefaultOrigin = 0x0801 })
                    .Assemble(source, Path.Combine(temp.Path, "noorg.o"));
                Assert.IsTrue(result.Success);
                Assert.AreEqual((ushort)0x0801, result.OriginAddress);
                CollectionAssert.AreEqual(new byte[] { 0xA9, 0x00, 0x60 }, result.OutputBytes);
            }
        }

        [TestMethod]
        public void Assembler_OrgOverridesDefaultOrigin()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "org.asm");
                File.WriteAllText(source, ".org $A000\nlda #$00\nrts\n");
                AssemblyResult result = new AssemblerFactory().Create(new AssemblerOptions { DefaultOrigin = 0x0801 })
                    .Assemble(source, Path.Combine(temp.Path, "org.o"));
                Assert.IsTrue(result.Success);
                Assert.AreEqual((ushort)0xA000, result.OriginAddress);
            }
        }

        [TestMethod]
        public void Assembler_PredefinedSymbolsAreVisibleToSource()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "hw.asm");
                File.WriteAllText(source, "lda PPUCTRL\n");
                System.Collections.Generic.Dictionary<string, long> symbols = new System.Collections.Generic.Dictionary<string, long>
                {
                    { "PPUCTRL", 0x2000 }
                };
                AssemblyResult result = new AssemblerFactory().Create(
                    new AssemblerOptions { Cpu = CpuFactory.Create("6502"), PredefinedSymbols = symbols })
                    .Assemble(source, Path.Combine(temp.Path, "hw.o"));
                Assert.IsTrue(result.Success);
                CollectionAssert.AreEqual(new byte[] { 0xAD, 0x00, 0x20 }, result.OutputBytes);
            }
        }

        [TestMethod]
        public void CommandLine_PublishesPrgWithAssembledOrigin()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "game.asm");
                string output = Path.Combine(temp.Path, "game.prg");
                File.WriteAllText(source, ".org $0800\nlda #$00\nrts\n");
                RecordingConsole console = new RecordingConsole();
                int exitCode = new CommandLineApplication(new AssemblerFactory(), new JsonConfigurationReader(),
                    new BinaryCombiner(), console, new ExecutablePublisher())
                    .Run(new[] { "-t", "c64", "-f", source, "-o", output });
                Assert.AreEqual(0, exitCode);
                Assert.AreEqual(string.Empty, console.Error);
                CollectionAssert.AreEqual(new byte[] { 0x00, 0x08, 0xA9, 0x00, 0x60 }, File.ReadAllBytes(output));
            }
        }

        [TestMethod]
        public void CommandLine_UsesTargetLoadAddressAsDefaultOrigin()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "game.asm");
                string output = Path.Combine(temp.Path, "game.prg");
                File.WriteAllText(source, "lda #$00\nrts\n");
                RecordingConsole console = new RecordingConsole();
                int exitCode = new CommandLineApplication(new AssemblerFactory(), new JsonConfigurationReader(),
                    new BinaryCombiner(), console, new ExecutablePublisher())
                    .Run(new[] { "-t", "c64", "-f", source, "-o", output });
                Assert.AreEqual(0, exitCode);
                CollectionAssert.AreEqual(new byte[] { 0x01, 0x08, 0xA9, 0x00, 0x60 }, File.ReadAllBytes(output));
            }
        }

        [TestMethod]
        public void CommandLine_UnknownSystemReportsConfigurationError()
        {
            RecordingConsole console = new RecordingConsole();
            int exitCode = new CommandLineApplication(new AssemblerFactory(), new JsonConfigurationReader(),
                new BinaryCombiner(), console, new ExecutablePublisher())
                .Run(new[] { "-t", "unknown_sys" });
            Assert.AreEqual(1, exitCode);
            StringAssert.Contains(console.Error, "Unknown system");
        }

        [TestMethod]
        public void CommandLine_TargetListPrintsCatalog()
        {
            RecordingConsole console = new RecordingConsole();
            int exitCode = new CommandLineApplication(new AssemblerFactory(), new JsonConfigurationReader(),
                new BinaryCombiner(), console, new ExecutablePublisher())
                .Run(new[] { "-t", "list" });
            Assert.AreEqual(0, exitCode);
            StringAssert.Contains(console.Output, "nes");
            StringAssert.Contains(console.Output, "c64");
        }

        [TestMethod]
        public void CommandLine_WithoutTargetKeepsRawBinaryOutput()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "plain.asm");
                string output = Path.Combine(temp.Path, "plain.o");
                File.WriteAllText(source, "lda #$00\nrts\n");
                RecordingConsole console = new RecordingConsole();
                int exitCode = new CommandLineApplication(new AssemblerFactory(), new JsonConfigurationReader(),
                    new BinaryCombiner(), console, new ExecutablePublisher())
                    .Run(new[] { "-f", source, "-o", output });
                Assert.AreEqual(0, exitCode);
                CollectionAssert.AreEqual(new byte[] { 0xA9, 0x00, 0x60 }, File.ReadAllBytes(output));
            }
        }

        [TestMethod]
        public void CommandLine_FormatOverrideProducesPortableOutput()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "port.asm");
                File.WriteAllText(source, "lda #$00\nrts\n");
                foreach (string format in new[] { "o65", "ihex", "srec" })
                {
                    string output = Path.Combine(temp.Path, "out." + format);
                    RecordingConsole console = new RecordingConsole();
                    int exitCode = new CommandLineApplication(new AssemblerFactory(), new JsonConfigurationReader(),
                        new BinaryCombiner(), console, new ExecutablePublisher())
                        .Run(new[] { "-format", format, "-f", source, "-o", output });
                    Assert.AreEqual(0, exitCode, format);
                    Assert.IsTrue(File.Exists(output), format);
                }
                CollectionAssert.AreEqual(new byte[] { 0x6F, 0x36, 0x35 },
                    new[] { File.ReadAllBytes(Path.Combine(temp.Path, "out.o65"))[0],
                            File.ReadAllBytes(Path.Combine(temp.Path, "out.o65"))[1],
                            File.ReadAllBytes(Path.Combine(temp.Path, "out.o65"))[2] });
                StringAssert.StartsWith(File.ReadAllText(Path.Combine(temp.Path, "out.ihex")), ":");
                StringAssert.StartsWith(File.ReadAllText(Path.Combine(temp.Path, "out.srec")), "S");
            }
        }

        [TestMethod]
        public void UneEtiquetteDuSourcePrimeSurUnSymboleMaterielPredefini()
        {
            // Le catalogue NES definit JOY1 a $4016, et example_bomberman-nes
            // utilise "JOY1:" comme etiquette de boucle. Refuser l'etiquette parce
            // qu'un nom de confort existe deja rendait le programme reel
            // inassemblable. Le source fait autorite sur ses propres etiquettes.
            AssemblyResult result = AssembleWithPredefined(".org $C000\nJOY1: nop\n        jmp JOY1\n",
                new Dictionary<string, long> { { "JOY1", 0x4016 } });

            Assert.IsTrue(result.Success, Describe(result));
            Assert.AreEqual(0xC000, SingleRelocation(result).Value,
                "JOY1 vaut l'adresse de l'etiquette, pas $4016");
        }

        [TestMethod]
        public void UnSymboleMaterielNonRedefiniResteUtilisable()
        {
            AssemblyResult result = AssembleWithPredefined(".org $C000\n        lda PPUSTATUS\n",
                new Dictionary<string, long> { { "PPUSTATUS", 0x2002 } });

            Assert.IsTrue(result.Success, Describe(result));
            Assert.AreEqual(0x2002, SingleRelocation(result).Value,
                "le symbole materiel non redefini s'applique");
        }

        [TestMethod]
        public void ReduireUnSymboleMaterielPuisLeRedefinirLaisseUneSeuleDefinition()
        {
            // Le nom predefini est consomme par la premiere definition du source :
            // ecrire la meme etiquette deux fois doit rester un doublon, sinon on
            // aurait achete la levee de cette erreur en la supprimant partout.
            AssemblyResult result = AssembleWithPredefined(".org $C000\nJOY1: nop\nJOY1: nop\n",
                new Dictionary<string, long> { { "JOY1", 0x4016 } });

            Assert.IsFalse(result.Success, "un doublon dans le source reste un doublon");
        }

        private static AssemblyResult AssembleWithPredefined(string source, Dictionary<string, long> predefined)
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string sourcePath = System.IO.Path.Combine(temp.Path, "unit.asm");
                File.WriteAllText(sourcePath, source);
                AssemblerOptions options = new AssemblerOptions();
                options.Cpu = CpuFactory.Create("6502");
                options.PredefinedSymbols = predefined;
                return new AssemblerFactory().Create(options)
                    .Assemble(sourcePath, System.IO.Path.Combine(temp.Path, "unit.o"));
            }
        }

        private static RelocationRecord SingleRelocation(AssemblyResult result)
        {
            Assert.AreEqual(1, result.Relocations.Count,
                "une seule relocation attendue, obtenue : " + result.Relocations.Count);
            return result.Relocations[0];
        }

        private static string Describe(AssemblyResult result)
        {
            string text = string.Empty;
            for (int i = 0; i < result.Diagnostics.Count; i++)
                text += result.Diagnostics[i].Message + " | ";
            return text;
        }

        private sealed class RecordingConsole : IConsoleOutput
        {
            public string Output { get; private set; }
            public string Error { get; private set; }

            public RecordingConsole()
            {
                Output = string.Empty;
                Error = string.Empty;
            }

            public void WriteLine(string value) { Output += value + "\n"; }
            public void WriteError(string value) { Error += value + "\n"; }
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }
            public TemporaryDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinASM65TargetTests_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
            public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
        }
    }
}
