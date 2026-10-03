// WinASM65 - Where the side files of a multi-file build are, and what they are called

using System.IO;

namespace WinASM65.Core
{
    /// <summary>
    /// The naming and locating of the files one unit of a build hands to the next.
    /// <para>
    /// A build of more than one source has no other channel between its units: a
    /// unit writes the names it knows and the expressions it could not evaluate, and
    /// the unit that declared it as a dependency reads them back. Those files are
    /// <c>.symb</c>, <c>.Unsolved</c> and <c>.UnsolvedExpr</c>.
    /// </para>
    /// <para>
    /// The rule lives here, once, because the writer and the reader have to agree
    /// exactly: a writer that names a file one way and a reader that looks for
    /// another produces a build that reports an undefined symbol for a name the
    /// dependency really does define.
    /// </para>
    /// </summary>
    public static class SideFiles
    {
        /// <summary>Extensions a side file of a build carries.</summary>
        public static readonly string[] Extensions = { ".symb", ".Unsolved", ".UnsolvedExpr" };

        /// <summary>
        /// The name a unit's side files are derived from: its source path with the
        /// extension taken off.
        /// <para>
        /// The whole path, not just the file name, because the file belongs beside
        /// the source it describes and <c>Assemble</c> has always put it there.
        /// </para>
        /// <para>
        /// This used to be the path up to its first dot, which is the same answer
        /// only while the path holds no dot ahead of the file name. In a project
        /// living at <c>C:\src\.kilo\work\unit.asm</c> it produced
        /// <c>C:\src\</c>, so every side file of every unit in the build was written
        /// to one name above the project, each overwriting the last.
        /// </para>
        /// </summary>
        public static string BaseNameOf(string sourceFile)
        {
            return Path.ChangeExtension(sourceFile, null);
        }

        /// <summary>
        /// Puts a side file where it is to be found.
        /// <para>
        /// With no directory named, the name is used as it stands -- which, being a
        /// path without its extension, already says beside the source. With one
        /// named, the file goes there under its own name, and the directory is
        /// created if it is missing: a caller that said where the files go is
        /// entitled to be the first thing in that directory.
        /// </para>
        /// </summary>
        public static string Locate(string baseName, string directory)
        {
            if (string.IsNullOrEmpty(directory))
                return baseName;
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            return Path.Combine(directory, Path.GetFileName(baseName));
        }
    }
}