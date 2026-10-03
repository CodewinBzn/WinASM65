using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// The NES homebrew, built through the library rather than through the command
    /// line, and what a listing knows once the project has been built.
    ///
    /// <para>
    /// <c>example_bomberman-nes</c> is the one program in the repository that is
    /// genuinely multi-file without being told to be: its bank 1, its bank 2 and
    /// its vectors each use names the others define, and nothing in them declares
    /// an import. The vectors file is three words, <c>NMI</c>, <c>RESET</c> and
    /// <c>IRQ</c>, and all three are defined in the other two. It is therefore the
    /// only workload in the tree that can tell a working multi-file build from one
    /// that quietly assembled to zero.
    /// </para>
    ///
    /// <para>
    /// The reference image is the file the original toolchain produced, and it is
    /// not in version control -- a <c>.nes</c> is build output. So the comparison is
    /// skipped, never faked, when it is absent, and the tests that do not need it
    /// still run.
    /// </para>
    /// </summary>
    [TestClass]
    public class ProjectSessionTests
    {
        #region The real homebrew, end to end

        [TestMethod]
        public void TheHomebrewAssemblesIntoTheImageTheOriginalToolchainProduced()
        {
            string project = HomebrewDirectory();
            byte[] reference = HomebrewReference();
            if (reference == null)
                return;

            ProjectBuildResult build = BuildHomebrew(project);

            Assert.IsTrue(build.Success, "the homebrew must build: " + Describe(build.Diagnostics));
            Assert.AreEqual(reference.Length, build.Image.Length,
                "the image is the header, both banks, the vectors and the character data");
            CollectionAssert.AreEqual(reference, build.Image,
                "the library must produce the bytes the original toolchain produced");
            Assert.AreEqual("4E57F08754A2FF7EC788245629FB70F99D4E003F66F86742566BCA99C810A244",
                HashOf(build.Image), "the golden NES image has changed");
        }

        [TestMethod]
        public void TheHomebrewIsBuiltFromItsSourcesInTheOrderTheConfigurationDeclares()
        {
            string project = HomebrewDirectory();

            ProjectBuildResult build = BuildHomebrew(project);

            Assert.IsTrue(build.Success, Describe(build.Diagnostics));
            Assert.AreEqual(3, build.Units.Count, "one unit per declared input");
            Assert.AreEqual("BMAN_BANK1.NAS", Path.GetFileName(build.Units[0].SourceFile));
            Assert.AreEqual("BMAN_BANK2.NAS", Path.GetFileName(build.Units[1].SourceFile));
            Assert.AreEqual("vectors.NAS", Path.GetFileName(build.Units[2].SourceFile));

            // The vectors are the unit that proves the order: they name three
            // routines the two banks define, and they are declared last.
            Assert.AreEqual(0, build.Units[0].Dependencies.Count,
                "the first bank depends on nothing");
            Assert.AreEqual(1, build.Units[1].Dependencies.Count);
            Assert.AreEqual(2, build.Units[2].Dependencies.Count);

            // And the names the banks defined are what the session now knows.
            Assert.IsTrue(build.Units[0].Symbols.Count > 0, "bank 1 defines the labels");
            Assert.IsTrue(build.Success, "a build that produced no names resolved nothing");
        }

        [TestMethod]
        public void TheVectorsGetTheAddressesTheBanksDefined()
        {
            string project = HomebrewDirectory();

            HomebrewBuild build = BuildHomebrewAndKeepSession(project, null);

            Assert.IsTrue(build.Result.Success, Describe(build.Result.Diagnostics));
            Assert.IsTrue(build.SymbolOr("NMI") > 0, "NMI is a label in bank 1");
            Assert.AreEqual(0xC000, build.SymbolOr("RESET"),
                "RESET is the first instruction of bank 1, and the vector has to say so");
            Assert.IsTrue(build.SymbolOr("IRQ") >= 0xF000,
                "IRQ is in the bank 2 module, not in bank 1");
        }

        #endregion

        #region What the listing knows after a build

        [TestMethod]
        public void AListingSeesTheNamesAnotherUnitOfTheProjectDefines()
        {
            string project = HomebrewDirectory();
            string bman = Path.Combine(project, "BMAN.NAS");
            ProjectSession session = ProjectSession.Open(Path.Combine(project, "config.json"));

            // Before the build the session knows nothing, so the four names these
            // lines use and no line in this file defines are reported.
            SourceListing before = session.ListSourceFile(bman);
            Assert.IsTrue(ReportsUndefinedSymbol(before, "VRAMADDRZ"),
                "without the project's names a name another unit defines reads as undefined");
            Assert.IsTrue(ReportsUndefinedSymbol(before, "APU_MELODIES_TAB"), "APU_MELODIES_TAB");
            Assert.IsTrue(ReportsUndefinedSymbol(before, "_pass_data_vars"), "_pass_data_vars");
            Assert.IsTrue(ReportsUndefinedSymbol(before, "aAofkcpgelbhmjd"), "aAofkcpgelbhmjd");

            // The helper builds and then tidies the artifacts away. The session keeps
            // the names in memory, which is the point: what the listing below reads is
            // what the build learned, not a file it re-read.
            BuildHomebrewAndKeepSession(project, session);

            SourceListing after = session.ListSourceFile(bman);
            Assert.IsFalse(ReportsUndefinedSymbol(after, "VRAMADDRZ"),
                "VRAMADDRZ is defined in BMAN_BANK1.NAS, and the project says so");
            Assert.IsFalse(ReportsUndefinedSymbol(after, "APU_MELODIES_TAB"), "APU_MELODIES_TAB");
            Assert.IsFalse(ReportsUndefinedSymbol(after, "_pass_data_vars"), "_pass_data_vars");
            Assert.IsFalse(ReportsUndefinedSymbol(after, "aAofkcpgelbhmjd"), "aAofkcpgelbhmjd");
        }

        [TestMethod]
        public void AListingAfterABuildStillReportsTheDefectInTheFileItself()
        {
            // Line 536 of BMAN.NAS is "VRAMADDRZ" with no colon: a label written
            // without one, which the assembler reads as a call to a macro of that
            // name. No amount of cross-file knowledge makes that line right, and a
            // session that hid it would be hiding a real defect -- so the diagnostic
            // has to survive the widening.
            string project = HomebrewDirectory();
            ProjectSession session = ProjectSession.Open(Path.Combine(project, "config.json"));
            BuildHomebrewAndKeepSession(project, session);

            SourceListing listing = session.ListSourceFile(Path.Combine(project, "BMAN.NAS"));

            Assert.IsTrue(Reports(listing, ErrorCodes.MACRO_NOT_EXISTS),
                "a label written without its colon is a defect of the file, and stays reported");
        }

        [TestMethod]
        public void AListingAfterABuildStillReportsANameNoUnitDefines()
        {
            // The other half of the same guarantee. A project listing knows more
            // names than a single-file one; it must not have become a listing that
            // reports nothing.
            string project = HomebrewDirectory();
            ProjectSession session = ProjectSession.Open(Path.Combine(project, "config.json"));
            BuildHomebrewAndKeepSession(project, session);

            SourceListing listing = session.ListSourceText(
                ".org $C000\nJMP NEVER_DEFINED_IN_THIS_PROJECT\n", "probe.asm");

            Assert.IsFalse(listing.Success, "a name no unit of the project defines must be reported");
            Assert.IsTrue(ReportsUndefinedSymbol(listing, "NEVER_DEFINED_IN_THIS_PROJECT"),
                "and it must be reported by name");
        }

        [TestMethod]
        public void AProjectListingStillResolvesAForwardReference()
        {
            string project = HomebrewDirectory();
            ProjectSession session = ProjectSession.Open(Path.Combine(project, "config.json"));
            BuildHomebrewAndKeepSession(project, session);

            SourceListing listing = session.ListSourceText(
                ".org $C000\n        jmp Later\nLater:\n        nop\n", "probe.asm");

            Assert.IsTrue(listing.Success, Describe(listing.Assembly.Diagnostics));
            Assert.AreEqual(0, listing.Assembly.Diagnostics.Count, Describe(listing.Assembly.Diagnostics));
            CollectionAssert.AreEqual(new byte[] { 0x4C, 0x03, 0xC0, 0xEA },
                listing.Assembly.OutputBytes,
                "knowing the project's names must not stop a forward reference resolving");
        }

        [TestMethod]
        public void AListingOfAFileThatIsNotThereSaysSo()
        {
            string project = HomebrewDirectory();
            ProjectSession session = ProjectSession.Open(Path.Combine(project, "config.json"));

            SourceListing listing = session.ListSourceFile(Path.Combine(project, "no-such-file.asm"));

            Assert.IsFalse(listing.Success);
            Assert.IsTrue(Reports(listing, ErrorCodes.FILE_NOT_EXISTS),
                "reported the way the assembler reports a missing source");
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
        /// The homebrew, built with its side files beside it and every artifact this
        /// run created removed afterwards, so a test run leaves the tree as it found
        /// it.
        /// </summary>
        private static ProjectBuildResult BuildHomebrew(string project)
        {
            return BuildHomebrewAndKeepSession(project, null).Result;
        }

        /// <summary>
        /// The homebrew, built with its side files beside it and every artifact this
        /// run created removed afterwards, so a test run leaves the tree as it found
        /// it.
        /// <para>
        /// <paramref name="session"/> is the session to build through, and null for a
        /// new one. It matters which: the names a listing afterwards knows are the
        /// ones <em>that</em> session's build collected, so a listing and the build it
        /// depends on have to be the same session.
        /// </para>
        /// </summary>
        private static HomebrewBuild BuildHomebrewAndKeepSession(string project, ProjectSession session)
        {
            HomebrewBuild outcome = new HomebrewBuild();
            List<string> created = new List<string>();
            try
            {
                outcome.Session = session
                    ?? ProjectSession.Open(Path.Combine(project, "config.json"));
                outcome.Result = outcome.Session.Build();
                foreach (string extension in new[] { ".o", ".lst" })
                {
                    foreach (string file in Directory.GetFiles(project, "*" + extension))
                        created.Add(file);
                }
                foreach (string extension in SideFiles.Extensions)
                {
                    foreach (string file in Directory.GetFiles(project, "*" + extension))
                        created.Add(file);
                }
                return outcome;
            }
            finally
            {
                foreach (string file in created)
                {
                    try
                    {
                        // bomber.nes is the reference image, not an artifact of this run.
                        if (Path.GetFileName(file) != "bomber.nes")
                            File.Delete(file);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }

        private sealed class HomebrewBuild
        {
            public ProjectSession Session { get; set; }
            public ProjectBuildResult Result { get; set; }

            public long SymbolOr(string name)
            {
                long value;
                return Session.ProjectSymbols.TryGetValue(name, out value) ? value : -1;
            }
        }

        private static string HomebrewDirectory()
        {
            string project = Path.Combine(FindRepositoryRoot(), "example_bomberman-nes");
            if (!Directory.Exists(project))
                Assert.Inconclusive("example_bomberman-nes is not in the built tree");
            foreach (string source in new[] { "BMAN_BANK1.NAS", "BMAN_BANK2.NAS", "vectors.NAS", "config.json" })
            {
                if (!File.Exists(Path.Combine(project, source)))
                    Assert.Inconclusive("example_bomberman-nes/" + source + " is missing");
            }
            return project;
        }

        /// <summary>
        /// The image the original toolchain produced, or null when it is absent. A
        /// <c>.nes</c> is build output and is not in version control, so its absence
        /// skips the comparison instead of inventing a value to compare against.
        /// </summary>
        private static byte[] HomebrewReference()
        {
            string reference = Path.Combine(FindRepositoryRoot(), "example_bomberman-nes", "bomber.nes");
            if (!File.Exists(reference))
                Assert.Inconclusive("bomber.nes is not in the built tree: the reference image is build "
                    + "output and is not in version control, so there is nothing to compare against");
            return File.ReadAllBytes(reference);
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
