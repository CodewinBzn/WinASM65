using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Terminal.Gui;
using WinASM65.Cpu;
using WinASM65.Monitor.Shell;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// The shell as a running thing: it starts, it draws at every width the
    /// breakpoints allow, it stops, and it leaves nothing behind.
    ///
    /// Driven through Terminal.Gui's own <see cref="FakeDriver"/> rather than by
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
                AppContext.BaseDirectory);
        }

        /// <summary>
        /// Runs <paramref name="body"/> with a live application, and guarantees the
        /// application is shut down whatever happens. A test that leaves the
        /// application initialised would make every test after it fail for a reason
        /// that has nothing to do with them.
        /// </summary>
        private static void WithApplication(int width, int height, Action<ShellWindow, FakeDriver> body)
        {
            FakeDriver driver = new FakeDriver();
            Application.Init(driver);
            try
            {
                driver.SetWindowSize(width, height);
                driver.Refresh();

                using (FakeMemoryBackend machine = new FakeMemoryBackend())
                {
                    MonitorSession session;
                    ShellWindow window = BuildWindow(machine, out session);

                    window.Frame = new System.Drawing.Rectangle(0, 0, width, height);
                    window.ApplyLayout(ShellLayout.ForSize(width, height));

                    body(window, driver);
                }
            }
            finally
            {
                Application.Shutdown();
            }
        }

        [TestMethod]
        public void TheShellStartsAndStopsCleanly()
        {
            bool initialisedInsideRun = false;

            WithApplication(120, 24, delegate (ShellWindow window, FakeDriver driver)
            {
                initialisedInsideRun = Application.Initialized;
                Assert.AreEqual(120, window.CurrentLayout.Width);
                Assert.IsFalse(window.CurrentLayout.IsRefused);
            });

            Assert.IsTrue(initialisedInsideRun, "the shell never had a live application");
            Assert.IsFalse(Application.Initialized, "the shell left the application initialised");
        }

        [TestMethod]
        public void TheShellDrawsAtEveryWidthTheBreakpointsAllow()
        {
            // Every mode boundary plus one either side. A layout that only draws at
            // 120 would still pass a test that checked 120.
            int[] widths = { 60, 61, 79, 80, 81, 99, 100, 101, 119, 120, 121, 200 };

            foreach (int width in widths)
            {
                WithApplication(width, 24, delegate (ShellWindow window, FakeDriver driver)
                {
                    Application.LayoutAndDraw(true);
                });
            }
        }

        [TestMethod]
        public void TheShellDrawsAtEveryHeightThatIsAllowed()
        {
            foreach (int height in new[] { 6, 7, 12, 24, 60 })
            {
                WithApplication(120, height, delegate (ShellWindow window, FakeDriver driver)
                {
                    Application.LayoutAndDraw(true);
                });
            }
        }

        [TestMethod]
        public void NoPaneEverReachesPastTheRightEdge()
        {
            for (int width = ShellLayout.AbsoluteMinimum; width <= 160; width++)
            {
                WithApplication(width, 24, delegate (ShellWindow window, FakeDriver driver)
                {
                    foreach (View pane in new View[] { window, Tree(window), Listing(window), Ram(window), Status(window) })
                    {
                        if (pane == null || !pane.Visible || pane.Frame.Width == 0)
                            continue;

                        Assert.IsTrue(pane.Frame.Right <= width,
                            "a pane reaches past " + width + " columns: " + pane.Frame);
                    }
                });
            }
        }

        [TestMethod]
        public void NoPaneEverStartsBeforeTheLeftEdge()
        {
            WithApplication(80, 24, delegate (ShellWindow window, FakeDriver driver)
            {
                foreach (View pane in new View[] { window, Tree(window), Listing(window), Ram(window), Status(window) })
                {
                    if (pane == null || !pane.Visible || pane.Frame.Width == 0)
                        continue;

                    Assert.IsTrue(pane.Frame.X >= 0, "a pane starts before the left edge: " + pane.Frame);
                }
            });
        }

        [TestMethod]
        public void TheEditorPaneIsAlwaysTheWidestThingOnScreen()
        {
            // The editor is the point of the tool. A layout that gives it less room
            // than a side pane has chosen the wrong priorities.
            foreach (int width in new[] { 60, 80, 100, 120 })
            {
                WithApplication(width, 24, delegate (ShellWindow window, FakeDriver driver)
                {
                    View listing = Listing(window);
                    View tree = Tree(window);
                    View ram = Ram(window);

                    foreach (View side in new View[] { tree, ram })
                    {
                        if (side == null || !side.Visible)
                            continue;

                        Assert.IsTrue(listing.Frame.Width >= side.Frame.Width,
                            width + " columns: the editor (" + listing.Frame.Width
                            + ") is narrower than a side pane (" + side.Frame.Width + ")");
                    }
                });
            }
        }

        [TestMethod]
        public void ATerminalTooNarrowToDrawSaysSoInsteadOfDrawingABrokenLayout()
        {
            WithApplication(40, 24, delegate (ShellWindow window, FakeDriver driver)
            {
                Assert.IsTrue(window.CurrentLayout.IsRefused);

                Application.LayoutAndDraw(true);

                Assert.IsFalse(Listing(window).Visible, "the panes must be hidden, not shrunk to nothing");
                Assert.IsFalse(Ram(window).Visible);
            });
        }

        [TestMethod]
        public void NarrowingAndWideningMovesThePanesBackAndForth()
        {
            WithApplication(120, 24, delegate (ShellWindow window, FakeDriver driver)
            {
                Assert.IsTrue(Tree(window).Visible);
                Assert.AreEqual(ShellLayout.TreeColumnWidth, Tree(window).Frame.Width);

                window.ApplyLayout(ShellLayout.ForSize(100, 24));
                Assert.AreEqual(ShellLayout.CollapsedTreeWidth, Tree(window).Frame.Width);
                Assert.IsFalse(window.CurrentLayout.TreeIsColumn);

                window.ApplyLayout(ShellLayout.ForSize(80, 24));
                Assert.IsFalse(Tree(window).Visible, "an overlay mode keeps the tree out of the way");

                window.ApplyLayout(ShellLayout.ForSize(120, 24));
                Assert.IsTrue(Tree(window).Visible);
                Assert.AreEqual(ShellLayout.TreeColumnWidth, Tree(window).Frame.Width);
            });
        }

        [TestMethod]
        public void OneRunOfTheApplicationLoopStartsAndEndsWithoutAnUninitialisedDriver()
        {
            FakeDriver driver = new FakeDriver();
            Application.Init(driver);
            try
            {
                driver.SetWindowSize(120, 24);
                driver.Refresh();

                using (FakeMemoryBackend machine = new FakeMemoryBackend())
                {
                    MonitorSession session;
                    ShellWindow window = BuildWindow(machine, out session);

                    // EndAfterFirstIteration is the library's own hook for exactly
                    // this: run the loop, do one pass, come back. It is the same
                    // path Ctrl+Q takes on a real terminal, minus the keystroke.
                    Application.EndAfterFirstIteration = true;
                    Application.Run(window);

                    Assert.IsTrue(Application.Initialized);
                }
            }
            finally
            {
                Application.EndAfterFirstIteration = false;
                Application.Shutdown();
            }

            Assert.IsFalse(Application.Initialized);
        }

        [TestMethod]
        public void TheWholeRunnerStartsAndTearsDownAndLeavesNoChildBehind()
        {
            // ShellRunner, not just the window: this is the path the executable
            // takes, and it is the only place Init and Shutdown are paired.
            Process child = StartChild();

            // The bound is asked for on the call rather than wired up from here.
            // Application.Init empties Terminal.Gui's static event table, so an
            // Application.Iteration handler added before the call is never raised and
            // the loop waits for a keystroke nobody is going to type — which is a
            // hang, not a failure. The runner owns the bound for exactly that reason.
            FakeDriver driver = new FakeDriver();
            driver.SetWindowSize(120, 24);
            driver.Refresh();

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
            Assert.IsFalse(Application.Initialized, "the runner left the application initialised");

            Assert.IsTrue(child.HasExited, "the runner left the emulator running");
            Assert.IsFalse(IsRunning(child.Id), "the emulator is still in the process list");

            child.Dispose();
        }

        [TestMethod]
        public void ABoundedRunEndsOnItsOwnAfterTheIterationsItWasGiven()
        {
            // More than one iteration, so the bound is the counting hook rather than
            // EndAfterFirstIteration. Nothing below can end this run but the bound:
            // there is no keystroke, and the whole point of the test is that the
            // suite finishes.
            CountingFakeDriver driver = new CountingFakeDriver();
            driver.SetWindowSize(120, 24);
            driver.Refresh();
            int screensBefore = driver.Screens;

            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                MonitorSession session =
                    new MonitorSession(machine, new Cpu6502(), AppContext.BaseDirectory);

                int exitCode = ShellRunner.Run(session, ShellTheme.Default,
                    ListingSourceFactory.Create(), AppContext.BaseDirectory, null, driver, 3);

                Assert.AreEqual(0, exitCode);
            }

            // The loop has to have drawn, not merely returned: a runner that
            // initialised, skipped the window and tore down again would satisfy every
            // other assertion here.
            Assert.IsTrue(driver.Screens > screensBefore,
                "three iterations produced nothing on the screen");
            Assert.IsFalse(Application.Initialized, "a bounded run left the application initialised");
            Assert.IsFalse(Application.EndAfterFirstIteration,
                "the run's stop hook was left armed for whoever runs next");
        }

        [TestMethod]
        public void ABoundTheRunnerCannotHonourIsRefusedWithoutTakingTheConsole()
        {
            // The console is the scarce resource here: a refusal that initialised
            // first would hand a build agent a raw-mode terminal nobody can type in.
            Process child = StartChild();

            int exitCode = ShellRunner.Run(null, ShellTheme.Default, new UnavailableListingSource(),
                AppContext.BaseDirectory, child, new FakeDriver(), -1);

            try
            {
                Assert.AreNotEqual(0, exitCode, "a negative bound is not a run anybody can honour");
                Assert.IsFalse(Application.Initialized, "the runner initialised before refusing");
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
                Assert.IsFalse(Application.Initialized,
                    "the runner reached the console on its way to refusing");
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

        private static View Tree(ShellWindow window)
        {
            return Find(window, "files");
        }

        private static View Ram(ShellWindow window)
        {
            return Find(window, "RAM");
        }

        private static View Listing(ShellWindow window)
        {
            return Find(window, "listing");
        }

        private static View Status(ShellWindow window)
        {
            return Find(window, "status");
        }

        private static View Find(View parent, string title)
        {
            foreach (View child in parent.Subviews)
            {
                if (child.Title == title)
                    return child;
            }
            return null;
        }

        /// <summary>
        /// A driver that counts the screen updates the shell asked for. A bound on
        /// the run loop is a claim that the loop ran, and an exit code alone cannot
        /// support it: a runner that initialised, skipped the window and tore down
        /// again would satisfy every other assertion in this class.
        ///
        /// Terminal.Gui 2.0.0's FakeDriver discards what it is handed, so
        /// <c>UpdateScreen</c> — where a real driver would put a finished frame on
        /// the terminal — is the only honest place to count. <c>WriteRaw</c> stays at
        /// zero even for a full layout and draw, so counting it would prove nothing.
        /// </summary>
        private sealed class CountingFakeDriver : FakeDriver
        {
            public int Screens { get; private set; }

            public override bool UpdateScreen()
            {
                Screens++;
                return base.UpdateScreen();
            }
        }
    }
}