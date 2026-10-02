using System;
using System.Collections.Generic;

namespace WinASM65.Monitor.Shell
{
    /// <summary>What the shell needs to draw one line of a listing.</summary>
    public sealed class ListingRequest
    {
        public ListingRequest(string sourceFile, int origin, int maxRows)
        {
            SourceFile = sourceFile;
            Origin = origin;
            MaxRows = maxRows;
        }

        /// <summary>The source to list. Absolute or relative to the session's directory.</summary>
        public string SourceFile { get; }

        /// <summary>The address the first emitted byte lands at.</summary>
        public int Origin { get; }

        /// <summary>Ceiling on rows returned, so a long file cannot stall the render loop.</summary>
        public int MaxRows { get; }
    }

    /// <summary>
    /// One run of text inside a listing row, tagged with the shell role to paint it.
    ///
    /// The tag is a <see cref="ThemeRole"/> name rather than a colour, on purpose:
    /// the palette stays editable and the 16-colour fallback stays a lookup. A
    /// lexer that produced colours would bake the theme into the assembler
    /// library, which is not where a colour belongs.
    /// </summary>
    public sealed class ListingToken
    {
        public ListingToken(string text, string role)
        {
            Text = text ?? string.Empty;

            // An unrecognised role falls back rather than travelling. A token
            // carrying a name the palette has never heard of would be painted
            // with whatever the theme does for a missing role, which is the
            // default — the same answer, reached at the point where the mistake
            // can still be reported.
            Role = ThemeRole.IsKnown(role) ? role : ThemeRole.Default;
        }

        public string Text { get; }

        /// <summary>A <see cref="ThemeRole"/> name.</summary>
        public string Role { get; }
    }

    /// <summary>
    /// One display row. Deliberately not one source line: an instruction emitting
    /// three bytes occupies three byte cells, and a source line number is not a
    /// display row. Keeping the distinction in the type stops the pane from
    /// inventing it later.
    /// </summary>
    public sealed class ListingRow
    {
        private readonly List<ListingToken> _tokens = new List<ListingToken>();

        /// <summary>Source line number, or 0 for a line that emits no bytes.</summary>
        public int LineNumber { get; set; }

        /// <summary>Address of the first byte, or -1 for a line that emits no bytes.</summary>
        public int Address { get; set; }

        /// <summary>Bytes emitted by this row. Never null.</summary>
        public IList<byte> Bytes { get; set; }

        /// <summary>Documented cycles, or null when the instruction has no documented cost.</summary>
        public int? Cycles { get; set; }

        /// <summary>The source line as written, for the source column.</summary>
        public string Source { get; set; }

        /// <summary>The source split into role-tagged runs, for the source column.</summary>
        public IList<ListingToken> Tokens
        {
            get { return _tokens; }
        }

        /// <summary>True for a line that emits nothing: a comment, a blank, an equ.</summary>
        public bool EmitsBytes
        {
            get { return Address >= 0 && Bytes != null && Bytes.Count > 0; }
        }

        /// <summary>Builds the rows' tokens from their source text.</summary>
        public void SetTokens(IEnumerable<ListingToken> tokens)
        {
            _tokens.Clear();
            if (tokens == null)
                return;
            _tokens.AddRange(tokens);
        }
    }

    /// <summary>
    /// The one thing the editor and listing panes ask for, and the entire surface
    /// they know about.
    ///
    /// This is a consumption seam, not the listing API. The library's
    /// <c>IListingService</c>, <c>InstructionDocs</c> and highlighting lexer are
    /// being written by another session, and their final shape is not fixed yet.
    /// Guessing it here would create a second definition of "a listing line" that
    /// has to be deleted later. So the shell states its own minimum need in terms
    /// it fully controls — role-tagged text and bytes per row — and one adapter
    /// class translates the library's types into it when they exist.
    ///
    /// Wiring it needs exactly three things from the library:
    ///
    /// 1. Given a source path and an origin, the emitted bytes and the address of
    ///    every row, which is what <c>IListingService</c> answers.
    /// 2. The documented cycle count and instruction length for an opcode, which is
    ///    what <c>InstructionDocs</c> answers.
    /// 3. A tokeniser over one source line returning text runs tagged with the
    ///    <see cref="ThemeRole"/> names above.
    ///
    /// Implement this interface over those three and replace the one line in
    /// <see cref="ListingSourceFactory"/> that returns
    /// <see cref="UnavailableListingSource"/>. Nothing else in the shell changes.
    /// </summary>
    public interface IListingSource
    {
        /// <summary>
        /// False while the listing cannot be produced. The panes show
        /// <see cref="UnavailableReason"/> instead of inventing rows; a pane with
        /// nothing to show must say so rather than show something invented.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>Why it is unavailable, in one sentence, for the pane.</summary>
        string UnavailableReason { get; }

        /// <summary>
        /// The rows to draw. Never null. An unavailable source returns an empty
        /// list rather than throwing, because a pane must not be able to take the
        /// shell down.
        /// </summary>
        IReadOnlyList<ListingRow> Rows(ListingRequest request);
    }

    /// <summary>
    /// The listing seam before the library side exists.
    ///
    /// It produces no rows and says why. This is the honest placeholder: the
    /// alternative, a pane full of made-up opcodes, is precisely the fiction this
    /// project refuses everywhere else — a monitor showing a listing that was
    /// never assembled looks exactly like a monitor that assembled something.
    /// </summary>
    public sealed class UnavailableListingSource : IListingSource
    {
        public const string Reason =
            "listing unavailable: the in-memory listing API (IListingService, InstructionDocs"
            + " and the highlighting lexer) is not in this build yet.";

        private static readonly ListingRow[] NoRows = new ListingRow[0];

        public bool IsAvailable
        {
            get { return false; }
        }

        public string UnavailableReason
        {
            get { return Reason; }
        }

        public IReadOnlyList<ListingRow> Rows(ListingRequest request)
        {
            return NoRows;
        }
    }

    /// <summary>
    /// Where the shell gets its listing from.
    ///
    /// One method, so that wiring the real API later is a one-line change in one
    /// place rather than a search for every construction site.
    /// </summary>
    public static class ListingSourceFactory
    {
        /// <summary>
        /// The listing source the shell runs with today.
        ///
        /// NOT YET WIRED. When the library side lands, this returns an adapter over
        /// it; nothing else in the shell needs to change.
        /// </summary>
        public static IListingSource Create()
        {
            return new UnavailableListingSource();
        }
    }
}