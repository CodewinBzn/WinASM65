using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
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
        private readonly IWindowControl _control;
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
            _control = Controls.ScrollablePanel()
                .AddControl(_list)
                .WithVerticalScroll(ScrollMode.None)
                .WithHorizontalScroll(ScrollMode.Scroll)
                .WithScrollbar(true)
                .Build();
        }

        public IWindowControl Control { get { return _list; } }

        public IWindowControl ScrollableControl { get { return _control; } }

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
            _list.Width = Math.Max(_viewportWidth, labels.Count == 0
                ? _viewportWidth
                : labels.Max(label => label.Length) + 1);
            if (_list.Items.Count > 0 && _list.SelectedIndex < 0)
                _list.SelectedIndex = 0;
            _list.Invalidate();
        }

        private int _viewportWidth = 1;

        public void SetViewportWidth(int width)
        {
            _viewportWidth = Math.Max(1, width);
            _list.Width = Math.Max(_viewportWidth, _list.StringItems.Count == 0
                ? _viewportWidth
                : _list.StringItems.Max(label => label.Length) + 1);
        }
    }
}