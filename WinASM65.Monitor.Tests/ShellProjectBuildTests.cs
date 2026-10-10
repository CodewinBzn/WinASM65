using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Drawing;
using System.Reflection;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drivers;
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
    /// The rule has to be asserted in both directions, and that is the whole point of
    /// this class. A key that quietly becomes "build the project" in one directory and
    /// "assemble the file" in another produces a tool whose behaviour depends on where
    /// a file sits, which is the one property a user cannot debug from the inside. So
    /// each test here states which of the two happened, in the command line the window
    /// issued — not in the message, which the implementation also writes, and not in
    /// the pane, which the implementation also fills.
    ///
    /// Note: These tests verify the project build logic without full TUI integration.
    /// Full TUI integration tests with SharpConsoleUI are pending rewrite.
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
            "  .import Shared\n" +
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
        public void F5AssemblesTheSelectedNestedSourceByItsProjectRelativePath()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                Directory.CreateDirectory(Path.Combine(temp.Path, "src"));
                File.WriteAllText(Path.Combine(temp.Path, "src", "standalone.asm"),
                    ".org $A000\nAlone: nop\n.export Alone\n");

                WithShell(temp.Path, delegate (ShellWindow window)
                {
                    window.OpenSource(Path.Combine("src", "standalone.asm"));
                    window.AssembleCurrentSource();

                    CollectionAssert.AreEqual(
                        new[] { EditorCommands.Assemble(Path.Combine("src", "standalone.asm")) },
                        new List<string>(window.Commands));
                    StringAssert.Contains(window.Message, "OK");
                });
            }
        }

        [TestMethod]
        public void BuildAndTransferActionSendsTheBuiltRomToTheAttachedEmulator()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            using (FakeMemoryBackend machine = new FakeMemoryBackend(65536, "build", "build"))
            {
                WriteProject(temp, Bank1, Bank2);
                MonitorSession session = new MonitorSession(machine, new Cpu6502(), temp.Path);
                ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                    new AssemblerListingSource(new Cpu6502(), temp.Path), temp.Path,
                    new ConsoleWindowSystem(new HeadlessConsoleDriver()));
                window.ApplyLayout(ShellLayout.ForSize(160, 40));
                window.OpenSource("bank2.asm");

                window.BuildAndTransferCurrentProject();

                Assert.AreEqual(Path.Combine(temp.Path, "game.nes"), machine.TransferredRomPath);
                StringAssert.Contains(window.Message, "ROM replaced");
            }
        }

        [TestMethod]
        public void ContextMenuOffersProjectActionsAndTransfersToTheAttachedEmulator()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            using (FakeMemoryBackend machine = new FakeMemoryBackend(65536, "build", "build"))
            {
                WriteProject(temp, Bank1, Bank2);
                MonitorSession session = new MonitorSession(machine, new Cpu6502(), temp.Path);
                ConsoleWindowSystem windowSystem = new ConsoleWindowSystem(new HeadlessConsoleDriver());
                ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                    new AssemblerListingSource(new Cpu6502(), temp.Path), temp.Path, windowSystem);
                window.ApplyLayout(ShellLayout.ForSize(160, 40));
                window.RegisterWindows(windowSystem);
                window.OpenSource("bank2.asm");

                MethodInfo openMenu = typeof(ShellWindow).GetMethod("ShowSourceContextMenu",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo treeField = typeof(ShellWindow).GetField("_tree",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FileTreePane tree = (FileTreePane)treeField.GetValue(window);
                openMenu.Invoke(window, new object[]
                {
                    new FileTreeRow(FileTreeKind.Source, "bank2.asm", Path.Combine(temp.Path, "bank2.asm")),
                    new Point(5, 5),
                    tree.ScrollableControl,
                });

                MenuControl menu = windowSystem.DesktopPortalService.TopPortal.Content as MenuControl;
                Assert.IsNotNull(menu, "right-clicking a source should open its contextual action menu");
                using (HostedSession hosted = windowSystem.BeginHosted())
                {
                    for (int tick = 0; tick < 5
                        && !windowSystem.DesktopPortalService.TopPortal.HasRendered; tick++)
                        Assert.IsTrue(hosted.Tick());
                }
                Assert.IsTrue(windowSystem.DesktopPortalService.TopPortal.HasRendered,
                    "the context menu portal should render above the shell windows");
                CollectionAssert.AreEqual(
                    new[] { "Assemble source", "Build project", "Build + Transfer" },
                    menu.Items.Select(item => item.Text).ToArray());

                menu.Items.Single(item => item.Text == "Build + Transfer").Action();

                using (HostedSession hosted = windowSystem.BeginHosted())
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                    while (DateTime.UtcNow < deadline
                        && !window.Message.Contains("ROM replaced"))
                    {
                        Assert.IsTrue(hosted.Tick());
                        System.Threading.Thread.Sleep(10);
                    }
                }

                Assert.AreEqual(Path.Combine(temp.Path, "game.nes"), machine.TransferredRomPath);
                StringAssert.Contains(window.Message, "ROM replaced");
            }
        }

[TestMethod]
        public void F5BuildsTheProjectWhenTheConfigurationIsInAParentDirectory()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                // Config in parent declares sub/bank2.asm
                string config = "{\n  \"name\": \"test\",\n  \"Input\": [\n    { \"FileName\": \"bank1.asm\", \"org\": \"$8000\" },\n    { \"FileName\": \"sub/bank2.asm\", \"org\": \"$9000\" }\n  ],\n  \"Output\": {\n    \"ObjectFile\": \"game.nes\"\n  }\n}";
                File.WriteAllText(Path.Combine(temp.Path, "bank1.asm"), Bank1);

                string sub = Path.Combine(temp.Path, "sub");
                Directory.CreateDirectory(sub);
                File.WriteAllText(Path.Combine(sub, "bank2.asm"), Bank2);
                File.WriteAllText(Path.Combine(temp.Path, "config.json"), config);

                WithShell(temp.Path, delegate (ShellWindow window)
                {
                    window.OpenSource(Path.Combine("sub", "bank2.asm"));
                    window.AssembleCurrentSource();

                    CollectionAssert.AreEqual(
                        new List<string> { "BUILD config.json" },
                        new List<string>(window.Commands),
                        "config.json is in the parent, so F5 must build the project");

                    StringAssert.Contains(window.Message, "-- project config.json");
                });
            }
        }

        [TestMethod]
        public void F5AssemblesWhenTheConfigurationExistsButDoesNotDeclareTheFile()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
{
                WriteProject(temp, Bank1, Bank2);
                File.WriteAllText(Path.Combine(temp.Path, "lone.asm"), Lone);

                WithShell(temp.Path, delegate (ShellWindow window)
                {
                    window.OpenSource("lone.asm");
                    window.AssembleCurrentSource();

                    CollectionAssert.AreEqual(
                        new List<string> { "ASSEMBLE lone.asm" },
                        new List<string>(window.Commands),
                        "config.json exists but does not name lone.asm, so F5 assembles it");

                    StringAssert.Contains(window.Message, "-- single file lone.asm");
                    StringAssert.Contains(window.Message, "OK");
                    Assert.IsFalse(window.Message.Contains("project"));
                });
            }
        }

        [TestMethod]
        public void TheMessageLineNamesWhichOfTheTwoThingsRan()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                WriteProject(temp, Bank1, Bank2);

                WithShell(temp.Path, delegate (ShellWindow window)
                {
                    window.OpenSource("bank2.asm");
                    window.AssembleCurrentSource();
                    StringAssert.Contains(window.Message, "-- project config.json");
                });
            }

            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "prog.asm");
                File.WriteAllText(path, ".org $8000\nStart:\n  lda #$5A\n  rts\n");

                WithShell(temp.Path, delegate (ShellWindow window)
                {
                    window.OpenSource("prog.asm");
                    window.AssembleCurrentSource();
                    StringAssert.Contains(window.Message, "-- single file prog.asm");
                });
            }
        }

[TestMethod]
        public void ABuildErrorAppearsInTheMessageLine()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                WriteProject(temp, Bank1, Bank2);
                // Bank2 with a syntax error
                File.WriteAllText(Path.Combine(temp.Path, "bank2.asm"),
                    ".org $9000\nEntry:\n  jmp Shared\n  not_an_opcode\n  .export Entry\n");

                WithShell(temp.Path, delegate (ShellWindow window)
                {
                    window.OpenSource("bank2.asm");
                    window.AssembleCurrentSource();

                    StringAssert.Contains(window.Message, "ERR");
                    StringAssert.Contains(window.Message, "Undefined Macro");
                });
            }
        }

[TestMethod]
        public void AnAssembleErrorOnASingleFileAppearsInTheMessageLine()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "prog.asm");
                File.WriteAllText(path, ".org $8000\nStart:\n  not_an_opcode\n  rts\n  .export Start\n");

                WithShell(temp.Path, delegate (ShellWindow window)
                {
                    window.OpenSource("prog.asm");
                    window.AssembleCurrentSource();

                    StringAssert.Contains(window.Message, "ERR");
                    StringAssert.Contains(window.Message, "assembly failed");
                });
            }
        }

        [TestMethod]
        public void F5WithNoOpenSourceSaysSoAndSendsNothing()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                WithShell(temp.Path, delegate (ShellWindow window)
                {
                    window.AssembleCurrentSource();

                    StringAssert.Contains(window.Message, "no source is open");
                    Assert.AreEqual(0, window.Commands.Count,
                        "nothing may be sent to the session when nothing is open");
                });
            }
        }

[TestMethod]
        public void TheBuiltImageIsWrittenWhereTheConfigurationSays()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string customOutput = Path.Combine(temp.Path, "custom.nes");
                string config = "{\n  \"name\": \"test\",\n  \"Input\": [\n    { \"FileName\": \"bank1.asm\", \"org\": \"$8000\" }\n  ],\n  \"Output\": {\n    \"ObjectFile\": \"" + customOutput.Replace("\\", "\\\\") + "\"\n  }\n}";
                File.WriteAllText(Path.Combine(temp.Path, "bank1.asm"), Bank1);
                File.WriteAllText(Path.Combine(temp.Path, "config.json"), config);

                WithShell(temp.Path, delegate (ShellWindow window)
                {
                    window.OpenSource("bank1.asm");
                    window.AssembleCurrentSource();

                    Assert.IsTrue(File.Exists(customOutput),
                        "the image must be at the configured output path");
                });
            }
        }

[TestMethod]
        public void AMissingConfigurationFileIsRefusedWithAMessage()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                File.WriteAllText(Path.Combine(temp.Path, "prog.asm"), ".org $8000\nStart:\n  lda #$5A\n  rts\n  .export Start\n");

                WithShell(temp.Path, delegate (ShellWindow window)
                {
                    window.OpenSource("prog.asm");
                    window.AssembleCurrentSource();

                    StringAssert.Contains(window.Message, "OK");
                    StringAssert.Contains(window.Message, "prog");
                    Assert.IsFalse(window.Message.Contains("project"));
                });
            }
        }

        [TestMethod]
        public void AMalformedConfigurationFileIsRefusedWithAMessage()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                File.WriteAllText(Path.Combine(temp.Path, "config.json"), "{ not json }");
File.WriteAllText(Path.Combine(temp.Path, "prog.asm"), ".org $8000\nStart:\n  lda #$5A\n  rts\n  .export Start\n");

                WithShell(temp.Path, delegate (ShellWindow window)
                {
                    window.OpenSource("prog.asm");
                    window.AssembleCurrentSource();

                    StringAssert.Contains(window.Message, "ERR");
                    StringAssert.Contains(window.Message, "configuration");
                    StringAssert.Contains(window.Message, "cannot be read");
                });
            }
        }

[TestMethod]
        public void TheProjectRuleIsExposedInTheHelpScreen()
        {
            WithShell(AppContext.BaseDirectory, delegate (ShellWindow window)
            {
                string help = window.ShowHelp();

                StringAssert.Contains(help, "F5  build and transfer project ROM");
                StringAssert.Contains(help, ShellWindow.ProjectRule);
            });
        }

        // ------------------------------------------------------------------ the helpers

        private static void WithShell(string directory, Action<ShellWindow> body)
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend(65536, "build", "build"))
            {
MonitorSession session = new MonitorSession(machine, new Cpu6502(), directory);
                ShellWindow window = new ShellWindow(session, ShellTheme.Default,
                    new AssemblerListingSource(new Cpu6502(), directory), directory,
                    new ConsoleWindowSystem(new HeadlessConsoleDriver()));

                window.ApplyLayout(ShellLayout.ForSize(160, 40));

                body(window);
            }
        }

private static void WriteProject(TemporaryDirectory temp, string bank1, string bank2)
        {
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
