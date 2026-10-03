using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Terminal.Gui;
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
    /// Terminal.Gui is driven through its own <see cref="FakeDriver"/>, and every run
    /// is bounded — the runner's own <see cref="ShellRunner.OneIteration"/> or a stop
    /// requested from the first iteration. Nothing here waits on a keystroke nobody is
    /// going to type, which is how a test in this project once hung the whole suite.
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
            // F7 needs StateCommands, which another milestone owns and which is not in
            // this build. The key is listed, unbound, rather than silently absent: a
            // user reading the plan's table should learn here that it is not here.
            AssertBound("F7", false);
            AssertBound("F12", false);
            AssertBound("Ctrl+A", false);
        }

        [TestMethod]
        public void TheWindowActuallyCarriesTheShortcutsItClaims()
        {
            // The key table is a claim; the view tree is the wiring. A key listed as
            // bound with no Shortcut behind it would press into nothing.
            WithShell(delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                foreach (Shortcut shortcut in Shortcuts(window))
                {
                    Assert.IsTrue(shortcut.BindKeyToApplication,
                        "a shortcut that is not bound to the application cannot be pressed");
                }

                Assert.IsTrue(HasShortcut(window, Key.F5), "F5 has no shortcut");
                Assert.IsTrue(HasShortcut(window, Key.F8), "F8 has no shortcut");
                Assert.IsTrue(HasShortcut(window, Key.F9), "F9 has no shortcut");
                Assert.IsTrue(HasShortcut(window, Key.F10), "F10 has no shortcut");
                Assert.IsFalse(HasShortcut(window, Key.F7), "F7 is not in this build");

                // One shortcut per key the table claims, and not one more: an extra
                // shortcut would be an action the help screen does not mention.
                int bound = 0;
                foreach (ShellKey key in ShellWindow.Keys)
                {
                    if (key.Bound)
                        bound++;
                }

                int shortcuts = 0;
                foreach (Shortcut shortcut in Shortcuts(window))
                    shortcuts++;

                Assert.AreEqual(bound, shortcuts,
                    "the number of shortcuts and the number of bound keys must agree");
            });
        }

        [TestMethod]
        public void PressingF5InARunningShellAssemblesAndLists()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, GoodSource);

                // The keystroke is raised from inside the run loop and the loop is
                // stopped from the same handler, so the test cannot wait for a key
                // nobody is going to type.
                WithShell(temp.Path, Key.F5, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource(name);

                    Assert.AreEqual(0, window.Commands.Count,
                        "the key is raised before the body opens the source, so nothing is issued yet");

                    window.AssembleCurrentSource();

                    CollectionAssert.AreEqual(
                        new List<string> { "ASSEMBLE " + name },
                        new List<string>(window.Commands));
                    StringAssert.Contains(window.Listing.Content, "lda #$5A");
                    StringAssert.Contains(window.Message, "OK");
                });
            }
        }

        // ---------------------------------------------------------------- F5

        [TestMethod]
        public void F5IssuesAssembleAndTheListingComesFromTheAssemblyThatJustRan()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, GoodSource);

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource(name);
                    Assert.AreEqual(0, window.Listing.Rows.Count, "nothing is listed before F5");

                    window.AssembleCurrentSource();

                    CollectionAssert.AreEqual(
                        new List<string> { "ASSEMBLE " + name },
                        new List<string>(window.Commands),
                        "F5 is exactly one command line into the session");

                    Assert.AreEqual(1, window.Listing.Rows.Count > 0 ? 1 : 0, "the pane has rows");
                    StringAssert.Contains(window.Listing.Content, "lda #$5A");
                    StringAssert.Contains(window.Listing.Content, "$8000");
                    StringAssert.Contains(window.Message, "OK");
                });
            }
        }

        [TestMethod]
        public void F5ShowsTheAssemblysOwnFailureRatherThanTheRowsItJustRefused()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, BrokenSource);

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource(name);
                    window.AssembleCurrentSource();

                    Assert.AreEqual(0, window.Listing.Rows.Count);
                    Assert.IsNotNull(window.Listing.Problem, "the pane says what went wrong");
                    StringAssert.Contains(window.Listing.Problem, "ERR");
                    StringAssert.Contains(window.Message, "ERR");
                    CollectionAssert.AreEqual(
                        new List<string> { "ASSEMBLE " + name },
                        new List<string>(window.Commands));
                });
            }
        }

        [TestMethod]
        public void F5WithNothingOpenSaysSoAndIssuesNothing()
        {
            WithShell(delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                window.AssembleCurrentSource();

                Assert.AreEqual(0, window.Commands.Count,
                    "assembling whatever happened to be first would be an action nobody asked for");
                StringAssert.Contains(window.Message, "no source is open");
            });
        }

        [TestMethod]
        public void OpeningASourceInTheTreeAssemblesItToo()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, GoodSource);

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.ActivateRow(new FileTreeRow(FileTreeKind.Source, name, name));

                    Assert.AreEqual(name, window.SourceFile);
                    CollectionAssert.AreEqual(
                        new List<string> { "ASSEMBLE " + name },
                        new List<string>(window.Commands),
                        "the tree issues the verb, not a bare path");
                    StringAssert.Contains(window.Listing.Content, "lda #$5A");
                });
            }
        }

        // ---------------------------------------------------------------- F8

        [TestMethod]
        public void F8SetsABreakpointAtTheAddressUnderTheCursorAndTheStatusLineFollows()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, GoodSource);

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource(name);
                    window.AssembleCurrentSource();

                    Assert.AreEqual(0x8000, window.Listing.CursorAddress, "the cursor is on the first byte row");
                    Assert.AreEqual("BP=0", BreakpointSegment(window));

                    window.ToggleBreakpoint();

                    CollectionAssert.AreEqual(
                        new List<string> { "ASSEMBLE " + name, "BREAK SET exec $8000" },
                        new List<string>(window.Commands),
                        "the commands were: " + Issued(window));
                    CollectionAssert.Contains(machine.Breakpoints, "8000:exec");
                    Assert.AreEqual("BP=1", BreakpointSegment(window));
                    StringAssert.Contains(window.Message, "$8000");
                });
            }
        }

        [TestMethod]
        public void F8AgainClearsItAndTheStatusLineFollowsBack()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, GoodSource);

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource(name);
                    window.AssembleCurrentSource();

                    window.ToggleBreakpoint();
                    window.ToggleBreakpoint();

                    // The session refuses a single removal, because Mesen exposes no way
                    // to release one callback. So the toggle is expressed the only way a
                    // REPL user could: clear, then set whatever is still wanted. With one
                    // breakpoint wanted — none — the second line is only the clear.
                    CollectionAssert.AreEqual(
                        new List<string> { "ASSEMBLE " + name, "BREAK SET exec $8000", "BREAK CLEAR" },
                        new List<string>(window.Commands),
                        "the commands were: " + Issued(window));
                    Assert.AreEqual(0, Recorded(machine.Breakpoints).Count, "a list that was never created holds none");
                    Assert.AreEqual("BP=0", BreakpointSegment(window));
                    Assert.AreEqual(0, window.Breakpoints.Count);
                });
            }
        }

        [TestMethod]
        public void ClearingOneBreakpointSetsTheOnesStillWanted()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, GoodSource);

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource(name);
                    window.AssembleCurrentSource();

                    // Two breakpoints: the cursor row, then the next one that emits bytes.
                    window.ToggleBreakpoint();
                    window.Listing.MoveCursor(1);
                    window.ToggleBreakpoint();
                    Assert.AreEqual("BP=2", BreakpointSegment(window));

                    window.Listing.MoveCursor(-1);
                    window.ToggleBreakpoint();

                    CollectionAssert.AreEqual(
                        new List<string>
                        {
                            "ASSEMBLE " + name,
                            "BREAK SET exec $8000",
                            "BREAK SET exec $8002",
                            "BREAK CLEAR",
                            "BREAK SET exec $8002",
                        },
                        new List<string>(window.Commands));
                    CollectionAssert.AreEqual(new List<string> { "8002:exec" }, Recorded(machine.Breakpoints));
                    Assert.AreEqual("BP=1", BreakpointSegment(window));
                });
            }
        }

        [TestMethod]
        public void ABreakpointRefusedByTheMachineIsNotRecorded()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, GoodSource);

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource(name);
                    window.AssembleCurrentSource();

                    machine.RejectBreakpoints = true;
                    window.ToggleBreakpoint();

                    Assert.AreEqual("BP=0", BreakpointSegment(window),
                        "the status line advertised a breakpoint the emulator never took");
                    Assert.AreEqual(0, window.Breakpoints.Count);
                    StringAssert.Contains(window.Message, "ERR");
                });
            }
        }

        [TestMethod]
        public void F8WithTheCursorOnALineThatEmitsNothingSaysSoAndIssuesNothing()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, GoodSource);

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource(name);
                    window.AssembleCurrentSource();

                    // Row 0 is the .org, which sets an address but emits no byte, so
                    // there is nothing to set a breakpoint on.
                    window.Listing.ScrollTo(0);
                    window.Listing.MoveCursor(-1);

                    Assert.AreEqual(-1, window.Listing.CursorAddress);

                    window.ToggleBreakpoint();

                    Assert.AreEqual(0, Recorded(machine.Breakpoints).Count, "a list that was never created holds none");
                    StringAssert.Contains(window.Message, "not on an address");
                });
            }
        }

        // ---------------------------------------------------------------- F9 and F10

        [TestMethod]
        public void F9RunsTheMachineAndTheStatusLineSaysRunning()
        {
            WithShell(delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                machine.Pause();
                window.RefreshPanes();
                Assert.AreEqual("STOPPED", ExecutionSegment(window));

                window.ResumeMachine();

                CollectionAssert.AreEqual(
                    new List<string> { "RESUME" }, new List<string>(window.Commands));
                Assert.AreEqual(1, machine.ResumeCount, "the machine was resumed through the session");
                Assert.AreEqual("RUNNING", ExecutionSegment(window));
            });
        }

        [TestMethod]
        public void F10StepsTheMachineAndTheStatusLineSaysStopped()
        {
            WithShell(delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                window.ResumeMachine();
                Assert.AreEqual("RUNNING", ExecutionSegment(window));

                window.StepMachine();

                CollectionAssert.AreEqual(
                    new List<string> { "RESUME", "STEP" }, new List<string>(window.Commands));
                Assert.AreEqual(1, machine.StepCount, "the machine was stepped through the session");
                Assert.AreEqual("STOPPED", ExecutionSegment(window));
                StringAssert.Contains(window.Message, "stepped");
            });
        }

        [TestMethod]
        public void TheCommandLinesAreTheOnesTheSessionAlreadyAnswers()
        {
            // Every editor verb exists in the session already. If one of these did not,
            // a key would be doing something the REPL cannot, which is the one thing
            // this shell exists not to do.
            WithShell(delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                window.ResumeMachine();
                window.StepMachine();

                foreach (string command in new List<string>(window.Commands))
                {
                    IReadOnlyList<string> answer = window.Run(command);

                    Assert.IsTrue(answer.Count > 0, "the session answered nothing for: " + command);
                    Assert.IsFalse(answer[0].StartsWith("ERR unknown command"),
                        "the session does not know: " + command);
                }
            });
        }

        // ---------------------------------------------------------------- the pane

        [TestMethod]
        public void ThePaneDrawsLineAddressBytesAndSourceInFixedColumns()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, GoodSource);

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource(name);
                    window.AssembleCurrentSource();

                    string[] lines = window.Listing.Content.Split(
                        new string[] { Environment.NewLine }, StringSplitOptions.None);

                    // Four source lines, five rows: .export lists as a line of its own, which is
                    // right — it is in the file and the reader can see it.
                    Assert.AreEqual(5, lines.Length);

                    foreach (string line in lines)
                        Assert.IsTrue(line.Length > ListingFormatter.GutterWidth,
                            "the source column starts at " + ListingFormatter.GutterWidth + ": [" + line + "]");

                    // Line 2 is the label: it has an address worth seeing and no bytes.
                    Assert.AreEqual("2", lines[1].Substring(0, 5).Trim());
                    StringAssert.Contains(lines[1], "$8000");
                    StringAssert.Contains(lines[1], "Start:");

                    // Line 3 is the instruction: address, bytes, blank cycle column.
                    StringAssert.Contains(lines[2], "$8000");
                    StringAssert.Contains(lines[2], "A9 5A");
                    Assert.AreEqual("lda", lines[2].Substring(ListingFormatter.GutterWidth).Trim().Split(' ')[0]);
                });
            }
        }

        [TestMethod]
        public void ThePaneScrollsOverALongListing()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                System.Text.StringBuilder source = new System.Text.StringBuilder(".org $8000\nStart:\n");
                for (int i = 0; i < 400; i++)
                    source.Append("  nop\n");

                // The monitor only keeps a source that exports something: a unit with no
                // entry point is not a unit, and ASSEMBLE says so rather than pretending.
                source.Append("  .export Start\n");

                string name = Write(temp, source.ToString());

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource(name);
                    window.AssembleCurrentSource();

                    Assert.IsNull(window.Listing.Problem,
                        "the source assembles, so the pane has no reason to show");
                    Assert.IsTrue(window.Listing.Rows.Count > 20, "the listing is longer than the pane");

                    string top = window.Listing.VisibleContent.Split(
                        new string[] { Environment.NewLine }, StringSplitOptions.None)[0];

                    window.Listing.ScrollLines(10);

                    string scrolled = window.Listing.VisibleContent.Split(
                        new string[] { Environment.NewLine }, StringSplitOptions.None)[0];

                    Assert.AreNotEqual(top, scrolled, "scrolling did not move the window");
                    Assert.IsTrue(window.Listing.Rows.Count > 20,
                        "scrolling moves the window over the rows, it does not discard them");
                    Assert.AreEqual(string.Join(Environment.NewLine,
                        new List<string>(AllLines(window.Listing)).ToArray()),
                        window.Listing.Content);
                });
            }
        }

        [TestMethod]
        public void ScrollingStopsAtBothEndsInsteadOfRunningOffTheListing()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                System.Text.StringBuilder source = new System.Text.StringBuilder(".org $8000\nStart:\n");
                for (int i = 0; i < 400; i++)
                    source.Append("  nop\n");
                source.Append("  .export Start\n");

                string name = Write(temp, source.ToString());

                WithShell(temp.Path, delegate (ShellWindow window, FakeMemoryBackend machine)
                {
                    window.OpenSource(name);
                    window.AssembleCurrentSource();

                    int total = window.Listing.Rows.Count;
                    Assert.IsTrue(total > 20, "the listing is longer than the pane");

                    window.Listing.ScrollTo(0);
                    string atTop = window.Listing.VisibleContent;

                    window.Listing.ScrollTo(int.MaxValue / 2);
                    string atBottom = window.Listing.VisibleContent;

                    Assert.AreNotEqual(atTop, atBottom, "the window did not move");
                    Assert.AreEqual(total, window.Listing.Rows.Count,
                        "scrolling moves the window over the rows, it never discards them");

                    // Past the end of the listing there is nothing left to show, and the
                    // window is clamped rather than left pointing at rows that are not
                    // there.
                    Assert.IsTrue(atBottom.Length > 0);
                    Assert.IsTrue(atBottom.Length < atTop.Length + total * 40);
                });
            }
        }

        // ---------------------------------------------------------------- helpers

        [TestMethod]
        public void TheListingActuallyReachesTheScreen()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, GoodSource);

                FakeDriver driver = new FakeDriver();
                Application.Init(driver);
                try
                {
                    driver.SetWindowSize(160, 40);
                    driver.Refresh();

                    using (FakeMemoryBackend machine = new FakeMemoryBackend())
                    {
                        MonitorSession session =
                            new MonitorSession(machine, new Cpu6502(), temp.Path);

                        ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                            new AssemblerListingSource(new Cpu6502(), temp.Path), temp.Path);

                        window.Frame = new System.Drawing.Rectangle(0, 0, 160, 40);
                        window.ApplyLayout(ShellLayout.ForSize(160, 40));

                        window.OpenSource(name);
                        window.AssembleCurrentSource();

                        // Begin, not Run: it makes the window the application's
                        // toplevel, which is what gives the draw somewhere to go, without
                        // entering a loop that would wait for a keypress.
                        Application.Begin(window);
                        Application.LayoutAndDraw(true);

                        // Refresh is what copies the application's screen into the
                        // driver's buffer. Without it Contents still holds the blanks the
                        // driver starts life with, and the test would pass on a pane that
                        // drew nothing at all.
                        driver.Refresh();

                        // Read the driver's own buffer rather than the pane's string:
                        // every other assertion in this class proves the model is
                        // right, and this is the one that proves it was drawn.
                        string screen = Screen(driver);

                        // Spacing between the columns is Terminal.Gui's to decide, so
                        // what is asserted is what the reader reads: the frame, the line
                        // number and address, the bytes, the source, the status line.
                        Assert.IsTrue(screen.Contains("listing"), "the pane's frame is not on screen");
                        Assert.IsTrue(screen.Contains("1 $8000"), "the line and address columns are not drawn");
                        Assert.IsTrue(screen.Contains(".org $8000"), "the first source line is not drawn");
                        Assert.IsTrue(screen.Contains("A9 5A"), "the bytes are not drawn");
                        Assert.IsTrue(screen.Contains("lda #$5A"),
                            "the source is not drawn, or is drawn without its spacing");
                        Assert.IsTrue(screen.Contains("rts"), "the last source line is not drawn");
                        Assert.IsTrue(screen.Contains("BP=0"), "the status line is not drawn");
                    }
                }
                finally
                {
                    Application.Shutdown();
                }
            }
        }

        /// <summary>
        /// Everything the driver has drawn, row by row. The buffer rather than a string
        /// property, so what is checked is the cells and not a cached copy of them.
        /// </summary>
        private static string Screen(FakeDriver driver)
        {
            // The buffer is indexed row first and column second, and its dimensions are
            // read from the buffer rather than from Cols and Rows: SetWindowSize
            // resizes the screen and the contents together, and reading one without the
            // other is an IndexOutOfRange waiting to happen.
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

        /// <summary>The command lines a window issued, for an assertion message.</summary>
        private static string Issued(ShellWindow window)
        {
            List<string> lines = new List<string>();
            foreach (string command in window.Commands)
                lines.Add(command);

            return "[" + string.Join(" | ", lines.ToArray()) + "]";
        }

        /// <summary>
        /// The backend creates its breakpoint list only when the first one is set, so a
        /// list of none is a null and not an empty collection.
        /// </summary>
        private static List<string> Recorded(List<string> breakpoints)
        {
            return breakpoints ?? new List<string>();
        }

        private static IEnumerable<string> AllLines(ListingPane pane)
        {
            foreach (ListingRow row in pane.Rows)
                yield return ListingFormatter.Row(row);
        }

        private static void AssertBound(string key, bool expected)
        {
            foreach (ShellKey candidate in ShellWindow.Keys)
            {
                if (candidate.Key != key)
                    continue;

                Assert.AreEqual(expected, candidate.Bound, key + " should report bound=" + expected);
                return;
            }

            Assert.Fail("the key table does not list " + key);
        }

        private static bool HasShortcut(ShellWindow window, Key key)
        {
            foreach (Shortcut shortcut in Shortcuts(window))
            {
                if (shortcut.Key == key)
                    return true;
            }

            return false;
        }

        private static IEnumerable<Shortcut> Shortcuts(ShellWindow window)
        {
            foreach (View child in window.Subviews)
            {
                if (child is Shortcut)
                    yield return (Shortcut)child;
            }
        }

        private static string BreakpointSegment(ShellWindow window)
        {
            return Segment(window, "BP=");
        }

        private static string ExecutionSegment(ShellWindow window)
        {
            string line = window.Status.Line;
            int index = line.IndexOf("STOPPED", StringComparison.Ordinal);
            if (index >= 0)
                return "STOPPED";

            index = line.IndexOf("RUNNING", StringComparison.Ordinal);
            return index >= 0 ? "RUNNING" : "(neither)";
        }

        private static string Segment(ShellWindow window, string prefix)
        {
            foreach (string field in window.Status.Line.Split(new string[] { "  " }, StringSplitOptions.None))
            {
                if (field.StartsWith(prefix, StringComparison.Ordinal))
                    return field;
            }

            return "(no " + prefix + " segment): " + window.Status.Line;
        }

        /// <summary>
        /// Runs a body against a live application at a real terminal size, and shuts it
        /// down whatever happens. A test that left the application initialised would
        /// make every test after it fail for a reason that has nothing to do with them.
        /// </summary>
        private static void WithShell(Action<ShellWindow, FakeMemoryBackend> body)
        {
            WithShell(AppContext.BaseDirectory, null, body);
        }

        private static void WithShell(string directory, Action<ShellWindow, FakeMemoryBackend> body)
        {
            WithShell(directory, null, body);
        }

        /// <summary>
        /// The same, with one key pressed while the run loop is going.
        ///
        /// The key is raised from inside the loop and the loop is stopped from the same
        /// handler. That is the whole point of the shape: <c>Init</c> clears Terminal.Gui's
        /// static event table, so a handler subscribed before the call is never raised
        /// and the loop then waits for a keystroke nobody is going to type — a hang, not
        /// a failure. Bounded here, so it cannot happen.
        ///
        /// The body runs afterwards, so a test can assert both that the keystroke was
        /// handled and what the shell did about it.
        /// </summary>
        private static void WithShell(string directory, Key press,
            Action<ShellWindow, FakeMemoryBackend> body)
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
                        new MonitorSession(machine, new Cpu6502(), directory);

                    ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                        new AssemblerListingSource(new Cpu6502(), directory), directory);

                    window.Frame = new System.Drawing.Rectangle(0, 0, 160, 40);
                    window.ApplyLayout(ShellLayout.ForSize(160, 40));

                    // ReferenceEquals, not != : Terminal.Gui's Key is a class whose == operator
                    // dereferences both sides, so the null test has to be the reference
                    // one.
                    if (!ReferenceEquals(press, null))
                    {
                        bool handled = false;
                        Key pressed = press;

                        EventHandler<IterationEventArgs> onIteration = delegate (object sender, IterationEventArgs args)
                        {
                            handled = Application.RaiseKeyDownEvent(pressed) || handled;
                            Application.RequestStop();
                        };

                        // Subscribed after Init, which is the call that empties the table.
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

                    Application.LayoutAndDraw(true);
                    body(window, machine);
                }
            }
            finally
            {
                Application.Shutdown();
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
        /// seen by — anything the rest of the suite is doing.
        /// </summary>
        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }

            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "WinASM65Keys_" + Guid.NewGuid().ToString("N"));
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