// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - In-memory listing sink

using System.Collections.Generic;

namespace WinASM65.Output
{
    /// <summary>
    /// A listing sink that keeps what the assembler reports instead of writing
    /// a <c>.lst</c>, and turns it into rows when the assembly finishes.
    /// <para>
    /// It goes through the same row builder as <see cref="ListingService"/>, so
    /// what a user interface shows is what the file would have contained.
    /// </para>
    /// </summary>
    public sealed class InMemoryListingService : IListingService, ISourceLineAwareListingService
    {
        private readonly List<List<ListingFragment>> _lines = new List<List<ListingFragment>>();
        private List<ListingFragment> _current;

        /// <summary>The rows built by the last <see cref="Finish"/>, empty before it.</summary>
        public IReadOnlyList<ListingRow> Rows { get; private set; }

        /// <summary>Always true: this sink has nothing to switch off.</summary>
        public bool IsEnabled { get; set; }

        public InMemoryListingService()
        {
            IsEnabled = true;
            Rows = new List<ListingRow>();
        }

        public void Start(string sourceFilePath)
        {
            _lines.Clear();
            _current = null;
            Rows = new List<ListingRow>();
        }

        public void StartLine(int lineNumber)
        {
            CommitLine();
            _current = new List<ListingFragment>();
            _current.Add(new ListingFragment(lineNumber));
        }

        public void PrintLine(string line)
        {
            Append(new ListingFragment(line));
        }

        public void PrintLine(LineType type, int value)
        {
            Append(new ListingFragment(type, value));
        }

        public void EndLine()
        {
            // Deliberately nothing. The assembler ends the line it started, and
            // the next StartLine closes this one; text printed in between -- a
            // macro body, a repeated block -- belongs to the line already open.
        }

        public void Finish(byte[] memoryBytes)
        {
            CommitLine();
            Rows = ListingRowBuilder.Build(_lines, memoryBytes);
        }

        private void Append(ListingFragment fragment)
        {
            if (_current == null)
                _current = new List<ListingFragment>();
            _current.Add(fragment);
        }

        private void CommitLine()
        {
            if (_current != null && _current.Count > 0)
                _lines.Add(_current);
            _current = null;
        }
    }
}