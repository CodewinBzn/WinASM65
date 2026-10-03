using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Terminal.Gui;
using WinASM65.Cpu;
using WinASM65.Monitor.Abstractions;
using WinASM65.Monitor.Protocol;
using WinASM65.Monitor.Shell;
using WinASM65.Projects;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// F5 against a project, and the rule that decides what it does.
    ///
    /// <para>
    /// The rule has to be asserted in both directions, and that is the whole point of
    /// this class. A key that quietly becomes "build the project" in one directory and
    /// "assemble the file" in another produces a tool whose behaviour depends on where
    /// a file sits, which is the one property a user cannot debug from the inside. So
    /// each test here states which of the two happened, in the command line the window
    /// issued — not in the message, which the implementation also writes, and not in
    /// the pane, which the implementation also fills.
    /// </para>
    ///
    /// <para>
    /// Terminal.Gui is driven through its own <see cref="FakeDriver"/>, as
    /// <c>ShellLifecycleTests</c> does, and nothing here enters a loop that would wait
    /// for a keystroke nobody is going to type.
    /// </para>
    /// </summary>
    [TestClass]
    public class ShellProjectBuildTests
    {
        private const string Bank1 =
            ".org $8000\n" +
            "Shared:\n" +
            "  lda #$5A\n" +
            "  rts\n" +
            "  .export Shared\n";

        private const string Bank2 =
            ".org $9000\n" +
            "Entry:\n" +
            "  jmp Shared\n" +
            "  .export Entry\n";

        /// <summary>A source no configuration declares. There is a configuration in the
        /// same directory, so this is the case a rule that only asked "is there a
        /// config.json somewhere" would get wrong.</summary>
        private const string Lone =
            ".org $A000\n" +
            "Alone:\n" +
            "  nop\n" +
            "  rts\n" +
            "  .export Alone\n";

        // ------------------------------------------------------------------ both branches

        [TestMethod]
        public void F5BuildsTheProjectWhenAConfigurationDeclaresTheOpenSource()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                WriteProject(temp, Bank1, Bank2);

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource("bank2.asm");
                    window.AssembleCurrentSource();

                    CollectionAssert.AreEqual(
                        new List<string> { "BUILD config.json" },
                        new List<string>(window.Commands),
                        "F5 is one command line into the session, and here it is BUILD");

                    StringAssert.Contains(window.Message, "OK");
                    StringAssert.Contains(window.Message, "2 unit(s)");
                    StringAssert.Contains(window.Message, "-- project config.json",
                        "the message line has to name which of the two things ran");

                    Assert.IsTrue(File.Exists(Path.Combine(temp.Path, "game.nes")),
                        "the build says it wrote an image, so there has to be one");
                });
            }
        }

        [TestMethod]
        public void F5AssemblesTheSingleFileWhenNoConfigurationDeclaresIt()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                WriteProject(temp, Bank1, Bank2);
                File.WriteAllText(Path.Combine(temp.Path, "lone.asm"), Lone);

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource("lone.asm");
                    window.AssembleCurrentSource();

                    CollectionAssert.AreEqual(
                        new List<string> { "ASSEMBLE lone.asm" },
                        new List<string>(window.Commands),
                        "a configuration sitting in the directory does not make every file a unit");

                    StringAssert.Contains(window.Message, "OK");
                    StringAssert.Contains(window.Message, "-- one file");
                    StringAssert.Contains(window.Listing.Content, "nop");
                });
            }
        }

        [TestMethod]
        public void F5SwitchesBackToTheSingleFileAfterAProjectBuild()
        {
            // The other half of "both asserted": the rule is read per press, not
            // latched. A pane left pointing at the project would then show a lone
            // file's listing with the project's caption, which is the confusion the
            // re-pointing exists to avoid.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                WriteProject(temp, Bank1, Bank2);
                File.WriteAllText(Path.Combine(temp.Path, "lone.asm"), Lone);

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource("bank2.asm");
                    window.AssembleCurrentSource();

                    // bank2 lists against the project, so the name bank1 defines
                    // resolves.
                    Assert.IsNull(window.Listing.Problem,
                        "a unit of a built project lists: " + Describe(window.Listing.Problem));
                    Assert.IsFalse(window.Listing.Content.Contains("Undefined symbol"),
                        "the project's names have to be in force: " + window.Listing.Content);

                    window.OpenSource("lone.asm");
                    window.AssembleCurrentSource();

                    CollectionAssert.AreEqual(
                        new List<string> { "BUILD config.json", "ASSEMBLE lone.asm" },
                        new List<string>(window.Commands));
                    StringAssert.Contains(window.Message, "-- one file");
                });
            }
        }

        [TestMethod]
        public void F5WithNothingOpenIssuesNothing()
        {
            // Unchanged by the rule: there is no source to ask which project governs.
            WithShell(AppContext.BaseDirectory, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                window.AssembleCurrentSource();

                Assert.AreEqual(0, window.Commands.Count);
                StringAssert.Contains(window.Message, "no source is open");
            });
        }

        // ------------------------------------------------------------------ the help text

        [TestMethod]
        public void TheHelpScreenNamesWhichRuleIsInForce()
        {
            WithShell(AppContext.BaseDirectory, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                Press(Key.F1, window);

                StringAssert.Contains(window.Message, ShellWindow.ProjectRule,
                    "the help screen must state the rule, not paraphrase it");

                // Both branches, by name, because a user reading only this screen has to
                // be able to predict what F5 is about to do.
                StringAssert.Contains(window.Message, "BUILD config.json");
                StringAssert.Contains(window.Message, "ASSEMBLE <file>");

                StringAssert.Contains(Line(window.Message, "F5"), "build");
                Assert.IsFalse(Line(window.Message, "F5").Contains("(bound, but"),
                    "F5 asks nothing of the machine, so a note about the machine gating it"
                    + " would be false");
            });
        }

        [TestMethod]
        public void TheKeyTableStillListsF5AsBoundAndUngated()
        {
            foreach (ShellKey key in ShellWindow.Keys)
            {
                if (key.Key != "F5")
                    continue;

                Assert.IsTrue(key.Bound, "F5 was bound before the project build and must stay bound");
                Assert.AreEqual(ExecutionCapability.None, key.Requires,
                    "a project build is the shell's own work; gating it would be gating the"
                    + " shell against itself");
                return;
            }

            Assert.Fail("the key table does not list F5");
        }

        // ------------------------------------------------------------------ the pane

        [TestMethod]
        public void AfterAProjectBuildThePaneListsTheSourceWithEveryUnitsNames()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                WriteProject(temp, Bank1, Bank2);

                // The single-file listing of the same file, asked directly, so the
                // difference is measured rather than assumed: bank2 calls Shared, which
                // only bank1 defines, so the two answers cannot look alike.
                IListingSource alone = new AssemblerListingSource(new Cpu6502(), temp.Path);
                Assert.AreEqual(0, alone.Rows(new ListingRequest("bank2.asm", 0x9000, 100)).Count,
                    "listed alone, a unit that calls another unit's name cannot list");
                StringAssert.Contains(alone.LastProblem, "Shared");

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource("bank2.asm");
                    window.AssembleCurrentSource();

                    Assert.IsTrue(window.Listing.Rows.Count > 0,
                        "a unit of a built project lists, and the pane has its rows");
                    StringAssert.Contains(window.Listing.Content, "jmp Shared");
                    StringAssert.Contains(window.Listing.Content, "$9000");
                    Assert.IsNull(window.Listing.Problem,
                        "the pane must not be showing the failure the build resolved: "
                        + Describe(window.Listing.Problem));
                });
            }
        }

        [TestMethod]
        public void AProjectBuildStillRefusesANameNoUnitDefines()
        {
            // The build widens what its units may use; it does not make silence
            // correct. A project build that hid every error in every unit would be a
            // worse failure than the wrong-listing one it replaced.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                WriteProject(temp, Bank1,
                    ".org $9000\n" +
                    "Entry:\n" +
                    "  jmp NowhereAtAll\n" +
                    "  .export Entry\n");

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource("bank2.asm");
                    window.AssembleCurrentSource();

                    CollectionAssert.AreEqual(
                        new List<string> { "BUILD config.json" },
                        new List<string>(window.Commands));

                    Assert.IsFalse(window.Message.Contains("OK"), window.Message);
                    StringAssert.Contains(window.Message, "ERR");
                    StringAssert.Contains(window.Message, "error(s) in the project at");
                    StringAssert.Contains(window.Message, "Undefined symbol");
                    StringAssert.Contains(window.Message, "bank2.asm");
                });
            }
        }

        [TestMethod]
        public void AProjectListingDoesNotExcuseWhatTheProjectNeverDefined()
        {
            // The listing's half of the same honesty. bank3 is in the directory and is
            // not a unit of anything, so the names it uses are ones no build resolved:
            // the project widened the listing, it did not silence it.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                WriteProject(temp, Bank1, Bank2);
                File.WriteAllText(Path.Combine(temp.Path, "bank3.asm"),
                    ".org $B000\nStray:\n  jmp Shared\n  jmp NowhereAtAll\n  .export Stray\n");

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource("bank2.asm");
                    window.AssembleCurrentSource();
                    Assert.IsNull(window.Listing.Problem, "the build is clean");

                    // Asked through the very source the pane is now reading, which is
                    // the seam the pane holds — not one arranged beside it.
                    IListingSource pane = window.ListingSource;
                    Assert.IsInstanceOfType(pane, typeof(ProjectListingSource),
                        "a project build leaves the pane reading through the project");

                    Assert.AreEqual(0, pane.Rows(new ListingRequest("bank3.asm", 0xB000, 100)).Count);
                    StringAssert.Contains(pane.LastProblem, "bank3.asm");
                    StringAssert.Contains(pane.LastProblem, "NowhereAtAll");

                    // And the file that does belong to the project still lists, so the
                    // refusal above is about bank3 and not about the project.
                    Assert.IsTrue(pane.Rows(new ListingRequest("bank2.asm", 0x9000, 100)).Count > 0);
                });
            }
        }

        [TestMethod]
        public void AFailedProjectBuildPutsTheRealDiagnosticInThePane()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                WriteProject(temp, Bank1,
                    ".org $9000\n" +
                    "Entry:\n" +
                    "  lda #$5A\n" +
                    "  not_an_instruction_at_all\n" +
                    "  .export Entry\n");

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource("bank2.asm");
                    window.AssembleCurrentSource();

                    Assert.AreEqual(0, window.Listing.Rows.Count,
                        "rows drawn beside the reason they could not be produced are a fiction");

                    string problem = window.Listing.Problem;
                    StringAssert.Contains(problem, "bank2.asm");
                    StringAssert.Contains(problem, ":4", "the line, as the assembler named it");
                    StringAssert.Contains(problem, "ERR");

                    Assert.IsFalse(problem.Contains("listing unavailable"),
                        "\"the listing API is not in this build\" would send the user looking"
                        + " for a problem that does not exist");

                    // And the same diagnostic on the message line, so the pane and the
                    // one-row status agree about what happened.
                    StringAssert.Contains(window.Message, "bank2.asm:4");
                });
            }
        }

        [TestMethod]
        public void AFailedProjectBuildLeavesThePaneOnTheRealDiagnosticRatherThanTheOldRows()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                WriteProject(temp, Bank1, Bank2);

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource("bank1.asm");
                    window.AssembleCurrentSource();
                    Assert.IsTrue(window.Listing.Rows.Count > 0, "the first build listed it");

                    // Break the project behind the shell's back, then press F5 again.
                    File.WriteAllText(Path.Combine(temp.Path, "bank2.asm"),
                        ".org $9000\nEntry:\n  not_an_instruction_at_all\n  .export Entry\n");
                    window.OpenSource("bank1.asm");
                    window.AssembleCurrentSource();

                    Assert.AreEqual(0, window.Listing.Rows.Count,
                        "the pane still shows the rows the project just refused to produce");
                    StringAssert.Contains(window.Listing.Problem, "bank2.asm");
                });
            }
        }

        [TestMethod]
        public void AMalformedConfigurationIsNamedRatherThanSilentlyAssemblingTheFile()
        {
            // The failure mode the rule has to refuse: a broken config.json beside a
            // source looks exactly like no project, and answering "no project" there
            // would be F5 doing something its own label does not say.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                File.WriteAllText(Path.Combine(temp.Path, "a.asm"), Bank1);
                File.WriteAllText(Path.Combine(temp.Path, "config.json"), "{ \"Input\": ");

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource("a.asm");
                    window.AssembleCurrentSource();

                    CollectionAssert.AreEqual(
                        new List<string> { "BUILD config.json" },
                        new List<string>(window.Commands),
                        "the file must not be assembled behind a configuration that cannot be read");

                    StringAssert.Contains(window.Message, "cannot be read");
                    StringAssert.Contains(window.Listing.Problem, "config.json");
                });
            }
        }

        // ------------------------------------------------------------------ the machine

        [TestMethod]
        public void AProjectBuildHappensOnAMachineThatCanDoNoneOfTheExecutionKeys()
        {
            // F5 asks the machine nothing. MAME has memory and registers and cannot
            // resume, step or break — and a project build has to run there exactly as
            // it does everywhere else, or the gating would have cost the key its
            // meaning on one host.
            WithShell("MAME", "0.289", AppContext.BaseDirectory,
                delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource("anything.asm");
                    window.AssembleCurrentSource();

                    Assert.AreEqual(1, window.Commands.Count, "the command was issued");
                    Assert.IsTrue(window.Commands[0].StartsWith("ASSEMBLE ", StringComparison.Ordinal)
                        || window.Commands[0].StartsWith("BUILD ", StringComparison.Ordinal),
                        "F5 reached the session: " + window.Commands[0]);
                    Assert.AreEqual(0, machine.ResumeCount);
                    Assert.AreEqual(0, machine.StepCount);
                });
        }

        // -------------------------------------------------------------------- the seam

        [TestMethod]
        public void TheProjectListingReachesThePaneThroughTheSameSeam()
        {
            // The seam's claim is that a pane cannot tell which implementation it is
            // holding except by IsAvailable. A project-aware listing that needed a
            // method of its own would have broken that, and the pane would be written
            // against ProjectListingSource rather than against IListingSource — which
            // is the thing ListingSeamTests exists to prevent.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                WriteProject(temp, Bank1, Bank2);

                IListingSource single = new AssemblerListingSource(new Cpu6502(), temp.Path);
                IListingSource unbuilt = new ProjectListingSource(new Cpu6502(), temp.Path,
                    null, "config.json");

                Assert.IsTrue(single.IsAvailable);
                Assert.IsFalse(unbuilt.IsAvailable,
                    "with no project built there are no other units whose names to know");
                StringAssert.Contains(unbuilt.UnavailableReason, "no project has been built");

                ProjectSession built;
                using (FakeMemoryBackend machine = new FakeMemoryBackend())
                {
                    MonitorSession session = new MonitorSession(machine, new Cpu6502(), temp.Path);
                    session.Execute("BUILD config.json");
                    built = session.Projects.LastSession;
                }

                Assert.IsNotNull(built, "the build kept no session");
                IListingSource project = new ProjectListingSource(new Cpu6502(), temp.Path,
                    built, "config.json");

                Assert.IsTrue(project.IsAvailable);
                Assert.AreEqual(string.Empty, project.UnavailableReason,
                    "an available source has no reason to give");

                IReadOnlyList<ListingRow> rows =
                    project.Rows(new ListingRequest("bank2.asm", 0x9000, 100));

                Assert.IsTrue(rows.Count > 0, "a unit of a built project lists");
                Assert.IsNull(project.LastProblem);
            }
        }

        // -------------------------------------------------------------------- helpers

        /// <summary>
        /// A two-unit project whose second unit calls a name the first defines, plus the
        /// image layout that puts both in one file.
        /// </summary>
        private static void WriteProject(TemporaryDirectory temp, string bank1, string bank2)
        {
            File.WriteAllText(Path.Combine(temp.Path, "bank1.asm"), bank1);
            File.WriteAllText(Path.Combine(temp.Path, "bank2.asm"), bank2);

            File.WriteAllText(Path.Combine(temp.Path, "config.json"),
                "{ \"Input\": ["
                + " { \"FileName\": \"bank1.asm\", \"Dependencies\": [] },"
                + " { \"FileName\": \"bank2.asm\", \"Dependencies\": [ \"bank1.asm\" ] } ],"
                + " \"Output\": { \"ObjectFile\": \"game.nes\", \"Files\": ["
                + " { \"FileName\": \"bank1.o\" },"
                + " { \"FileName\": \"bank2.o\" } ] } }");
        }

        private static string Describe(string problem)
        {
            return problem ?? "(nothing)";
        }

        private static string Line(string help, string key)
        {
            foreach (string line in help.Split(new string[] { Environment.NewLine }, StringSplitOptions.None))
            {
                if (line.StartsWith("  " + key + " ", StringComparison.Ordinal))
                    return line;
            }

            Assert.Fail("the help screen does not list " + key + ":\n" + help);
            return null;
        }

        /// <summary>
        /// Raises one key inside the run loop and ends the loop from the same handler.
        /// <c>Application.Init</c> empties Terminal.Gui's static event table, so a
        /// handler subscribed before the call is never raised and the loop then waits
        /// for a keystroke nobody is going to type — a hang, not a failure.
        /// </summary>
        private static void Press(Key key, ShellWindow window)
        {
            bool handled = false;
            Key pressed = key;

            EventHandler<IterationEventArgs> onIteration = delegate (object sender, IterationEventArgs args)
            {
                handled = Application.RaiseKeyDownEvent(pressed) || handled;
                Application.RequestStop();
            };

            Application.Iteration += onIteration;
            try
            {
                Application.Run(window);
            }
            finally
            {
                Application.Iteration -= onIteration;
            }

            Assert.IsTrue(handled, "the key went nowhere: " + pressed);
        }

        private static void WithShell(string directory, Action<ShellWindow, FakeMemoryBackend> body)
        {
            WithShell("Mesen2", "2.1.1", directory, body);
        }

        /// <summary>
        /// The shell over a real bridge to a machine whose handshake names the backend
        /// being simulated. Real adapter, real handshake, real socket, real session;
        /// the only thing faked is the emulator at the far end.
        /// </summary>
        private static void WithShell(string name, string version, string directory,
            Action<ShellWindow, FakeMemoryBackend> body)
        {
            FakeDriver driver = new FakeDriver();
            Application.Init(driver);
            try
            {
                driver.SetWindowSize(160, 40);
                driver.Refresh();

                using (FakeMemoryBackend machine = new FakeMemoryBackend(65536, name, version))
                using (ProtocolServer server = new ProtocolServer(machine, CpuFactory.Create("6502")))
                {
                    server.Start();

                    using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(server.Port))
                    {
                        MonitorSession session = new MonitorSession(bridge, new Cpu6502(), directory);

                        ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                            new AssemblerListingSource(new Cpu6502(), directory), directory);

                        window.Frame = new System.Drawing.Rectangle(0, 0, 160, 40);
                        window.ApplyLayout(ShellLayout.ForSize(160, 40));

                        body(window, machine);
                    }
                }
            }
            finally
            {
                Application.Shutdown();
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
                    System.IO.Path.GetTempPath(), "WinASM65ProjectF5_" + Guid.NewGuid().ToString("N"));
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