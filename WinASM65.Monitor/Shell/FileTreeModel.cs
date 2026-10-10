using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
        Unit,

        /// <summary>A ROM image that can be transferred to a connected emulator.</summary>
        Rom,

        /// <summary>A project text/configuration file that can be edited.</summary>
        TextFile
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

        /// <summary>True for a section header (e.g. "sources/", "projects/").</summary>
        public bool IsDirectory
        {
            get { return Kind == FileTreeKind.Section; }
        }

        /// <summary>The absolute path on disk. Empty for section headers.</summary>
        public string Path
        {
            get { return Value ?? string.Empty; }
        }

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
        ///
        /// .nas is here because the NES homebrew this tool is built to debug is
        /// written in it, and a source the tree will not list is a source the user
        /// cannot open. The assembler reads .incbin, .include and .org, so the
        /// dialect is one it can be pointed at — whether it accepts every construct
        /// in a given file is the listing's answer to report, not the tree's to
        /// pre-judge by hiding the file.
        private static readonly string[] SourceExtensions = { ".asm", ".s", ".inc", ".65", ".nas" };
        private static readonly HashSet<string> ExcludedDirectories = new HashSet<string>(
            new[] { ".git", ".vs", ".vscode", ".tmp", "bin", "obj", "node_modules",
                ".kilo", ".idea", ".cache", ".gradle", ".next", ".terraform", ".pytest_cache",
                "TestResults", "packages", "artifacts", "coverage", "dist", "TempInspect",
                "SharpConsoleUI.Temp" },
            StringComparer.OrdinalIgnoreCase);

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
            List<string> textFiles;
            List<string> romFiles;
            try
            {
                sources = SourceFiles(directory, out problem);
                textFiles = TextFiles(directory, out string textProblem);
                romFiles = RomFiles(directory, out string romProblem);
                if (problem == null)
                    problem = textProblem;
                if (problem == null)
                    problem = romProblem;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                sources = new List<string>();
                textFiles = new List<string>();
                romFiles = new List<string>();
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
                    rows.Add(new FileTreeRow(FileTreeKind.Source, Path.GetRelativePath(directory, path), path));
            }

            if (textFiles.Count > 0)
            {
                rows.Add(Section("project files"));
                foreach (string path in textFiles)
                    rows.Add(new FileTreeRow(FileTreeKind.TextFile,
                        Path.GetRelativePath(directory, path), path));
            }

            if (romFiles.Count > 0)
            {
                rows.Add(Section("ROM images"));
                foreach (string path in romFiles)
                    rows.Add(new FileTreeRow(FileTreeKind.Rom,
                        Path.GetRelativePath(directory, path), path));
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
            if (!Directory.Exists(directory))
            {
                problem = "cannot read " + directory + ": directory does not exist";
                return found;
            }
            try
            {
                foreach (string path in EnumerateProjectFiles(directory))
                {
                    if (!IsSource(path))
                        continue;
                    found.Add(path);
                    if (found.Count >= MaxSources)
                    {
                        problem = "listing the first " + MaxSources + " source files only";
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                problem = "cannot read " + directory + ": " + ex.Message;
            }

            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        private static List<string> TextFiles(string directory, out string problem)
        {
            problem = null;
            List<string> found = new List<string>();
            try
            {
                foreach (string path in EnumerateProjectFiles(directory))
                {
                    if (!SourceDocument.IsEditableTextFile(path) || IsSource(path))
                        continue;
                    found.Add(path);
                    if (found.Count >= MaxSources)
                    {
                        problem = "listing the first " + MaxSources + " project text files only";
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                problem = "cannot list project text files: " + ex.Message;
            }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        private static List<string> RomFiles(string directory, out string problem)
        {
            problem = null;
            List<string> found = new List<string>();
            try
            {
                foreach (string path in EnumerateProjectFiles(directory))
                {
                    string extension = Path.GetExtension(path);
                    if (!string.Equals(extension, ".nes", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(extension, ".bin", StringComparison.OrdinalIgnoreCase))
                        continue;

                    found.Add(path);
                    if (found.Count >= MaxSources)
                    {
                        problem = "listing the first " + MaxSources + " ROM images only";
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                problem = "cannot list project ROM images: " + ex.Message;
            }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        private static IEnumerable<string> EnumerateProjectFiles(string directory)
        {
            Stack<string> pending = new Stack<string>();
            pending.Push(directory);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                IEnumerable<string> files;
                IEnumerable<string> directories;
                try
                {
                    files = Directory.EnumerateFiles(current).ToArray();
                    directories = Directory.EnumerateDirectories(current).ToArray();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    continue;
                }
                foreach (string file in files)
                {
                    if (!IsReparsePoint(file))
                        yield return file;
                }
                foreach (string subdirectory in directories)
                {
                    string name = Path.GetFileName(subdirectory);
                    if (ExcludedDirectories.Contains(name) || IsReparsePoint(subdirectory))
                        continue;
                    pending.Push(subdirectory);
                }
            }
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
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