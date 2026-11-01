using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Output;
using WinASM65.Projects;
using WinASM65.Segments;

namespace WinASM65.Tests
{
    /// <summary>
    /// The NES example game, built through the library rather than through the
    /// command line, and what a listing knows once the project has been built.
    ///
    /// <para>
    /// <c>example_rock_patrol</c> is genuinely multi-file: <c>game.asm</c> uses the
    /// tables and the designer's exports <c>data.asm</c> defines, and
    /// <c>vectors.asm</c> is three words, <c>NMI</c>, <c>RESET</c> and <c>IRQ</c>,
    /// all defined in <c>game.asm</c>. Nothing in them declares an import, so it can
    /// tell a working multi-file build from one that quietly assembled to zero.
    /// </para>
    ///
    /// <para>
    /// Each test builds a copy of the example in a temporary directory, so the side
    /// files a build writes beside its sources never reach the working tree. The
    /// image is pinned by its SHA-256, the value <see cref="GoldenOutputTests"/>
    /// pins too.
    /// </para>
    /// </summary>
    [TestClass]
    public class ProjectSessionTests
    {
        /// <summary>The SHA-256 of the Rock Patrol image (GoldenOutputTests pins the same).</summary>
        private const string ExampleGameHash =
            "F99BB587D623D0B88523424771359E6D94CE58622D69DF51F512CC4276EE0B25";

        #region The example game, end to end

        [TestMethod]
        public void TheExampleGameAssemblesIntoItsPinnedImage()
        {
            using (TemporaryDirectory project = ExampleGameCopy())
            {
                ProjectBuildResult build = BuildExampleGame(project.Path);

                Assert.IsTrue(build.Success, "the example game must build: " + Describe(build.Diagnostics));
                Assert.AreEqual(40976, build.Image.Length,
                    "the image is the header, both halves of the program, the vectors and the tiles");
                Assert.AreEqual(ExampleGameHash, HashOf(build.Image), "the golden NES image has changed");
            }
        }

        [TestMethod]
        public void TheExampleGameIsBuiltFromItsSourcesInTheOrderTheConfigurationDeclares()
        {
            using (TemporaryDirectory project = ExampleGameCopy())
            {
                ProjectBuildResult build = BuildExampleGame(project.Path);

                Assert.IsTrue(build.Success, Describe(build.Diagnostics));
                Assert.AreEqual(4, build.Units.Count, "one unit per declared input");
                Assert.AreEqual("header.asm", Path.GetFileName(build.Units[0].SourceFile));
                Assert.AreEqual("data.asm", Path.GetFileName(build.Units[1].SourceFile));
                Assert.AreEqual("game.asm", Path.GetFileName(build.Units[2].SourceFile));
                Assert.AreEqual("vectors.asm", Path.GetFileName(build.Units[3].SourceFile));

                // The vectors are the unit that proves the order: they name three
                // routines the game defines, and they are declared last.
                Assert.AreEqual(0, build.Units[0].Dependencies.Count, "the header depends on nothing");
                Assert.AreEqual(0, build.Units[1].Dependencies.Count, "the data depends on nothing");
                Assert.AreEqual(1, build.Units[2].Dependencies.Count, "the game depends on the data");
                Assert.AreEqual(1, build.Units[3].Dependencies.Count, "the vectors depend on the game");

                // And the names the units defined are what the session now knows.
                Assert.IsTrue(build.Units[1].Symbols.Count > 0, "the data defines the tables");
                Assert.IsTrue(build.Units[2].Symbols.Count > 0, "the game defines the routines");
            }
        }

        [TestMethod]
        public void TheVectorsGetTheAddressesTheGameDefined()
        {
            using (TemporaryDirectory project = ExampleGameCopy())
            {
                ExampleGameBuild build = BuildExampleGameAndKeepSession(project.Path, null);

                Assert.IsTrue(build.Result.Success, Describe(build.Result.Diagnostics));
                Assert.AreEqual(0xC0DF, build.SymbolOr("NMI"), "NMI is a label of game.asm");
                Assert.AreEqual(0xC079, build.SymbolOr("RESET"),
                    "RESET follows the metasprites at the start of the $C000 half");
                Assert.AreEqual(0xC103, build.SymbolOr("IRQ"), "IRQ is the rti that ends the NMI");

                // The last six program bytes of the image are those three addresses.
                byte[] image = build.Result.Image;
                int vectors = 16 + 0x8000 - 6;
                Assert.AreEqual(build.SymbolOr("NMI"), image[vectors] | (image[vectors + 1] << 8));
                Assert.AreEqual(build.SymbolOr("RESET"), image[vectors + 2] | (image[vectors + 3] << 8));
                Assert.AreEqual(build.SymbolOr("IRQ"), image[vectors + 4] | (image[vectors + 5] << 8));
            }
        }

        #endregion

        #region What the listing knows after a build

        [TestMethod]
        public void AListingSeesTheNamesAnotherUnitOfTheProjectDefines()
        {
            using (TemporaryDirectory project = ExampleGameCopy())
            {
                string game = Path.Combine(project.Path, "src", "game.asm");
                ProjectSession session = ProjectSession.Open(Path.Combine(project.Path, "config.json"));

                // Before the build the session knows nothing, so the names game.asm
                // uses and data.asm defines are reported.
                SourceListing before = session.ListSourceFile(game);
                Assert.IsTrue(ReportsUndefinedSymbol(before, "RockLanes"),
                    "without the project's names a name another unit defines reads as undefined");
                Assert.IsTrue(ReportsUndefinedSymbol(before, "FallSpeed"), "FallSpeed");
                Assert.IsTrue(ReportsUndefinedSymbol(before, "SpawnGap"), "SpawnGap");
                Assert.IsTrue(ReportsUndefinedSymbol(before, "palettes"), "palettes");

                // The session keeps the names in memory, which is the point: what the
                // listing below reads is what the build learned, not a file it re-read.
                BuildExampleGameAndKeepSession(project.Path, session);

                SourceListing after = session.ListSourceFile(game);
                Assert.IsFalse(ReportsUndefinedSymbol(after, "RockLanes"),
                    "RockLanes is defined in data.asm, and the project says so");
                Assert.IsFalse(ReportsUndefinedSymbol(after, "FallSpeed"), "FallSpeed");
                Assert.IsFalse(ReportsUndefinedSymbol(after, "SpawnGap"), "SpawnGap");
                Assert.IsFalse(ReportsUndefinedSymbol(after, "palettes"), "palettes");
            }
        }

        [TestMethod]
        public void AListingAfterABuildStillReportsTheDefectInTheFileItself()
        {
            // "RESET" alone on a line is a label written without its colon, which the
            // assembler reads as a call to a macro of that name. The project defines
            // RESET, but no amount of cross-file knowledge makes that line right, and a
            // session that hid it would be hiding a real defect -- so the diagnostic
            // has to survive the widening.
            using (TemporaryDirectory project = ExampleGameCopy())
            {
                ProjectSession session = ProjectSession.Open(Path.Combine(project.Path, "config.json"));
                BuildExampleGameAndKeepSession(project.Path, session);

                SourceListing listing = session.ListSourceText(".org $C000\nRESET\n", "probe.asm");

                Assert.IsTrue(Reports(listing, ErrorCodes.MACRO_NOT_EXISTS),
                    "a label written without its colon is a defect of the file, and stays reported");
            }
        }

        [TestMethod]
        public void AListingAfterABuildStillReportsANameNoUnitDefines()
        {
            // The other half of the same guarantee. A project listing knows more
            // names than a single-file one; it must not have become a listing that
            // reports nothing.
            using (TemporaryDirectory project = ExampleGameCopy())
            {
                ProjectSession session = ProjectSession.Open(Path.Combine(project.Path, "config.json"));
                BuildExampleGameAndKeepSession(project.Path, session);

                SourceListing listing = session.ListSourceText(
                    ".org $C000\nJMP NEVER_DEFINED_IN_THIS_PROJECT\n", "probe.asm");

                Assert.IsFalse(listing.Success, "a name no unit of the project defines must be reported");
                Assert.IsTrue(ReportsUndefinedSymbol(listing, "NEVER_DEFINED_IN_THIS_PROJECT"),
                    "and it must be reported by name");
            }
        }

        [TestMethod]
        public void AProjectListingStillResolvesAForwardReference()
        {
            using (TemporaryDirectory project = ExampleGameCopy())
            {
                ProjectSession session = ProjectSession.Open(Path.Combine(project.Path, "config.json"));
                BuildExampleGameAndKeepSession(project.Path, session);

                SourceListing listing = session.ListSourceText(
                    ".org $C000\n        jmp Later\nLater:\n        nop\n", "probe.asm");

                Assert.IsTrue(listing.Success, Describe(listing.Assembly.Diagnostics));
                Assert.AreEqual(0, listing.Assembly.Diagnostics.Count, Describe(listing.Assembly.Diagnostics));
                CollectionAssert.AreEqual(new byte[] { 0x4C, 0x03, 0xC0, 0xEA },
                    listing.Assembly.OutputBytes,
                    "knowing the project's names must not stop a forward reference resolving");
            }
        }

        [TestMethod]
        public void AListingPlacesAUnitWithoutOrgAtTheTargetsLoadAddress()
        {
            // The build assembles a unit with no .org at the target's load address;
            // a listing that ignored the target would show every line elsewhere.
            ProjectSession session = ProjectSession.Open(new ConfigFile
            {
                Target = new TargetConf { System = "c64", Format = "prg", LoadAddress = "$0801" }
            }, Path.GetTempPath());

            SourceListing listing = session.ListSourceText("  lda #$01\n  rts\n", "probe.asm");

            Assert.IsTrue(listing.Success, Describe(listing.Assembly.Diagnostics));
            ListingRow first = listing.Rows.First(r => r.Kind == ListingRowKind.Instruction);
            Assert.AreEqual(0x0801, first.Address);
        }

        [TestMethod]
        public void AListingOfAFileThatIsNotThereSaysSo()
        {
            using (TemporaryDirectory project = ExampleGameCopy())
            {
                ProjectSession session = ProjectSession.Open(Path.Combine(project.Path, "config.json"));

                SourceListing listing = session.ListSourceFile(Path.Combine(project.Path, "no-such-file.asm"));

                Assert.IsFalse(listing.Success);
                Assert.IsTrue(Reports(listing, ErrorCodes.FILE_NOT_EXISTS),
                    "reported the way the assembler reports a missing source");
            }
        }

        #endregion

        #region A project of two units, built where the sources live

        [TestMethod]
        public void ANameThatCrossesUnitFilesResolvesThroughTheSession()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                // The cross-file case with no import anywhere, which is how this
                // assembler has always been asked to do it: provider defines, the
                // consumer calls, and the only channel between them is the symbol
                // file of the first pass.
                string provider = temp.File("provider.asm");
                string consumer = temp.File("consumer.asm");
                File.WriteAllText(provider, ".org $8000\nRoutine:\n        rts\n");
                File.WriteAllText(consumer, ".org $8000\n        jsr Routine\n        rts\n");

                WriteConfiguration(temp.Path, new[]
                {
                    new Segment { FileName = "provider.asm", Dependencies = new string[0] },
                    new Segment { FileName = "consumer.asm", Dependencies = new[] { "provider.asm" } }
                }, new[]
                {
                    new FileConf { FileName = "consumer.o" }
                });

                ProjectBuildResult build = ProjectSession.Open(temp.File("config.json")).Build();

                Assert.IsTrue(build.Success, Describe(build.Diagnostics));
                CollectionAssert.AreEqual(new byte[] { 0x20, 0x00, 0x80, 0x60 }, build.Image,
                    "JSR $8000 then RTS: the name has to come back with the address the provider gave it");
            }
        }

        [TestMethod]
        public void AUnitThatNamesSomethingNoUnitDefinesFailsTheBuild()
        {
            // The multi-file path is the one place the assembler is allowed to leave
            // a name unresolved while a source is read, so it is also the one place
            // a zero could survive into a burnable image. The orchestrator has to
            // ask the question once every unit is in; this is that question.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string provider = temp.File("provider.asm");
                string consumer = temp.File("consumer.asm");
                File.WriteAllText(provider, ".org $8000\nRoutine:\n        rts\n");
                File.WriteAllText(consumer, ".org $8000\n        jsr NeverDefinedAnywhere\n        rts\n");

                WriteConfiguration(temp.Path, new[]
                {
                    new Segment { FileName = "provider.asm", Dependencies = new string[0] },
                    new Segment { FileName = "consumer.asm", Dependencies = new[] { "provider.asm" } }
                }, new[]
                {
                    new FileConf { FileName = "consumer.o" }
                });

                ProjectBuildResult build = ProjectSession.Open(temp.File("config.json")).Build();

                Assert.IsFalse(build.Success, "a name no unit defines must not assemble clean");
                Assert.AreEqual(0, build.Image.Length, "and no image is produced");
            }
        }

        [TestMethod]
        public void AFailedBuildLeavesNothingBehindThatFailsTheNextOne()
        {
            // A unit that named something nobody defined leaves its .Unsolved and
            // .UnsolvedExpr files on disk. Once the name is fixed the unit has
            // nothing left to resolve, so it writes no such files -- and the stale
            // ones used to stay, be read back by the orchestrator, and fail a build
            // that had nothing wrong with it.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string provider = temp.File("provider.asm");
                string consumer = temp.File("consumer.asm");
                File.WriteAllText(provider, ".org $8000\nRoutine:\n        rts\n");
                File.WriteAllText(consumer, ".org $8000\n        jsr Misspelt\n        rts\n");
                WriteConfiguration(temp.Path, new[]
                {
                    new Segment { FileName = "provider.asm", Dependencies = new string[0] },
                    new Segment { FileName = "consumer.asm", Dependencies = new[] { "provider.asm" } }
                }, new[]
                {
                    new FileConf { FileName = "consumer.o" }
                });
                Assert.IsFalse(ProjectSession.Open(temp.File("config.json")).Build().Success,
                    "the first build names something nobody defines");
                Assert.IsTrue(File.Exists(temp.File("consumer.Unsolved")),
                    "and leaves the name it could not resolve on disk");

                // Fixed by defining the name in the unit itself: nothing is left for
                // the orchestrator to resolve, so the unit writes no .Unsolved at all.
                File.WriteAllText(consumer, ".org $8000\nMisspelt:\n        jsr Misspelt\n        rts\n");
                ProjectBuildResult build = ProjectSession.Open(temp.File("config.json")).Build();

                Assert.IsTrue(build.Success, Describe(build.Diagnostics));
                CollectionAssert.AreEqual(new byte[] { 0x20, 0x00, 0x80, 0x60 }, build.Image);
                Assert.IsFalse(File.Exists(temp.File("consumer.Unsolved")), "no stale .Unsolved is left");
                Assert.IsFalse(File.Exists(temp.File("consumer.UnsolvedExpr")), "no stale .UnsolvedExpr is left");
            }
        }

        [TestMethod]
        public void AProjectInADirectoryWhosePathHasADotInItStillBuilds()
        {
            // Every side file of a build is named after its source. The name used to
            // be the path up to its first dot, so in a directory called "my.game"
            // all three units of this project asked for the same object file, each
            // overwriting the last, and the build either read the wrong bytes or
            // found no object file at all. Nothing about the program was wrong; only
            // where it lived.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string project = Path.Combine(temp.Path, "my.game");
                Directory.CreateDirectory(project);

                File.WriteAllText(Path.Combine(project, "bank0.asm"),
                    ".org $8000\nRoutine:\n        rts\n");
                File.WriteAllText(Path.Combine(project, "bank1.asm"),
                    ".org $8000\n        jsr Routine\n        rts\n");

                WriteConfiguration(project, new[]
                {
                    new Segment { FileName = "bank0.asm", Dependencies = new string[0] },
                    new Segment { FileName = "bank1.asm", Dependencies = new[] { "bank0.asm" } }
                }, new[]
                {
                    new FileConf { FileName = "bank1.o" }
                });

                ProjectSession session = ProjectSession.Open(Path.Combine(project, "config.json"));
                ProjectBuildResult build = session.Build();

                Assert.IsTrue(build.Success, Describe(build.Diagnostics));
                CollectionAssert.AreEqual(new byte[] { 0x20, 0x00, 0x80, 0x60 }, build.Image);
                Assert.AreNotEqual(build.Units[0].ObjectFile, build.Units[1].ObjectFile,
                    "two units cannot share one object file, whatever the directory is called");
                Assert.IsTrue(File.Exists(Path.Combine(project, "bank0.o")), "bank0.o is where it belongs");
                Assert.IsTrue(File.Exists(Path.Combine(project, "bank1.o")), "bank1.o is where it belongs");
            }
        }

        [TestMethod]
        public void SideFilesGoWhereTheCallerAsks()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            using (TemporaryDirectory scratch = new TemporaryDirectory())
            {
                string provider = temp.File("provider.asm");
                string consumer = temp.File("consumer.asm");
                File.WriteAllText(provider, ".org $8000\nRoutine:\n        rts\n");
                File.WriteAllText(consumer, ".org $8000\n        jsr Routine\n        rts\n");
                WriteConfiguration(temp.Path, new[]
                {
                    new Segment { FileName = "provider.asm", Dependencies = new string[0] },
                    new Segment { FileName = "consumer.asm", Dependencies = new[] { "provider.asm" } }
                }, new[]
                {
                    new FileConf { FileName = "consumer.o" }
                });

                ProjectBuildResult build = ProjectSession.Open(temp.File("config.json"))
                    .Build(new ProjectBuildOptions { SideFileDirectory = scratch.Path });

                Assert.IsTrue(build.Success, Describe(build.Diagnostics));
                Assert.IsTrue(File.Exists(Path.Combine(scratch.Path, "provider.symb")),
                    "the symbol files go where the caller asked, not into the project");
                Assert.IsFalse(File.Exists(temp.File("provider.symb")),
                    "and not into the project either");
            }
        }

        [TestMethod]
        public void TheProjectDirectoryIsTheOneHoldingTheConfiguration()
        {
            // Not the process's working directory. A build driven from a test host or
            // a language server has a working directory that has nothing to do with
            // the project, and every relative name in the configuration has to mean
            // what it meant when the project was written.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                Directory.CreateDirectory(Path.Combine(temp.Path, "src"));
                string project = Path.Combine(temp.Path, "src");
                File.WriteAllText(Path.Combine(project, "code.asm"), ".org $8000\n        rts\n");
                WriteConfiguration(project, new[]
                {
                    new Segment { FileName = "code.asm", Dependencies = new string[0] }
                }, new[]
                {
                    new FileConf { FileName = "code.o" }
                });

                ProjectSession session = ProjectSession.Open(Path.Combine(project, "config.json"));

                Assert.AreEqual(project, session.BaseDirectory);
                Assert.AreEqual(1, session.SourceFiles.Count);
                Assert.IsTrue(Path.IsPathRooted(session.SourceFiles[0]),
                    "the sources are reported absolute, so a caller never has to guess the base");
            }
        }

        #endregion

        #region The surface itself

        [TestMethod]
        public void SideFilesAreNamedAfterTheWholeSourcePathAndNotThePartBeforeADot()
        {
            // The rule both the writer and the reader follow. It used to be the path
            // up to its first dot, which put every side file of every unit of a build
            // living at "C:\src\.kilo\work" on one name above the project.
            string dotted = Path.Combine("C:", "src", ".kilo", "work", "unit.asm");

            Assert.AreEqual(Path.Combine("C:", "src", ".kilo", "work", "unit"),
                SideFiles.BaseNameOf(dotted),
                "the directory is kept, so the file lands beside its source");

            // A name with two dots keeps the first: it is the extension that goes.
            Assert.AreEqual(Path.Combine("src", "a.b"), SideFiles.BaseNameOf(Path.Combine("src", "a.b.asm")));

            // A name with none is already what a side file is named after.
            Assert.AreEqual(Path.Combine("src", "unit"), SideFiles.BaseNameOf(Path.Combine("src", "unit")));

            // Beside the source when no directory is named, and in it when one is.
            Assert.AreEqual(Path.Combine("src", "unit.symb"),
                SideFiles.Locate(Path.Combine("src", "unit.symb"), null));
            Assert.AreEqual(Path.Combine(AppContext.BaseDirectory, "unit.symb"),
                SideFiles.Locate(Path.Combine("src", "unit.symb"), AppContext.BaseDirectory));
        }

        [TestMethod]
        public void OpeningAConfigurationThatIsNotThereSaysSo()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                try
                {
                    ProjectSession.Open(temp.File("no-such-config.json"));
                    Assert.Fail("opening a configuration that does not exist must not succeed");
                }
                catch (FileNotFoundException)
                {
                }
            }
        }

        [TestMethod]
        public void AProjectWithNothingToBuildSaysSo()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                File.WriteAllText(temp.File("config.json"), "{ \"Input\": [] }");

                ProjectBuildResult build = ProjectSession.Open(temp.File("config.json")).Build();

                Assert.IsFalse(build.Success);
                Assert.IsTrue(build.Image.Length == 0, "no image is invented out of no inputs");
            }
        }

        [TestMethod]
        public void TheImageIsOnlyWrittenWhenItIsAskedFor()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                File.WriteAllText(temp.File("code.asm"), ".org $8000\n        rts\n");
                WriteConfiguration(temp.Path, new[]
                {
                    new Segment { FileName = "code.asm", Dependencies = new string[0] }
                }, new[]
                {
                    new FileConf { FileName = "code.o" }
                });

                string image = temp.File("out.bin");
                ProjectSession session = ProjectSession.Open(temp.File("config.json"));

                ProjectBuildResult inMemory = session.Build();
                Assert.IsTrue(inMemory.Success, Describe(inMemory.Diagnostics));
                CollectionAssert.AreEqual(new byte[] { 0x60 }, inMemory.Image,
                    "the bytes are the answer an interface wants from F5");
                Assert.IsFalse(File.Exists(image), "and it wrote nothing it was not asked to write");

                ProjectBuildResult onDisk = session.Build(new ProjectBuildOptions { WriteImage = true });
                Assert.IsTrue(onDisk.Success, Describe(onDisk.Diagnostics));
                Assert.AreEqual(image, onDisk.ImagePath);
                Assert.IsTrue(File.Exists(image), "this time it was asked to");
                CollectionAssert.AreEqual(inMemory.Image, File.ReadAllBytes(image),
                    "the file holds what the caller was already holding");
            }
        }

        #endregion

        // ------------------------------------------------------------------ setup

        /// <summary>
        /// A copy of <c>example_rock_patrol</c> in a temporary directory. A build
        /// writes its side files beside the sources, so the tests build the copy and
        /// the working tree is never touched.
        /// </summary>
        private static TemporaryDirectory ExampleGameCopy()
        {
            string source = Path.Combine(FindRepositoryRoot(), "example_rock_patrol");
            if (!File.Exists(Path.Combine(source, "config.json")))
                Assert.Inconclusive("example_rock_patrol is not in the built tree");
            TemporaryDirectory copy = new TemporaryDirectory();
            CopyTree(source, copy.Path);
            return copy;
        }

        private static void CopyTree(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(source))
                CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)));
        }

        private static ProjectBuildResult BuildExampleGame(string project)
        {
            return BuildExampleGameAndKeepSession(project, null).Result;
        }

        /// <summary>
        /// Builds the example game through <paramref name="session"/>, or through a new
        /// session when it is null. It matters which: the names a listing afterwards
        /// knows are the ones <em>that</em> session's build collected, so a listing and
        /// the build it depends on have to be the same session.
        /// </summary>
        private static ExampleGameBuild BuildExampleGameAndKeepSession(string project, ProjectSession session)
        {
            ExampleGameBuild outcome = new ExampleGameBuild();
            outcome.Session = session ?? ProjectSession.Open(Path.Combine(project, "config.json"));
            outcome.Result = outcome.Session.Build();
            return outcome;
        }

        private sealed class ExampleGameBuild
        {
            public ProjectSession Session { get; set; }
            public ProjectBuildResult Result { get; set; }

            public long SymbolOr(string name)
            {
                long value;
                return Session.ProjectSymbols.TryGetValue(name, out value) ? value : -1;
            }
        }

        private static void WriteConfiguration(string directory, Segment[] inputs, FileConf[] files)
        {
            System.Text.StringBuilder json = new System.Text.StringBuilder();
            json.Append("{ \"Input\": [");
            for (int i = 0; i < inputs.Length; i++)
            {
                if (i > 0) json.Append(',');
                json.Append("{\"FileName\":\"").Append(inputs[i].FileName).Append("\"");
                if (inputs[i].OutputFile != null)
                    json.Append(",\"OutputFile\":\"").Append(inputs[i].OutputFile).Append("\"");
                json.Append(",\"Dependencies\":[");
                string[] dependencies = inputs[i].Dependencies ?? new string[0];
                for (int d = 0; d < dependencies.Length; d++)
                {
                    if (d > 0) json.Append(',');
                    json.Append('"').Append(dependencies[d]).Append('"');
                }
                json.Append("]}");
            }
            json.Append("], \"Output\": {\"ObjectFile\":\"out.bin\",\"Files\":[");
            for (int i = 0; i < files.Length; i++)
            {
                if (i > 0) json.Append(',');
                json.Append("{\"FileName\":\"").Append(files[i].FileName).Append("\"");
                if (!string.IsNullOrEmpty(files[i].Size))
                    json.Append(",\"Size\":\"").Append(files[i].Size).Append('"');
                json.Append('}');
            }
            json.Append("]}}");
            File.WriteAllText(Path.Combine(directory, "config.json"), json.ToString());
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "WinASM65.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            return AppContext.BaseDirectory;
        }

        // ----------------------------------------------------------------- checks

        private static bool ReportsUndefinedSymbol(SourceListing listing, string name)
        {
            for (int i = 0; i < listing.Assembly.Diagnostics.Count; i++)
            {
                string message = listing.Assembly.Diagnostics[i].Message;
                if (message.IndexOf(ErrorCodes.UNDEFINED_SYMBOL, StringComparison.Ordinal) >= 0
                    && message.IndexOf(name, StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        private static bool Reports(SourceListing listing, string code)
        {
            for (int i = 0; i < listing.Assembly.Diagnostics.Count; i++)
            {
                if (listing.Assembly.Diagnostics[i].Message.IndexOf(code, StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        private static string HashOf(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                StringBuilder text = new StringBuilder();
                byte[] hash = sha.ComputeHash(bytes);
                for (int i = 0; i < hash.Length; i++)
                    text.Append(hash[i].ToString("X2", CultureInfo.InvariantCulture));
                return text.ToString();
            }
        }

        private static string Describe(IReadOnlyList<Diagnostic> diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0)
                return "no diagnostic given";
            string text = string.Empty;
            for (int i = 0; i < diagnostics.Count; i++)
                text += diagnostics[i].ToString() + " | ";
            return text;
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }

            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "WinASM65Project_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string File(string name)
            {
                return System.IO.Path.Combine(Path, name);
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
