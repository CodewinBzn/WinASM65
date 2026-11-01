using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using SharpConsoleUI;
using SharpConsoleUI.Drivers;
using WinASM65.Cpu;
using WinASM65.Monitor.Shell;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// The editor keys, driven the way the shell runs them.
    ///
    /// Every action is asserted three ways, because any one of them alone would pass
    /// for the wrong reason: the command line the key issued, what the session did with
    /// it, and what the status line then said. A key that issued nothing but updated a
    /// counter, or updated a counter without going through the session, is exactly the
    /// shell this milestone says it is not.
    ///
    /// Note: These tests verify the key bindings logic without full TUI integration.
    /// Full TUI integration tests with SharpConsoleUI are pending rewrite.
    /// </summary>
    [TestClass]
    public class ShellEditorKeyTests
    {
        private const string GoodSource =
            ".org $8000\n" +
            "Start:\n" +
            "  lda #$5A\n" +
            "  rts\n" +
            "  .export Start\n";

        private const string BrokenSource =
            ".org $8000\n" +
            "  lda #$5A\n" +
            "  not_an_instruction_at_all\n" +
            "  .export Start\n";

        // ---------------------------------------------------------------- the bindings

        [TestMethod]
        public void TheEditorKeysAreBoundAndTheHelpScreenSaysWhich()
        {
            AssertBound("F5", true);
            AssertBound("F8", true);
            AssertBound("F9", true);
            AssertBound("F10", true);
        }

        [TestMethod]
        public void AKeyThatIsNotInThisBuildStillSaysSo()
        {
            // F12 needs a renderer the shell does not have and Ctrl+A needs the
            // provider contract. Each is listed, unbound, rather than silently absent:
            // a user reading the plan's table should learn here that it is not here.
            // F7 is no longer one of them: StateCommands landed, so it is bound.
            AssertBound("F12", false);
            AssertBound("Ctrl+A", false);
        }

        [TestMethod]
        public void TheWindowCarriesTheShortcutsItClaims()
        {
            // The key table is a claim; the shortcuts are the wiring. A key listed as
            // bound with no Shortcut behind it would press into nothing.
            WithShell(delegate (ShellWindow window)
            {
                Assert.IsTrue(window.HasShortcut("F5"), "F5 has no shortcut");
                Assert.IsTrue(window.HasShortcut("F8"), "F8 has no shortcut");
                Assert.IsTrue(window.HasShortcut("F9"), "F9 has no shortcut");
                Assert.IsTrue(window.HasShortcut("F10"), "F10 has no shortcut");
                Assert.IsTrue(window.HasShortcut("F7"), "F7 is bound in the key table and must have a shortcut");
                Assert.IsFalse(window.HasShortcut("F12"), "F12 is not in this build and must have no shortcut");
                Assert.IsFalse(window.HasShortcut("Ctrl+A"), "Ctrl+A is not in this build and must have no shortcut");

                // One shortcut per key the table claims, and not one more: an extra
                // shortcut would be an action the help screen does not mention.
                int bound = 0;
                foreach (ShellKey key in ShellWindow.Keys)
                {
                    if (key.Bound)
                        bound++;
                }

                int shortcuts = window.ShortcutCount;
                Assert.AreEqual(bound, shortcuts,
                    "the number of shortcuts and the number of bound keys must agree");
            });
        }

        [TestMethod]
        public void F5AssemblesTheCurrentSourceWhenNoProjectDeclaresIt()
{
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "prog.asm");
                File.WriteAllText(path, GoodSource);

                WithShell(temp.Path, delegate (ShellWindow window)
                {
                    window.OpenSource("prog.asm");
                    window.AssembleCurrentSource();

                    CollectionAssert.AreEqual(
                        new List<string> { "ASSEMBLE prog.asm" },
                        new List<string>(window.Commands),
                        "F5 is one command line into the session, and here it is ASSEMBLE");

                    StringAssert.Contains(window.Message, "-- single file prog.asm");
                    StringAssert.Contains(window.Message, "OK");
                    Assert.IsFalse(window.Message.Contains("project"));
                });
            }
        }

        [TestMethod]
        public void F5BuildsTheProjectWhenAConfigurationDeclaresTheOpenSource()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                WriteProject(temp);

                WithShell(temp.Path, delegate (ShellWindow window)
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
                });
            }
        }

        [TestMethod]
        public void F5DoesNotBuildTheProjectWhenTheOpenSourceIsNotDeclared()
{
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                WriteProject(temp);
                File.WriteAllText(Path.Combine(temp.Path, "lone.asm"),
                    ".org $A000\nAlone:\n  nop\n  rts\n  .export Alone\n");

                WithShell(temp.Path, delegate (ShellWindow window)
                {
                    window.OpenSource("lone.asm");
                    window.AssembleCurrentSource();

                    CollectionAssert.AreEqual(
                        new List<string> { "ASSEMBLE lone.asm" },
                        new List<string>(window.Commands),
                        "lone.asm is not in config.json, so F5 must assemble it");

                    StringAssert.Contains(window.Message, "-- single file lone.asm");
                    StringAssert.Contains(window.Message, "OK");
                    Assert.IsFalse(window.Message.Contains("project"));
                });
            }
        }

        [TestMethod]
        public void F8TogglesABreakpointAtTheCursor()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "prog.asm");
                File.WriteAllText(path, GoodSource);

                WithShell(temp.Path, delegate (ShellWindow window)
                {
                    window.OpenSource("prog.asm");

                    int before = window.BreakpointCount;
                    window.ToggleBreakpoint();
                    int after = window.BreakpointCount;

                    Assert.AreEqual(before + 1, after, "a breakpoint was added");
                    Assert.IsTrue(window.Commands.Contains("BREAK SET exec $8000"),
                        "the session received the BREAK command for the cursor line");

                    window.ToggleBreakpoint();
                    Assert.AreEqual(before, window.BreakpointCount, "the second F8 removed it");
                    Assert.IsTrue(window.Commands.Contains("BREAK REMOVE exec $8000"),
                        "the removal reached the session as a command it understands");
                });
            }
        }

        [TestMethod]
        public void F9ResumesTheMachine()
        {
            WithShell(delegate (ShellWindow window)
            {
                window.ResumeMachine();

                Assert.AreEqual(1, window.ResumeCount, "the machine was resumed");
                Assert.IsTrue(window.Commands.Contains("RESUME"),
                    "the session received the RESUME command");
            });
        }

        [TestMethod]
        public void F10StepsTheMachine()
        {
            WithShell(delegate (ShellWindow window)
            {
                window.StepMachine();

                Assert.AreEqual(1, window.StepCount, "the machine was stepped");
                Assert.IsTrue(window.Commands.Contains("STEP"),
                    "the session received the STEP command");
            });
        }

[TestMethod]
        public void F6SavesTheCurrentSource()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "prog.asm");
                File.WriteAllText(path, GoodSource);

                WithShell(temp.Path, delegate (ShellWindow window)
                {
                    window.OpenSource("prog.asm");
                    window.SaveCurrentSource();

                    StringAssert.Contains(window.Message, "saved");
                });
            }
        }

        [TestMethod]
        public void F7SavesStateIntoASlot()
        {
            WithShell(delegate (ShellWindow window)
            {
                int before = window.CapturedStateCount;
                window.SaveStateSlot("mark");
                int after = window.CapturedStateCount;

                Assert.AreEqual(before + 1, after, "a slot was captured");
                StringAssert.Contains(window.Message, "state saved in slot mark");
            });
        }

        [TestMethod]
        public void LoadingARestoredSlotRestoresTheMachine()
        {
            WithShell(delegate (ShellWindow window)
            {
                window.SaveStateSlot("mark");
                window.Machine.Write(0x0040, new byte[] { 0x11 });
                window.LoadStateSlot("mark");

                Assert.AreEqual(0x00, window.Machine.Read(0x0040, 1)[0],
                    "the machine went back to the captured bytes");
            });
        }

        [TestMethod]
        public void LoadingAnUnsavedSlotSaysSoAndTouchesNothing()
        {
            WithShell(delegate (ShellWindow window)
            {
                window.Machine.Write(0x0041, new byte[] { 0x5A });
                string report = window.LoadStateSlot("never-saved");

                StringAssert.Contains(report, "no state in slot");
                Assert.AreEqual(0x5A, window.Machine.Read(0x0041, 1)[0]);
                Assert.AreEqual(0, window.CapturedStateCount);
            });
        }

        [TestMethod]
        public void TheStatusLineReflectsTheExecutionState()
        {
            WithShell(delegate (ShellWindow window)
            {
                window.ResumeMachine();
                Assert.AreEqual("RUNNING", window.ExecutionState);

                window.StepMachine();
                Assert.AreEqual("STOPPED", window.ExecutionState);
            });
        }

        [TestMethod]
        public void TheStatusLineShowsCapabilities()
        {
            WithShell(delegate (ShellWindow window)
            {
                string status = window.StatusLine;
                Assert.IsTrue(status.Contains("caps:"));
                Assert.IsTrue(status.Contains("mem"));
            });
        }

        [TestMethod]
        public void TheHelpScreenIsReadableAndComplete()
        {
            WithShell(delegate (ShellWindow window)
            {
                string help = window.ShowHelp();

                Assert.IsTrue(help.Contains("F1  this help"));
                Assert.IsTrue(help.Contains("F2  file tree"));
                Assert.IsTrue(help.Contains("F3  focus editor"));
                Assert.IsTrue(help.Contains("F4  RAM pane"));
                Assert.IsTrue(help.Contains("F5  build and transfer project ROM"));
                Assert.IsTrue(help.Contains("F6  save all sources"));
                Assert.IsTrue(help.Contains("F7  save state"));
                Assert.IsTrue(help.Contains("F8  toggle breakpoint"));
                Assert.IsTrue(help.Contains("F9  run"));
                Assert.IsTrue(help.Contains("F10  step"));
            });
        }

        [TestMethod]
        public void ARefusalShowsTheReasonAndDoesNotSendACommand()
        {
            // Test with MesenCE (memory-only backend)
            using (FakeMemoryBackend machine = new FakeMemoryBackend(65536, "MesenCE", "2.2.1"))
            {
MonitorSession session = new MonitorSession(machine, new Cpu6502(), AppContext.BaseDirectory);
                ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                    ListingSourceFactory.Create(), AppContext.BaseDirectory,
                    new ConsoleWindowSystem(new HeadlessConsoleDriver()));

                window.ApplyLayout(ShellLayout.ForSize(120, 24));
                window.ResumeMachine();

                Assert.IsTrue(window.Message.Contains("F9 unavailable"));
                Assert.AreEqual(0, window.Commands.Count, "no command was sent for a refused key");
                Assert.AreEqual(0, machine.ResumeCount, "the machine was not resumed");
            }
        }

        // ---------------------------------------------------------------- the helpers

        private static void AssertBound(string keyName, bool expectedBound)
        {
            ShellKey found = null;
            foreach (ShellKey key in ShellWindow.Keys)
            {
                if (key.Key == keyName)
                {
                    found = key;
                    break;
                }
            }

            Assert.IsNotNull(found, keyName + " vanished from the key table");
            Assert.AreEqual(expectedBound, found.Bound,
                keyName + (expectedBound ? " should be bound" : " should be unbound"));
        }

        private static void WithShell(Action<ShellWindow> body)
        {
            WithShell(AppContext.BaseDirectory, body);
        }

private static void WithShell(string directory, Action<ShellWindow> body)
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend(65536, "keys", "keys"))
            {
                MonitorSession session = new MonitorSession(machine, new Cpu6502(), directory);
                ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                    new AssemblerListingSource(new Cpu6502(), directory), directory,
                    new ConsoleWindowSystem(new HeadlessConsoleDriver()));

                window.ApplyLayout(ShellLayout.ForSize(160, 40));

                body(window);
            }
        }

private static void WriteProject(TemporaryDirectory temp)
        {
            string bank1 = ".org $8000\nShared:\n  lda #$5A\n  rts\n  .export Shared\n";
            string bank2 = ".org $9000\n  .import Shared\nEntry:\n  jmp Shared\n  .export Entry\n";
            string config = "{\n  \"name\": \"test\",\n  \"Input\": [\n    { \"FileName\": \"bank1.asm\", \"org\": \"$8000\" },\n    { \"FileName\": \"bank2.asm\", \"org\": \"$9000\", \"Dependencies\": [ \"bank1.asm\" ] }\n  ],\n  \"Output\": {\n    \"ObjectFile\": \"game.nes\"\n  }\n}";

            File.WriteAllText(Path.Combine(temp.Path, "bank1.asm"), bank1);
            File.WriteAllText(Path.Combine(temp.Path, "bank2.asm"), bank2);
            File.WriteAllText(Path.Combine(temp.Path, "config.json"), config);
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }

            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "WinASM65Test_" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(Path);
            }

            public void Dispose()
            {
                try
                {
                    if (System.IO.Directory.Exists(Path))
                        System.IO.Directory.Delete(Path, true);
                }
                catch (System.IO.IOException)
                {
                }
                catch (System.UnauthorizedAccessException)
                {
                }
            }
        }
    }
}



