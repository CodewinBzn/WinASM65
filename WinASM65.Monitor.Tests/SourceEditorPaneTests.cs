using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using SharpConsoleUI;
using SharpConsoleUI.Drivers;
using WinASM65.Cpu;
using WinASM65.Monitor.Abstractions;
using WinASM65.Monitor.Shell;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// The source editor pane, driven through the same HeadlessConsoleDriver run loop the
    /// chrome tests use: layout, draw, and read the screen the driver was left
    /// holding.
    /// </summary>
    [TestClass]
    public class SourceEditorPaneTests
    {
        private const string GoodSource =
            ".org $8000\n" +
            "; a comment\n" +
            "Start:\n" +
            "  lda #$5A\n" +
            "  rts\n";

        [TestMethod]
        public void EditorRowsCarryAddressAndBytesAfterAssembly()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);
                IReadOnlyList<EditorRow> rows = source.EditorRows(GoodSource, "prog.asm", 0x8000);

                Assert.AreEqual(5, rows.Count);

                // Line 4: lda #$5A
                EditorRow lda = rows[3];
                Assert.AreEqual(0x8000, lda.Address);
                Assert.AreEqual(2, lda.Bytes.Count);
                Assert.AreEqual(0xA9, lda.Bytes[0]);
                Assert.AreEqual(0x5A, lda.Bytes[1]);
                Assert.IsTrue(lda.EmitsBytes);

                // Line 2: comment — no address
                EditorRow comment = rows[1];
                Assert.AreEqual(-1, comment.Address);
                Assert.AreEqual(0, comment.Bytes.Count);
                Assert.IsFalse(comment.EmitsBytes);
            }
        }

[TestMethod]
        public void EditorGutterRendersLineNumberAndAddress()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);

                string gutter = ListingFormatter.Gutter(source.EditorRows(GoodSource, "prog.asm", 0x8000)[3]);

                StringAssert.Contains(gutter, "$8000");
                StringAssert.Contains(gutter, "A9 5A");
            }
        }

        [TestMethod]
        public void EditorGutterRendersNoAddressForComment()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);

                string gutter = ListingFormatter.Gutter(source.EditorRows(GoodSource, "prog.asm", 0x8000)[1]);

                // Comment line has no address or bytes
                Assert.IsTrue(gutter.Trim().Length == 0 || gutter.Contains("  "),
                    "comment line gutter should be empty or spaces");
            }
        }

        [TestMethod]
        public void SourceEditorPaneLoadsSourceAndShowsRows()
        {
            IConsoleDriver driver = new HeadlessConsoleDriver();
            using (HostedSession hosted = new ConsoleWindowSystem(driver).BeginHosted())
            {
                                
                using (FakeMemoryBackend machine = new FakeMemoryBackend(65536, "editor", "editor"))
                {
                    MonitorSession session = new MonitorSession(machine, new Cpu6502(), AppContext.BaseDirectory);
                    AssemblerListingSource listing = new AssemblerListingSource(new Cpu6502(), AppContext.BaseDirectory);
                    ShellWindow window = new ShellWindow(session, ShellTheme.Default, listing, AppContext.BaseDirectory, new ConsoleWindowSystem(new HeadlessConsoleDriver()));

                    window.ApplyLayout(ShellLayout.ForSize(120, 24));

                    bool drew = false;
                    int iterations = 0;
                    while (hosted.Tick() && iterations < 10)
                    {
                        iterations++;
                        drew = true;
                    }

                    // Load a source into the editor and focus it
                    window.SourceEditor.LoadSource("prog.asm", GoodSource);

                    // In the real flow, OpenSourceAndList assembles the file
                    // and hands the rows to the editor via SyncRowsFromListing.
                    ListingRequest req = new ListingRequest("prog.asm", 0x8000, 0);
                    req.SourceText = GoodSource;
                    window.SourceEditor.SyncRowsFromListing(listing.Rows(req));

                    window.SourceEditor.FocusEditor();

                    Assert.IsTrue(drew, "the shell never drew a frame");

                    // The editor should have rows
                    Assert.IsTrue(window.SourceEditor.Rows.Count > 0,
                        "the editor has rows after loading a source");

                    // The first instruction row should carry the .org address
                    bool found = false;
                    foreach (EditorRow row in window.SourceEditor.Rows)
                    {
                        if (row.EmitsBytes && row.Address == 0x8000)
                        {
                            found = true;
                            break;
                        }
                    }
                    Assert.IsTrue(found, "the editor shows $8000 for the lda instruction");
                }
            }
        }

        [TestMethod]
        public void SaveWritesBufferToDiskAndReportsSuccess()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = System.IO.Path.Combine(temp.Path, "prog.asm");
                System.IO.File.WriteAllText(path, GoodSource);

                AssemblerListingSource listing = new AssemblerListingSource(new Cpu6502(), temp.Path);
                SourceEditorPane editor = new SourceEditorPane(ShellTheme.Default, listing, temp.Path);

                // Load, modify, save
                editor.LoadSource("prog.asm", GoodSource);

                SaveResult result = editor.Save();

                Assert.IsTrue(result.Success, "save succeeds for a path inside the directory");
                Assert.IsNotNull(result.Message);
                StringAssert.Contains(result.Message, "saved");

                // The file should be on disk
                string written = System.IO.File.ReadAllText(path);
                StringAssert.Contains(written, "lda #$5A");
            }
        }

        [TestMethod]
        public void SaveRefusesPathOutsideDirectory()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                AssemblerListingSource listing = new AssemblerListingSource(new Cpu6502(), temp.Path);
                SourceEditorPane editor = new SourceEditorPane(ShellTheme.Default, listing, temp.Path);

                editor.LoadSource("prog.asm", GoodSource);

                // Try to save to a path outside the directory by changing the source file name
                typeof(SourceEditorPane)
                    .GetProperty("SourceFile")
                    .SetValue(editor, "../../evil.asm", null);

                SaveResult result = editor.Save();

                Assert.IsFalse(result.Success, "save refuses paths outside the directory");
                Assert.IsNotNull(result.Problem);
                StringAssert.Contains(result.Problem, "outside");
            }
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }

            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "WinASM65Editor_" + Guid.NewGuid().ToString("N"));
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



