using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharpConsoleUI;
using SharpConsoleUI.Drivers;
using WinASM65.Cpu;
using WinASM65.Monitor.Shell;

namespace WinASM65.Monitor.Tests
{
    [TestClass]
    public class SourceEditorTabsTests
    {
        [TestMethod]
        public void OpeningAnExistingTabActivatesIt()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "first.asm");
                string secondPath = Path.Combine(temp.Path, "second.asm");
                File.WriteAllText(path, "lda #$00\n");
                File.WriteAllText(secondPath, "rts\n");

                SourceEditorTabs tabs = CreateTabs(temp.Path);
                SourceEditorPane first = tabs.Open(path, null);
                SourceEditorPane second = tabs.Open(secondPath, null);
                SourceEditorPane activated = null;
                tabs.ActiveChanged += editor => activated = editor;

                tabs.Open(path, null);

                Assert.AreSame(first, tabs.ActiveEditor);
                Assert.AreSame(first, activated);
                Assert.AreNotSame(second, tabs.ActiveEditor);
            }
        }

        private static SourceEditorTabs CreateTabs(string directory)
        {
            return new SourceEditorTabs(ShellTheme.Default,
                new AssemblerListingSource(new Cpu6502(), directory),
                directory,
                new ConsoleWindowSystem(new HeadlessConsoleDriver()));
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }

            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "WinASM65Tabs_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public void Dispose()
            {
                try { Directory.Delete(Path, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
