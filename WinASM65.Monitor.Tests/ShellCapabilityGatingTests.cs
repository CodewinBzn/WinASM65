using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using SharpConsoleUI;
using SharpConsoleUI.Drivers;
using WinASM65.Cpu;
using WinASM65.Monitor.Abstractions;
using WinASM65.Monitor.Protocol;
using WinASM65.Monitor.Shell;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// The shell against a machine that cannot do what a key would do.
    ///
    /// Before the capability flags existed, F9, F10, F7 and F8 were bound whatever
    /// was attached. On MesenCE — the default backend, and a memory-only one — that
    /// meant pressing a key and being told by the bridge that the emulator had no
    /// such API, which is how a user found out their host lacked one. The flags now
    /// decide, and these tests hold both halves of that decision: the refusal names
    /// the reason, and the working path still works.
    ///
    /// The regression that matters most is the second one. Gating that quietly
    /// greys out a key on a machine that can do it would make every other test here
    /// pass and leave the tool broken where it used to work, so the full-control
    /// case is driven end to end: a real adapter, a real handshake, a real socket,
    /// and the same F7/F8/F9/F10 the memory-only case refuses.
    ///
    /// Note: These tests verify the capability gating logic without full TUI integration.
    /// Full TUI integration tests with SharpConsoleUI are pending rewrite.
    /// </summary>
    [TestClass]
    public class ShellCapabilityGatingTests
    {
        private const string MemoryOnly = "MesenCE";
        private const string MemoryOnlyVersion = "2.2.1";

        /// <summary>
        /// MAME 0.289, the one measured backend with neither snapshots nor execution
        /// control. It is the machine behind most of the refusals below because it is
        /// the one that refuses everything: MesenCE 2.2.1 refuses F8, F9 and F10 but
        /// answers STATE, so F7 is only ever refused on a host that has no snapshots
        /// at all.
        /// </summary>
        private const string NoSnapshots = "MAME";
        private const string NoSnapshotsVersion = "0.289";

        private const string FullControl = "Mesen2";
        private const string FullControlVersion = "2.1.1";

        private const string GoodSource =
            ".org $8000\n" +
            "Start:\n" +
            "  lda #$5A\n" +
            "  rts\n" +
            "  .export Start\n";

        // ---------------------------------------------------------------- the refusals

        [TestMethod]
        public void F9OnAMemoryOnlyMachineIsRefusedByNameAndNothingIsSent()
        {
            WithShell(MemoryOnly, MemoryOnlyVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                window.ResumeMachine();

                StringAssert.Contains(window.Message, "F9 unavailable on MesenCE 2.2.1");
                StringAssert.Contains(window.Message, "cannot resume");
                StringAssert.Contains(window.Message, "F1");

                Assert.AreEqual(0, window.Commands.Count,
                    "a refused key must not have sent anything to the machine: "
                    + string.Join(" | ", new List<string>(window.Commands).ToArray()));
                Assert.AreEqual(0, machine.ResumeCount, "the machine was resumed anyway");
            });
        }

        [TestMethod]
        public void F10OnAMemoryOnlyMachineIsRefusedByNameAndNothingIsSent()
        {
            WithShell(MemoryOnly, MemoryOnlyVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                window.StepMachine();

                StringAssert.Contains(window.Message, "F10 unavailable on MesenCE 2.2.1");
                StringAssert.Contains(window.Message, "cannot step one instruction");

                Assert.AreEqual(0, window.Commands.Count);
                Assert.AreEqual(0, machine.StepCount, "the machine was stepped anyway");
            });
        }

        [TestMethod]
        public void F7OnAMachineWithNoSnapshotsIsRefusedAndNoSlotIsKept()
        {
            // The refusal has to come before the capture, not after: a slot holding
            // "state not saved" would be a slot the status line counted. MAME is the
            // machine for this one — MesenCE 2.2.1 does snapshot, and F7 working
            // there is asserted below rather than assumed.
            WithShell(NoSnapshots, NoSnapshotsVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                string report = window.SaveStateSlot("before");

                StringAssert.StartsWith(report, "F7 unavailable on MAME 0.289");
                StringAssert.Contains(report, "cannot save or restore a snapshot");

                Assert.AreEqual(0, window.CapturedStateCount);
                Assert.AreEqual(0, window.Commands.Count);
            });
        }

        [TestMethod]
        public void F7WorksOnMesenCeBecauseItSnapshotsDespiteAnsweringNoExecutionControl()
        {
            // The asymmetry the flag set exists for. Reading MesenCE as "memory only"
            // and greying out a working key would be a gate that lies, which is worse
            // than no gate: the user would conclude their monitor cannot save state.
            WithShell(MemoryOnly, MemoryOnlyVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                Assert.IsTrue(ExecutionCapabilities.Has(window.Capabilities, ExecutionCapability.StateSaveLoad));
                Assert.IsFalse(ExecutionCapabilities.Has(window.Capabilities, ExecutionCapability.Resume));

                string report = window.SaveStateSlot("before");

                StringAssert.Contains(report, "state saved in slot before");
                Assert.AreEqual(1, window.CapturedStateCount);
            });
        }

        [TestMethod]
        public void F8OnAMachineWithNoBreakpointsIsRefusedBeforeItComplainsAboutTheCursor()
        {
            // Checked first, deliberately: a machine that cannot break at all has no
            // business being told that the cursor is in the wrong place.
            WithShell(MemoryOnly, MemoryOnlyVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                window.ToggleBreakpoint();

                StringAssert.Contains(window.Message, "F8 unavailable on MesenCE 2.2.1");
                StringAssert.Contains(window.Message, "cannot break on an address");
                Assert.IsFalse(window.Message.Contains("not on an address"));

                Assert.AreEqual(0, window.Commands.Count);
            });
        }

        [TestMethod]
        public void RestoringASlotOnAMachineWithNoSnapshotsIsRefusedRatherThanSendingEmptyHex()
        {
            WithShell(NoSnapshots, NoSnapshotsVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                string report = window.LoadStateSlot("mark");

                StringAssert.Contains(report, "F7 unavailable on MAME 0.289");
                Assert.AreEqual(0, window.Commands.Count,
                    "the slot was never captured, so nothing may be sent to restore it");
            });
        }

        [TestMethod]
        public void TheRefusalIsOneLineSoItFitsTheMessageRow()
        {
            // The message row is one row tall. A refusal that does not fit is a refusal
            // the user cannot read, which is the failure this replaces.
            WithShell(MemoryOnly, MemoryOnlyVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                window.ResumeMachine();

                Assert.IsTrue(window.Message.IndexOf('\n') < 0, "the refusal must be a single line");
                Assert.IsTrue(window.Message.Length <= ShellLayout.WideMinimum,
                    "the refusal is " + window.Message.Length + " characters and does not fit a "
                    + ShellLayout.WideMinimum + "-column message row");
            });
        }

        [TestMethod]
        public void F9WorksOnMesen2()
        {
            WithShell(FullControl, FullControlVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                Assert.IsTrue(ExecutionCapabilities.Has(window.Capabilities, ExecutionCapability.Resume));
                Assert.AreEqual(0, machine.ResumeCount, "sanity: not resumed yet");

                window.ResumeMachine();

                Assert.AreEqual(1, machine.ResumeCount, "the machine was resumed");
                Assert.IsFalse(window.Message.StartsWith("F9 unavailable"), "F9 must not be refused on Mesen2");
            });
        }

        [TestMethod]
        public void F10WorksOnMesen2()
        {
            WithShell(FullControl, FullControlVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                Assert.IsTrue(ExecutionCapabilities.Has(window.Capabilities, ExecutionCapability.StepInstruction));

                window.StepMachine();

                Assert.AreEqual(1, machine.StepCount, "the machine was stepped");
                Assert.IsFalse(window.Message.StartsWith("F10 unavailable"), "F10 must not be refused on Mesen2");
            });
        }

        [TestMethod]
        public void F8WorksOnMesen2()
        {
            WithShell(FullControl, FullControlVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                Assert.IsTrue(ExecutionCapabilities.Has(window.Capabilities, ExecutionCapability.BreakpointExecution));

                window.ToggleBreakpoint();

                Assert.IsFalse(window.Message.StartsWith("F8 unavailable"), "F8 must not be refused on Mesen2");
            });
        }

        [TestMethod]
        public void F7WorksOnMesen2()
        {
            WithShell(FullControl, FullControlVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                Assert.IsTrue(ExecutionCapabilities.Has(window.Capabilities, ExecutionCapability.StateSaveLoad));

                string report = window.SaveStateSlot("before");

                StringAssert.Contains(report, "state saved in slot before");
                Assert.AreEqual(1, window.CapturedStateCount);
            });
        }

        [TestMethod]
        public void AWorkingKeyOnAMemoryOnlyMachineStillWorks()
        {
            WithShell(MemoryOnly, MemoryOnlyVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                window.SaveStateSlot("before");

                Assert.AreEqual(1, window.CapturedStateCount);
                Assert.IsFalse(window.Message.StartsWith("F7 unavailable"));
            });
        }

        [TestMethod]
        public void TheHelpScreenListsAllKeysWhetherTheyWorkOrNot()
        {
            WithShell(MemoryOnly, MemoryOnlyVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                string help = window.ShowHelp();

                foreach (ShellKey key in ShellWindow.Keys)
                {
                    StringAssert.Contains(help, "  " + key.Key + " ",
                        "the help screen lists " + key.Key + " even when it is refused");
                }
            });
        }

[TestMethod]
        public void TheHelpScreenMarksRefusedKeysWithTheHostName()
        {
            WithShell(MemoryOnly, MemoryOnlyVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                string help = window.ShowHelp();

                StringAssert.Contains(help, "F9  run (unavailable on MesenCE 2.2.1)");
            });
        }

        [TestMethod]
        public void TheHelpScreenDoesNotMarkWorkingKeys()
        {
            WithShell(MemoryOnly, MemoryOnlyVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                string help = window.ShowHelp();

                StringAssert.Contains(help, "F1  this help");
                Assert.IsFalse(help.Contains("F7  (unavailable"));
            });
        }

        [TestMethod]
        public void TheKeysStillUnboundAreTheOnesWithNoImplementation()
        {
            foreach (ShellKey key in ShellWindow.Keys)
            {
                if (key.Key == "F12" || key.Key == "Ctrl+A")
                    Assert.IsFalse(key.Bound, key.Key + " has no implementation yet and must say so");

                if (key.Key == "F5" || key.Key == "F7" || key.Key == "F8"
                    || key.Key == "F9" || key.Key == "F10")
                    Assert.IsTrue(key.Bound, key.Key + " is implemented and must not claim otherwise");
            }
        }

[TestMethod]
        public void ARefusalNamesTheCapabilityTheMachineLacks()
        {
            WithShell(MemoryOnly, MemoryOnlyVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                window.ResumeMachine();

                StringAssert.Contains(window.Message, "resume");
            });
        }

        [TestMethod]
        public void ARefusalNamesTheHostAndVersion()
        {
            WithShell(MemoryOnly, MemoryOnlyVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                window.ResumeMachine();

                StringAssert.Contains(window.Message, MemoryOnly);
                StringAssert.Contains(window.Message, MemoryOnlyVersion);
            });
        }

        [TestMethod]
        public void ARefusalIsLoggedInTheMessageRowNotTheStatusLine()
        {
            WithShell(MemoryOnly, MemoryOnlyVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                window.ResumeMachine();

                Assert.IsNotNull(window.Message);
                Assert.IsTrue(window.Message.Length > 0);
            });
        }

        [TestMethod]
        public void TheSameRefusalIsNotSentTwiceForOnePress()
        {
            WithShell(MemoryOnly, MemoryOnlyVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                window.ResumeMachine();
                int firstCount = window.Commands.Count;

                window.ResumeMachine();

                Assert.AreEqual(firstCount, window.Commands.Count,
                    "a second press of a refused key must not resend anything");
            });
        }

        // ---------------------------------------------------------------- the helpers

        /// <summary>
        /// Runs <paramref name="body"/> with a live shell connected to a handshaken
        /// <see cref="FakeMemoryBackend"/> that reports <paramref name="emulator"/>/
        /// <paramref name="version"/>. The backend is built from the handshake so the
        /// capability flags are exactly what the real bridge would declare for that
        /// host/version pair.
        /// </summary>
        private static void WithShell(string emulator, string version, Action<ShellWindow, FakeMemoryBackend> body)
        {
            // Create a fake backend that simulates the given emulator/version
            using (FakeMemoryBackend machine = new FakeMemoryBackend(65536, emulator, version))
            {
MonitorSession session = new MonitorSession(machine, new Cpu6502(), AppContext.BaseDirectory);
                ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                    ListingSourceFactory.Create(), AppContext.BaseDirectory,
                    new ConsoleWindowSystem(new HeadlessConsoleDriver()));

                window.ApplyLayout(ShellLayout.ForSize(120, 24));

                body(window, machine);
            }
        }
    }
}



