using System;
using System.Collections.Generic;
using System.IO;

namespace WinASM65.Monitor.Shell
{
    /// <summary>What a tree row stands for.</summary>
    public enum FileTreeKind
    {
        /// <summary>A section title, not selectable.</summary>
        Section,

        /// <summary>An assemblable source file.</summary>
        Source,

        /// <summary>A unit that has been assembled and kept by the library.</summary>
        Unit
    }

    /// <summary>One row of the file tree.</summary>
    public sealed class FileTreeRow
    {
        public FileTreeRow(FileTreeKind kind, string label, string value)
        {
            Kind = kind;
            Label = label ?? string.Empty;
            Value = value;
        }

        public FileTreeKind Kind { get; }

        /// <summary>What is drawn.</summary>
        public string Label { get; }

        /// <summary>
        /// What the row means to the shell: a file path for a source, a unit name
        /// for a unit, null for a section. Kept out of the label so a file called
        /// <c>ERR</c> cannot be mistaken for a monitor reply.
        /// </summary>
        public string Value { get; }

        public bool IsSelectable
        {
            get { return Kind != FileTreeKind.Section; }
        }
    }

    /// <summary>
    /// The file tree's rows, built from the working directory and from the units
    /// <see cref="UnitLibrary"/> is keeping.
    ///
    /// Two sections, because they answer two different questions. "Sources" is what
    /// could be assembled; "units" is what has been. A tree that merged them would
    /// show a file as loaded the moment it was listed, which is the mistake this
    /// project makes everywhere else too: reporting a state that was not reached.
    ///
    /// Reading the directory is bounded and never throws. A project directory with
    /// ten thousand files, or one the monitor cannot read at all, produces a short
    /// tree with a reason in it — not an exception that takes the shell down.
    /// </summary>
    public static class FileTreeModel
    {
        /// <summary>Extensions the assembler can be pointed at.</summary>
        private static readonly string[] SourceExtensions = { ".asm", ".s", ".inc", ".65" };

        /// <summary>
        /// How many files are listed. The tree is a navigation aid, not a file
        /// browser; a project with more sources than this wants a search box, which
        /// is not this pane's job.
        /// </summary>
        public const int MaxSources = 500;

        public static FileTreeRow Section(string label)
        {
            return new FileTreeRow(FileTreeKind.Section, label, null);
        }

        /// <summary>
        /// The rows for a directory and a set of kept units.
        ///
        /// <paramref name="directory"/> is where relative source paths resolve, so
        /// it is the same directory the session was given, not the process's
        /// current one.
        /// </summary>
        public static IReadOnlyList<FileTreeRow> Rows(string directory, UnitLibrary library, out string problem)
        {
            problem = null;
            List<FileTreeRow> rows = new List<FileTreeRow>();

            if (string.IsNullOrEmpty(directory))
                directory = Directory.GetCurrentDirectory();

            List<string> sources;
            try
            {
                sources = SourceFiles(directory, out problem);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                sources = new List<string>();
                problem = "cannot read " + directory + ": " + ex.Message;
            }

            rows.Add(Section("sources"));
            if (sources.Count == 0 && problem == null)
            {
                rows.Add(new FileTreeRow(FileTreeKind.Source, "(no .asm here)", null));
            }
            else
            {
                foreach (string path in sources)
                    rows.Add(new FileTreeRow(FileTreeKind.Source, Path.GetFileName(path), path));
            }

            IReadOnlyList<RelocatableUnit> units =
                library == null ? new List<RelocatableUnit>() : library.Units;

            rows.Add(Section("units"));
            if (units.Count == 0)
            {
                rows.Add(new FileTreeRow(FileTreeKind.Unit, "(nothing assembled)", null));
            }
            else
            {
                List<RelocatableUnit> sorted = new List<RelocatableUnit>(units);
                sorted.Sort(delegate (RelocatableUnit a, RelocatableUnit b)
                {
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });

                foreach (RelocatableUnit unit in sorted)
                {
                    rows.Add(new FileTreeRow(FileTreeKind.Unit,
                        unit.Name + " $" + unit.NaturalOrigin.ToString("X4") + " " + unit.Length + "o",
                        unit.Name));
                }
            }

            return rows;
        }

        private static List<string> SourceFiles(string directory, out string problem)
        {
            problem = null;
            List<string> found = new List<string>();

            // EnumerationOptions exists on net8.0 and is what keeps a slow or huge
            // directory from turning the first paint into a stall.
            EnumerationOptions options = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive,
                MaxRecursionDepth = 1,
            };

            foreach (string path in Directory.EnumerateFiles(directory, "*", options))
            {
                if (IsSource(path))
                    found.Add(path);
                if (found.Count >= MaxSources)
                {
                    problem = "listing the first " + MaxSources + " source files only";
                    break;
                }
            }

            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        private static bool IsSource(string path)
        {
            string extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension))
                return false;

            for (int i = 0; i < SourceExtensions.Length; i++)
            {
                if (string.Equals(extension, SourceExtensions[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}