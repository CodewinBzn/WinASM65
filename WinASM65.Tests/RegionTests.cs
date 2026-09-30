using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Segments;
using WinASM65.Targets;

namespace WinASM65.Tests
{
    /// <summary>
    /// Memoire multi-regions configurable (T6). Une region est declarative : elle
    /// dit ce que la cible attend, elle ne place aucun octet et ne remplace pas le
    /// <c>.org</c>. Le seul effet observable est la validation croisee.
    /// </summary>
    [TestClass]
    public class RegionTests
    {
        private const string NesConfig = @"{
  ""Target"": {
    ""System"": ""nes"",
    ""Regions"": [
      { ""Name"": ""PRG0"", ""Address"": ""$8000"", ""Size"": ""$4000"", ""Bank"": 0, ""Type"": ""ro"" },
      { ""Name"": ""PRG1"", ""Address"": ""$C000"", ""Size"": ""$4000"", ""Bank"": 1, ""Type"": ""ro"" },
      { ""Name"": ""OAM"",   ""Address"": ""$0200"", ""Size"": ""$0100"", ""Bank"": 0, ""Type"": ""rw"" },
      { ""Name"": ""VARS"",  ""Address"": ""$0300"", ""Size"": ""$0080"", ""Type"": ""bss"" }
    ]
  }
}";

        private const string ConfigWithoutRegions = @"{
  ""Target"": { ""System"": ""raw"" }
}";

        /// <summary>Deux banques PRG laissees entre $BF00 et $BFFF, donc un trou.</summary>
        private const string GapConfig = @"{
  ""Target"": { ""Regions"": [
    { ""Name"": ""PRG0"", ""Address"": ""$8000"", ""Size"": ""$3F00"", ""Bank"": 0, ""Type"": ""ro"" },
    { ""Name"": ""PRG1"", ""Address"": ""$C000"", ""Size"": ""$4000"", ""Bank"": 1, ""Type"": ""ro"" }
  ] }
}";

        #region Lecture de la configuration

        [TestMethod]
        public void ConfigurationSansRegions_LaCarteEstVide()
        {
            ConfigFile config = ReadConfig(ConfigWithoutRegions);
            Assert.AreEqual(0, config.Target.Regions.Length, "une configuration sans section Regions reste valide");

            MemoryMap map;
            IReadOnlyList<Diagnostic> errors;
            Assert.IsTrue(MemoryMap.TryBuild(config.Target.Regions, out map, out errors));
            Assert.IsTrue(map.IsEmpty);
            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        public void RegionsDeclarees_LireAdresseTailleBanqueTypeEtTypeParDefaut()
        {
            ConfigFile config = ReadConfig(NesConfig);
            Assert.AreEqual(4, config.Target.Regions.Length);

            // Les adresses restent des chaines : c'est ce qui permet d'ecrire $8000.
            Assert.AreEqual("$8000", config.Target.Regions[0].Address);
            Assert.AreEqual("$4000", config.Target.Regions[0].Size);
            Assert.AreEqual(0, config.Target.Regions[0].Bank.Value);
            Assert.AreEqual("ro", config.Target.Regions[0].Type);
            Assert.IsNull(config.Target.Regions[0].Load, "Load est optionnel : il vaut l'adresse de la region");

            MemoryMap map = Build(NesConfig);
            Assert.IsFalse(map.IsEmpty);
            Assert.AreEqual(0x8000, map.Regions[0].Start);
            Assert.AreEqual(0xC000, map.Regions[0].End, "la borne haute est exclusive");
            Assert.AreEqual(0x4000, map.Regions[0].Size);
            Assert.AreEqual(0, map.Regions[0].Bank.Value);
            Assert.AreEqual(RegionType.Ro, map.Regions[0].Type);
            Assert.AreEqual(0x8000, map.Regions[0].LoadAddress, "Load absent vaut l'adresse de la region");
            Assert.AreEqual(RegionType.Rw, map.Regions[2].Type);
            Assert.AreEqual(RegionType.Bss, map.Regions[3].Type);
        }

        [TestMethod]
        public void TypeParDefaut_EstRo()
        {
            MemoryMap map = Build(@"{ ""Target"": { ""Regions"": [ { ""Name"": ""X"", ""Address"": ""$10"" } ] } }");
            Assert.AreEqual(RegionType.Ro, map.Regions[0].Type);
            Assert.IsFalse(map.Regions[0].HasSize);
            Assert.AreEqual(MemoryMap.AddressSpaceSize, map.Regions[0].End, "sans Size, la region va jusqu'au sommet");
        }

        [TestMethod]
        public void TypeInconnu_NommeLesTypesAcceptes()
        {
            MemoryMap map;
            IReadOnlyList<Diagnostic> errors;
            Assert.IsFalse(MemoryMap.TryBuild(new[] { new RegionConf { Name = "X", Address = "$10", Type = "rom" } }, out map, out errors));
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains(errors[0].Message, "ro/rw/bss");
        }

        [TestMethod]
        public void RegionSansAdresse_EstUnDiagnostic()
        {
            MemoryMap map;
            IReadOnlyList<Diagnostic> errors;
            Assert.IsFalse(MemoryMap.TryBuild(new[] { new RegionConf { Name = "X" } }, out map, out errors));
            StringAssert.Contains(errors[0].Message, "declares no Address");
        }

        [TestMethod]
        public void RegionQuiDepasseLaFinDeLEspaceDAdressage_EstUnDiagnostic()
        {
            MemoryMap map;
            IReadOnlyList<Diagnostic> errors;
            Assert.IsFalse(MemoryMap.TryBuild(
                new[] { new RegionConf { Name = "X", Address = "$F000", Size = "$2000" } }, out map, out errors));
            StringAssert.Contains(errors[0].Message, "$F000");
            Assert.AreEqual(0, map.Regions.Count, "une region rejetee n'entre pas dans la carte");
        }

        [TestMethod]
        public void RegionsQuiSeChevauchent_EstUnDiagnostic()
        {
            MemoryMap map;
            IReadOnlyList<Diagnostic> errors;
            Assert.IsFalse(MemoryMap.TryBuild(new[]
            {
                new RegionConf { Name = "A", Address = "$8000", Size = "$5000" },
                new RegionConf { Name = "B", Address = "$9000", Size = "$4000" }
            }, out map, out errors));
            StringAssert.Contains(errors[0].Message, "overlap");
        }

        [TestMethod]
        public void NomsDeRegionsDupliques_EstUnDiagnostic()
        {
            MemoryMap map;
            IReadOnlyList<Diagnostic> errors;
            Assert.IsFalse(MemoryMap.TryBuild(new[]
            {
                new RegionConf { Name = "A", Address = "$8000", Size = "$10" },
                new RegionConf { Name = "a", Address = "$9000", Size = "$10" }
            }, out map, out errors));
            StringAssert.Contains(errors[0].Message, "Duplicate region name");
        }

        [TestMethod]
        public void ChargementCroise_ExposeLAdresseDeStockage()
        {
            MemoryMap map = Build(@"{ ""Target"": { ""Regions"": [
                { ""Name"": ""CODE"", ""Address"": ""$0801"", ""Size"": ""$1000"", ""Load"": ""$2000"", ""Type"": ""ro"" } ] } }");

            Assert.AreEqual(0x0801, map.Regions[0].Start, "la fenetre de la region est l'adresse d'execution");
            Assert.AreEqual(0x2000, map.Regions[0].LoadAddress, "Load est l'adresse de stockage");

            MemoryRegion byLoad;
            Assert.IsTrue(map.TryFindByLoad(0x2000, out byLoad));
            Assert.AreEqual("CODE", byLoad.Name);

            MemoryRegion byRun;
            Assert.IsTrue(map.TryFind(0x0801, out byRun));
            Assert.AreEqual("CODE", byRun.Name);
        }

        [TestMethod]
        public void SymbolesDeRegions_ExposentLesBornes()
        {
            IDictionary<string, long> symbols = Build(NesConfig).BuildPredefinedSymbols();
            Assert.AreEqual(0x8000L, symbols["PRG0_START"]);
            Assert.AreEqual(0xBFFFL, symbols["PRG0_END"]);
            Assert.AreEqual(0x4000L, symbols["PRG0_SIZE"]);
            Assert.AreEqual(0x8000L, symbols["PRG0_LOAD"], "Load absent : la region est stockee sur place");
            Assert.AreEqual(0x8000L, symbols["PRG0_RUN"]);
            Assert.AreEqual(0x80L, symbols["VARS_SIZE"]);
            Assert.AreEqual(0x300L, symbols["VARS_START"]);
        }

        #endregion

        #region Validation croisee des .org

        [TestMethod]
        public void OrgDansUneRegion_EstAccepte()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                MemoryMap map = Build(NesConfig);
                AssemblyResult result = Assemble(temp, "in.asm", ".org $C000\nlda #$01\nrts\n", map);
                Assert.IsTrue(result.Success);
                Assert.AreEqual(0, result.Diagnostics.Count);
                CollectionAssert.AreEqual(new byte[] { 0xA9, 0x01, 0x60 }, result.OutputBytes);
            }
        }

        [TestMethod]
        public void OrgHorsDeTouteRegion_DiagnostiqueLaRegionAttendue()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = temp.File("out.asm");
                File.WriteAllText(source, ".org $5000\nlda #$01\n");
                MemoryMap map = Build(NesConfig);

                AssemblyResult result;
                using (MemoryMapScope.Activate(map))
                    result = new AssemblerEngine().Assemble(source, temp.File("out.o"));

                Assert.IsFalse(result.Success, "un .org hors de toute region doit faire echouer la construction");
                Assert.AreEqual(1, result.Diagnostics.Count);
                Diagnostic diagnostic = result.Diagnostics[0];
                StringAssert.Contains(diagnostic.Message, "$5000");
                StringAssert.Contains(diagnostic.Message, "outside every declared region");
                StringAssert.Contains(diagnostic.Message, "PRG0 $8000-$BFFF bank 0 (ro)");
                StringAssert.Contains(diagnostic.Message, "PRG1 $C000-$FFFF bank 1 (ro)");
                Assert.AreEqual(source, diagnostic.Location.FilePath, "le diagnostic nomme le fichier source");
                Assert.AreEqual(1, diagnostic.Location.LineNumber);
            }
        }

        [TestMethod]
        public void RegionAttendue_DansUnTrou_EstCelleQuiSuitLeTrou()
        {
            MemoryMap map = Build(GapConfig);
            DiagnosticReporter reporter = new DiagnosticReporter();
            map.ValidateOrigin(0xBF00, new SourceLocation("x.asm", 1), reporter);

            Assert.AreEqual(1, reporter.Diagnostics.Count);
            StringAssert.Contains(reporter.Diagnostics[0].Message,
                "Expected region: PRG0 $8000-$BEFF bank 0 (ro)",
                "l'adresse est sous PRG1 mais apres PRG0 : c'est PRG0 qui est attendu");
        }

        [TestMethod]
        public void RegionAttendue_SousToutesLesRegions_EstLaPremiereTriee()
        {
            MemoryMap map = Build(NesConfig);
            MemoryRegion expected = map.ExpectedFor(0x0100);
            Assert.AreEqual("OAM", expected.Name, "la regle est la region triee dont le debut est le plus bas");
            Assert.AreEqual("PRG1", map.ExpectedFor(0xE000).Name);
            Assert.AreEqual("PRG0", Build(GapConfig).ExpectedFor(0xBF80).Name, "$BF80 est dans le trou de la config a trou");
        }

        [TestMethod]
        public void OrgDansUneRegionBss_DiagnostiqueQueLaRegionNePortePasDOctets()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                AssemblyResult result = Assemble(temp, "bss.asm", ".org $0300\nnop\n", Build(NesConfig));
                Assert.IsFalse(result.Success);
                StringAssert.Contains(result.Diagnostics[0].Message, "VARS");
                StringAssert.Contains(result.Diagnostics[0].Message, "bss");
            }
        }

        [TestMethod]
        public void SansRegions_UnOrgNImporteOuNeChangeRien()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                AssemblyResult withMap = Assemble(temp, "map.asm", ".org $5000\nlda #$01\n", Build(NesConfig));
                AssemblyResult withoutMap = Assemble(temp, "nomap.asm", ".org $5000\nlda #$01\n", null);

                Assert.IsFalse(withMap.Success);
                Assert.IsTrue(withoutMap.Success, "sans region declaree, aucune validation n'a lieu");
                CollectionAssert.AreEqual(withoutMap.OutputBytes, withMap.OutputBytes,
                    "le diagnostic ne change pas les octets produits");
            }
        }

        #endregion

        #region Le mode direct reste le contrat

        [TestMethod]
        public void RegionsSeulementDeclarees_LaSortieEstIdentiqueAuGravageDirect()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                const string source = ".org $8000\nlda #$05\nsta $10\nrts\n";

                AssemblyResult without = Assemble(temp, "a.asm", source, null);
                AssemblyResult with = Assemble(temp, "b.asm", source, Build(NesConfig));
                Assert.IsTrue(without.Success);
                Assert.IsTrue(with.Success);
                CollectionAssert.AreEqual(without.OutputBytes, with.OutputBytes);

                // Et de bout en bout, en passant par la ligne de commande et la publication.
                // Meme cible, meme format, seule la section Regions differe.
                byte[] plain = Publish(temp, "plain.prg", ".org $0801\nlda #$05\nrts\n",
                    @"{ ""Target"": { ""System"": ""c64"" } }");
                byte[] declared = Publish(temp, "declared.prg", ".org $0801\nlda #$05\nrts\n",
                    @"{ ""Target"": { ""System"": ""c64"", ""Regions"": [
                          { ""Name"": ""CODE"", ""Address"": ""$0801"", ""Size"": ""$07FF"", ""Type"": ""ro"" } ] } }");
                CollectionAssert.AreEqual(plain, declared);
            }
        }

        [TestMethod]
        public void RegionBss_ReserveDeLEspaceEtNOccupeAucunOctetDuFichier()
        {
            MemoryMap map = Build(NesConfig);
            Assert.AreEqual(0x80L, map.TotalBssSize, "128 octets reserves");
            Assert.AreEqual(0x80L, map.ReservedAt(0x0300));
            Assert.AreEqual(0x01L, map.ReservedAt(0x037F));
            Assert.AreEqual(0L, map.ReservedAt(0x0380), "hors de la region, rien n'est reserve");
            Assert.AreEqual(0L, map.ReservedAt(0x8000), "une region ro ne reserve pas, elle occupe des octets");

            // Cote source : le bss se reserve par .res dans le .memarea de la region,
            // et n'emet rien. Seuls les trois octets du LDA arrivent dans le fichier.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                IDictionary<string, long> symbols = map.BuildPredefinedSymbols();
                Dictionary<string, long> injected = new Dictionary<string, long>(symbols);
                string source = temp.File("vars.asm");
                File.WriteAllText(source, ".memarea VARS_START\nvars .res VARS_SIZE\n.org $8000\nlda vars\nrts\n");
                AssemblyResult result;
                using (MemoryMapScope.Activate(map))
                    result = new AssemblerEngine(predefinedSymbols: injected).Assemble(source, temp.File("vars.o"));

                Assert.IsTrue(result.Success);
                CollectionAssert.AreEqual(new byte[] { 0xAD, 0x00, 0x03, 0x60 }, result.OutputBytes,
                    "128 octets de bss reserves, zero octet emis");
            }
        }

        [TestMethod]
        public void RegionIncoherente_LaLigneDeCommandeRefuseLaConstruction()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string config = temp.File("config.json");
                File.WriteAllText(config, @"{ ""Target"": { ""Regions"": [ { ""Name"": ""X"", ""Address"": ""$8000"", ""Type"": ""rom"" } ] } }");
                RecordingConsole console = new RecordingConsole();
                int exitCode = new CommandLineApplication(new AssemblerFactory(), new JsonConfigurationReader(),
                    new BinaryCombiner(), console, new ExecutablePublisher())
                    .Run(new[] { "-c", config, "-f", temp.File("game.asm"), "-o", temp.File("game.o") });

                Assert.AreEqual(1, exitCode);
                StringAssert.Contains(console.Error, "ro/rw/bss");
            }
        }

        [TestMethod]
        public void ConfigurationSansRegions_LaLigneDeCommandeSeComporteCommeAvant()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string config = temp.File("config.json");
                File.WriteAllText(config, ConfigWithoutRegions);
                string source = temp.File("game.asm");
                File.WriteAllText(source, ".org $5000\nlda #$01\nrts\n");

                RecordingConsole console = new RecordingConsole();
                int exitCode = new CommandLineApplication(new AssemblerFactory(), new JsonConfigurationReader(),
                    new BinaryCombiner(), console, new ExecutablePublisher())
                    .Run(new[] { "-c", config, "-f", source, "-o", temp.File("game.o") });

                Assert.AreEqual(0, exitCode);
                Assert.AreEqual(string.Empty, console.Error);
                CollectionAssert.AreEqual(new byte[] { 0xA9, 0x01, 0x60 }, File.ReadAllBytes(temp.File("game.o")));
            }
        }

        #endregion

        private static ConfigFile ReadConfig(string json)
        {
            string path = Path.Combine(Path.GetTempPath(), "WinASM65Regions_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, json);
                return new JsonConfigurationReader().Read(path);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static MemoryMap Build(string json)
        {
            MemoryMap map;
            IReadOnlyList<Diagnostic> errors;
            Assert.IsTrue(MemoryMap.TryBuild(ReadConfig(json).Target.Regions, out map, out errors),
                Describe(errors));
            return map;
        }

        private static AssemblyResult Assemble(TemporaryDirectory temp, string fileName, string source, MemoryMap map)
        {
            string path = temp.File(fileName);
            File.WriteAllText(path, source);
            using (MemoryMapScope.Activate(map))
                return new AssemblerEngine().Assemble(path, temp.File(Path.ChangeExtension(fileName, ".o")));
        }

        private static byte[] Publish(TemporaryDirectory temp, string outputName, string source, string configJson)
        {
            string sourcePath = temp.File("src.asm");
            File.WriteAllText(sourcePath, source);
            string output = temp.File(outputName);

            List<string> args = new List<string>();
            if (configJson != null)
            {
                string config = temp.File("config.json");
                File.WriteAllText(config, configJson);
                args.Add("-c");
                args.Add(config);
            }
            args.Add("-f");
            args.Add(sourcePath);
            args.Add("-o");
            args.Add(output);

            RecordingConsole console = new RecordingConsole();
            int exitCode = new CommandLineApplication(new AssemblerFactory(), new JsonConfigurationReader(),
                new BinaryCombiner(), console, new ExecutablePublisher())
                .Run(args.ToArray());
            Assert.AreEqual(0, exitCode, console.Error);
            return File.ReadAllBytes(output);
        }

        private static string Describe(IReadOnlyList<Diagnostic> diagnostics)
        {
            string text = string.Empty;
            foreach (Diagnostic diagnostic in diagnostics)
                text += diagnostic.ToString() + " | ";
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

            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinASM65RegionTests_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string File(string name) { return System.IO.Path.Combine(Path, name); }

            public void Dispose()
            {
                if (Directory.Exists(Path)) Directory.Delete(Path, true);
            }
        }
    }
}
