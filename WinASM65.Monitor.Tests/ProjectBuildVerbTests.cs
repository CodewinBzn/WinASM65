using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Cpu;
using WinASM65.Monitor.Protocol;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// <c>BUILD</c>, the verb that makes the library's project build reachable from the
    /// monitor.
    ///
    /// <para>
    /// The workload is the NES example game, <c>example_rock_patrol</c>: its
    /// <c>game.asm</c> uses the tables <c>data.asm</c> defines and its vectors name
    /// three routines <c>game.asm</c> defines, and none of them declares an import.
    /// Building it and comparing the hash with the pinned Rock Patrol image is the
    /// only check here that says anything: a verb that assembled nothing and
    /// reported success would pass every other test in this class.
    /// </para>
    ///
    /// <para>
    /// The example is built in a temporary copy, so the side files and the image a
    /// build writes never reach the working tree.
    /// </para>
    ///
    /// <para>
    /// Every failure is asserted to be a string. <c>ProjectSession.Open</c> raises for
    /// a missing file and the JSON reader behind it raises its own types for a document
    /// that is not a configuration; a monitor that answered those with an exception
    /// would be answering a typo with a crash.
    /// </para>
    /// </summary>
    [TestClass]
    public class ProjectBuildVerbTests
    {
        /// <summary>The SHA256 of the Rock Patrol image, the value GoldenOutputTests pins.</summary>
        private const string GoldenImage =
            "F99BB587D623D0B88523424771359E6D94CE58622D69DF51F512CC4276EE0B25";

        private const string GoodSource =
            ".org $8000\n" +
            "Start:\n" +
            "  lda #$5A\n" +
            "  rts\n" +
            "  .export Start\n";

        private const string BrokenSource =
            ".org $8000\n" +
            "Start:\n" +
            "  lda #$5A\n" +
            "  not_an_instruction_at_all\n" +
            "  rts\n" +
            "  .export Start\n";

        // ------------------------------------------------------------ the example game

        [TestMethod]
        public void TheVerbBuildsTheExampleGameAndWritesItsPinnedImage()
        {
            using (TemporaryDirectory project = ExampleGameCopy())
            {
                string image = Path.Combine(project.Path, "rock_patrol.nes");
                Assert.IsFalse(File.Exists(image), "the copy starts without an image");

                IReadOnlyList<string> answer = Execute(project.Path,
                    "BUILD " + Path.Combine(project.Path, "config.json"));

                string all = string.Join(" | ", new List<string>(answer).ToArray());
                Assert.IsFalse(all.Contains("ERR"), "the example game must build: " + all);
                Assert.IsTrue(all.StartsWith("OK ", StringComparison.Ordinal), all);
                Assert.IsTrue(all.Contains("4 unit(s)"), all);
                Assert.IsTrue(all.Contains("40976 byte(s)"), all);
                Assert.IsTrue(all.Contains(image), "the answer has to say where it wrote: " + all);

                // The point of the whole milestone: the bytes on disk are the pinned
                // bytes, so the editor's build and the command line's build agree.
                Assert.AreEqual(GoldenImage, HashOf(File.ReadAllBytes(image)),
                    "the image F5 writes must be the pinned Rock Patrol image");
            }
        }

        [TestMethod]
        public void TheVerbLeavesTheProjectSymbolsForTheListingToUse()
        {
            using (TemporaryDirectory project = ExampleGameCopy())
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                MonitorSession session = new MonitorSession(machine, new Cpu6502(), project.Path);
                session.Execute("BUILD config.json");

                Assert.IsTrue(session.Projects.LastSession != null, "the build kept no session");
                Assert.IsTrue(session.Projects.LastSession.ProjectSymbols.Count > 0,
                    "the vectors unit cannot be built without the names the game defines");
            }
        }

        [TestMethod]
        public void TheVerbResolvesAConfigurationRelativeToTheSessionNotTheProcess()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                WriteProject(temp, "a.asm", GoodSource);

                using (FakeMemoryBackend machine = new FakeMemoryBackend())
                {
                    // The test process's working directory is the test output
                    // directory, not the project, so a build that resolved a relative
                    // configuration against the process would fail here and nowhere
                    // else in the suite.
                    MonitorSession session = new MonitorSession(machine, new Cpu6502(), temp.Path);

                    IReadOnlyList<string> answer = session.Execute("BUILD config.json");

                    string all = string.Join(" | ", new List<string>(answer).ToArray());
                    Assert.IsFalse(all.Contains("ERR"), all);
                    Assert.IsTrue(File.Exists(Path.Combine(temp.Path, "out.bin")),
                        "the image goes beside the configuration, which is what the library does");
                }
            }
        }

        [TestMethod]
        public void TheVerbAnswersSuccessWithWhatItWroteAndWhere()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                WriteProject(temp, "a.asm", GoodSource);

                using (FakeMemoryBackend machine = new FakeMemoryBackend())
                {
                    MonitorSession session = new MonitorSession(machine, new Cpu6502(), temp.Path);
                    IReadOnlyList<string> answer = session.Execute("BUILD config.json");

                    Assert.AreEqual(1, answer.Count);
                    Assert.IsTrue(answer[0].StartsWith("OK ", StringComparison.Ordinal), answer[0]);
                    StringAssert.Contains(answer[0], "1 unit(s)");
                    StringAssert.Contains(answer[0], "3 byte(s)");
                    StringAssert.Contains(answer[0], Path.Combine(temp.Path, "out.bin"));
                }
            }
        }

        // ------------------------------------------------------------ refused by name

        [TestMethod]
        public void AMissingConfigurationIsRefusedByNameAndThrowsNothing()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                using (FakeMemoryBackend machine = new FakeMemoryBackend())
                {
                    MonitorSession session = new MonitorSession(machine, new Cpu6502(), temp.Path);

                    IReadOnlyList<string> answer = null;
                    try
                    {
                        answer = session.Execute("BUILD no-such-config.json");
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail("a missing configuration must be an ERR line, not " + ex.GetType().Name);
                    }

                    Assert.AreEqual(1, answer.Count);
                    Assert.IsTrue(answer[0].StartsWith(MonitorProtocol.ErrPrefix, StringComparison.Ordinal), answer[0]);
                    StringAssert.Contains(answer[0], "no-such-config.json");
                    StringAssert.Contains(answer[0], temp.Path);
                }
            }
        }

        [TestMethod]
        public void AMalformedConfigurationIsRefusedByNameAndThrowsNothing()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                File.WriteAllText(Path.Combine(temp.Path, "config.json"), "{ \"Input\": [ {\"FileName\": ");

                using (FakeMemoryBackend machine = new FakeMemoryBackend())
                {
                    MonitorSession session = new MonitorSession(machine, new Cpu6502(), temp.Path);

                    IReadOnlyList<string> answer = null;
                    try
                    {
                        answer = session.Execute("BUILD config.json");
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail("a malformed configuration must be an ERR line, not " + ex.GetType().Name);
                    }

                    Assert.AreEqual(1, answer.Count, "one line, because there is one thing wrong");
                    Assert.IsTrue(answer[0].StartsWith(MonitorProtocol.ErrPrefix, StringComparison.Ordinal), answer[0]);
                    StringAssert.Contains(answer[0], "cannot be read");
                    StringAssert.Contains(answer[0], "config.json");
                }
            }
        }

        [TestMethod]
        public void AConfigurationThatIsNotAConfigurationIsRefusedRatherThanBuilt()
        {
            // Valid JSON of the wrong shape. The reader's own answer for it is a cast,
            // which is exactly the sort of thing that must not reach a pane.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                File.WriteAllText(Path.Combine(temp.Path, "config.json"), "[1, 2, 3]");

                using (FakeMemoryBackend machine = new FakeMemoryBackend())
                {
                    MonitorSession session = new MonitorSession(machine, new Cpu6502(), temp.Path);

                    IReadOnlyList<string> answer = null;
                    try
                    {
                        answer = session.Execute("BUILD config.json");
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail("a configuration of the wrong shape must be an ERR line, not "
                            + ex.GetType().Name);
                    }

                    StringAssert.StartsWith(answer[0], MonitorProtocol.ErrPrefix + " ");
                }
            }
        }

        [TestMethod]
        public void BuildWithNoArgumentIsAUsageLineNotACrash()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                MonitorSession session = new MonitorSession(machine, new Cpu6502(), AppContext.BaseDirectory);

                IReadOnlyList<string> answer = session.Execute("BUILD");

                Assert.AreEqual(1, answer.Count);
                StringAssert.StartsWith(answer[0], MonitorProtocol.ErrPrefix + " usage: BUILD <config.json>");
            }
        }

        // ------------------------------------------------------------ the real diagnostic

        [TestMethod]
        public void AFailedProjectBuildNamesTheFileAndTheLineItFailedOn()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                WriteProject(temp, "a.asm", BrokenSource);

                using (FakeMemoryBackend machine = new FakeMemoryBackend())
                {
                    MonitorSession session = new MonitorSession(machine, new Cpu6502(), temp.Path);
                    IReadOnlyList<string> answer = session.Execute("BUILD config.json");

                    string all = string.Join(Environment.NewLine, new List<string>(answer).ToArray());
                    Assert.IsTrue(answer[0].StartsWith(MonitorProtocol.ErrPrefix, StringComparison.Ordinal), all);
                    StringAssert.Contains(all, "error(s) in the project at");
                    StringAssert.Contains(all, "a.asm:4", "the real line, named as the assembler named it");
                    Assert.IsFalse(all.Contains("Undefined symbol"),
                        "a syntax error is not an undefined symbol, and saying so would be a guess");

                    Assert.IsFalse(File.Exists(Path.Combine(temp.Path, "out.bin")),
                        "a build that failed must not leave an image behind");
                }
            }
        }

        [TestMethod]
        public void AProjectWithNothingToBuildIsRefusedByName()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                File.WriteAllText(Path.Combine(temp.Path, "config.json"),
                    "{ \"Input\": [], \"Output\": { \"ObjectFile\": \"out.bin\" } }");

                using (FakeMemoryBackend machine = new FakeMemoryBackend())
                {
                    MonitorSession session = new MonitorSession(machine, new Cpu6502(), temp.Path);
                    IReadOnlyList<string> answer = session.Execute("BUILD config.json");

                    string all = string.Join(" ", new List<string>(answer).ToArray());
                    StringAssert.Contains(all, MonitorProtocol.ErrPrefix);
                    StringAssert.Contains(all, "Nothing to build");
                }
            }
        }

        // ------------------------------------------------------------ the REPL can do it

        [TestMethod]
        public void TheSessionKnowsBuildAndSaysSoInItsOwnHelp()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                IReadOnlyList<string> lines = new MonitorSession(
                    machine, new Cpu6502(), AppContext.BaseDirectory).Execute("HELP");

                string all = string.Join(" ", new List<string>(lines).ToArray());
                StringAssert.Contains(all, "BUILD");
                StringAssert.Contains(all, "config.json");
            }
        }

        [TestMethod]
        public void AnUnknownVerbIsStillAnUnknownVerb()
        {
            // BUILD is a new verb, not a rewritten one: nothing else moved.
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                IReadOnlyList<string> answer = new MonitorSession(
                    machine, new Cpu6502(), AppContext.BaseDirectory).Execute("REBUILD config.json");

                StringAssert.StartsWith(answer[0], MonitorProtocol.ErrPrefix + " unknown command: REBUILD");
            }
        }

        // -------------------------------------------------------------------- helpers

        private static IReadOnlyList<string> Execute(string directory, string command)
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                MonitorSession session = new MonitorSession(machine, new Cpu6502(), directory);
                return session.Execute(command);
            }
        }

        /// <summary>
        /// A copy of <c>example_rock_patrol</c> in a temporary directory, or
        /// inconclusive when the example is not in the tree.
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

        /// <summary>
        /// A project of one unit, so a test can fail a build without depending on which
        /// part of the example game it wants to break.
        /// </summary>
        private static void WriteProject(TemporaryDirectory temp, string name, string source)
        {
            File.WriteAllText(Path.Combine(temp.Path, name), source);
            File.WriteAllText(Path.Combine(temp.Path, "config.json"),
                "{ \"Input\": [ { \"FileName\": \"" + name + "\", \"Dependencies\": [] } ],"
                + " \"Output\": { \"ObjectFile\": \"out.bin\", \"Files\": ["
                + " { \"FileName\": \"" + Path.GetFileNameWithoutExtension(name) + ".o\" } ] } }");
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

        /// <summary>
        /// A directory of its own, so a test that writes a source cannot see — or be
        /// seen by — anything else in the suite.
        /// </summary>
        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }

            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "WinASM65Build_" + Guid.NewGuid().ToString("N"));
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
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}