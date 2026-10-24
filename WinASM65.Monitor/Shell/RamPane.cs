using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// The RAM pane: $0000-$3FFF, with the zero page and the stack marked.
    ///
    /// Read-only in this milestone. Editing a byte, and reporting a write that did
    /// not come back, is A3; a pane that could not do it correctly yet is better
    /// as one that plainly does not do it yet.
    /// </summary>
    public sealed class RamPane
    {
        private readonly ListControl _list;
        private readonly IWindowControl _control;
        private readonly ShellTheme _theme;
        private readonly List<RamDumpRow> _rows = new List<RamDumpRow>();
        private readonly List<int> _faults = new List<int>();
        private readonly List<string> _labels = new List<string>();
        private int _contentWidth = 38;

        public RamPane(ShellTheme theme)
        {
            _theme = theme ?? ShellTheme.Default;
            Title = "RAM";

            _list = new ListControl
            {
                IsSelectable = true,
                Title = Title,
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

        public string Title { get; set; }

        public int FaultCount { get { return _faults.Count; } }

        public bool HasZeroPageMarker { get { return _rows.Exists(r => r.Address >= 0x0000 && r.Address <= 0x00FF); } }

        public bool HasStackMarker { get { return _rows.Exists(r => r.Address >= 0x0100 && r.Address <= 0x01FF); } }

        public IReadOnlyList<RamDumpRow> Rows { get { return _rows; } }

        public void Refresh(Func<int, int, byte[]> reader)
        {
            IReadOnlyList<RamDumpRow> rows = RamDump.Window(reader);
            RefreshRows(rows);
        }

        public void RefreshRows(IReadOnlyList<RamDumpRow> rows)
        {
            if (rows == null)
                rows = new List<RamDumpRow>();

            _rows.Clear();
            _faults.Clear();

            foreach (RamDumpRow row in rows)
            {
                if (row.Bytes.Count == 0)
                    _faults.Add(row.Address);

                _rows.Add(row);
            }

            _labels.Clear();
            foreach (RamDumpRow row in _rows)
                _labels.AddRange(FormatRows(row));

            _list.StringItems = new List<string>(_labels);
            int contentWidth = _labels.Count == 0
                ? _contentWidth
                : _labels.Max(label => label.Length) + 1;
            _list.Width = Math.Max(_contentWidth, contentWidth);
            _list.Invalidate();
        }

        public void SetContentWidth(int width)
        {
            _contentWidth = Math.Max(1, width);
            RefreshRows(_rows);
        }

        public void SetVisibleItems(int height)
        {
            _list.MaxVisibleItems = Math.Max(1, height);
        }

        public void InvalidateWindow(Window window)
        {
            window?.Invalidate(true);
        }

        public void ApplyChanges(IReadOnlyList<(int Address, byte Value)> changes)
        {
            if (changes == null || changes.Count == 0 || _rows.Count == 0)
                return;

            IReadOnlyList<RamDumpRow> changed = RamDump.ApplyChanges(_rows, changes);

            if (changed.Count == 0)
                return;

            _labels.Clear();
            foreach (RamDumpRow row in _rows)
                _labels.AddRange(FormatRows(row));

            _list.StringItems = new List<string>(_labels);
            int contentWidth = _labels.Count == 0
                ? _contentWidth
                : _labels.Max(label => label.Length) + 1;
            _list.Width = Math.Max(_contentWidth, contentWidth);
            _list.Invalidate();
        }

        private List<string> FormatRows(RamDumpRow row)
        {
            List<string> lines = new List<string>();
            if (row.Bytes.Count == 0)
            {
                lines.Add(row.Address.ToString("$X4", CultureInfo.InvariantCulture)
                    + "  unreadable");
                return lines;
            }

            lines.Add(row.Text);
            return lines;
        }
    }
}