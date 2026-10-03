using System;
using System.Collections.Generic;
using System.IO;
using WinASM65.Projects;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// The configuration that governs an open source.
    ///
    /// Two answers and no third: one governs it, or none does. An unreadable
    /// configuration counts as governing, and that is deliberate rather than
    /// convenient. A source sitting beside a broken <c>config.json</c> looks exactly
    /// like a source with no project, and answering "no project" there would silently
    /// assemble the single file — F5 doing something other than what its own label
    /// says, with nothing on screen to say so. Treating it as governing hands the file
    /// to <c>BUILD</c>, which is the one place that knows how to name a configuration
    /// it cannot read, so the user is told what to fix instead of getting a different
    /// build than the one they asked for.
    /// </summary>
    public sealed class ProjectGovernance
    {
        private ProjectGovernance(bool governed, string configuration)
        {
            Governed = governed;
            Configuration = configuration ?? string.Empty;
        }

        /// <summary>Whether a configuration claims the source, readable or not.</summary>
        public bool Governed { get; private set; }

        /// <summary>
        /// The configuration, as the session would be given it: relative to the
        /// session's directory when it is inside it, absolute otherwise. So the
        /// command line F5 issues is the one a user could have typed.
        /// </summary>
        public string Configuration { get; private set; }

        internal static ProjectGovernance Governing(string configuration)
        {
            return new ProjectGovernance(true, configuration);
        }

        internal static ProjectGovernance None()
        {
            return new ProjectGovernance(false, string.Empty);
        }
    }

    /// <summary>
    /// Which project, if any, the open source belongs to.
    ///
    /// <para>
    /// The rule, in one sentence, because a rule a user cannot state is a rule they
    /// cannot debug: <b>a <c>config.json</c> governs the open source when it declares
    /// that source as one of its inputs</b>. The nearest such configuration wins, and
    /// the search runs from the source's own directory upwards to the session's
    /// directory, so a project in a subdirectory governs its own files and a
    /// monorepo root does not swallow them.
    /// </para>
    ///
    /// <para>
    /// "Declares as one of its inputs" is read from the library rather than from the
    /// text of the file. The configuration's own names are relative to its own
    /// directory, and the library is the one place that knows it, so the shell asking
    /// the session which files a project holds is asking the only authority on the
    /// question instead of parsing JSON and hoping to agree with it.
    /// </para>
    ///
/// <para>
        /// Nothing here throws. A directory that cannot be listed, a configuration that
        /// cannot be parsed and a path with a character no file system accepts are all
        /// ordinary answers, because this runs inside a key handler.
        /// </para>
        /// </summary>
    public static class ProjectConfigLocator
    {
        /// <summary>The name a project configuration is looked for under.</summary>
        public const string ConfigurationFileName = "config.json";

        /// <summary>
        /// A walk with no bottom would climb to the drive root on a pathologically
        /// deep tree. Bounded so a mis-set session directory costs a few probes
        /// rather than a hang.
        /// </summary>
        private const int MaxProbed = 32;

        /// <summary>
        /// The configuration governing <paramref name="sourceFile"/>, searched for
        /// from that file's directory upwards to <paramref name="sessionDirectory"/>.
        /// </summary>
        public static ProjectGovernance Locate(string sourceFile, string sessionDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceFile))
                return ProjectGovernance.None();

            string root = string.IsNullOrEmpty(sessionDirectory)
                ? Directory.GetCurrentDirectory()
                : sessionDirectory;

            string source;
            try
            {
                source = Full(sourceFile, root);
            }
            catch (ArgumentException)
            {
                return ProjectGovernance.None();
            }
            catch (NotSupportedException)
            {
                return ProjectGovernance.None();
            }
            catch (PathTooLongException)
            {
                return ProjectGovernance.None();
            }

            string stop;
            try
            {
                stop = Path.GetFullPath(root);
            }
            catch (ArgumentException)
            {
                stop = Path.GetFullPath(Path.GetDirectoryName(source));
            }
            catch (NotSupportedException)
            {
                stop = Path.GetFullPath(Path.GetDirectoryName(source));
            }

            string directory = Path.GetDirectoryName(source);

            for (int depth = 0; directory != null && depth < MaxProbed; depth++)
            {
                string candidate = Path.Combine(directory, ConfigurationFileName);

                if (File.Exists(candidate))
                {
                    ProjectGovernance verdict = Read(candidate, source, root);
                    if (verdict != null)
                        return verdict;
                }

                if (SameDirectory(directory, stop))
                    break;

                string parent = Path.GetDirectoryName(directory);
                if (parent == null || SameDirectory(parent, directory))
                    break;

                directory = parent;
            }

            return ProjectGovernance.None();
        }

        /// <summary>
        /// What one configuration has to say about one source, or null to keep looking
        /// upwards because this configuration simply does not claim it.
        /// </summary>
        private static ProjectGovernance Read(string configuration, string source, string root)
        {
            ProjectSession session;
            try
            {
                session = ProjectSession.Open(configuration);
            }
            catch (Exception)
            {
                // Broad on purpose: the same third-party JSON reader as the builder, and
                // the same reason. Whether it governs this source is exactly the
                // question that cannot be asked, so it is treated as governing and the
                // build verb is what names the file — one owner for "this configuration
                // cannot be read", rather than a second sentence here.
                return ProjectGovernance.Governing(AsSessionPath(configuration, root));
            }

            IReadOnlyList<string> declared = session.SourceFiles;
            for (int i = 0; i < declared.Count; i++)
            {
                if (SamePath(declared[i], source))
                    return ProjectGovernance.Governing(AsSessionPath(configuration, root));
            }

            return null;
        }

        /// <summary>
        /// The configuration as the session would be given it. Relative when it is
        /// inside the session's directory, so F5's command line reads like something a
        /// user typed; absolute when it is not, because a relative path to somewhere
        /// else would be a path to somewhere else again.
        /// </summary>
        private static string AsSessionPath(string configuration, string root)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(configuration))
                return configuration;

            // Trimmed first, so the answer does not depend on whether the caller wrote
            // the session's directory with a trailing separator. Without this the same
            // directory yields two different command lines, and F5's recorded line
            // would stop being a reliable witness of what it did.
            string basePath = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (basePath.Length == 0)
                return configuration;

            if (!configuration.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                return configuration;

            if (configuration.Length <= basePath.Length + 1)
                return configuration;

            if (configuration[basePath.Length] != Path.DirectorySeparatorChar)
                return configuration;

            return configuration.Substring(basePath.Length + 1);
        }

        private static bool SamePath(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
                return false;

            // Windows file names are case-insensitive and the paths come from two
            // different places — a configuration's own text and the tree the user
            // clicked — so an ordinal comparison would call "bman.nas" and
            // "BMAN.NAS" different files and F5 would fall through to the
            // single-file path for a source the project plainly declares.
            return string.Equals(Full(left, null), Full(right, null), StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameDirectory(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static string Full(string path, string relativeTo)
        {
            if (Path.IsPathRooted(path))
                return Path.GetFullPath(path);

            return Path.GetFullPath(Path.Combine(
                string.IsNullOrEmpty(relativeTo) ? Directory.GetCurrentDirectory() : relativeTo,
                path));
        }
    }
}