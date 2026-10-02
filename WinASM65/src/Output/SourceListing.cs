// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Listing API consumed by a user interface

using System.Collections.Generic;
using WinASM65.Core;

namespace WinASM65.Output
{
    /// <summary>
    /// The result of listing a source: the assembly itself and the rows that
    /// describe it, in display order.
    /// </summary>
    public sealed class SourceListing
    {
        /// <summary>The name the source was listed under.</summary>
        public string SourceName { get; private set; }

        /// <summary>The assembly the listing was taken from.</summary>
        public AssemblyResult Assembly { get; private set; }

        /// <summary>
        /// One entry per display row. A row is not a source line: an instruction
        /// emitting more than four bytes spans several rows, so the mapping from
        /// row to source line is carried per row in
        /// <see cref="ListingRow.LineNumber"/>.
        /// </summary>
        public IReadOnlyList<ListingRow> Rows { get; private set; }

        public SourceListing(string sourceName, AssemblyResult assembly, IReadOnlyList<ListingRow> rows)
        {
            SourceName = sourceName ?? string.Empty;
            Assembly = assembly;
            Rows = rows ?? new List<ListingRow>();
        }

        /// <summary>Whether the assembly produced no error.</summary>
        public bool Success
        {
            get { return Assembly != null && Assembly.Success; }
        }
    }

    /// <summary>
    /// Optional companion to <see cref="IListingService"/>. A listing sink that
    /// implements it is told which source line the text it is about to receive
    /// came from, which is what lets a row carry a line number instead of
    /// leaving the interface to reconstruct one.
    /// <para>
    /// It is a separate interface on purpose: <see cref="IListingService"/> is
    /// the contract the assembler has always been given, and adding a member to
    /// it would break every sink already written against it.
    /// </para>
    /// </summary>
    public interface ISourceLineAwareListingService
    {
        /// <summary>
        /// Announces the source line the next <see cref="IListingService.PrintLine(string)"/>
        /// belongs to, numbered from one.
        /// </summary>
        void StartLine(int lineNumber);
    }

    /// <summary>
    /// Lists a source in memory. This is what a user interface binds to: it
    /// hands over source text and gets back rows, with no file of its own and
    /// nothing to clean up.
    /// <para>
    /// It is not <see cref="IListingService"/>, which is the other direction:
    /// the sink the assembler writes into while it runs.
    /// </para>
    /// </summary>
    public interface ISourceListingService
    {
        /// <summary>Lists source text held in memory, under the name "source".</summary>
        SourceListing ListSourceText(string sourceText);

        /// <summary>
        /// Lists source text held in memory, under a name the caller chooses.
        /// The name is what diagnostics report and what an <c>.include</c>
        /// resolves against; nothing is read from or written to it.
        /// </summary>
        SourceListing ListSourceText(string sourceText, string sourceName);

        /// <summary>Lists a source file, reading it from disk.</summary>
        SourceListing ListSourceFile(string sourceFilePath);
    }
}