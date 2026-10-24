using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using WinASM65.Core;
using WinASM65.Monitor.Protocol;
using WinASM65.Projects;

namespace WinASM65.Monitor
{
    /// <summary>
    /// What one <c>BUILD</c> did: the lines to show, and the session the listing can
    /// then be asked about.
    ///
    /// The answer and the objects are both here because a caller needs both and
    /// re-running the build to get the second one would double the work for no gain.
    /// A failed build keeps whatever the session managed to hold, because the
    /// diagnostics are what the pane shows and a report that threw them away would be
    /// the one place a real error got lost.
    /// </summary>
    public sealed class ProjectBuildReport
    {
        public ProjectBuildReport(bool succeeded, string configurationPath, ProjectSession session,
            ProjectBuildResult result, IReadOnlyList<string> answer)
        {
            Succeeded = succeeded;
            ConfigurationPath = configurationPath ?? string.Empty;
            Session = session;
            Result = result;
            Answer = answer ?? new List<string>();
        }

        /// <summary>Whether the project produced an image and no error.</summary>
        public bool Succeeded { get; private set; }

        /// <summary>Absolute path of the configuration, or empty when none was opened.</summary>
        public string ConfigurationPath { get; private set; }

        /// <summary>
        /// The session that was opened, or null when the configuration could not be
        /// read at all. Present on a failed build too, so the caller can tell a
        /// configuration that was refused from a project that was built and failed.
        /// </summary>
        public ProjectSession Session { get; private set; }

        /// <summary>The build's own result, or null when the configuration was refused.</summary>
        public ProjectBuildResult Result { get; private set; }

        /// <summary>The lines the session answered with, in protocol form.</summary>
        public IReadOnlyList<string> Answer { get; private set; }

        /// <summary>
        /// The whole answer as one string, newlines included. What a pane shows: a
        /// project failure is several diagnostics, and showing only the first would
        /// hide the line the user has to look at next.
        /// </summary>
        public string AnswerText
        {
            get { return string.Join(Environment.NewLine, new List<string>(Answer).ToArray()); }
        }
    }

    /// <summary>
    /// Builds a whole project from a configuration, and answers in strings.
    ///
    /// <para>
    /// This class holds no assembly logic of its own, exactly as
    /// <see cref="UnitLibrary"/> does not: it opens a <see cref="ProjectSession"/>,
    /// asks it to build, and renders what comes back. Every name the configuration
    /// holds is resolved by the library against the configuration's own directory,
    /// never against the process's working directory — so the only path this class has
    /// to get right is the one it was handed, and that is resolved against the
    /// session's directory for the same reason <c>ASSEMBLE</c> resolves its argument
    /// there.
    /// </para>
    ///
    /// <para>
    /// Nothing here throws. <c>ProjectSession.Open</c> raises
    /// <see cref="FileNotFoundException"/> for a missing file and the JSON reader
    /// behind it raises whatever its own library raises for a document that is not a
    /// configuration; a build can fail on a locked file while it publishes. All of
    /// those become named <c>ERR</c> lines, because a monitor that dies on a typo in a
    /// path cannot be used to look up what the typo was, and because a pane must not be
    /// able to take the shell down.
    /// </para>
    /// </summary>
    public sealed class ProjectBuilder
    {
        private readonly string _directory;
        private string _lastConfiguration = string.Empty;
        private ProjectSession _lastSession;
        private ProjectBuildResult _lastResult;

        /// <summary>
        /// <paramref name="directory"/> is where a relative configuration path is
        /// resolved. It is the same directory the session was given, so
        /// <c>BUILD config.json</c> at the prompt and F5 in the shell mean the same
        /// project.
        /// </summary>
        public ProjectBuilder(string directory)
        {
            _directory = string.IsNullOrEmpty(directory) ? Directory.GetCurrentDirectory() : directory;
        }

        /// <summary>The session the last build opened, or null before the first one.</summary>
        public ProjectSession LastSession
        {
            get { return _lastSession; }
        }

        /// <summary>The last build's result, or null before the first one.</summary>
        public ProjectBuildResult LastResult
        {
            get { return _lastResult; }
        }

        /// <summary>The configuration the last build was asked for, by absolute path.</summary>
        public string LastConfiguration
        {
            get { return _lastConfiguration; }
        }

        /// <summary>
        /// Builds the project <paramref name="configurationFile"/> names and answers
        /// with what happened. Never throws.
        /// </summary>
        public ProjectBuildReport Build(string configurationFile)
        {
            if (string.IsNullOrWhiteSpace(configurationFile))
                return Refuse("BUILD expects <config.json>", null, null);

            string path = Absolute(configurationFile);
            _lastConfiguration = path;

            ProjectSession session;
            try
            {
                session = ProjectSession.Open(path);
            }
            catch (FileNotFoundException)
            {
                return Refuse("there is no configuration at " + path
                    + ". BUILD takes the path of a config.json, relative to " + _directory, path, null);
            }
            catch (DirectoryNotFoundException)
            {
                return Refuse("there is no directory at " + path
                    + ". BUILD takes the path of a config.json, relative to " + _directory, path, null);
            }
            catch (Exception ex)
            {
                // Deliberately broad, and deliberately here. This is the boundary to a
                // JSON reader whose exception types are not ours to enumerate: a
                // truncated document, a document that is an array rather than an
                // object, and a document that is not JSON at all all arrive as different
                // types from a library that is free to change them. Every one of them
                // is the same fact to a user — the configuration cannot be read — and
                // it is named as one, with the reader's own words attached so a user
                // can tell a stray comma from a missing brace.
                return Refuse("the configuration at " + path + " cannot be read: " + ex.Message, path, null);
            }

            _lastSession = session;

            ProjectBuildResult result;
            try
            {
                result = session.Build(Options());
            }
            catch (Exception ex)
            {
                // The same boundary, one step further on: publishing the image is a
                // file write, and a locked or read-only directory has to come back as
                // a sentence rather than as an exception out of a key handler.
                _lastResult = null;
                return new ProjectBuildReport(false, path, session, null,
                    new List<string> { MonitorProtocol.ErrPrefix + " the project at " + path
                        + " could not be built: " + ex.Message });
            }

            _lastResult = result;
            return new ProjectBuildReport(result.Success, path, session, result, Describe(result, path));
        }

        /// <summary>
        /// The options every <c>BUILD</c> uses.
        ///
        /// <see cref="ProjectBuildOptions.WriteImage"/> is the one departure from the
        /// library's default, and it is a departure on purpose. The library leaves it
        /// off because an interface pressing F5 wants the bytes in memory; this
        /// interface cannot use them, because the monitor has no way to hand an image
        /// to the attached emulator — its protocol has no verb for it and the bridges
        /// are not to be touched. The file on disk is therefore the only artefact the
        /// user can do anything with, and a build that reported success while leaving
        /// no file would be reporting success at something nobody could use.
        /// </summary>
        private static ProjectBuildOptions Options()
        {
            ProjectBuildOptions options = new ProjectBuildOptions();
            options.WriteImage = true;
            return options;
        }

        /// <summary>
        /// The build's answer: one <c>OK</c> line naming the units, the size and the
        /// file, or a refusal followed by the project's own diagnostics.
        /// </summary>
        private static IReadOnlyList<string> Describe(ProjectBuildResult result, string configurationPath)
        {
            List<string> lines = new List<string>();

            if (result.Success)
            {
                string name = string.IsNullOrEmpty(result.ImagePath)
                    ? "(in memory only)"
                    : result.ImagePath;

                lines.Add(MonitorProtocol.OkPrefix + " " + result.Units.Count + " unit(s), "
                    + result.Image.Length.ToString(CultureInfo.InvariantCulture)
                    + " byte(s) written to " + name);
                return lines;
            }

            List<string> problems = new List<string>();
            if (result.Diagnostics != null)
            {
                foreach (Diagnostic diagnostic in result.Diagnostics)
                {
                    if (diagnostic == null || diagnostic.Severity != DiagnosticSeverity.Error)
                        continue;

                    problems.Add(MonitorProtocol.ErrPrefix + " " + Where(diagnostic));
                }
            }

            if (problems.Count == 0)
            {
                lines.Add(MonitorProtocol.ErrPrefix + " the project at " + configurationPath
                    + " failed without naming a reason");
                return lines;
            }

            lines.Add(MonitorProtocol.ErrPrefix + " " + problems.Count.ToString(CultureInfo.InvariantCulture)
                + " error(s) in the project at " + configurationPath);
            lines.AddRange(problems);
            return lines;
        }

        /// <summary>
        /// One diagnostic as <c>file:line: message</c>, or <c>file: message</c> when
        /// the diagnostic belongs to the project rather than to a line of a source —
        /// an unknown target, a memory map that does not build.
        ///
        /// The file is shown by name and not by path, for the reason the shell's own
        /// listing adapter gives: a project failure is usually several lines of each
        /// file, and repeating the absolute path of each one is noise the reader has
        /// to look past to find the line number.
        /// </summary>
        private static string Where(Diagnostic diagnostic)
        {
            string file = diagnostic.Location.FilePath;
            string name = string.IsNullOrEmpty(file)
                ? "the project"
                : System.IO.Path.GetFileName(file);

            string line = diagnostic.Location.LineNumber > 0
                ? name + ":" + diagnostic.Location.LineNumber.ToString(CultureInfo.InvariantCulture)
                : name;

            return line + ": " + diagnostic.Message;
        }

        private ProjectBuildReport Refuse(string reason, string path, ProjectSession session)
        {
            _lastSession = session;
            _lastResult = null;

            return new ProjectBuildReport(false, path ?? string.Empty, session, null,
                new List<string> { MonitorProtocol.ErrPrefix + " " + reason });
        }

        private string Absolute(string configurationFile)
        {
            string full = Path.IsPathRooted(configurationFile)
                ? configurationFile
                : Path.Combine(_directory, configurationFile);

            try
            {
                return Path.GetFullPath(full);
            }
            catch (ArgumentException)
            {
                // A path with a character no file system accepts. Reported by name
                // rather than thrown out of here, for the same reason the read is.
                return full;
            }
            catch (NotSupportedException)
            {
                return full;
            }
        }
    }
}