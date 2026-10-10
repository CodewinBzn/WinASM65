using System;
using System.Collections.Generic;
using System.Globalization;
using SharpConsoleUI;
using SharpConsoleUI.Controls;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// The listing pane: a stock list of assembled rows, with the address and
    /// bytes drawn as one attribute per row.
    ///
    /// Read-only in this milestone. Clicking a row and following a symbol is A3;
    /// a pane that cannot do it correctly yet is better as one that plainly does
    /// not do it yet.
    /// </summary>
    public sealed class ListingPane
    {
        private readonly ListControl _list;
        private readonly ShellTheme _theme;
        private readonly IListingSource _source;
        private readonly List<ListingRow> _rows = new List<ListingRow>();

        public ListingPane(ShellTheme theme, IListingSource source)
        {
            _theme = theme ?? ShellTheme.Default;
            _source = source;

            _list = new ListControl
            {
                IsSelectable = true,
                Title = "listing",
            };
        }

        public IWindowControl Control { get { return _list; } }

        public IReadOnlyList<ListingRow> Rows { get { return _rows; } }

        public ListingRow Selected
        {
            get
            {
                int index = _list.SelectedIndex;
                return index >= 0 && index < _rows.Count ? _rows[index] : null;
            }
        }

        public void SetRows(IReadOnlyList<ListingRow> rows)
        {
            _rows.Clear();
            List<string> labels = new List<string>();

            if (rows != null)
            {
                foreach (ListingRow row in rows)
                {
                    _rows.Add(row);
                    labels.Add(ListingFormatter.Row(row));
                }
            }

            _list.StringItems = labels;
            if (_list.Items.Count > 0 && _list.SelectedIndex < 0)
                _list.SelectedIndex = 0;
            _list.Invalidate();
        }
    }
}