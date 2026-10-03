using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Terminal.Gui;
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
    /// Terminal.Gui is driven through its own <see cref="FakeDriver"/> and every run
    /// is bounded by a stop requested from inside the loop, as in
    /// <c>ShellEditorKeyTests</c>. Nothing here waits on a keystroke nobody is going
    /// to type.
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
                Assert.AreEqual(0, window.Breakpoints.Count);
                Assert.AreEqual(0, (machine.Breakpoints == null ? 0 : machine.Breakpoints.Count));
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

        // ---------------------------------------------------------------- help and status

        [TestMethod]
        public void TheHelpScreenAndTheStatusLineDescribeTheSameMachine()
        {
            WithShell(MemoryOnly, MemoryOnlyVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                Press(Key.F1, window, driver: null);

                string help = window.Message;
                string status = window.Status.Line;

                // One flag set, one wording, two screens. Two vocabularies for the same
                // machine is how a help screen promises something the status line has
                // denied.
                string summary = ExecutionCapabilities.Summary(window.Capabilities);

                StringAssert.Contains(status, "caps: " + summary);
                StringAssert.Contains(help, summary);
                StringAssert.Contains(help, "MesenCE 2.2.1");
                StringAssert.Contains(status, "MesenCE 2.2.1");

                StringAssert.Contains(help, "this machine:");
                StringAssert.Contains(status, ExecutionCapabilities.MissingExecutionControl(window.Capabilities));
            });
        }

        [TestMethod]
        public void TheHelpScreenNamesWhyEveryGatedKeyIsGated()
        {
            WithShell(MemoryOnly, MemoryOnlyVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                Press(Key.F1, window, driver: null);

                string help = window.Message;

                // Bound by this build, refused by this machine, and the reason is in
                // the bridges' own words: F1 is the screen a user opens precisely to
                // find out why a key did nothing.
                StringAssert.Contains(help,
                    "(bound, but MesenCE 2.2.1 cannot resume: emu.resume refuses calls made outside a callback)");
                StringAssert.Contains(help,
                    "(bound, but MesenCE 2.2.1 cannot step one instruction:");
                StringAssert.Contains(help,
                    "(bound, but MesenCE 2.2.1 cannot break on an address:");

                // F7 is not among them, and that is the point: this host snapshots, so
                // gating F7 here would be a gate that lies.
                Assert.IsFalse(Line(help, "F7").Contains("(bound, but"));

                // F12 is a different fact and keeps its own wording: this build never
                // wrote it, which is not the machine's fault.
                StringAssert.Contains(help, "(not in this build)");
            });
        }

        [TestMethod]
        public void TheHelpScreenGatesSnapshottingOnlyOnAMachineWithNoSnapshots()
        {
            WithShell(NoSnapshots, NoSnapshotsVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                Press(Key.F1, window, driver: null);

                string help = window.Message;

                StringAssert.Contains(help,
                    "(bound, but MAME 0.289 cannot save or restore a snapshot:");

                // Everything MAME has, it keeps, and the summary says so in the same
                // words the status line uses.
                Assert.IsFalse(Line(help, "F5").Contains("(bound, but"));
                StringAssert.Contains(help, "this machine: MAME 0.289 -- memory + registers");
            });
        }

        [TestMethod]
        public void AKeyTheShellCanDoOnItsOwnIsNeverGated()
        {
            // F5 assembles and Ctrl+Q quits: neither asks anything of the machine, so
            // gating them would be gating the shell against itself. The other keys on
            // the same help screen are gated on this machine, so the check is per line
            // rather than over the whole screen.
            WithShell(MemoryOnly, MemoryOnlyVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                Assert.AreEqual(ExecutionCapability.None, Requires("F5"));
                Assert.AreEqual(ExecutionCapability.None, Requires("Ctrl+Q"));
                Assert.AreEqual(ExecutionCapability.None, Requires("F1"));
                Assert.AreEqual(ExecutionCapability.None, Requires("F2"));

                Press(Key.F1, window, driver: null);

                foreach (string key in new[] { "F1", "F2", "F5", "Ctrl+Q" })
                    Assert.IsFalse(Line(window.Message, key).Contains("(bound, but"),
                        key + " needs nothing from the machine and must not be gated");

                // F9 is the contrast: it does ask something, so it is gated, and the two
                // facts are on the same screen at the same time.
                Assert.IsTrue(Line(window.Message, "F9").Contains("(bound, but"));
            });
        }

        [TestMethod]
        public void TheStatusLineSaysWhatTheMachineCannotDoWithoutAnyKeyBeingPressed()
        {
            WithShell(MemoryOnly, MemoryOnlyVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                string status = window.Status.Line;

                StringAssert.Contains(status, "caps: memory + snapshots");
                StringAssert.Contains(status, "cannot run, step or break");
                StringAssert.Contains(status, "MesenCE 2.2.1");
            });
        }

        [TestMethod]
        public void TheStatusLineKeepsTheBreakpointCountEvenWhileItExplainsALimitedMachine()
        {
            // The gating must not cost the status line anything it already said. BP=0
            // is the count of what this shell believes it set, and on a machine that
            // can set none that is zero — still worth showing, and worth showing at
            // the width this project calls wide.
            WithShell(MemoryOnly, MemoryOnlyVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                string status = window.Status.Line;

                StringAssert.Contains(status, "caps: memory + snapshots");
                StringAssert.Contains(status, "BP=0");
            });
        }

        [TestMethod]
        public void AFullControlMachineSaysFullControlAndNoWarning()
        {
            WithShell(FullControl, FullControlVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                string status = window.Status.Line;

                StringAssert.Contains(status, "caps: full control");
                Assert.IsFalse(status.Contains("cannot run, step or break"),
                    "a machine that can do all three has nothing to warn about");
            });
        }

        // ---------------------------------------------------------------- the working path

        [TestMethod]
        public void AFullControlMachineStillRunsStepsBreaksAndSnapshots()
        {
            // The regression that matters most. End to end: real adapter, real
            // handshake, real socket, real session — the same four keys the memory-only
            // case above refuses.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, GoodSource);

                WithShell(FullControl, FullControlVersion, temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource(name);
                    window.AssembleCurrentSource();

                    window.ResumeMachine();
                    Assert.AreEqual(1, machine.ResumeCount, "F9 did not reach the machine");
                    StringAssert.Contains(window.Message, "OK");

                    window.StepMachine();
                    Assert.AreEqual(1, machine.StepCount, "F10 did not reach the machine");

                    window.ToggleBreakpoint();
                    Assert.AreEqual(1, window.Breakpoints.Count, "F8 did not set a breakpoint");
                    Assert.AreEqual(1, (machine.Breakpoints == null ? 0 : machine.Breakpoints.Count));

                    string report = window.SaveStateSlot("mark");
                    Assert.AreEqual(1, window.CapturedStateCount, "F7 captured nothing");
                    StringAssert.Contains(report, "state saved in slot mark");

                    CollectionAssert.AreEqual(
                        new List<string>
                        {
                            "ASSEMBLE " + name,
                            "RESUME",
                            "STEP",
                            "BREAK SET exec $8000",
                            "STATE SAVE",
                        },
                        new List<string>(window.Commands),
                        "a key was gated on a machine that can do it");
                });
            }
        }

        [TestMethod]
        public void AFullControlMachineKeepsTheShortcutsAndTheHelpScreenSaysNothingIsGated()
        {
            WithShell(FullControl, FullControlVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                // The wiring: every bound key still has a Shortcut, whatever the machine.
                foreach (ShellKey key in ShellWindow.Keys)
                {
                    Key bound = KeyOf(key.Key);

                    // ReferenceEquals, not == : Terminal.Gui's Key is a class whose ==
                    // operator dereferences both sides, so the null test has to be the
                    // reference one. Ctrl+Q is the entry that returns null.
                    if (!key.Bound || ReferenceEquals(bound, null))
                        continue;

                    Assert.IsTrue(HasShortcut(window, bound),
                        key.Key + " lost its shortcut because the gate ran");
                }

                Press(Key.F1, window, driver: null);

                Assert.IsFalse(window.Message.Contains("(bound, but"),
                    "a machine with full control has no gated key to explain");
                Assert.IsFalse(window.Message.Contains("cannot run, step or break"));
            });
        }

        [TestMethod]
        public void TheStatusLineStillReportsTheBreakpointCountOnAMachineThatCannotBreak()
        {
            WithShell(NoSnapshots, NoSnapshotsVersion, delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                // Nothing can be set here, so the count is zero, and saying so is
                // still the truth about what this shell believes it did.
                Assert.AreEqual(0, window.Breakpoints.Count);
                StringAssert.Contains(window.Status.Line, "BP=0");
            });
        }

        // ---------------------------------------------------------------- through a key

        [TestMethod]
        public void PressingF9ThroughTheRunLoopRefusesItOnScreenAndLeavesTheShellAlive()
        {
            // The whole point, driven the way a user drives it: a real key event
            // through Terminal.Gui, then a real draw, then the reason read off the
            // driver's own buffer. If the gate threw, the run would never end and the
            // suite would hang — so this is also the test that says a refusal is a
            // string and not an exception.
            FakeDriver driver = new FakeDriver();
            Application.Init(driver);
            try
            {
                driver.SetWindowSize(160, 40);
                driver.Refresh();

                using (FakeMemoryBackend machine = new FakeMemoryBackend(65536, MemoryOnly, MemoryOnlyVersion))
                using (ProtocolServer server = new ProtocolServer(machine, CpuFactory.Create("6502")))
                {
                    server.Start();

                    using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(server.Port))
                    {
                        MonitorSession session =
                            new MonitorSession(bridge, new Cpu6502(), AppContext.BaseDirectory);

                        ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                            ListingSourceFactory.Create(), AppContext.BaseDirectory);

                        window.Frame = new System.Drawing.Rectangle(0, 0, 160, 40);
                        window.ApplyLayout(ShellLayout.ForSize(160, 40));

                        string screen = Press(Key.F9, window, driver);

                        StringAssert.Contains(screen, "F9 unavailable on MesenCE 2.2.1",
                            "the refusal did not reach the screen: " + screen);
                        StringAssert.Contains(screen, "caps: memory + snapshots",
                            "the status line did not say what this machine can do");

                        Assert.AreEqual(0, machine.ResumeCount);
                    }
                }
            }
            finally
            {
                Application.Shutdown();
            }
        }

        [TestMethod]
        public void PressingF1ThroughTheRunLoopPutsTheMachineLimitsOnScreen()
        {
            FakeDriver driver = new FakeDriver();
            Application.Init(driver);
            try
            {
                driver.SetWindowSize(160, 40);
                driver.Refresh();

                using (FakeMemoryBackend machine = new FakeMemoryBackend(65536, MemoryOnly, MemoryOnlyVersion))
                using (ProtocolServer server = new ProtocolServer(machine, CpuFactory.Create("6502")))
                {
                    server.Start();

                    using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(server.Port))
                    {
                        MonitorSession session =
                            new MonitorSession(bridge, new Cpu6502(), AppContext.BaseDirectory);

                        ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                            ListingSourceFactory.Create(), AppContext.BaseDirectory);

                        window.Frame = new System.Drawing.Rectangle(0, 0, 160, 40);
                        window.ApplyLayout(ShellLayout.ForSize(160, 40));

                        string screen = Press(Key.F1, window, driver);

                        // The first line of the help is the machine's own summary,
                        // because the message row is one row tall and that is the line
                        // worth having without pressing anything else.
                        StringAssert.Contains(screen, "this machine: MesenCE 2.2.1");
                    }
                }
            }
            finally
            {
                Application.Shutdown();
            }
        }

        // ---------------------------------------------------------------- the pure backend

        [TestMethod]
        public void ThePureTestBackendIsGatedForRegistersRatherThanHandedACpuView()
        {
            // No emulator here at all, and no processor: the precedent is
            // ICpuStateSource, and the flag now says the same thing to the shell.
            WithFakeShell(delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                Assert.IsFalse(ExecutionCapabilities.Has(window.Capabilities, ExecutionCapability.CpuState));

                StringAssert.Contains(window.Status.Line, "no CPU view");
                StringAssert.Contains(window.Status.Line, "cannot report registers");
            });
        }

        [TestMethod]
        public void ThePureTestBackendStillDrivesEverythingItActuallyImplements()
        {
            // The other half of the same test: gated for registers, not gated for
            // everything. A backend that declared nothing would make the whole working
            // path of the shell untestable, which is the opposite of honest.
            WithFakeShell(delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                Assert.IsTrue(ExecutionCapabilities.Has(window.Capabilities, ExecutionCapability.Resume));
                Assert.IsTrue(ExecutionCapabilities.Has(window.Capabilities, ExecutionCapability.StepInstruction));
                Assert.IsTrue(ExecutionCapabilities.Has(window.Capabilities, ExecutionCapability.StateSaveLoad));
                Assert.IsTrue(ExecutionCapabilities.Has(window.Capabilities, ExecutionCapability.BreakpointExecution));

                window.ResumeMachine();
                window.StepMachine();

                Assert.AreEqual(1, machine.ResumeCount);
                Assert.AreEqual(1, machine.StepCount);
                Assert.IsFalse(window.Message.Contains("unavailable"));
            });
        }

        // ---------------------------------------------------------------- the sentences

        [TestMethod]
        public void TheRefusalSentenceIsBuiltFromTheHostAndTheAction()
        {
            // Asserted here as a sentence, without a terminal, because a refusal the
            // user reads is a claim and a claim has to be checkable on its own.
            string refusal = ShellCapabilities.Refusal("F9", "MesenCE 2.2.1", ExecutionCapability.Resume);

            Assert.AreEqual(
                "F9 unavailable on MesenCE 2.2.1: this machine cannot resume. F1 names the measurement.",
                refusal);
        }

        [TestMethod]
        public void ARefusalOnAnUnnamedBackendStillNamesSomething()
        {
            StringAssert.Contains(
                ShellCapabilities.Refusal("F10", null, ExecutionCapability.StepInstruction),
                "the attached backend");
        }

        [TestMethod]
        public void TheGateAsksForEveryBitRatherThanAnyBit()
        {
            Assert.IsTrue(ShellCapabilities.Allows(
                ExecutionCapability.Memory, ExecutionCapability.MemoryRead));
            Assert.IsFalse(ShellCapabilities.Allows(
                ExecutionCapability.MemoryRead, ExecutionCapability.Memory));
            Assert.IsFalse(ShellCapabilities.Allows(
                ExecutionCapability.Pause | ExecutionCapability.Resume, ExecutionCapability.StepInstruction));
        }

        [TestMethod]
        public void ASummaryIsShortEnoughForAStatusLineAndSaysNothingIsMeasuredWhenNothingIs()
        {
            Assert.AreEqual("full control", ExecutionCapabilities.Summary(ExecutionCapability.FullControl));
            Assert.AreEqual("memory + snapshots", ExecutionCapabilities.Summary(ExecutionCapabilities.MesenCe_2_2_1));
            Assert.AreEqual("memory + registers", ExecutionCapabilities.Summary(ExecutionCapabilities.Mame_0_289));
            Assert.AreEqual("nothing measured", ExecutionCapabilities.Summary(ExecutionCapability.None));

            // A status-line quantity: short for every backend that can actually be
            // attached, and bounded for every combination, including the ones no
            // measured backend declares. "caps: " plus the widest summary still has to
            // leave room for the host, the run state and the breakpoint count.
            foreach (ExecutionCapability measured in new[]
            {
                ExecutionCapabilities.Mesen2_2_1_1,
                ExecutionCapabilities.MesenCe_2_2_1,
                ExecutionCapabilities.Mame_0_289,
            })
            {
                Assert.IsTrue(ExecutionCapabilities.Summary(measured).Length <= 24,
                    "'" + ExecutionCapabilities.Summary(measured) + "' is longer than a measured backend needs to be");
            }

            foreach (ExecutionCapability bits in AllCombinations())
            {
                string summary = ExecutionCapabilities.Summary(bits);

                Assert.IsTrue(summary.Length <= 64,
                    "'" + summary + "' is " + summary.Length + " characters and does not belong on a status line");
            }
        }

        [TestMethod]
        public void TheMissingControlSentenceFollowsTheBitsRatherThanAScript()
        {
            Assert.IsNull(ExecutionCapabilities.MissingExecutionControl(ExecutionCapability.FullControl));
            Assert.AreEqual("cannot run, step or break",
                ExecutionCapabilities.MissingExecutionControl(ExecutionCapability.Memory));

            // Gaining one verb drops that word from the sentence, so a backend that
            // starts being able to resume is not still told it cannot run.
            Assert.AreEqual("cannot step or break",
                ExecutionCapabilities.MissingExecutionControl(
                    ExecutionCapability.Memory | ExecutionCapability.Resume));
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// A shell over a real bridge to a machine whose handshake names the backend
        /// being simulated.
        ///
        /// Real adapter, real handshake, real socket, real session. The only thing
        /// faked is the emulator at the far end, which is the one thing these tests
        /// are not about.
        /// </summary>
        private static void WithShell(string name, string version, Action<ShellWindow, FakeMemoryBackend> body)
        {
            WithShell(name, version, AppContext.BaseDirectory, body);
        }

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

        /// <summary>The shell against the project's own test backend, with no emulator.</summary>
        private static void WithFakeShell(Action<ShellWindow, FakeMemoryBackend> body)
        {
            FakeDriver driver = new FakeDriver();
            Application.Init(driver);
            try
            {
                driver.SetWindowSize(160, 40);
                driver.Refresh();

                using (FakeMemoryBackend machine = new FakeMemoryBackend())
                {
                    MonitorSession session =
                        new MonitorSession(machine, new Cpu6502(), AppContext.BaseDirectory);

                    ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                        ListingSourceFactory.Create(), AppContext.BaseDirectory);

                    window.Frame = new System.Drawing.Rectangle(0, 0, 160, 40);
                    window.ApplyLayout(ShellLayout.ForSize(160, 40));

                    body(window, machine);
                }
            }
            finally
            {
                Application.Shutdown();
            }
        }

        /// <summary>
        /// Raises one key inside the run loop and ends the loop from the same handler,
        /// returning what the driver had drawn by then.
        ///
        /// <c>Application.Init</c> empties Terminal.Gui's static event table, so a
        /// handler subscribed before the call is never raised and the loop then waits
        /// for a keystroke nobody is going to type — a hang, not a failure. The bound
        /// lives here so it cannot happen.
        ///
        /// The screen is read from inside the loop rather than after it. <c>Run</c>
        /// tears the driver down on the way out, so a draw attempted afterwards
        /// produces nothing and every assertion about it would pass on an empty buffer
        /// — which is exactly the sort of test that proves nothing.
        /// </summary>
        private static string Press(Key key, ShellWindow window, FakeDriver driver)
        {
            if (driver != null)
                Application.LayoutAndDraw(true);

            bool handled = false;
            string screen = null;
            Key pressed = key;

            EventHandler<IterationEventArgs> onIteration = delegate (object sender, IterationEventArgs args)
            {
                handled = Application.RaiseKeyDownEvent(pressed) || handled;

                if (driver != null)
                {
                    Application.LayoutAndDraw(true);
                    driver.Refresh();
                    screen = Screen(driver);
                }

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
            return screen;
        }

        private static ExecutionCapability Requires(string key)
        {
            foreach (ShellKey candidate in ShellWindow.Keys)
            {
                if (candidate.Key == key)
                    return candidate.Requires;
            }

            Assert.Fail("the key table does not list " + key);
            return ExecutionCapability.None;
        }

        private static bool HasShortcut(ShellWindow window, Key key)
        {
            foreach (View child in window.Subviews)
            {
                if (child is Shortcut && ((Shortcut)child).Key == key)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The key a table entry is wired to, or null for one this test does not need
        /// to match. Ctrl+Q is a code with a modifier bit on it, and building that
        /// here would be a second copy of <c>ShellWindow</c>'s own little helper.
        /// </summary>
        private static Key KeyOf(string name)
        {
            switch (name)
            {
                case "F1": return Key.F1;
                case "F2": return Key.F2;
                case "F3": return Key.F3;
                case "F4": return Key.F4;
                case "F5": return Key.F5;
                case "F7": return Key.F7;
                case "F8": return Key.F8;
                case "F9": return Key.F9;
                case "F10": return Key.F10;
                case "F12": return Key.F12;
                default: return null;
            }
        }

        /// <summary>
        /// One line of the help screen, by the key it starts with.
        ///
        /// Checked line by line because the two facts being compared live on the same
        /// screen: a key this machine can use and a key it cannot, both listed.
        /// </summary>
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

        /// <summary>Everything the driver has drawn, row by row.</summary>
        private static string Screen(FakeDriver driver)
        {
            int lines = driver.Contents.GetLength(0);
            int cols = driver.Contents.GetLength(1);

            System.Text.StringBuilder text = new System.Text.StringBuilder();

            for (int row = 0; row < lines; row++)
            {
                for (int col = 0; col < cols; col++)
                    text.Append((char)driver.Contents[row, col].Rune.Value);

                text.Append('\n');
            }

            return text.ToString();
        }

        /// <summary>
        /// Every subset of the eleven bits, so a claim that cannot be phrased briefly
        /// is found here rather than on a status line in front of a user.
        /// </summary>
        private static IEnumerable<ExecutionCapability> AllCombinations()
        {
            ExecutionCapability[] bits =
            {
                ExecutionCapability.MemoryRead, ExecutionCapability.MemoryWrite,
                ExecutionCapability.CpuState, ExecutionCapability.Pause, ExecutionCapability.Resume,
                ExecutionCapability.Reset, ExecutionCapability.StepInstruction,
                ExecutionCapability.BreakpointExecution, ExecutionCapability.WatchpointRead,
                ExecutionCapability.WatchpointWrite, ExecutionCapability.StateSaveLoad,
            };

            for (int subset = 0; subset < (1 << bits.Length); subset++)
            {
                ExecutionCapability combined = ExecutionCapability.None;

                for (int bit = 0; bit < bits.Length; bit++)
                {
                    if ((subset & (1 << bit)) != 0)
                        combined |= bits[bit];
                }

                yield return combined;
            }
        }

        private static string Write(TemporaryDirectory temp, string text)
        {
            const string Name = "prog.asm";
            File.WriteAllText(Path.Combine(temp.Path, Name), text);
            return Name;
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
                    System.IO.Path.GetTempPath(), "WinASM65Gating_" + Guid.NewGuid().ToString("N"));
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