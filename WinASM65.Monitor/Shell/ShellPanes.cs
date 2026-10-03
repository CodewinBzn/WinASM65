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

    /// <summary>
    /// One screen line of the listing, holding one label per colour run.
    ///
    /// Every label's position is stated outright, X and Y both. Terminal.Gui 2 lays
    /// subviews out by accumulating their positions, so a run that left its position
    /// to the flow lands wherever the runs before it ended: a flat list of runs draws a
    /// listing as a staircase, and a run per line draws the bytes of every row down the
    /// same column. One container per row and an absolute position per run is what makes
    /// the columns line up, which is the only thing a listing is for.
    /// </summary>
    internal sealed class ListingRowView : View
    {
        private readonly List<Label> _labels = new List<Label>();

        public ListingRowView()
        {
            CanFocus = false;
            Height = 1;
            Width = Dim.Fill();
            BorderStyle = LineStyle.None;
        }

        /// <summary>Throws away the runs of the previous row and draws this one's.</summary>
        public void Set(ListingRow row, ShellTheme theme)
        {
            for (int i = _labels.Count - 1; i >= 0; i--)
                Remove(_labels[i]);

            _labels.Clear();

            Add(Run(ListingFormatter.Gutter(row), theme, ThemeRole.Gutter, 0));

            if (row.Tokens.Count == 0)
            {
                if (row.Source.Length > 0)
                    Add(Run(row.Source, theme, ThemeRole.Default, ListingFormatter.GutterWidth));

                return;
            }

            foreach (ListingToken token in row.Tokens)
            {
                // Placed at the column the lexer put the run at, not one after the
                // previous run: the lexer emits no whitespace tokens, so laying the
                // runs end to end would close every gap in the source and draw
                // lda #$5A as lda#$5A.
                Add(Run(token.Text, theme, token.Role,
                    ListingFormatter.GutterWidth + token.Start));
            }
        }

        private static Label Run(string text, ShellTheme theme, string role, int column)
        {
            Label label = new Label
            {
                CanFocus = false,
                Height = 1,
                X = column,
                Y = 0,
                BorderStyle = LineStyle.None,
                Text = text ?? string.Empty,
            };

            label.SetAttribute(theme.Attribute(role));
            return label;
        }
    }

    /// <summary>
    /// The body of the listing pane: the rows, the role each run is painted with, and
    /// a cursor that can be moved through them.
    ///
    /// Only the rows that fit are built. A listing of a long source is a few thousand
    /// rows and the pane is a few dozen lines tall, so materialising all of them as
    /// views would cost a screenful to show a screenful. Scrolling rebuilds the window
    /// rather than moving it, from a pool that is reused rather than reallocated.
    /// </summary>
    public sealed class ListingView : View
    {
        private readonly ShellTheme _theme;
        private readonly List<ListingRow> _rows = new List<ListingRow>();
        private readonly List<ListingRowView> _screens = new List<ListingRowView>();
        private readonly Label _message;

        private int _firstRow;
        private int _cursor;

        public ListingView(ShellTheme theme)
        {
            _theme = theme ?? ShellTheme.Default;

            CanFocus = true;
            BorderStyle = LineStyle.None;
            Width = Dim.Fill();
            Height = Dim.Fill();
            ColorScheme = ShellColorScheme.Build(_theme, ColorSchemeRoles.Body);

            _message = new Label
            {
                CanFocus = false,
                Height = 1,
                BorderStyle = LineStyle.None,
                Y = 0,
                Text = string.Empty,
                Visible = false,
            };
            _message.SetAttribute(_theme.Attribute(ThemeRole.Warning));
            Add(_message);
        }

        /// <summary>The whole listing, in display order, including the rows scrolled off.</summary>
        public IReadOnlyList<ListingRow> Rows
        {
            get { return _rows; }
        }

        /// <summary>The first row on screen. Scrolling moves this, not the rows.</summary>
        public int FirstRow
        {
            get { return _firstRow; }
        }

        /// <summary>The row the cursor is on, within <see cref="Rows"/>.</summary>
        public int CursorRow
        {
            get { return _rows.Count == 0 ? 0 : _cursor; }
        }

        /// <summary>How many rows fit on screen at the current height.</summary>
        public int VisibleRowCount
        {
            get { return Math.Max(1, Frame.Height); }
        }

        /// <summary>
        /// Drawn instead of the rows, when there are none and a reason. Null otherwise.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// The address the cursor is on, or -1 when it is not on a row that emits bytes
        /// — a comment, a label, an origin, or nothing at all. -1 rather than 0,
        /// because $0000 is a real address and a breakpoint set on it by accident
        /// would be a breakpoint the user never asked for.
        /// </summary>
        public int CursorAddress
        {
            get
            {
                if (_rows.Count == 0)
                    return -1;

                ListingRow row = _rows[CursorRow];
                return row.EmitsBytes ? row.Address : -1;
            }
        }

        public void SetRows(IReadOnlyList<ListingRow> rows)
        {
            _rows.Clear();
            if (rows != null)
                _rows.AddRange(rows);

            _firstRow = 0;
            _cursor = 0;
            Clamp();
            Rebuild();
        }

        /// <summary>
        /// Puts a row at the top of the window, clamped to what exists.
        ///
        /// A cursor left outside the window is moved to the top of it, because the two
        /// cannot be allowed to disagree: an action asking for "the row under the
        /// cursor" has to get a row, and silently keeping a cursor that is not on
        /// screen is how a key press ends up refusing an action the user could see they
        /// were pointing at.
        /// </summary>
        public void ScrollTo(int firstRow)
        {
            _firstRow = Math.Max(0, Math.Min(firstRow, _rows.Count));

            if (_rows.Count > 0 && (_cursor < _firstRow || _cursor >= _firstRow + VisibleRowCount))
                _cursor = Math.Max(0, Math.Min(_firstRow, _rows.Count - 1));

            Clamp();
            Rebuild();
        }

        /// <summary>Moves the cursor, scrolling the window when it reaches an edge.</summary>
        public void MoveCursor(int delta)
        {
            if (_rows.Count == 0)
                return;

            _cursor = Math.Max(0, Math.Min(_cursor + delta, _rows.Count - 1));
            Clamp();
            Rebuild();
        }

        public void ScrollLines(int lines)
        {
            ScrollTo(_firstRow + lines);
        }

        public void PageUp()
        {
            ScrollTo(_firstRow - VisibleRowCount);
        }

        public void PageDown()
        {
            ScrollTo(_firstRow + VisibleRowCount);
        }

        public void ScrollToTop()
        {
            ScrollTo(0);
        }

        public void ScrollToBottom()
        {
            ScrollTo(_rows.Count);
        }

        /// <summary>
        /// The visible window as plain text, for tests. Uses the same formatter as the
        /// labels, so the string and the screen cannot disagree.
        /// </summary>
        public string VisibleText
        {
            get { return string.Join(Environment.NewLine, Lines(_firstRow, VisibleRowCount).ToArray()); }
        }

        /// <summary>The whole listing as plain text, whether or not it is on screen.</summary>
        public string ListingText
        {
            get { return string.Join(Environment.NewLine, Lines(0, _rows.Count).ToArray()); }
        }

        protected override bool OnKeyDown(Key key)
        {
            // Compared rather than switched on: the key names are properties, not
            // constants, so a case label would not compile.
            //
            // Scrolling is here rather than in the window so the pane is usable on its
            // own, and so a test can drive it by calling these directly instead of
            // needing the pane to hold focus first.
            if (key == Key.CursorUp)
            {
                MoveCursor(-1);
                return true;
            }

            if (key == Key.CursorDown)
            {
                MoveCursor(1);
                return true;
            }

            if (key == Key.PageUp)
            {
                PageUp();
                return true;
            }

            if (key == Key.PageDown)
            {
                PageDown();
                return true;
            }

            if (key == Key.Home)
            {
                ScrollToTop();
                MoveCursor(0);
                return true;
            }

            if (key == Key.End)
            {
                ScrollToBottom();
                MoveCursor(0);
                return true;
            }

            return base.OnKeyDown(key);
        }

        protected override void OnSubviewsLaidOut(LayoutEventArgs args)
        {
            base.OnSubviewsLaidOut(args);

            // A resize is the only thing that changes how many rows fit, and this runs
            // after the frame has moved. Only the window is rebuilt, never the rows:
            // rebuilding on every draw would fight the layout it is drawn into.
            Rebuild();
        }

        /// <summary>
        /// Puts the visible rows on screen, reusing the views of the rows that were
        /// already there. The pool only grows, and only by the height of one screen.
        /// </summary>
        private void Rebuild()
        {
            bool showingMessage = !string.IsNullOrEmpty(Message);

            _message.Visible = showingMessage;
            _message.Text = showingMessage ? Message : string.Empty;

            int height = showingMessage ? 0 : VisibleRowCount;
            int shown = Math.Min(height, _rows.Count - _firstRow);

            while (_screens.Count < shown)
            {
                ListingRowView screen = new ListingRowView();
                Add(screen);
                _screens.Add(screen);
            }

            for (int i = 0; i < _screens.Count; i++)
            {
                if (i >= shown)
                {
                    _screens[i].Visible = false;
                    continue;
                }

                _screens[i].X = 0;
                _screens[i].Y = i;
                _screens[i].Visible = true;
                _screens[i].Set(_rows[_firstRow + i], _theme);
            }

            SetNeedsDraw();
        }

        /// <summary>
        /// Keeps the window inside the listing and the cursor inside the window. Done
        /// here rather than where a scroll was asked for, because the height is only
        /// known once the frame has moved. The bottom of the window is the last row and
        /// not the row after it: a window whose top row is one past the end shows
        /// nothing, which reads as an empty listing.
        /// </summary>
        private void Clamp()
        {
            int height = VisibleRowCount;

            _firstRow = Math.Max(0, Math.Min(_firstRow, Math.Max(0, _rows.Count - height)));

            if (_rows.Count == 0)
            {
                _cursor = 0;
                return;
            }

            _cursor = Math.Max(0, Math.Min(_cursor, _rows.Count - 1));

            if (_cursor < _firstRow)
                _cursor = _firstRow;
            else if (_cursor >= _firstRow + height)
                _cursor = _firstRow + height - 1;
        }

        private List<string> Lines(int firstRow, int count)
        {
            List<string> lines = new List<string>();

            int last = Math.Min(_rows.Count, firstRow + count);
            for (int i = Math.Max(0, firstRow); i < last; i++)
                lines.Add(ListingFormatter.Row(_rows[i]));

            return lines;
        }
    }

    /// <summary>
    /// The listing pane's columns, in one place.
    ///
    /// Line, address, bytes, cycles, source. The widths are fixed rather than fitted
    /// to the terminal, because a listing is read down the address column: a pane that
    /// reflowed its hex when the window narrowed would stop being scannable, and the
    /// bytes are the reason the pane exists. Text that does not fit is cut at the right
    /// edge, which is what the terminal does to everything else too.
    /// </summary>
    public static class ListingFormatter
    {
        /// <summary>Cells before the source column starts.</summary>
        public const int GutterWidth = 32;

        private const int AddressWidth = 8;
        private const int BytesWidth = 11;
        private const int CyclesWidth = 5;

        /// <summary>The line number, address, bytes and cycles, as one padded run.</summary>
        public static string Gutter(ListingRow row)
        {
            // The address is shown whenever the row has one, which is not the same as
            // whenever it emits bytes: a label or a .org has an address worth seeing and
            // no bytes to put beside it, and hiding it would leave the reader unable to
            // tell where the next instruction lands.
            string address = row.Address >= 0
                ? "$" + row.Address.ToString("X4", CultureInfo.InvariantCulture) + "  "
                : new string(' ', AddressWidth);

            string cycles = row.Cycles.HasValue
                ? row.Cycles.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            return string.Format(CultureInfo.InvariantCulture,
                "{0,5} {1}{2}{3}  ",
                row.LineNumber,
                address,
                Bytes(row).PadRight(BytesWidth),
                cycles.PadLeft(CyclesWidth));
        }

        /// <summary>One whole row: the gutter then the source, as drawn.</summary>
        public static string Row(ListingRow row)
        {
            return Gutter(row) + (row.Source ?? string.Empty);
        }

        /// <summary>
        /// Up to three groups of three, then <c>+N</c>. A 6502 instruction is at most
        /// three bytes, so the continuation is rare, and saying how many are hidden
        /// rather than truncating keeps the byte column the same width for every row.
        /// </summary>
        public static string Bytes(ListingRow row)
        {
            if (!row.EmitsBytes)
                return new string(' ', BytesWidth);

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

    /// <summary>
    /// The listing pane: address, bytes, cycles and source, in the plan's fixed
    /// columns, and the honest reason when it has none of them.
    ///
    /// The pane holds a listing and paints it; it does not fetch. The window asks the
    /// seam and hands over rows, so a test can put a listing on screen without an
    /// assembler, and a failure can be shown with the words the assembler used rather
    /// than a paraphrase of them.
    /// </summary>
    public sealed class ListingPane : FrameView
    {
        private readonly ShellTheme _theme;
        private readonly IListingSource _source;
        private readonly ListingView _view;

        public ListingPane(ShellTheme theme, IListingSource source)
        {
            _theme = theme ?? ShellTheme.Default;
            _source = source ?? new UnavailableListingSource();

            Title = "listing";
            ColorScheme = ShellColorScheme.Build(_theme, ColorSchemeRoles.Chrome);

            _view = new ListingView(_theme);
            Add(_view);

            ShowUnavailable();
        }

        /// <summary>The rows on screen, for the window and for tests.</summary>
        public IReadOnlyList<ListingRow> Rows
        {
            get { return _view.Rows; }
        }

        /// <summary>The whole listing as plain text, for tests.</summary>
        public string Content
        {
            get { return _view.ListingText; }
        }

        /// <summary>The visible window as plain text, for tests.</summary>
        public string VisibleContent
        {
            get { return _view.VisibleText; }
        }

        /// <summary>The row the cursor is on.</summary>
        public int CursorRow
        {
            get { return _view.CursorRow; }
        }

        /// <summary>The address under the cursor, or -1 when there is none.</summary>
        public int CursorAddress
        {
            get { return _view.CursorAddress; }
        }

        /// <summary>Why the pane is showing the reason instead of rows, or null when it is not.</summary>
        public string Problem { get; private set; }

        public void MoveCursor(int delta)
        {
            _view.MoveCursor(delta);
        }

        public void ScrollLines(int lines)
        {
            _view.ScrollLines(lines);
        }

        public void ScrollTo(int firstRow)
        {
            _view.ScrollTo(firstRow);
        }

        /// <summary>
        /// Draws rows, using the fixed column widths from the specification.
        ///
        /// The cursor is put on the first row that emits bytes, because that is what
        /// F8 acts on: a cursor parked on a comment would refuse an action the user
        /// could plainly see they were pointing at.
        /// </summary>
        public void Show(IReadOnlyList<ListingRow> rows)
        {
            Problem = null;
            _view.Message = null;
            _view.SetRows(rows);

            if (rows != null)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].EmitsBytes)
                    {
                        _view.ScrollTo(i);
                        break;
                    }
                }
            }

            _view.SetNeedsDraw();
            SetNeedsDraw();
        }

        /// <summary>
        /// Draws the reason there is no listing. Kept as its own method so the pane has
        /// exactly one way to be empty, and so a test can assert the empty state
        /// without a terminal.
        /// </summary>
        public void ShowUnavailable()
        {
            string reason = _source == null ? null : _source.UnavailableReason;

            ShowProblem(string.IsNullOrEmpty(reason)
                ? "no listing: nothing has been listed yet."
                : reason);
        }

        /// <summary>
        /// Draws a failure that has a name: an assembly error, a missing file, a
        /// directory the process cannot read. The rows are cleared, because rows drawn
        /// beside the reason they could not be produced would be the fiction this
        /// project refuses everywhere else.
        /// </summary>
        public void ShowProblem(string reason)
        {
            Problem = string.IsNullOrEmpty(reason) ? "no listing." : reason;

            _view.Message = Problem;
            _view.SetRows(new ListingRow[0]);
            _view.ScrollTo(0);
            _view.SetNeedsDraw();
            SetNeedsDraw();
        }
    }
}