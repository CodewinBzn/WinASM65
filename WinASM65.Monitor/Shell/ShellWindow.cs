using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using Terminal.Gui;
using WinASM65.Monitor.Protocol;

namespace WinASM65.Monitor.Shell
{
    /// <summary>One key the plan binds, and whether this build binds it.</summary>
    public sealed class ShellKey
    {
        public ShellKey(string key, string description, bool bound)
        {
            Key = key;
            Description = description;
            Bound = bound;
        }

        /// <summary>How the key is written on the help screen.</summary>
        public string Key { get; }

        public string Description { get; }

        /// <summary>
        /// False when the action belongs to a later milestone. The key is still
        /// listed: a user reading the plan's table should find out here that a key
        /// is not there, rather than press it and conclude the shell is broken.
        /// </summary>
        public bool Bound { get; }
    }

    /// <summary>
    /// The window: panes, status line, keys, and the rule that says which pane is
    /// where.
    ///
    /// Every action here is a typed line into <see cref="MonitorSession"/>, so
    /// nothing the shell can do is unreachable from the REPL. That is what keeps
    /// the TUI honest: the TUI adds no behaviour the REPL cannot express.
    ///
    /// The layout decision itself is not made in this class. <see cref="ShellLayout"/>
    /// decides it, purely, from a width and a height; this class only puts panes
    /// where it is told.
    /// </summary>
    public sealed class ShellWindow : Toplevel
    {
        private readonly MonitorSession _session;
        private readonly ShellTheme _theme;
        private readonly IListingSource _listing;
        private readonly BreakpointSet _breakpoints;
        private readonly string _directory;

        private readonly StatusLine _status;
        private readonly Label _message;
        private readonly FileTreePane _tree;
        private readonly ListingPane _listingPane;
        private readonly RamPane _ram;
        private readonly Label _refusal;

        private readonly List<Shortcut> _shortcuts = new List<Shortcut>();

        private ShellLayout _layout;
        private bool _treeOpen = true;
        private bool _rightPaneOpen = true;
        private bool _ramOpen = true;

        public ShellWindow(MonitorSession session, ShellTheme theme, IListingSource listing, string directory)
        {
            if (session == null)
                throw new ArgumentNullException("session");

            _session = session;
            _theme = theme ?? ShellTheme.Default;
            _listing = listing ?? new UnavailableListingSource();
            _directory = directory;
            _breakpoints = new BreakpointSet(session.Backend);

            Title = "WinASM65 monitor";
            ColorScheme = ShellColorScheme.Build(_theme, ColorSchemeRoles.Chrome);

            _tree = new FileTreePane(_theme);
            _listingPane = new ListingPane(_theme, _listing);
            _ram = new RamPane(_theme);
            _tree.Activated += OnTreeActivated;

            _status = new StatusLine(_theme) { Y = 0, X = 0, Width = Dim.Fill(), Height = 1 };
            _message = new Label
            {
                Y = 1,
                X = 0,
                Width = Dim.Fill(),
                Height = 1,
                CanFocus = false,
                ColorScheme = ShellColorScheme.Build(_theme, ColorSchemeRoles.Warning),
                Text = string.Empty,
            };

            _refusal = new Label
            {
                CanFocus = false,
                Visible = false,
                ColorScheme = ShellColorScheme.Build(_theme, ColorSchemeRoles.Warning),
            };

            Add(_tree);
            Add(_listingPane);
            Add(_ram);
            Add(_refusal);
            Add(_status);
            Add(_message);

            BindKeys();
        }

        /// <summary>The theme in force, for the status line and the help screen.</summary>
        public ShellTheme Theme
        {
            get { return _theme; }
        }

        /// <summary>The breakpoints the monitor believes it set.</summary>
        public BreakpointSet Breakpoints
        {
            get { return _breakpoints; }
        }

        /// <summary>The decision currently in force.</summary>
        public ShellLayout CurrentLayout
        {
            get { return _layout; }
        }

        /// <summary>The keys this build binds, and the ones a later milestone owns.</summary>
        public static IReadOnlyList<ShellKey> Keys
        {
            get
            {
                return new List<ShellKey>
                {
                    new ShellKey("F1", "this help", true),
                    new ShellKey("F2", "file tree", true),
                    new ShellKey("F3", "listing pane", true),
                    new ShellKey("F4", "RAM pane", true),
                    new ShellKey("F5", "assemble under the cursor", false),
                    new ShellKey("F7", "save state", false),
                    new ShellKey("F8", "toggle breakpoint", false),
                    new ShellKey("F9", "run", false),
                    new ShellKey("F10", "step", false),
                    new ShellKey("F12", "video pane", false),
                    new ShellKey("Ctrl+A", "assistant", false),
                    new ShellKey("Ctrl+Q", "quit", true),
                };
            }
        }

        /// <summary>
        /// Puts the panes where the layout says, and reads whatever the mode needs
        /// read. Idempotent, and callable with any size, so a test can assert the
        /// breakpoints without resizing a terminal.
        /// </summary>
        public void ApplyLayout(ShellLayout layout)
        {
            if (layout == null)
                throw new ArgumentNullException("layout");

            _layout = layout;

            if (layout.IsRefused)
            {
                ShowRefusal(layout);
                return;
            }

            _refusal.Visible = false;
            _listingPane.Visible = true;
            _status.Visible = true;
            _message.Visible = true;

            int bodyTop = 2;
            int bodyHeight = layout.Height - ShellLayout.StatusLines;
            if (bodyHeight < 1)
                bodyHeight = 1;

            int left = 0;
            int right = layout.Width;
            int bodyWidth = right - left;

            bool showTreeColumn = layout.TreeIsColumn;
            bool showRightColumn = layout.RightPaneIsColumn && _rightPaneOpen;

            if (showTreeColumn && _treeOpen)
            {
                _tree.Visible = true;
                _tree.Frame = new Rectangle(0, bodyTop, layout.TreeWidth, bodyHeight);
                left = layout.TreeWidth;
                bodyWidth = right - left;
            }
            else if (layout.Mode == ShellLayoutMode.CollapsedTree)
            {
                // The drawer keeps a sliver of the tree so a pane the user closed
                // can be reopened from the keyboard without a layout jump.
                _tree.Visible = _treeOpen;
                _tree.Frame = new Rectangle(0, bodyTop, ShellLayout.CollapsedTreeWidth, bodyHeight);
                left = ShellLayout.CollapsedTreeWidth;
                bodyWidth = right - left;
            }
            else
            {
                _tree.Visible = false;
            }

            if (showRightColumn)
            {
                _ram.Visible = _ramOpen;
                _ram.Frame = new Rectangle(right - layout.RightPaneWidth, bodyTop, layout.RightPaneWidth, bodyHeight);
                bodyWidth = right - layout.RightPaneWidth - left;
            }
            else
            {
                _ram.Visible = false;
            }

            if (bodyWidth < 1)
                bodyWidth = 1;

            // The listing pane gets whatever is left. This is the Toplevel's own
            // Frame that belongs to the terminal, not to us: writing to it would
            // fight the layout system over the size of the whole window.
            _listingPane.Frame = new Rectangle(left, bodyTop, bodyWidth, bodyHeight);

            _status.Frame = new Rectangle(0, 0, layout.Width, 1);
            _message.Frame = new Rectangle(0, 1, layout.Width, 1);

            RefreshPanes();
        }

        /// <summary>Re-reads the tree, the RAM window and the status line.</summary>
        public void RefreshPanes()
        {
            if (_layout != null && _layout.IsRefused)
                return;

            string problem;
            IReadOnlyList<FileTreeRow> rows = FileTreeModel.Rows(_directory, _session.Library, out problem);
            _tree.SetRows(rows, problem);

            _ram.Refresh(delegate (int address, int length)
            {
                return _session.Backend.Read(address, length);
            });

            _status.Update(ShellStatus.Read(_session.Backend, _breakpoints.Count), _layout == null ? 0 : _layout.Width);
            _status.SetNeedsDraw();
            _message.SetNeedsDraw();
        }

        /// <summary>The line under the status bar, for the result of the last action.</summary>
        public string Message
        {
            get { return _message.Text ?? string.Empty; }
        }

        private void ShowRefusal(ShellLayout layout)
        {
            _tree.Visible = false;
            _listingPane.Visible = false;
            _ram.Visible = false;
            _status.Visible = false;
            _message.Visible = false;

            List<string> lines = new List<string>(layout.RefusalLines());
            _refusal.Text = string.Join(Environment.NewLine, lines.ToArray());
            _refusal.Visible = true;
            _refusal.Frame = new Rectangle(0, 0, Math.Max(1, layout.Width), Math.Max(1, layout.Height));
            _refusal.SetNeedsDraw();
        }

        private void BindKeys()
        {
            AddShortcut(Key.F1, "Help", ShowHelp);
            AddShortcut(Key.F2, "Tree", ToggleTree);
            AddShortcut(Key.F3, "Listing", ToggleRightPane);
            AddShortcut(Key.F4, "RAM", ToggleRam);
            AddShortcut(CtrlQ(), "Quit", RequestQuit);
        }

        /// <summary>
        /// Ctrl+Q. A key is a code plus modifier bits, and the library exposes no
        /// combining helper, so the bits are set here. Quitting is deliberately not
        /// plain Q: a user reading a hex dump types Q more than once by accident,
        /// and a shell that exits on it loses the session.
        /// </summary>
        internal static Key CtrlQ()
        {
            return (Key)((uint)Key.Q | (uint)KeyCode.CtrlMask);
        }

        private void AddShortcut(Key key, string text, Action action)
        {
            Shortcut shortcut = new Shortcut(key, text, action, text + " pane");
            shortcut.BindKeyToApplication = true;
            shortcut.CanFocus = false;
            _shortcuts.Add(shortcut);
            Add(shortcut);
        }

        private void ShowHelp()
        {
            List<string> lines = new List<string>();
            lines.Add("keys");

            foreach (ShellKey key in Keys)
                lines.Add("  " + key.Key.PadRight(7) + key.Description + (key.Bound ? string.Empty : "  (not in this build)"));

            lines.Add(string.Empty);
            lines.Add(_theme.DescribeSource());

            foreach (string problem in _theme.Diagnostics)
                lines.Add("  " + problem);

            SetMessage(string.Join(Environment.NewLine, lines.ToArray()));
        }

        private void ToggleTree()
        {
            _treeOpen = !_treeOpen;
            Relayout();
            SetMessage("file tree " + (_treeOpen ? "open" : "closed"));
        }

        private void ToggleRightPane()
        {
            _rightPaneOpen = !_rightPaneOpen;
            Relayout();
            SetMessage("listing pane " + (_rightPaneOpen ? "open" : "closed"));
        }

        private void ToggleRam()
        {
            _ramOpen = !_ramOpen;
            Relayout();
            SetMessage("RAM pane " + (_ramOpen ? "open" : "closed"));
        }

        private void RequestQuit()
        {
            Application.RequestStop(this);
        }

        private void Relayout()
        {
            if (_layout != null)
                ApplyLayout(_layout);
        }

        private void SetMessage(string message)
        {
            _message.Text = message ?? string.Empty;
            _message.SetNeedsDraw();
        }

        /// <summary>
        /// Acts on a tree row. A source is assembled through the session, which is
        /// the same call the REPL makes, so the file tree cannot mean something else
        /// than <c>ASSEMBLE</c> does.
        /// </summary>
        private void OnTreeActivated(object sender, FileTreeRow row)
        {
            if (row == null || row.Kind != FileTreeKind.Source || string.IsNullOrEmpty(row.Value))
            {
                SetMessage("that is not a source file");
                return;
            }

            string result = string.Join(" ", new List<string>(Run(row.Value)).ToArray());
            SetMessage(result);
            RefreshPanes();
        }

        /// <summary>
        /// Runs one session command and reports it. Every failure is a string, never
        /// an exception: a pane that could take the shell down on a bad path would
        /// make the shell worse than the REPL it replaces.
        /// </summary>
        public IReadOnlyList<string> Run(string commandLine)
        {
            try
            {
                return _session.Execute(commandLine);
            }
            catch (MonitorException ex)
            {
                return new List<string> { MonitorProtocol.ErrPrefix + " " + ex.Message };
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return new List<string> { MonitorProtocol.ErrPrefix + " bad input: " + ex.Message };
            }
            catch (FormatException ex)
            {
                return new List<string> { MonitorProtocol.ErrPrefix + " bad input: " + ex.Message };
            }
            catch (IOException ex)
            {
                return new List<string> { MonitorProtocol.ErrPrefix + " " + ex.Message };
            }
        }

        /// <summary>
        /// The tree row that activating would act on, for tests that drive the pane
        /// without a terminal.
        /// </summary>
        public void ActivateRow(FileTreeRow row)
        {
            OnTreeActivated(this, row);
        }

        /// <summary>Sets the message line, for tests and for the runner's banner.</summary>
        public void PostMessage(string message)
        {
            SetMessage(message);
        }

        /// <summary>Hex, for a pane title that names an address.</summary>
        public static string Hex(int value)
        {
            return "$" + value.ToString("X4", CultureInfo.InvariantCulture);
        }
    }
}