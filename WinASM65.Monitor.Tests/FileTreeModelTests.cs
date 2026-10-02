using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Cpu;
using WinASM65.Monitor.Shell;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// The file tree's rows, and its refusals to guess.
    ///
    /// The two sections are the point. "Sources" is what could be assembled and
    /// "units" is what has been; a tree that merged them would show a file as
    /// loaded the moment it was listed.
    /// </summary>
    [TestClass]
    public class FileTreeModelTests
    {
        private static string TempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "WinASM65Tree_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        [TestMethod]
        public void SourceFilesAreListedAndOtherFilesAreNot()
        {
            string directory = TempDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "b.asm"), "; b");
                File.WriteAllText(Path.Combine(directory, "a.asm"), "; a");
                File.WriteAllText(Path.Combine(directory, "notes.txt"), "not source");

                string problem;
                IReadOnlyList<FileTreeRow> rows = FileTreeModel.Rows(directory, null, out problem);

                List<string> names = Names(rows, FileTreeKind.Source);
                CollectionAssert.AreEqual(new[] { "a.asm", "b.asm" }, names);
                Assert.IsNull(problem);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void TheExtensionsTheAssemblerUnderstandsAreTheOnesListed()
        {
            string directory = TempDirectory();
            try
            {
                foreach (string name in new[] { "a.asm", "b.s", "c.inc", "d.65" })
                    File.WriteAllText(Path.Combine(directory, name), "; x");
                File.WriteAllText(Path.Combine(directory, "e.asm.bak"), "; x");
                File.WriteAllText(Path.Combine(directory, "readme"), "x");

                string problem;
                IReadOnlyList<FileTreeRow> rows = FileTreeModel.Rows(directory, null, out problem);

                CollectionAssert.AreEqual(
                    new[] { "a.asm", "b.s", "c.inc", "d.65" },
                    Names(rows, FileTreeKind.Source));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void AnEmptyDirectorySaysSoRatherThanLookingBroken()
        {
            string directory = TempDirectory();
            try
            {
                string problem;
                IReadOnlyList<FileTreeRow> rows = FileTreeModel.Rows(directory, null, out problem);

                Assert.IsTrue(ContainsLabel(rows, "(no .asm here)"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void SourcesAndUnitsAreSeparateSections()
        {
            string directory = TempDirectory();
            try
            {
                // A .export, because a unit is only assembled when the assembler is
                // told which symbols leave the module.
                File.WriteAllText(Path.Combine(directory, "a.asm"),
                    ".org $8000\nRoutine: lda #$5A\n rts\n .export Routine\n");

                using (FakeMemoryBackend machine = new FakeMemoryBackend())
                {
                    MonitorSession session = new MonitorSession(machine, new Cpu6502(), directory);

                    string source = Path.Combine(directory, "a.asm");
                    Assert.IsTrue(session.Execute("ASSEMBLE " + source)[0].StartsWith("OK", StringComparison.Ordinal));

                    string problem;
                    IReadOnlyList<FileTreeRow> rows = FileTreeModel.Rows(directory, session.Library, out problem);

                    Assert.AreEqual(2, CountKind(rows, FileTreeKind.Section), "sources and units");
                    Assert.AreEqual(1, CountKind(rows, FileTreeKind.Source));
                    Assert.AreEqual(1, CountKind(rows, FileTreeKind.Unit), "the assembled file appears once, under units");
                    Assert.IsFalse(ContainsLabel(rows, "(nothing assembled)"));
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void NothingAssembledSaysSo()
        {
            string directory = TempDirectory();
            try
            {
                string problem;
                IReadOnlyList<FileTreeRow> rows = FileTreeModel.Rows(directory, null, out problem);

                Assert.IsTrue(ContainsLabel(rows, "(nothing assembled)"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void ADirectoryThatCannotBeReadIsAProblemAndNotAnException()
        {
            string missing = Path.Combine(Path.GetTempPath(),
                "WinASM65NoSuchDirectory_" + Guid.NewGuid().ToString("N"));

            string problem;
            IReadOnlyList<FileTreeRow> rows = FileTreeModel.Rows(missing, null, out problem);

            Assert.IsNotNull(problem, "a missing directory must say so");
            StringAssert.Contains(problem, "cannot read");
            Assert.IsTrue(rows.Count > 0, "the units section is still drawn");
        }

        [TestMethod]
        public void ASourceRowCarriesItsFullPathSoAssemblyCanResolveIt()
        {
            string directory = TempDirectory();
            try
            {
                string path = Path.Combine(directory, "a.asm");
                File.WriteAllText(path, "; a");

                string problem;
                IReadOnlyList<FileTreeRow> rows = FileTreeModel.Rows(directory, null, out problem);

                foreach (FileTreeRow row in rows)
                {
                    if (row.Kind == FileTreeKind.Source && row.Value != null)
                        Assert.AreEqual(path, row.Value);
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void SectionRowsAreNotSelectable()
        {
            FileTreeRow section = FileTreeModel.Section("sources");

            Assert.AreEqual(FileTreeKind.Section, section.Kind);
            Assert.IsFalse(section.IsSelectable);
            Assert.IsNull(section.Value);
        }

        [TestMethod]
        public void AnEmptyDirectoryNameFallsBackToTheCurrentOne()
        {
            string problem;
            IReadOnlyList<FileTreeRow> rows = FileTreeModel.Rows(null, null, out problem);

            Assert.IsTrue(rows.Count > 0);
        }

        private static List<string> Names(IReadOnlyList<FileTreeRow> rows, FileTreeKind kind)
        {
            List<string> names = new List<string>();
            foreach (FileTreeRow row in rows)
            {
                if (row.Kind == kind && row.Value != null)
                    names.Add(row.Label);
            }
            return names;
        }

        private static int CountKind(IReadOnlyList<FileTreeRow> rows, FileTreeKind kind)
        {
            int count = 0;
            foreach (FileTreeRow row in rows)
            {
                if (row.Kind == kind)
                    count++;
            }
            return count;
        }

        private static bool ContainsLabel(IReadOnlyList<FileTreeRow> rows, string label)
        {
            foreach (FileTreeRow row in rows)
            {
                if (row.Label == label)
                    return true;
            }
            return false;
        }
    }
}