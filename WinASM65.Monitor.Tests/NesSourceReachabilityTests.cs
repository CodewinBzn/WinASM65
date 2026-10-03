using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Cpu;
using WinASM65.Monitor;
using WinASM65.Monitor.Shell;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// The NES homebrew is the reference workload: if the editor cannot open it, the
    /// editor is a toy. These tests are what turned that from an assumption into a
    /// measurement, and two real defects came out of it.
    ///
    /// The first: .nas was not a recognised source extension, so the tree listed
    /// "(no .asm here)" over a directory full of sources. Renaming one file to .asm
    /// made it appear, which is how the cause was isolated to the extension list
    /// rather than to the assembler.
    ///
    /// The second, and the worse one: a source that failed to assemble produced an
    /// empty pane, and the pane then showed UnavailableReason — "the listing API is
    /// not in this build". A user whose file has an undefined symbol on line 536 was
    /// being told to go looking for a build that is present.
    /// </summary>
    [TestClass]
    public class NesSourceReachabilityTests
    {
        private static string BombermanDirectory
        {
            get
            {
                // Walk up from the test binaries until the repository root shows up, so
                // the test does not depend on the working directory or a machine path.
                DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "example_bomberman-nes");
                    if (Directory.Exists(candidate))
                        return candidate;

                    dir = dir.Parent;
                }

                return null;
            }
        }

        [TestMethod]
        public void TheNasSourceExtensionIsRecognised()
        {
            // The extension list is private, so this is observed the way a user meets
            // it: by whether the tree offers the file.
            string source = Path.Combine(AppContext.BaseDirectory, "Reachability", "sample.nas");
            Directory.CreateDirectory(Path.GetDirectoryName(source));
            File.WriteAllText(source, "        LDA #1" + Environment.NewLine);

            try
            {
                string problem;
                IReadOnlyList<FileTreeRow> rows =
                    FileTreeModel.Rows(Path.GetDirectoryName(source), new UnitLibrary(null, AppContext.BaseDirectory), out problem);

                Assert.IsTrue(Contains(rows, "sample.nas"),
                    "a .nas file is not offered by the tree, so it cannot be opened. Listed: " + Describe(rows));
            }
            finally
            {
                File.Delete(source);
            }
        }

        [TestMethod]
        public void TheOtherSourceExtensionsAreStillRecognised()
        {
            foreach (string extension in new[] { ".asm", ".s", ".inc", ".65" })
            {
                string source = Path.Combine(AppContext.BaseDirectory, "Reachability", "sample" + extension);
                Directory.CreateDirectory(Path.GetDirectoryName(source));
                File.WriteAllText(source, "        LDA #1" + Environment.NewLine);

                try
                {
                    string problem;
                    IReadOnlyList<FileTreeRow> rows =
                        FileTreeModel.Rows(Path.GetDirectoryName(source), new UnitLibrary(null, AppContext.BaseDirectory), out problem);

                    Assert.IsTrue(Contains(rows, "sample" + extension),
                        extension + " stopped being recognised. Listed: " + Describe(rows));
                }
                finally
                {
                    File.Delete(source);
                }
            }
        }

        [TestMethod]
        public void AFailedAssemblyReportsTheFailureRatherThanAMissingFeature()
        {
            // The exact shape of the bomberman failure at BMAN.NAS:536, which is a bare
            // VRAMADDRZ that the assembler reports as "Undefined Macro". That token is
            // what a genuine, reported failure looks like here.
            //
            // Note what this deliberately does NOT use: `JMP NEVER_DEFINED` does not
            // fail. The assembler resolves an undefined symbol to zero and assembles
            // 4C 00 00 with no diagnostic. Silence there is an assembler defect, not a
            // listing one, so it is measured separately rather than mistaken for a
            // case this seam handles.
            string source = Path.Combine(AppContext.BaseDirectory, "Reachability", "broken.asm");
            Directory.CreateDirectory(Path.GetDirectoryName(source));
            File.WriteAllText(source,
                "        .org $C000" + Environment.NewLine
                + "        VRAMADDRZ" + Environment.NewLine);

            try
            {
                IListingSource listing = ListingSourceFactory.Create(new Cpu6502(), AppContext.BaseDirectory);
                Assert.IsTrue(listing.IsAvailable);

                IReadOnlyList<ListingRow> rows =
                    listing.Rows(new ListingRequest(source, 0xC000, 50));

                Assert.AreEqual(0, rows.Count, "a file that does not assemble must not produce rows");

                Assert.IsFalse(string.IsNullOrEmpty(listing.LastProblem),
                    "a failed assembly has to say so; LastProblem was empty, so the pane fell "
                    + "back to UnavailableReason and blamed the build instead of the source");

                Assert.IsFalse(listing.LastProblem.Contains("not in this build"),
                    "the reported reason blames the build, but the build is present: "
                    + listing.LastProblem);

                StringAssert.Contains(listing.LastProblem, "broken.asm",
                    "the reason must name the file the user was looking at");
            }
            finally
            {
                File.Delete(source);
            }
        }

        [TestMethod]
        public void AGoodAssemblyLeavesNoProblemToReport()
        {
            // The other half of the contract: null means the caller can tell "nothing
            // was emitted" from "something failed".
            string source = Path.Combine(AppContext.BaseDirectory, "Reachability", "good.asm");
            Directory.CreateDirectory(Path.GetDirectoryName(source));
            File.WriteAllText(source,
                "        .org $C000" + Environment.NewLine
                + "        LDA #$5A" + Environment.NewLine
                + "        STA $0200" + Environment.NewLine);

            try
            {
                IListingSource listing = ListingSourceFactory.Create(new Cpu6502(), AppContext.BaseDirectory);
                IReadOnlyList<ListingRow> rows = listing.Rows(new ListingRequest(source, 0xC000, 50));

                Assert.IsTrue(rows.Count > 0, "a valid source listed nothing");
                Assert.IsNull(listing.LastProblem,
                    "a successful listing reported a problem: " + listing.LastProblem);
            }
            finally
            {
                File.Delete(source);
            }
        }

        [TestMethod]
        public void ThePlaceholderSourceReportsItsOwnReason()
        {
            // The seam has two implementations and both must answer the new member.
            IListingSource unavailable = new UnavailableListingSource();

            Assert.AreEqual(unavailable.UnavailableReason, unavailable.LastProblem,
                "a source that never becomes available has exactly one reason, and it is the same one");
        }

        [TestMethod]
        public void TheBombermanSourcesAreReachableWhenTheyArePresent()
        {
            string directory = BombermanDirectory;
            if (directory == null)
                Assert.Inconclusive("example_bomberman-nes is not in this checkout");

            string problem;
            IReadOnlyList<FileTreeRow> rows =
                FileTreeModel.Rows(directory, new UnitLibrary(null, directory), out problem);

            Assert.IsTrue(ContainsExtension(rows, ".nas"),
                "the homebrew is written in .nas and none of it is listed. Listed: " + Describe(rows));
        }

        private static bool Contains(IReadOnlyList<FileTreeRow> rows, string name)
        {
            foreach (FileTreeRow row in rows)
            {
                if (row.Value != null && row.Value.EndsWith(name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool ContainsExtension(IReadOnlyList<FileTreeRow> rows, string extension)
        {
            foreach (FileTreeRow row in rows)
            {
                if (row.Kind == FileTreeKind.Source && row.Value != null
                    && row.Value.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string Describe(IReadOnlyList<FileTreeRow> rows)
        {
            List<string> names = new List<string>();
            foreach (FileTreeRow row in rows)
                names.Add(row.Kind + ":" + (row.Value ?? row.Label ?? "?"));

            return string.Join(", ", names.ToArray());
        }
    }
}