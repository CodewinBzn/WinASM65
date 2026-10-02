using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Terminal.Gui;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// The status line, painted as one label per role run.
    ///
    /// Separate labels rather than one coloured string, because a role is the unit
    /// the palette is written in: a single label would force the whole line to one
    /// colour and throw the palette away at exactly the place it is most visible.
    /// Whole segments are hidden rather than truncated, for the reason given on
    /// <see cref="ShellStatus.SelectFitting"/>.
    /// </summary>
    public sealed class StatusLine : View
    {
        private readonly List<Label> _labels = new List<Label>();
        private readonly List<StatusSegment> _segments = new List<StatusSegment>();
        private readonly ShellTheme _theme;

        public StatusLine(ShellTheme theme)
        {
            _theme = theme ?? ShellTheme.Default;

            Title = "status";
            CanFocus = false;
            Height = 1;
            Width = Dim.Fill();
            BorderStyle = LineStyle.None;
        }

        /// <summary>The segments currently shown, which is not all of them on a narrow line.</summary>
        public IReadOnlyList<StatusSegment> ShownSegments
        {
            get { return _segments; }
        }

        /// <summary>Replaces the line and re-flows it to <paramref name="width"/> columns.</summary>
        public void Update(IReadOnlyList<StatusSegment> segments, int width)
        {
            List<StatusSegment> fitting = ShellStatus.SelectFitting(segments, width);

            while (_labels.Count > fitting.Count)
            {
                Label extra = _labels[_labels.Count - 1];
                _labels.RemoveAt(_labels.Count - 1);
                Remove(extra);
            }

            for (int i = 0; i < fitting.Count; i++)
            {
                StatusSegment segment = fitting[i];

                Label label;
                if (i < _labels.Count)
                {
                    label = _labels[i];
                }
                else
                {
                    label = new Label { CanFocus = false, Height = 1, Text = string.Empty };
                    _labels.Add(label);
                    Add(label);
                }

                label.Text = segment.Text;
                label.SetAttribute(_theme.Attribute(segment.Role));

                // Re-flow every label: dropping a segment shifts all the ones after
                // it, and a label left at its old column would leave a gap.
                label.X = i == 0 ? 0 : Pos.Right(_labels[i - 1]) + 2;
            }

            _segments.Clear();
            _segments.AddRange(fitting);
        }

        /// <summary>The line as plain text, for tests and for the help screen.</summary>
        public string Line
        {
            get { return ShellStatus.Fit(_segments, int.MaxValue); }
        }
    }

    /// <summary>
    /// The file tree: what could be assembled, and what has been.
    ///
    /// A stock <see cref="ListView"/> with a row attribute, rather than a custom
    /// renderer. The rows are already text, and the only colour that carries meaning
    /// here is section versus content — which is one attribute per row, not a
    /// drawing pipeline.
    /// </summary>
    public sealed class FileTreePane : FrameView
    {
        private readonly ListView _list;
        private readonly ShellTheme _theme;
        private readonly List<FileTreeRow> _rows = new List<FileTreeRow>();

        public FileTreePane(ShellTheme theme)
        {
            _theme = theme ?? ShellTheme.Default;
            Title = "files";

            _list = new ListView
            {
                CanFocus = true,
                BorderStyle = LineStyle.None,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ColorScheme = ShellColorScheme.Build(_theme, ColorSchemeRoles.Body),
            };

            _list.RowRender += OnRowRender;
            _list.OpenSelectedItem += OnOpen;

            Add(_list);
        }

        /// <summary>Raised when the user picks a source file or a unit.</summary>
        public event EventHandler<FileTreeRow> Activated;

        /// <summary>The rows as built, for tests and for the pane's own selection.</summary>
        public IReadOnlyList<FileTreeRow> Rows
        {
            get { return _rows; }
        }

        /// <summary>The selected row, or null when there is none.</summary>
        public FileTreeRow Selected
        {
            get
            {
                int index = _list.SelectedItem;
                return index >= 0 && index < _rows.Count ? _rows[index] : null;
            }
        }

        public void SetRows(IReadOnlyList<FileTreeRow> rows, string problem)
        {
            _rows.Clear();
            List<string> labels = new List<string>();

            if (problem != null)
            {
                labels.Add("! " + problem);
            }

            if (rows != null)
            {
                foreach (FileTreeRow row in rows)
                {
                    _rows.Add(row);
                    labels.Add(row.IsSelectable ? "  " + row.Label : row.Label);
                }
            }

            _list.Source = new ListWrapper<string>(new ObservableCollection<string>(labels));
            if (_list.SelectedItem < 0 && labels.Count > 0)
                _list.SelectedItem = 0;
            _list.SetNeedsDraw();
        }

        public void SelectFirstSelectable()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].IsSelectable)
                {
                    _list.SelectedItem = i;
                    return;
                }
            }
        }

        private void OnRowRender(object sender, ListViewRowEventArgs args)
        {
            if (args == null)
                return;

            int index = args.Row;
            if (index < 0 || index >= _rows.Count)
                return;

            FileTreeRow row = _rows[index];
            if (row.Kind == FileTreeKind.Section)
            {
                args.RowAttribute = _theme.Attribute(ThemeRole.Chrome);
                return;
            }

            args.RowAttribute = row.Value == null
                ? _theme.Attribute(ThemeRole.Comment)
                : _theme.Attribute(ThemeRole.Default);
        }

        private void OnOpen(object sender, ListViewItemEventArgs args)
        {
            FileTreeRow row = Selected;
            if (row != null && row.IsSelectable && Activated != null)
                Activated(this, row);
        }
    }

    /// <summary>
    /// The RAM pane: $0000-$07FF, with the zero page and the stack marked.
    ///
    /// Read-only in this milestone. Editing a byte, and reporting a write that did
    /// not come back, is A3; a pane that could not do it correctly yet is better
    /// as one that plainly does not do it yet.
    /// </summary>
    public sealed class RamPane : FrameView
    {
        private readonly ListView _list;
        private readonly ShellTheme _theme;
        private readonly List<RamDumpRow> _rows = new List<RamDumpRow>();
        private readonly List<int> _faults = new List<int>();

        public RamPane(ShellTheme theme)
        {
            _theme = theme ?? ShellTheme.Default;
            Title = "RAM";

            _list = new ListView
            {
                CanFocus = true,
                BorderStyle = LineStyle.None,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ColorScheme = ShellColorScheme.Build(_theme, ColorSchemeRoles.Body),
            };

            _list.RowRender += OnRowRender;

            Add(_list);
        }

        /// <summary>How many rows the last read could not deliver.</summary>
        public int FaultCount
        {
            get { return _faults.Count; }
        }

        /// <summary>The rows as read, for tests.</summary>
        public IReadOnlyList<RamDumpRow> Rows
        {
            get { return _rows; }
        }

        /// <summary>
        /// Re-reads the window and redraws.
        ///
        /// <paramref name="reader"/> never throws out of here: a refusal becomes an
        /// empty row, because a RAM pane that takes the shell down when the bridge
        /// is slow is worse than a RAM pane with holes in it.
        /// </summary>
        public void Refresh(Func<int, int, byte[]> reader)
        {
            _rows.Clear();
            _faults.Clear();

            IReadOnlyList<RamDumpRow> rows = RamDump.Window(reader);
            List<string> labels = new List<string>();

            foreach (RamDumpRow row in rows)
            {
                if (row.Bytes.Count == 0)
                {
                    _faults.Add(row.Address);
                    labels.Add(row.Address.ToString("$X4", CultureInfo.InvariantCulture)
                        + "  (not readable)");
                }
                else
                {
                    labels.Add(row.Text);
                }

                _rows.Add(row);
            }

            _list.Source = new ListWrapper<string>(new ObservableCollection<string>(labels));
            _list.SetNeedsDraw();
            SetNeedsDraw();
        }

        private void OnRowRender(object sender, ListViewRowEventArgs args)
        {
            if (args == null || args.Row < 0 || args.Row >= _rows.Count)
                return;

            RamDumpRow row = _rows[args.Row];

            if (row.Bytes.Count == 0)
            {
                args.RowAttribute = _theme.Attribute(ThemeRole.Error);
                return;
            }

            // The region boundary rows are the ones a programmer scans for, so the
            // three starts are the only rows that get a colour of their own.
            if (RamDump.StartsRegion(row.Address))
                args.RowAttribute = _theme.Attribute(ThemeRole.Directive);
            else if (row.Region == RamRegion.ZeroPage)
                args.RowAttribute = _theme.Attribute(ThemeRole.Operand);
            else
                args.RowAttribute = _theme.Attribute(ThemeRole.Default);
        }
    }

    /// <summary>
    /// The listing pane: address, bytes, cycles and source, in the plan's fixed
    /// columns.
    ///
    /// Until the listing API exists it shows why it is empty instead of rows. That
    /// is the whole design of the seam: a pane with nothing to show says so, because
    /// a pane full of invented opcodes is indistinguishable from a pane that worked.
    /// </summary>
    public sealed class ListingPane : FrameView
    {
        private readonly TextView _text;
        private readonly ShellTheme _theme;
        private readonly IListingSource _source;

        public ListingPane(ShellTheme theme, IListingSource source)
        {
            _theme = theme ?? ShellTheme.Default;
            _source = source ?? new UnavailableListingSource();

            Title = "listing";
            ColorScheme = ShellColorScheme.Build(_theme, ColorSchemeRoles.Chrome);

            _text = new TextView
            {
                CanFocus = false,
                ReadOnly = true,
                WordWrap = false,
                BorderStyle = LineStyle.None,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ColorScheme = ShellColorScheme.Build(_theme, ColorSchemeRoles.Body),
            };

            Add(_text);
            ShowUnavailable();
        }

        /// <summary>What the pane is currently showing.</summary>
        public string Content
        {
            get { return _text.Text ?? string.Empty; }
        }

        /// <summary>
        /// Draws the placeholder. Kept as its own method so the pane has exactly one
        /// way to be empty, and so a test can assert the empty state without a
        /// terminal.
        /// </summary>
        public void ShowUnavailable()
        {
            _text.Text = _source.IsAvailable
                ? "(no listing)"
                : _source.UnavailableReason + Environment.NewLine
                  + "The panes are laid out and the rest of the shell works;"
                  + " only this pane has nothing to show.";
            _text.ColorScheme = ShellColorScheme.Build(_theme, ColorSchemeRoles.Warning);
            _text.SetNeedsDraw();
            SetNeedsDraw();
        }

        /// <summary>Draws rows, using the fixed column widths from the specification.</summary>
        public void Show(IReadOnlyList<ListingRow> rows)
        {
            if (_source == null || !_source.IsAvailable || rows == null || rows.Count == 0)
            {
                ShowUnavailable();
                return;
            }

            List<string> lines = new List<string>();
            foreach (ListingRow row in rows)
            {
                string address = row.EmitsBytes
                    ? "$" + row.Address.ToString("X4") + "  "
                    : new string(' ', 8);

                string bytes = FormatBytes(row);
                string cycles = row.Cycles.HasValue
                    ? row.Cycles.Value.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;

                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0,5} {1}{2}{3}  {4}",
                    row.LineNumber,
                    address,
                    bytes.PadRight(11),
                    cycles.PadLeft(5),
                    row.Source ?? string.Empty));
            }

            _text.Text = string.Join(Environment.NewLine, lines.ToArray());
            _text.ColorScheme = ShellColorScheme.Build(_theme, ColorSchemeRoles.Body);
            _text.SetNeedsDraw();
            SetNeedsDraw();
        }

        /// <summary>
        /// Up to three groups of three, then <c>+N</c>. A 6502 instruction is at
        /// most three bytes, so the continuation is rare, and showing it rather
        /// than truncating keeps the byte column the same width for every row.
        /// </summary>
        private static string FormatBytes(ListingRow row)
        {
            if (!row.EmitsBytes)
                return new string(' ', 11);

            System.Text.StringBuilder text = new System.Text.StringBuilder();
            int shown = Math.Min(3, row.Bytes.Count);
            for (int i = 0; i < shown; i++)
                text.Append(row.Bytes[i].ToString("X2", CultureInfo.InvariantCulture)).Append(' ');

            int rest = row.Bytes.Count - shown;
            if (rest > 0)
                text.Append('+').Append(rest.ToString(CultureInfo.InvariantCulture));

            return text.ToString();
        }
    }
}