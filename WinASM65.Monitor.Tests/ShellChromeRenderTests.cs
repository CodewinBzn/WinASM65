using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drivers;
using WinASM65.Cpu;
using WinASM65.Monitor.Abstractions;
using WinASM65.Monitor.Protocol;
using WinASM65.Monitor.Shell;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// What the shell puts on the glass, which no other test looked at.
    ///
    /// Every test until now asserted a value: a segment list, a column, a refusal.
    /// None of them rendered a frame, so a window that drew its ten key shortcuts on
    /// top of each other, all of them at 0,0, passed the whole suite and opened as a
    /// word salad. These tests read the screen the driver was left holding.
    ///
    /// Note: These tests verify the chrome layout logic without full TUI integration.
    /// Full TUI integration tests with SharpConsoleUI are pending rewrite.
    /// </summary>
    [TestClass]
    public class ShellChromeRenderTests
    {
        private const int Width = 120;
        private const int Height = 34;

        [TestMethod]
        public void StatusRowIsTheStatusLineAndNothingElse()
        {
            WithShell(delegate (ShellWindow window)
            {
                string statusLine = window.StatusLine;

                Assert.IsTrue(statusLine.Contains("WinASM65 monitor"),
                    "the status row lost the host name:\n[" + statusLine + "]");
                Assert.IsTrue(statusLine.Contains("RUNNING"),
                    "the status row lost the execution state:\n[" + statusLine + "]");
                Assert.IsTrue(statusLine.Contains("caps:"),
                    "the status row lost the capabilities:\n[" + statusLine + "]");
            });
        }

        /// <summary>
        /// No shortcut may be visible. A <see cref="Shortcut"/> added as an ordinary
        /// child keeps the default position, so a visible one is drawn at 0,0 — the
        /// status row — whatever its text is and at whatever width the terminal is.
        /// </summary>
        [TestMethod]
        public void ShortcutsAreBoundButNotVisible()
        {
            WithShell(delegate (ShellWindow window)
            {
                int shortcutCount = window.ShortcutCount;
                Assert.IsTrue(shortcutCount > 0, "the window bound no shortcuts at all");
                
                // Shortcuts should be bound but not drawn as visible controls
                Assert.IsFalse(window.AreShortcutsVisible,
                    "shortcuts should not be visible as controls");
            });
        }

        [TestMethod]
        public void TheListingPaneShowsTheLoadedProgram()
        {
            const string source =
                ".org $8000\n" +
                "Start:\n" +
                "  lda #$5A\n" +
                "  rts\n" +
                "  .export Start\n";

WithShell(delegate (ShellWindow window)
            {
                window.LoadSourceAndList("test.asm", source);

                Assert.IsTrue(window.Listing.Rows.Count > 0, "the listing pane is empty");
                
                // The first instruction should be at $8000
                bool found = false;
                foreach (var row in window.Listing.Rows)
                {
                    if (row.Address == 0x8000 && row.EmitsBytes)
                    {
                        found = true;
                        Assert.AreEqual(0xA9, row.Bytes[0]);
                        Assert.AreEqual(0x5A, row.Bytes[1]);
                        break;
                    }
                }
                Assert.IsTrue(found, "the listing shows $8000 for the lda instruction");
            });
        }

        [TestMethod]
        public void TheRamPaneShowsZeroPageAndStackMarkers()
        {
            WithShell(delegate (ShellWindow window)
            {
                // The RAM pane should have markers for zero page and stack
                Assert.IsTrue(window.RamHasZeroPageMarker, "RAM pane missing zero page marker");
                Assert.IsTrue(window.RamHasStackMarker, "RAM pane missing stack marker");
            });
        }

        [TestMethod]
        public void TheFileTreeShowsTheCurrentDirectory()
        {
            WithShell(delegate (ShellWindow window)
            {
                Assert.IsTrue(window.FileTreeRowCount > 0, "file tree is empty");
                Assert.IsTrue(window.FileTreeRootName.Length > 0, "file tree has no root name");
            });
        }

        [TestMethod]
        public void TheMessageRowStartsEmpty()
        {
            WithShell(delegate (ShellWindow window)
            {
                Assert.AreEqual("", window.Message, "message row should start empty");
            });
        }

        [TestMethod]
        public void SourceAndRamPanesContainTheirContent()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend(65536, "render", "render"))
            {
                MonitorSession session = new MonitorSession(machine, new Cpu6502(), AppContext.BaseDirectory);
                ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                    ListingSourceFactory.Create(), AppContext.BaseDirectory,
                    new ConsoleWindowSystem(new HeadlessConsoleDriver()));

                window.ApplyLayout(ShellLayout.ForSize(120, 24));
                window.LoadSourceAndList("test.asm", ".org $8000\nStart:\n  lda #$5A\n  rts\n");

                Assert.IsTrue(window.SourceEditor.Rows.Count > 0, "the source editor has no rows");
                Assert.IsTrue(window.SourceEditor.Rows.Any(row => row.Text.Contains("lda")),
                    "the source editor did not retain the source text");
                Assert.IsTrue(window.RamHasZeroPageMarker, "the RAM pane did not include the zero-page marker");
                Assert.IsTrue(window.RamHasStackMarker, "the RAM pane did not include the stack marker");
                Assert.IsTrue(window.Ram.Rows.Count > 0, "the RAM pane has no rows");
                Assert.IsTrue(window.Ram.Rows[0].Text.Contains("$0000"),
                    "the RAM pane did not start at $0000");
            }
        }

        [TestMethod]
        public void OverflowingSourceTabsShowScrollIndicators()
        {
            WithShell(delegate (ShellWindow window)
            {
                Assert.IsTrue(window.SourceEditors.Control.ShowTabScrollIndicators);
            });
        }

        [TestMethod]
        public void SourceAndRamContentsArePresentInTheRenderedFrame()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend(65536, "render", "render"))
            {
                MonitorSession session = new MonitorSession(machine, new Cpu6502(), AppContext.BaseDirectory);
                HeadlessConsoleDriver driver = new HeadlessConsoleDriver();
                ConsoleWindowSystem ws = new ConsoleWindowSystem(driver);
                ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                    ListingSourceFactory.Create(), AppContext.BaseDirectory, ws);

                window.LoadSourceAndList("test.asm", ".org $8000\nStart:\n  lda #$5A\n  rts\n");
                window.ApplyLayout(ShellLayout.ForSize(120, 34));
                window.RegisterWindows(ws);

                using (HostedSession hosted = ws.BeginHosted())
                {
                    for (int i = 0; i < 5 && hosted.Tick(); i++)
                        ws.ForceFullRedraw();
                }

                Window editorWindow = ws.Windows.Values.First(window => window.Title == "SOURCE & LISTING");
                Window ramWindow = ws.Windows.Values.First(window => window.Title == "RAM ($0000-$3FFF)");
                string editorFrame = string.Join("\n", editorWindow.Buffer.ToLines(Color.Default, Color.Default));
                string ramFrame = string.Join("\n", ramWindow.Buffer.ToLines(Color.Default, Color.Default));
                StringAssert.Contains(editorFrame.ToLowerInvariant(), "lda");
                StringAssert.Contains(editorFrame.ToLowerInvariant(), "5a");
                StringAssert.Contains(editorFrame, "$8000");
                StringAssert.Contains(ramFrame, "$0000  00");
            }
        }

        [TestMethod]
        public void SourceViewportLeavesRoomForTheTabControl()
        {
            foreach (int width in new[] { 80, 100, 120 })
            {
                using (FakeMemoryBackend machine = new FakeMemoryBackend(65536, "render", "render"))
                {
                    MonitorSession session = new MonitorSession(machine, new Cpu6502(), AppContext.BaseDirectory);
                    HeadlessConsoleDriver driver = new HeadlessConsoleDriver();
                    ConsoleWindowSystem windowSystem = new ConsoleWindowSystem(driver);
                    ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                        ListingSourceFactory.Create(), AppContext.BaseDirectory, windowSystem);

                    window.ApplyLayout(ShellLayout.ForSize(width, 34));
                    string source = string.Join("\n", Enumerable.Range(1, 40).Select(i => "line" + i));
                    window.LoadSourceAndList("test.asm", source);
                    window.RegisterWindows(windowSystem);
                    using (HostedSession hosted = windowSystem.BeginHosted())
                    {
                        for (int i = 0; i < 5 && hosted.Tick(); i++)
                            windowSystem.ForceFullRedraw();
                    }

                    MultilineEditControl editor = (MultilineEditControl)window.SourceEditor.Control;
                    Assert.AreEqual(27, editor.ViewportHeight, width + " columns");
                    Window editorWindow = windowSystem.Windows.Values.First(item => item.Title == "SOURCE & LISTING");
                    string frame = string.Join("\n", editorWindow.Buffer.ToLines(Color.Default, Color.Default));
                    StringAssert.Contains(frame, "line27", width + " columns");
                }
            }
        }

        [TestMethod]
        public void RamRowsAreDrawnWholeAndScrollWhenThePaneIsNarrow()
        {
            foreach (int width in new[] { 80, 100, 120 })
            {
                using (FakeMemoryBackend machine = new FakeMemoryBackend(65536, "render", "render"))
                {
                    MonitorSession session = new MonitorSession(machine, new Cpu6502(), AppContext.BaseDirectory);
                    HeadlessConsoleDriver driver = new HeadlessConsoleDriver();
                    ConsoleWindowSystem windowSystem = new ConsoleWindowSystem(driver);
                    ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                        ListingSourceFactory.Create(), AppContext.BaseDirectory, windowSystem);

                    window.ApplyLayout(ShellLayout.ForSize(width, 34));
                    window.RegisterWindows(windowSystem);
                    using (HostedSession hosted = windowSystem.BeginHosted())
                    {
                        for (int i = 0; i < 5 && hosted.Tick(); i++)
                            windowSystem.ForceFullRedraw();
                    }

                    Assert.IsTrue(window.Ram.Rows.Count > 0);
                    ListControl list = (ListControl)window.Ram.Control;
                    Assert.IsTrue(list.StringItems.Count > 0);
                    // A row is never split: address, bytes and characters stay on
                    // one line, and a pane too narrow for it scrolls instead.
                    Assert.AreEqual(window.Ram.Rows.Count, list.StringItems.Count,
                        width + " columns: a RAM row was split across display lines");
                    for (int i = 0; i < list.StringItems.Count; i++)
                        Assert.AreEqual(window.Ram.Rows[i].Text, list.StringItems[i],
                            width + " columns: RAM display row differs from its dump row");
                    int longest = list.StringItems.Max(line => line.Length);
                    Assert.IsTrue(list.Width >= longest,
                        width + " columns: the RAM list is narrower than its longest row, so it cannot scroll to it");
                }
            }
        }

        [TestMethod]
public void ARefusalAppearsInTheMessageRow()
        {
            // Create a shell with a memory-only backend (MesenCE)
            using (FakeMemoryBackend machine = new FakeMemoryBackend(65536, "MesenCE", "2.2.1"))
            {
                MonitorSession session = new MonitorSession(machine, new Cpu6502(), AppContext.BaseDirectory);
                ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                    ListingSourceFactory.Create(), AppContext.BaseDirectory,
                    new ConsoleWindowSystem(new HeadlessConsoleDriver()));

                window.ApplyLayout(ShellLayout.ForSize(120, 24));
                window.ResumeMachine();

                Assert.IsTrue(window.Message.Contains("F9 unavailable on MesenCE 2.2.1"),
                    "the refusal did not appear in the message row: " + window.Message);
            }
        }

        [TestMethod]
        public void TheHelpScreenListsAllKeysWhetherTheyWorkOrNot()
        {
            WithShell(delegate (ShellWindow window)
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
// Create a shell with a memory-only backend (MesenCE)
            using (FakeMemoryBackend machine = new FakeMemoryBackend(65536, "MesenCE", "2.2.1"))
            {
                MonitorSession session = new MonitorSession(machine, new Cpu6502(), AppContext.BaseDirectory);
                ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                    ListingSourceFactory.Create(), AppContext.BaseDirectory,
                    new ConsoleWindowSystem(new HeadlessConsoleDriver()));

                window.ApplyLayout(ShellLayout.ForSize(120, 24));
                string help = window.ShowHelp();

                StringAssert.Contains(help, "F9  run (unavailable on MesenCE 2.2.1)");
            }
        }

        [TestMethod]
        public void TheHelpScreenDoesNotMarkWorkingKeys()
        {
            WithShell(delegate (ShellWindow window)
            {
                string help = window.ShowHelp();

                StringAssert.Contains(help, "F1  this help");
                Assert.IsFalse(help.Contains("F7  (unavailable"));
            });
        }

        [TestMethod]
        public void TheLayoutHasNoOverlapsAtWideWidth()
        {
            WithShell(160, 40, delegate (ShellWindow window)
            {
                ShellLayout layout = window.CurrentLayout;
                
                // Verify no overlapping panes
                Assert.IsTrue(layout.ListingX + layout.ListingWidth <= layout.RamX || 
                             layout.RamX + layout.RamWidth <= layout.ListingX,
                    "listing and RAM panes overlap");
                Assert.IsTrue(layout.TreeX + layout.TreeWidth <= layout.ListingX,
                    "tree and listing panes overlap");
            });
        }

[TestMethod]
        public void NarrowLayoutCollapsesTheTreeToOverlay()
        {
            WithShell(80, 24, delegate (ShellWindow window)
            {
                ShellLayout layout = window.CurrentLayout;
                
                Assert.IsFalse(layout.TreeIsColumn, "tree should be overlay at narrow width");
                Assert.AreEqual(0, layout.TreeWidth,
                    "tree should be hidden in overlay mode");
            });
        }

        [TestMethod]
        public void WideLayoutShowsTreeAsColumn()
        {
            WithShell(120, 24, delegate (ShellWindow window)
            {
                ShellLayout layout = window.CurrentLayout;
                
                Assert.IsTrue(layout.TreeIsColumn, "tree should be column at wide width");
                Assert.AreEqual(ShellLayout.TreeColumnWidth, layout.TreeWidth,
                    "tree column should have fixed width");
            });
        }

        [TestMethod]
public void TheRefusalIsOneLineSoItFitsTheMessageRow()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend(65536, "MesenCE", "2.2.1"))
            {
                MonitorSession session = new MonitorSession(machine, new Cpu6502(), AppContext.BaseDirectory);
                ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                    ListingSourceFactory.Create(), AppContext.BaseDirectory,
                    new ConsoleWindowSystem(new HeadlessConsoleDriver()));

                window.ApplyLayout(ShellLayout.ForSize(120, 24));
                window.ResumeMachine();

                Assert.IsTrue(window.Message.IndexOf('\n') < 0, "the refusal must be a single line");
                Assert.IsTrue(window.Message.Length <= ShellLayout.WideMinimum,
                    "the refusal is " + window.Message.Length + " characters and does not fit a "
                    + ShellLayout.WideMinimum + "-column message row");
            }
        }

        // ---------------------------------------------------------------- the helpers

        private static void WithShell(Action<ShellWindow> body)
        {
            WithShell(120, 24, body);
        }

private static void WithShell(int width, int height, Action<ShellWindow> body)
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend(65536, "render", "render"))
            {
                MonitorSession session = new MonitorSession(machine, new Cpu6502(), AppContext.BaseDirectory);
                ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                    ListingSourceFactory.Create(), AppContext.BaseDirectory,
                    new ConsoleWindowSystem(new HeadlessConsoleDriver()));

                window.ApplyLayout(ShellLayout.ForSize(width, height));

                body(window);
            }
        }
    }
}



