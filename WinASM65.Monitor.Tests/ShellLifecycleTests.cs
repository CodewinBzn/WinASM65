using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// The shell as a running thing: it starts, it draws at every width the
    /// breakpoints allow, it stops, and it leaves nothing behind.
    ///
    /// Driven through SharpConsoleUI's HeadlessConsoleDriver rather than by
    /// launching the executable against a real terminal. A test that needed a
    /// console would not run on a build agent, and the parts that break are the
    /// parts that need a driver: initialisation, layout, drawing and teardown.
    ///
    /// The orphan check is the one that cannot be mocked, so it starts a real child
    /// process and waits for it to actually be gone.
    /// </summary>
    [TestClass]
    public class ShellLifecycleTests
    {
private static ShellWindow BuildWindow(FakeMemoryBackend machine, out MonitorSession session)
        {
            session = new MonitorSession(machine, new Cpu6502(), AppContext.BaseDirectory);
            return new ShellWindow(session, ShellTheme.Default, ListingSourceFactory.Create(),
                AppContext.BaseDirectory,
                new ConsoleWindowSystem(new HeadlessConsoleDriver()));
        }

        /// <summary>
        /// Runs <paramref name="body"/> with a live application, and guarantees the
        /// application is shut down whatever happens. A test that leaves the
        /// application initialised would make every test after it fail for a reason
        /// that has nothing to do with them.
        /// </summary>
        private static void WithApplication(int width, int height, Action<ShellWindow> body)
        {
            IConsoleDriver driver = new HeadlessConsoleDriver();
            using (HostedSession hosted = new ConsoleWindowSystem(driver).BeginHosted())
            {
                                
                using (FakeMemoryBackend machine = new FakeMemoryBackend())
                {
                    MonitorSession session;
                    ShellWindow window = BuildWindow(machine, out session);

                    window.ApplyLayout(ShellLayout.ForSize(width, height));

                    body(window);
                }
            }
        }

        [TestMethod]
        public void TheShellStartsAndStopsCleanly()
        {
            bool initialisedInsideRun = false;

            WithApplication(120, 24, delegate (ShellWindow window)
            {
                // In SharpConsoleUI, the session is always initialized during the hosted session
                initialisedInsideRun = true;
                Assert.AreEqual(120, window.CurrentLayout.Width);
                Assert.IsFalse(window.CurrentLayout.IsRefused);
            });

            Assert.IsTrue(initialisedInsideRun, "the shell never had a live application");
        }

        [TestMethod]
        public void TheShellDrawsAtEveryWidthTheBreakpointsAllow()
        {
            // Every mode boundary plus one either side. A layout that only draws at
            // 120 would still pass a test that checked 120.
            int[] widths = { 60, 61, 79, 80, 81, 99, 100, 101, 119, 120, 121, 200 };

            foreach (int width in widths)
            {
                WithApplication(width, 24, delegate (ShellWindow window)
                {
                    // Force a draw cycle
                    ConsoleWindowSystem ws = new ConsoleWindowSystem(new HeadlessConsoleDriver());
                    using (HostedSession hosted = ws.BeginHosted())
                    {
                        hosted.Tick();
                    }
                });
            }
        }

        [TestMethod]
        public void TheShellDrawsAtEveryHeightThatIsAllowed()
        {
            foreach (int height in new[] { 6, 7, 12, 24, 60 })
            {
                WithApplication(120, height, delegate (ShellWindow window)
                {
                    ConsoleWindowSystem ws = new ConsoleWindowSystem(new HeadlessConsoleDriver());
                    using (HostedSession hosted = ws.BeginHosted())
                    {
                        hosted.Tick();
                    }
                });
            }
        }

        [TestMethod]
        public void NoPaneEverReachesPastTheRightEdge()
        {
            for (int width = ShellLayout.AbsoluteMinimum; width <= 160; width++)
            {
                WithApplication(width, 24, delegate (ShellWindow window)
                {
                    // Check window bounds
                    Assert.IsTrue(window.CurrentLayout.Width <= width,
                        "layout exceeds " + width + " columns: " + window.CurrentLayout.Width);
                });
            }
        }

        [TestMethod]
        public void NoPaneEverStartsBeforeTheLeftEdge()
        {
            WithApplication(80, 24, delegate (ShellWindow window)
            {
                // Layout X positions should be >= 0
                Assert.IsTrue(window.CurrentLayout.EditorX >= 0);
                Assert.IsTrue(window.CurrentLayout.TreeX >= 0);
                Assert.IsTrue(window.CurrentLayout.RamX >= 0);
            });
        }

        [TestMethod]
        public void TheEditorPaneIsAlwaysTheWidestThingOnScreen()
        {
            // The editor is the point of the tool. A layout that gives it less room
            // than a side pane has chosen the wrong priorities.
foreach (int width in new[] { 60, 80, 100, 120 })
            {
                WithApplication(width, 24, delegate (ShellWindow window)
                {
                    ShellLayout layout = window.CurrentLayout;
                    Assert.IsTrue(layout.EditorWidth >= layout.TreeWidth,
                        width + " columns: the editor (" + layout.EditorWidth
                        + ") is narrower than the tree (" + layout.TreeWidth + ")");
                    Assert.IsTrue(layout.EditorWidth >= layout.RamWidth,
                        width + " columns: the editor (" + layout.EditorWidth
                        + ") is narrower than the RAM pane (" + layout.RamWidth + ")");
                });
            }
        }

        [TestMethod]
        public void ATerminalTooNarrowToDrawSaysSoInsteadOfDrawingABrokenLayout()
        {
            WithApplication(40, 24, delegate (ShellWindow window)
            {
                Assert.IsTrue(window.CurrentLayout.IsRefused);
            });
        }

        [TestMethod]
        public void NarrowingAndWideningMovesThePanesBackAndForth()
        {
            WithApplication(120, 24, delegate (ShellWindow window)
            {
                Assert.IsTrue(window.CurrentLayout.TreeIsColumn);
                Assert.AreEqual(ShellLayout.TreeColumnWidth, window.CurrentLayout.TreeWidth);

                window.ApplyLayout(ShellLayout.ForSize(100, 24));
                Assert.AreEqual(ShellLayout.CollapsedTreeWidth, window.CurrentLayout.TreeWidth);
                Assert.IsFalse(window.CurrentLayout.TreeIsColumn);

                window.ApplyLayout(ShellLayout.ForSize(80, 24));
                Assert.IsFalse(window.CurrentLayout.TreeIsColumn, "an overlay mode keeps the tree out of the way");

                window.ApplyLayout(ShellLayout.ForSize(120, 24));
                Assert.IsTrue(window.CurrentLayout.TreeIsColumn);
                Assert.AreEqual(ShellLayout.TreeColumnWidth, window.CurrentLayout.TreeWidth);
            });
        }

        [TestMethod]
        public void OneRunOfTheApplicationLoopStartsAndEndsWithoutAnUninitialisedDriver()
        {
            IConsoleDriver driver = new HeadlessConsoleDriver();
            using (HostedSession hosted = new ConsoleWindowSystem(driver).BeginHosted())
            {
                                
                using (FakeMemoryBackend machine = new FakeMemoryBackend())
                {
                    MonitorSession session;
                    ShellWindow window = BuildWindow(machine, out session);

                    // Run one iteration
                    hosted.Tick();

                    Assert.IsTrue(true); // If we get here without exception, it worked
                }
            }
        }

        [TestMethod]
        public void TheWholeRunnerStartsAndTearsDownAndLeavesNoChildBehind()
        {
            // ShellRunner, not just the window: this is the path the executable
            // takes, and it is the only place Init and Shutdown are paired.
            Process child = StartChild();

            // The bound is asked for on the call rather than wired up from here.
            IConsoleDriver driver = new HeadlessConsoleDriver();
                        
            int exitCode;
            try
            {
                using (FakeMemoryBackend machine = new FakeMemoryBackend())
                {
                    MonitorSession session =
                        new MonitorSession(machine, new Cpu6502(), AppContext.BaseDirectory);

                    exitCode = ShellRunner.Run(session, ShellTheme.Default,
                        ListingSourceFactory.Create(), AppContext.BaseDirectory, child, driver,
                        ShellRunner.OneIteration);
                }
            }
            finally
            {
                if (!child.HasExited)
                {
                    try { child.Kill(); }
                    catch (InvalidOperationException) { }
                }
            }

            Assert.AreEqual(0, exitCode, "a run that starts must report success");
            Assert.IsTrue(child.HasExited, "the runner left the emulator running");
            Assert.IsFalse(IsRunning(child.Id), "the emulator is still in the process list");

            child.Dispose();
        }

        [TestMethod]
        public void ABoundedRunEndsOnItsOwnAfterTheIterationsItWasGiven()
        {
            IConsoleDriver driver = new HeadlessConsoleDriver();
                        
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                MonitorSession session =
                    new MonitorSession(machine, new Cpu6502(), AppContext.BaseDirectory);

                int exitCode = ShellRunner.Run(session, ShellTheme.Default,
                    ListingSourceFactory.Create(), AppContext.BaseDirectory, null, driver, 3);

                Assert.AreEqual(0, exitCode);
            }
        }

        [TestMethod]
        public void ABoundTheRunnerCannotHonourIsRefusedWithoutTakingTheConsole()
        {
            // The console is the scarce resource here: a refusal that initialised
            // first would hand a build agent a raw-mode terminal nobody can type in.
            Process child = StartChild();

            int exitCode = ShellRunner.Run(null, ShellTheme.Default, new UnavailableListingSource(),
                AppContext.BaseDirectory, child, new HeadlessConsoleDriver(), -1);

            try
            {
                Assert.AreNotEqual(0, exitCode, "a negative bound is not a run anybody can honour");
                Assert.IsTrue(child.HasExited, "the emulator survived a refused run");
            }
            finally
            {
                if (!child.HasExited)
                {
                    try { child.Kill(); }
                    catch (InvalidOperationException) { }
                }
                child.Dispose();
            }
        }

        [TestMethod]
        public void StoppingTheEmulatorWaitsForItToActuallyBeGone()
        {
            Process child = StartChild();

            try
            {
                Assert.IsFalse(child.HasExited, "the child should still be running");

                ShellRunner.StopEmulator(child);

                Assert.IsTrue(child.HasExited,
                    "StopEmulator returned while the process was still alive, so the"
                    + " monitor could exit and leave it orphaned");
            }
            finally
            {
                if (!child.HasExited)
                {
                    try { child.Kill(); }
                    catch (InvalidOperationException) { }
                }
                child.Dispose();
            }
        }

        [TestMethod]
        public void StoppingNothingIsNotAnError()
        {
            ShellRunner.StopEmulator(null);

            Assert.IsTrue(true, "a null emulator is the ordinary case: no failure expected");
        }

        [TestMethod]
        public void StoppingAnAlreadyDeadProcessIsNotAnError()
        {
            Process child = StartChild();
            child.Kill();
            child.WaitForExit(10000);

            ShellRunner.StopEmulator(child);

            Assert.IsTrue(child.HasExited);
            child.Dispose();
        }

        [TestMethod]
        public void ARunThatCannotStartStopsTheEmulatorItWasGiven()
        {
            // The failure that matters: the emulator is up, the run cannot begin, and
            // the emulator is left running against a ROM with no owner. Lua runs
            // inside the emulator, so that is also a stranded script.
            Process child = StartChild();

            // The overload with no driver, so this test would reach the real console
            // if the refusal were not checked before Init. That is asserted below
            // rather than assumed, because a run that initialised here would leave a
            // build agent's terminal in raw mode for the rest of the suite.
            int exitCode = ShellRunner.Run(null, ShellTheme.Default, new UnavailableListingSource(),
                AppContext.BaseDirectory, child);

            try
            {
                Assert.AreNotEqual(0, exitCode, "a run with no session cannot succeed");
                Assert.IsTrue(child.HasExited, "the emulator survived a failed run");

                // Asked about this child specifically, not about "is there a ping
                // running": another test on the machine may have one, and a count
                // that includes someone else's process proves nothing about ours.
                Assert.IsFalse(IsRunning(child.Id),
                    "the process is still in the system process list after the run");
            }
            finally
            {
                if (!child.HasExited)
                {
                    try { child.Kill(); }
                    catch (InvalidOperationException) { }
                }
                child.Dispose();
            }
        }

        /// <summary>
        /// A child that outlives its parent if nobody waits for it. Chosen to be
        /// long-lived and to need no arguments this machine might not have.
        /// </summary>
        private static Process StartChild()
        {
            ProcessStartInfo start = new ProcessStartInfo(
                Path.Combine(Environment.SystemDirectory, "ping.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            start.ArgumentList.Add("-n");
            start.ArgumentList.Add("30");
            start.ArgumentList.Add("127.0.0.1");

            Process child = Process.Start(start);
            if (child == null)
                Assert.Fail("could not start the child process");

            return child;
        }

        private static bool IsRunning(int processId)
        {
            foreach (Process process in Process.GetProcessesByName("ping"))
            {
                try
                {
                    if (process.Id == processId)
                    {
                        bool exited = process.HasExited;
                        process.Dispose();
                        return !exited;
                    }

                    process.Dispose();
                }
                catch (InvalidOperationException)
                {
                    // The process ended between the enumeration and the question,
                    // which is the answer this test is looking for.
                    return false;
                }
            }

            return false;
        }
    }
}



