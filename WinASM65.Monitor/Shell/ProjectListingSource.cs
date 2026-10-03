using System;
using System.Collections.Generic;
using System.IO;
using WinASM65.Cpu;
using WinASM65.Output;
using WinASM65.Projects;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// The listing seam, asked about a project that has just been built.
    ///
    /// <para>
    /// The problem this answers is not "how do I list a file" — the single-file
    /// adapter already does that, and does it well. It is that a listing of one bank
    /// of a three-file program is wrong in a way the reader cannot see: every routine
    /// another bank defines reads as an undefined symbol, on a line that is perfectly
    /// correct. That is the whole reason the library grew
    /// <see cref="ProjectSession.ProjectSymbols"/> and
    /// <see cref="ProjectSession.ListSourceFile"/>, and this is the adapter that puts
    /// them behind the seam the panes already speak.
    /// </para>
    ///
    /// <para>
    /// It adds nothing to <see cref="IListingSource"/>. The seam is four members wide
    /// and stays four members wide: a project-aware listing is a different
    /// <em>source</em> behind the same door, which is what the door was cut for, and
    /// adding a member for it would be the seam closing over the thing it exists to
    /// keep swappable.
    /// </para>
    ///
    /// <para>
    /// It also does not widen what is known. A name no unit of the project defines is
    /// still reported, and a defect in the file itself is still reported: the session
    /// widens the set of names, it does not make silence correct. Without that, F5 on
    /// a project would make every error in every unit of it disappear, which is a worse
    /// failure than the one this replaces.
    /// </para>
    /// </summary>
    public sealed class ProjectListingSource : IListingSource
    {
        private static readonly ListingRow[] NoRows = new ListingRow[0];

        private readonly AssemblerListingSource _adapter;
        private readonly string _directory;

        private string _projectPath;
        private ProjectSession _session;
        private string _problem;

        /// <summary>
        /// Lists through <paramref name="session"/>, resolving relative source paths
        /// against <paramref name="directory"/> and reporting <paramref name="projectPath"/>
        /// as the project in force.
        /// </summary>
        /// <param name="adapter">
        /// The single-file adapter, reused rather than reimplemented. It carries the
        /// CPU's opcode table and the lexer, and those two decide what the pane paints
        /// a row as — so a second copy would be a second answer to the same question.
        /// </param>
        public ProjectListingSource(ICpuInstructionSet cpu, string directory,
            ProjectSession session, string projectPath)
            : this(new AssemblerListingSource(cpu, directory), directory, session, projectPath)
        {
        }

        /// <summary>The same, over an adapter the caller already has.</summary>
        public ProjectListingSource(AssemblerListingSource adapter, string directory,
            ProjectSession session, string projectPath)
        {
            _adapter = adapter ?? new AssemblerListingSource(null, directory);
            _directory = string.IsNullOrEmpty(directory) ? Directory.GetCurrentDirectory() : directory;
            _projectPath = projectPath ?? string.Empty;
            _session = session;
        }

        /// <summary>The project this source lists against, or null before one is built.</summary>
        public ProjectSession Session
        {
            get { return _session; }
        }

        /// <summary>The configuration in force, for the pane's own caption.</summary>
        public string ProjectPath
        {
            get { return _projectPath; }
        }

        /// <summary>
        /// Points this source at a session that has been built. Called once per
        /// successful build rather than constructed afresh, so the pane keeps the
        /// source it was given and only the knowledge behind it changes.
        /// </summary>
        public void Use(ProjectSession session, string projectPath)
        {
            _session = session;
            _projectPath = projectPath ?? _projectPath;
        }

        /// <summary>
        /// Whether it can list at all. False only when there is no CPU to list for, or
        /// no project yet: a project that has not been built has no names to widen the
        /// listing with, and a listing widened by nothing is the single-file listing
        /// under a project caption, which is the confusion this class exists to remove.
        /// </summary>
        public bool IsAvailable
        {
            get { return _adapter != null && _adapter.IsAvailable && _session != null; }
        }

        /// <summary>
        /// Why there are no rows. The missing CPU when there is none, the reason the
        /// last listing failed when one did, and the plain fact that no project has
        /// been built otherwise — each named, so a pane showing this is making a
        /// statement a user can act on.
        /// </summary>
        public string UnavailableReason
        {
            get
            {
                if (_adapter == null || !_adapter.IsAvailable)
                    return AssemblerListingSource.NoCpuReason;

                if (_session == null)
                    return "listing unavailable: no project has been built yet, so there are no"
                        + " other units whose names this listing could know. F5 builds the"
                        + " project first.";

                return _problem ?? string.Empty;
            }
        }

        /// <summary>
        /// The reason the last <see cref="Rows"/> call produced no rows, or null when it
        /// produced some. The same failure a pane shows, kept as a value so a caller
        /// can put it in a message line without re-parsing the pane's text.
        /// </summary>
        public string LastProblem
        {
            get { return _problem; }
        }

        /// <summary>
        /// The rows for one source of the project, listed with every unit's names.
        /// Never throws: a missing file, a directory the process cannot read and a
        /// source that does not list are all ordinary answers, and a pane must not be
        /// able to take the shell down.
        /// </summary>
        public IReadOnlyList<ListingRow> Rows(ListingRequest request)
        {
            _problem = null;

            if (!IsAvailable)
            {
                _problem = UnavailableReason;
                return NoRows;
            }

            if (request == null || string.IsNullOrEmpty(request.SourceFile))
            {
                _problem = "no source file was named, so there is nothing to list.";
                return NoRows;
            }

            string path = Resolve(request.SourceFile);

            SourceListing listing;
            try
            {
                listing = _session.ListSourceFile(path);
            }
            catch (IOException ex)
            {
                _problem = "cannot read " + path + ": " + ex.Message;
                return NoRows;
            }
            catch (UnauthorizedAccessException ex)
            {
                _problem = "cannot read " + path + ": " + ex.Message;
                return NoRows;
            }

            IReadOnlyList<ListingRow> rows = _adapter.RowsOf(listing, Path.GetFileName(path), request.MaxRows);
            _problem = _adapter.LastProblem;
            return rows;
        }

        private string Resolve(string sourceFile)
        {
            return Path.IsPathRooted(sourceFile) ? sourceFile : Path.Combine(_directory, sourceFile);
        }
    }
}