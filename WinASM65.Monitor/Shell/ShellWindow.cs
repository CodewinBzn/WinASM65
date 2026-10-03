using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using Terminal.Gui;
using WinASM65.Cpu;
using WinASM65.Monitor.Abstractions;
using WinASM65.Monitor.Protocol;
using WinASM65.Projects;

namespace WinASM65.Monitor.Shell
{
    /// <summary>One key the plan binds, whether this build binds it, and what it needs.</summary>
    public sealed class ShellKey
    {
        public ShellKey(string key, string description, bool bound)
            : this(key, description, bound, ExecutionCapability.None)
        {
        }

        /// <param name="key">How the key is written on the help screen.</param>
        /// <param name="description">What it does.</param>
        /// <param name="bound">Whether this build binds it at all.</param>
        /// <param name="requires">
        /// The capability the action needs from the attached machine, or
        /// <see cref="ExecutionCapability.None"/> when the shell can do it on its
        /// own.
        /// </param>
        public ShellKey(string key, string description, bool bound, ExecutionCapability requires)
        {
            Key = key;
            Description = description;
            Bound = bound;
            Requires = requires;
        }

        /// <summary>How the key is written on the help screen.</summary>
        public string Key { get; }

        public string Description { get; }

        /// <summary>
        /// False when the action belongs to a later milestone. The key is still
        /// listed: a user reading the plan's table should find out here that a key
        /// is not there, rather than press it and conclude the shell is broken.
        ///
        /// A property of this build and of nothing else. Whether the machine behind
        /// the shell can perform the action is <see cref="Requires"/>, which is
        /// about the attached backend; keeping the two apart is what lets the help
        /// screen distinguish a key this build never wrote from a key this machine
        /// cannot answer.
        /// </summary>
        public bool Bound { get; }

        /// <summary>
        /// The capability the attached machine must declare for this key to do
        /// anything. Exactly the bit behind the command the key issues — F9 resumes,
        /// so it needs <see cref="ExecutionCapability.Resume"/> and not "running",
        /// which no backend declares and every backend does some of.
        /// </summary>
        public ExecutionCapability Requires { get; }

        /// <summary>Whether <paramref name="machine"/> grants everything this key needs.</summary>
        public bool AvailableWhen(ExecutionCapability machine)
        {
            return ExecutionCapabilities.Has(machine, Requires);
        }
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
        /// <summary>
        /// Where the editor expects the first emitted byte to land.
        ///
        /// Stated, not applied. A source declares its own origin with <c>.org</c>, and
        /// the listing reports the address the assembler actually used — a pane that
        /// showed the origin it was asked for rather than the one the code was built
        /// at would be wrong for every source that does not agree with this number.
        /// The request carries it so the seam has one place to be told, and a future
        /// one that relocates has somewhere to start.
        /// </summary>
        public const int DefaultOrigin = 0x0800;

        /// <summary>
        /// Rows the listing pane will ever be asked for.
        ///
        /// A ceiling, not a target: a source that lists in fewer rows shows fewer. It
        /// exists so that assembling something enormous cannot turn one keystroke into
        /// a screenful per frame, and it is generous enough that a real source is
        /// never truncated by it.
        /// </summary>
        public const int MaxRows = 4000;

        private readonly MonitorSession _session;
        private readonly ShellTheme _theme;
        private readonly IListingSource _singleFileListing;
        private readonly BreakpointSet _breakpoints;
        private readonly string _directory;

        // Which listing source the pane is reading through, decided by what F5 last
        // did. Re-pointed rather than wrapped: the seam exists so that swapping the
        // implementation behind it is a one-line change, and a wrapper around both
        // would put the choice back inside the thing the choice was extracted from.
        private IListingSource _listing;

        // Held across builds so the pane keeps one object and only the knowledge
        // behind it changes. Null until a project has been built.
        private ProjectListingSource _projectListing;

        // What the machine behind the session says it can do, read once at
        // construction because a backend's capabilities are a fact about the build
        // it was measured on and do not change while the shell is open.
        private readonly ExecutionCapability _capabilities;

        private readonly StatusLine _status;
        private readonly Label _message;
        private readonly FileTreePane _tree;
        private readonly ListingPane _listingPane;
        private readonly RamPane _ram;
        private readonly Label _refusal;

        private readonly List<Shortcut> _shortcuts = new List<Shortcut>();
        private readonly List<string> _commands = new List<string>();

        private ShellLayout _layout;
        private bool _treeOpen = true;
        private bool _rightPaneOpen = true;
        private bool _ramOpen = true;

        /// <summary>
        /// The source the editor keys act on, as the session would be given it: a path
        /// relative to the directory the session was constructed with, or absolute.
        /// Null until a source has been opened, because opening one is an action the
        /// user takes and assuming one would mean F5 assembled whatever happened to be
        /// first in the tree.
        /// </summary>
        private string _sourceFile;

        public ShellWindow(MonitorSession session, ShellTheme theme, IListingSource listing, string directory)
        {
            if (session == null)
                throw new ArgumentNullException("session");

            _session = session;
            _theme = theme ?? ShellTheme.Default;
            _singleFileListing = listing ?? ListingSourceFactory.Create();
            _listing = _singleFileListing;
            _directory = directory;
            _capabilities = ExecutionCapabilities.Of(session.Backend);

            // A record, not a second actuator. The machine is told through the session
            // and nothing else, so the breakpoints are kept over a backend that does
            // not forward them: otherwise setting one through BREAK SET and then
            // recording it here would install the same callback twice on the emulator,
            // and BreakpointSet's own documentation is explicit that the second one is
            // invisible to the user and never released.
            _breakpoints = new BreakpointSet(new RecordOnlyBackend(session.Backend));

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

        /// <summary>
        /// Captures the machine into a named slot, and restores it back out of one.
        ///
        /// The slot lives here, on the shell's side, because <c>STATE SAVE</c> hands
        /// back hex and <c>STATE LOAD</c> takes hex: the bridge has no slot concept and
        /// never had one. Keeping the hex here is also what makes the round trip
        /// reachable from a key, and it stays reachable from the REPL by hand, because
        /// the two verbs and the hex between them are all the round trip ever was.
        /// </summary>
        private readonly Dictionary<string, string> _stateSlots =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The slots currently holding captured state, for the status line.</summary>
        public int CapturedStateCount
        {
            get { return _stateSlots.Count; }
        }

        /// <summary>
        /// Runs <c>STATE SAVE</c> and keeps the hex under <paramref name="slot"/>.
        /// Stores nothing, and says so, when the capture failed.
        /// </summary>
        public string SaveStateSlot(string slot)
        {
            string refusal = Refusal("F7", ExecutionCapability.StateSaveLoad);
            if (refusal != null)
                return refusal;

            string name = string.IsNullOrWhiteSpace(slot) ? EditorCommands.DefaultStateSlot : slot;

            IReadOnlyList<string> answer = Run(EditorCommands.SaveState(name));
            string hex = ExtractStateHex(answer);

            if (hex == null)
            {
                _stateSlots.Remove(name);
                return JoinAnswer(answer, "state not saved");
            }

            _stateSlots[name] = hex;
            return "state saved in slot " + name + " (" + (hex.Length / 2) + " bytes)";
        }

        /// <summary>
        /// Restores a slot through <c>STATE LOAD</c>. Reports honestly when the slot was
        /// never captured, rather than sending an empty hex to the machine.
        /// </summary>
        public string LoadStateSlot(string slot)
        {
            string refusal = Refusal("F7", ExecutionCapability.StateSaveLoad);
            if (refusal != null)
                return refusal;

            string name = string.IsNullOrWhiteSpace(slot) ? EditorCommands.DefaultStateSlot : slot;

            string hex;
            if (!_stateSlots.TryGetValue(name, out hex))
                return "no state in slot " + name + ": press F7 to capture one first";

            return JoinAnswer(Run(EditorCommands.LoadState(hex)), "state restored from slot " + name);
        }

        /// <summary>
        /// Pulls the hex out of an <c>OK &lt;hex&gt;</c> answer, or null when the answer
        /// is an error. The session answers <c>STATE SAVE</c> with the hex on a second
        /// token; anything else means there is nothing worth storing.
        /// </summary>
        private static string ExtractStateHex(IReadOnlyList<string> answer)
        {
            if (answer == null)
                return null;

            foreach (string line in answer)
            {
                if (line == null)
                    continue;

                string trimmed = line.Trim();
                if (trimmed.StartsWith(MonitorProtocol.ErrPrefix, StringComparison.Ordinal))
                    return null;

                string[] parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[0] == MonitorProtocol.OkPrefix)
                    return parts[1];
            }

            return null;
        }

        private static string JoinAnswer(IReadOnlyList<string> answer, string onSuccess)
        {
            if (answer == null || answer.Count == 0)
                return onSuccess;

            return string.Join(" ", new List<string>(answer).ToArray());
        }

        private void SaveCurrentState()
        {
            SetMessage(SaveStateSlot(null));
        }

        /// The F5 rule, as the help screen states it.
        ///
        /// One public constant so the screen and the tests read the same sentence. A
        /// rule the help screen paraphrases and the tests quote separately is two
        /// rules, and they drift on the day somebody tidies the wording.
        ///
        /// It has to be on the screen rather than in a comment because the alternative
        /// is a key that assembles one file in one directory and a whole project in
        /// another, with nothing on screen to say which just happened. The message
        /// line names the branch on every press; this line is what makes the branch
        /// predictable in advance.
        /// </summary>
        public const string ProjectRule =
            "F5 builds the project when a config.json in the open source's directory, or"
            + " in one above it up to the session directory, declares that source as an"
            + " input; otherwise it assembles that one file (BUILD config.json, or"
            + " ASSEMBLE <file>).";

        /// <summary>
        /// The keys this build binds, the ones a later milestone owns, and what each
        /// bound key needs from the machine behind the shell.
        ///
        /// A key that is not bound still says so, in this list and on the help screen.
        /// A user reading the plan's table of keys should find out here that one is not
        /// in this build, rather than press it and conclude the shell is broken.
        ///
        /// The third column is the other half of the same honesty, and it is not a
        /// property of this build: F9 is bound here whatever is attached, and on a
        /// machine that cannot resume it does nothing. Pressing it used to produce
        /// either silence or the bridge's own refusal, which is how a user found out
        /// that their emulator lacked an API. Now the table says so before the key is
        /// pressed, from the bit the backend declared.
        ///
        /// F12 and Ctrl+A are the outstanding ones: a video pane needs a renderer the
        /// shell does not have yet, and the assistant needs the provider contract.
        /// </summary>
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
                    new ShellKey("F5", "build: the project, or the one file", true),
                    new ShellKey("F7", "save state", true, ExecutionCapability.StateSaveLoad),
                    new ShellKey("F8", "toggle breakpoint", true, ExecutionCapability.BreakpointExecution),
                    new ShellKey("F9", "run", true, ExecutionCapability.Resume),
                    new ShellKey("F10", "step", true, ExecutionCapability.StepInstruction),
                    new ShellKey("F12", "video pane", false),
                    new ShellKey("Ctrl+A", "assistant", false),
                    new ShellKey("Ctrl+Q", "quit", true),
                };
            }
        }

        /// <summary>
        /// What the attached machine declared it can do — the flag set every key and
        /// every refusal in this window is decided against.
        ///
        /// Read once, at construction. A backend's capabilities are a fact about the
        /// build it was measured against, so they cannot change under a shell that is
        /// already open; re-reading them per keypress would only invite a machine to
        /// change its answer halfway through a session.
        /// </summary>
        public ExecutionCapability Capabilities
        {
            get { return _capabilities; }
        }

        /// <summary>
        /// The machine's name, as its handshake spelled it.
        ///
        /// Every refusal starts with it, because a refusal that does not say which
        /// machine refused is not something anybody can act on: the whole difference
        /// between F9 working and F9 doing nothing is what is on the other end of the
        /// bridge.
        /// </summary>
        public string MachineName
        {
            get
            {
                IExecutionAdapter adapter = _session.Backend as IExecutionAdapter;
                if (adapter != null && !string.IsNullOrWhiteSpace(adapter.DisplayName))
                    return adapter.DisplayName;

                string name = _session.Backend.EmulatorName;
                string version = _session.Backend.EmulatorVersion;

                if (string.IsNullOrWhiteSpace(name))
                    return null;

                return string.IsNullOrWhiteSpace(version) ? name : name + " " + version;
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
            AddShortcut(Key.F5, "Assemble", AssembleCurrentSource);
            AddShortcut(Key.F8, "Breakpoint", ToggleBreakpoint);
            AddShortcut(Key.F9, "Run", ResumeMachine);
            AddShortcut(Key.F10, "Step", StepMachine);
            AddShortcut(Key.F7, "SaveState", SaveCurrentState);
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

            // First, because the message line is one row tall and this is the line
            // worth seeing without pressing anything else: what this machine is, and
            // what it cannot be asked to do. The table below says which key that
            // affects; this says whether there is one.
            lines.Add(ShellCapabilities.MachineLine(MachineName, _capabilities));

            string missing = ExecutionCapabilities.MissingExecutionControl(_capabilities);
            if (missing != null)
                lines.Add("  " + missing);

            lines.Add("keys");

            foreach (ShellKey key in Keys)
                lines.Add("  " + key.Key.PadRight(7) + key.Description + Note(key));

            lines.Add(string.Empty);

            // The F5 rule, on the screen a user opens precisely to find out what a key
            // does. Two lines rather than a table, because it is one sentence of rule
            // and a line per source would be a help screen nobody reads.
            lines.Add(ProjectRule);
            lines.Add("  The message line names which of the two ran, every time.");

            lines.Add(string.Empty);
            lines.Add(_theme.DescribeSource());

            foreach (string problem in _theme.Diagnostics)
                lines.Add("  " + problem);

            SetMessage(string.Join(Environment.NewLine, lines.ToArray()));
        }

        /// <summary>
        /// The parenthetical a key carries on the help screen, or nothing.
        ///
        /// Two different reasons to say nothing useful, kept apart because they are
        /// two different facts: a key this build never wrote, and a key this build
        /// wrote for a machine that cannot answer it. The second one names the host
        /// and the measurement, because F1 is the screen a user opens precisely to
        /// find out why a key refused.
        /// </summary>
        private string Note(ShellKey key)
        {
            if (!key.Bound)
                return "  (not in this build)";

            if (key.AvailableWhen(_capabilities))
                return string.Empty;

            return "  " + ShellCapabilities.HelpNote(MachineName, key.Requires);
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

        /// <summary>
        /// The source the editor keys act on, as the session would be given it. Null
        /// until a source has been opened.
        /// </summary>
        public string SourceFile
        {
            get { return _sourceFile; }
        }

        /// <summary>The listing pane, for the editor keys and for tests.</summary>
        public ListingPane Listing
        {
            get { return _listingPane; }
        }

        /// <summary>
        /// The source the listing pane is reading through right now, which is the
        /// single-file adapter or the project one depending on what F5 last did.
        /// <para>
        /// Exposed because "which of the two" is the claim this window makes about
        /// itself, and a claim a test cannot read is a claim nobody can check. It is the
        /// same four-member seam the pane holds, so asking costs the pane nothing.
        /// </para>
        /// </summary>
        public IListingSource ListingSource
        {
            get { return _listing; }
        }

        /// <summary>
        /// The status line. Exposed because "STOPPED" versus "RUNNING" and the
        /// breakpoint count are the statements the editor keys make about the machine,
        /// and a claim the test cannot read is a claim nobody can check.
        /// </summary>
        public StatusLine Status
        {
            get { return _status; }
        }

        /// <summary>
        /// Every command line this window has issued into the session, oldest first.
        ///
        /// Recorded rather than reconstructed, because the invariant the class keeps is
        /// that every action is a command line: being able to name the last ones is
        /// what makes that claim checkable instead of a promise in a comment. Bounded,
        /// so a session left open for a day cannot grow the list without limit — a
        /// monitor is not an audit log, and the last hundred keystrokes are more than
        /// any user reads back.
        /// </summary>
        public IReadOnlyList<string> Commands
        {
            get { return _commands; }
        }

        /// <summary>
        /// Opens a source without acting on it, for the runner and for tests.
        ///
        /// The path is what the session was given as the working directory's, so the
        /// listing pane asks the seam for exactly the file <c>ASSEMBLE</c> would name.
        /// </summary>
        public void OpenSource(string sourceFile)
        {
            _sourceFile = string.IsNullOrEmpty(sourceFile) ? null : sourceFile;
        }

        /// <summary>
        /// F5. Builds the project the open source belongs to, or assembles that one
        /// file.
        ///
        /// <para>
        /// The rule is stated on the help screen and repeated on the message line
        /// after every press, because a key that quietly changes what it does
        /// depending on where a file happens to sit is the one thing a user cannot
        /// debug. <see cref="ProjectConfigLocator"/> decides it — a
        /// <c>config.json</c> governs the open source when it declares that source as
        /// one of its inputs — and the branch is one command line either way:
        /// <c>BUILD config.json</c> or <c>ASSEMBLE &lt;file&gt;</c>. Nothing here calls
        /// the assembler or the project session itself, so the REPL can do either, and
        /// even the refusal of a configuration that cannot be read is the session's
        /// answer rather than a second one invented here.
        /// </para>
        ///
        /// <para>
        /// The order afterwards is the same for both branches, and for the same reason
        /// as before: the listing comes from a run that just happened, so the pane
        /// cannot be one assembly behind the source. A refused build is given to the
        /// pane rather than the rows it would otherwise have shown — rows drawn beside
        /// the reason they could not be produced are the fiction this project refuses
        /// everywhere else.
        /// </para>
        /// </summary>
        public void AssembleCurrentSource()
        {
            if (_sourceFile == null)
            {
                SetMessage("no source is open. F2 focuses the file tree; pick a .asm and"
                    + " F5 assembles it.");
                return;
            }

            ProjectGovernance project = ProjectConfigLocator.Locate(_sourceFile, _directory);

            if (!project.Governed)
            {
                AssembleSingleFile();
                return;
            }

            BuildProject(project.Configuration);
        }

        /// <summary>The no-project branch of F5: exactly what it always issued.</summary>
        private void AssembleSingleFile()
        {
            IReadOnlyList<string> answer = Run(EditorCommands.Assemble(_sourceFile));

            if (Failed(answer))
            {
                ShowFailure(answer);
                return;
            }

            _listing = _singleFileListing;
            SetMessage(Said(answer) + " -- one file: no config.json declares it.");
            ReloadListing();
            RefreshPanes();
        }

        /// <summary>
        /// The project branch of F5. The answer is the build's own, with the branch
        /// named at the end, so a user reading the message line knows which of the two
        /// things F5 just did before they read anything else.
        /// </summary>
        private void BuildProject(string configuration)
        {
            IReadOnlyList<string> answer = Run(EditorCommands.Build(configuration));

            if (Failed(answer))
            {
                ShowFailure(answer);
                return;
            }

            ProjectSession session = _session.Projects.LastSession;

            if (session != null)
            {
                ProjectListingSource project = _projectListing;
                if (project == null)
                {
                    project = new ProjectListingSource(Adapter(), _directory, session, configuration);
                    _projectListing = project;
                }
                else
                {
                    project.Use(session, configuration);
                }

                _listing = project;
            }

            SetMessage(Said(answer) + " -- project " + configuration
                + ". Load the image in the emulator; this shell has no verb that pushes"
                + " one into a running machine.");
            ReloadListing();
            RefreshPanes();
        }

        /// <summary>
        /// The row translation the project source reuses, rather than a second one.
        ///
        /// The adapter this window was built with is normally already the single-file
        /// one, which is what carries the CPU's opcode table and the lexer — the two
        /// things that decide what a pane paints a row as. When the caller handed over
        /// some other <see cref="IListingSource"/> there is no adapter to borrow, and a
        /// default-CPU one is built: a project listing then reports the standard 6502's
        /// mnemonics and forms, which is what the factory's own source would have
        /// reported too. A stub listing source has no CPU to be consistent with, so
        /// there is nothing better to pick.
        /// </summary>
        private AssemblerListingSource Adapter()
        {
            return _singleFileListing as AssemblerListingSource
                ?? new AssemblerListingSource(CpuFactory.Create("6502"), _directory);
        }

        /// <summary>
        /// A refused command, in the pane and in the message line. Both get the whole
        /// answer: a project failure is several diagnostics, and the message row and
        /// the pane are two places to show them rather than one.
        /// </summary>
        private void ShowFailure(IReadOnlyList<string> answer)
        {
            SetMessage(Said(answer));
            _listingPane.ShowProblem(answer == null || answer.Count == 0
                ? "the command was refused without saying why."
                : string.Join(Environment.NewLine, new List<string>(answer).ToArray()));
            RefreshPanes();
        }

        /// <summary>The answer as one line, which is what the message row can show.</summary>
        private static string Said(IReadOnlyList<string> answer)
        {
            return answer == null || answer.Count == 0
                ? string.Empty
                : string.Join(" ", new List<string>(answer).ToArray());
        }

        /// <summary>
        /// Re-lists the open source through the seam.
        ///
        /// The reason the listing pane cannot produce is shown in the pane rather than
        /// only in the message line: a user looking at the editor pane is looking at
        /// the pane, and the message line is one keypress from being overwritten.
        /// </summary>
        public void ReloadListing()
        {
            if (_sourceFile == null)
                return;

            if (!_listing.IsAvailable)
            {
                _listingPane.ShowUnavailable();
                return;
            }

            IReadOnlyList<ListingRow> rows =
                _listing.Rows(new ListingRequest(_sourceFile, DefaultOrigin, MaxRows));

            if (rows.Count == 0)
            {
                // LastProblem, not UnavailableReason. A file that fails to assemble is
                // not a missing feature, and answering "the listing API is not in this
                // build" to someone whose source has an undefined symbol on line 536
                // sends them to look for a problem that does not exist.
                _listingPane.ShowProblem(
                    string.IsNullOrEmpty(_listing.LastProblem) ? _listing.UnavailableReason : _listing.LastProblem);
                return;
            }

            _listingPane.Show(rows);
        }

        /// <summary>
        /// F8. Toggles an exec breakpoint at the address under the cursor.
        ///
        /// Setting it is one command line, <c>BREAK SET exec $addr</c>. Clearing it is
        /// not, and that asymmetry is the session's, not this method's: Mesen exposes
        /// no way to release a single callback, so <c>BREAK REMOVE</c> is refused in
        /// favour of <c>BREAK CLEAR</c>. A REPL user who wanted one breakpoint gone
        /// types exactly what follows — clear, then set each one still wanted — so the
        /// shell types it too rather than reaching around the session to do something
        /// no command line could express.
        /// </summary>
        public void ToggleBreakpoint()
        {
            if (Refusal("F8", ExecutionCapability.BreakpointExecution) != null)
                return;

            int address = _listingPane.CursorAddress;
            if (address < 0)
            {
                SetMessage("the cursor is not on an address: move it to a line that emits"
                    + " bytes before setting a breakpoint.");
                return;
            }

            const string Kind = BreakpointKind.Exec;
            bool wasSet = _breakpoints.Contains(address, Kind);

            List<string> report = new List<string>();

            if (wasSet)
            {
                report.AddRange(Run(EditorCommands.ClearBreakpoints()));

                foreach (Breakpoint wanted in _breakpoints.Items)
                {
                    if (wanted.Address == address)
                        continue;

                    report.AddRange(Run(EditorCommands.SetBreakpoint(wanted.Kind, wanted.Address)));
                }
            }
            else
            {
                report.AddRange(Run(EditorCommands.SetBreakpoint(Kind, address)));
            }

            if (Failed(report))
            {
                SetMessage(string.Join(" ", report.ToArray()));
                RefreshPanes();
                return;
            }

            // Recorded only now, from an answer that succeeded. A refusal leaves no
            // entry, so the status line cannot advertise a breakpoint the emulator
            // never took.
            if (wasSet)
                _breakpoints.Remove(address, Kind);
            else
                _breakpoints.Add(address, Kind);

            SetMessage((wasSet ? "breakpoint cleared at " : "breakpoint set at ")
                + EditorCommands.Hex(address));
            RefreshPanes();
        }

        /// <summary>F9. Runs the machine, and the status line says so afterwards.</summary>
        public void ResumeMachine()
        {
            if (Refusal("F9", ExecutionCapability.Resume) != null)
                return;

            Report(Run(EditorCommands.Resume()));
        }

        /// <summary>F10. Advances one instruction.</summary>
        public void StepMachine()
        {
            if (Refusal("F10", ExecutionCapability.StepInstruction) != null)
                return;

            Report(Run(EditorCommands.Step()));
        }

        /// <summary>
        /// The refusal this machine cannot do what <paramref name="key"/> asks, or
        /// null when it can — in which case the message line says it by name.
        ///
        /// The gate lives in the action rather than in <see cref="BindKeys"/>, and
        /// that is the whole point of it. A binding decides which key reaches a
        /// handler; only the handler knows what the machine on the other end can
        /// actually do. Gating in BindKeys would leave F9 bound to a handler that
        /// runs the command anyway the moment anything else calls it — the REPL
        /// tests, a future menu, the key handler itself.
        ///
        /// Checked before anything else the action could report, including the
        /// cursor's own complaint in F8: a machine that cannot break at all has no
        /// business being told that the cursor is in the wrong place.
        ///
        /// Issues nothing on refusal. A refused key that had already sent
        /// <c>STATE SAVE</c> to a machine with no snapshots would be the exact
        /// capability-the-user-already-pressed failure the flag set exists to avoid.
        /// </summary>
        private string Refusal(string key, ExecutionCapability requires)
        {
            if (ShellCapabilities.Allows(_capabilities, requires))
                return null;

            string text = ShellCapabilities.Refusal(key, MachineName, requires);
            SetMessage(text);
            return text;
        }

        /// <summary>
        /// Shows what a command answered and re-reads the panes, which is what keeps
        /// the status line honest: "RUNNING" has to come from a fresh reading of the
        /// machine, not from the keypress that asked for it.
        /// </summary>
        private void Report(IReadOnlyList<string> answer)
        {
            SetMessage(string.Join(" ", new List<string>(answer).ToArray()));
            RefreshPanes();
        }

        /// <summary>
        /// Whether the session refused. Read from the answers, not from an exception:
        /// the session reports a bad command as an <c>ERR</c> line precisely so that
        /// it stays usable, and a key handler that only caught exceptions would report
        /// success for every one of them.
        /// </summary>
        private static bool Failed(IReadOnlyList<string> answer)
        {
            if (answer == null)
                return false;

            for (int i = 0; i < answer.Count; i++)
            {
                string line = answer[i];
                if (line != null && line.StartsWith(MonitorProtocol.ErrPrefix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// What a breakpoint record is allowed to do to the machine: nothing.
        ///
        /// Everything else is forwarded, because the record is the same backend for
        /// every other purpose — the RAM pane and the status line read the real
        /// machine, and only the two breakpoint calls are turned away. That they are
        /// the two is the whole point: they are the two that would install or release
        /// a callback a second time, and the session has already done that work.
        /// </summary>
        private sealed class RecordOnlyBackend : IMemoryBackend
        {
            private readonly IMemoryBackend _inner;

            public RecordOnlyBackend(IMemoryBackend inner)
            {
                if (inner == null)
                    throw new ArgumentNullException("inner");

                _inner = inner;
            }

            public string EmulatorName
            {
                get { return _inner.EmulatorName; }
            }

            public string EmulatorVersion
            {
                get { return _inner.EmulatorVersion; }
            }

            public bool IsRunning
            {
                get { return _inner.IsRunning; }
            }

            public byte[] Read(int address, int length)
            {
                return _inner.Read(address, length);
            }

            public void Write(int address, byte[] bytes)
            {
                _inner.Write(address, bytes);
            }

            public void Pause()
            {
                _inner.Pause();
            }

            public void Resume()
            {
                _inner.Resume();
            }

            public void Step()
            {
                _inner.Step();
            }

            public void Reset()
            {
                _inner.Reset();
            }

            public void AddBreakpoint(int address, string kind)
            {
            }

            public void ClearBreakpoints()
            {
            }

            public byte[] SaveState()
            {
                return _inner.SaveState();
            }

            public void LoadState(byte[] state)
            {
                _inner.LoadState(state);
            }

            public void Dispose()
            {
                // The inner backend belongs to the session, which disposes it. A record
                // that disposed the machine it is only recording would take the
                // emulator down with the shell.
            }
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
        /// Acts on a tree row. A source is opened and assembled through the session,
        /// which is the same call the REPL makes, so the file tree cannot mean
        /// something else than <c>ASSEMBLE</c> does.
        /// </summary>
        private void OnTreeActivated(object sender, FileTreeRow row)
        {
            if (row == null || row.Kind != FileTreeKind.Source || string.IsNullOrEmpty(row.Value))
            {
                SetMessage("that is not a source file");
                return;
            }

            // Opened before it is assembled, so the editor keys that follow act on the
            // source the user just chose rather than on whatever was open before.
            OpenSource(row.Value);
            AssembleCurrentSource();
        }

        /// <summary>
        /// Runs one session command and reports it. Every failure is a string, never
        /// an exception: a pane that could take the shell down on a bad path would
        /// make the shell worse than the REPL it replaces.
        /// </summary>
        public IReadOnlyList<string> Run(string commandLine)
        {
            Remember(commandLine);

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

        /// <summary>
        /// Keeps the last hundred command lines. A null line is not remembered: there
        /// was no action to record, and an entry that claimed otherwise would make the
        /// list a worse witness than nothing.
        /// </summary>
        private void Remember(string commandLine)
        {
            if (commandLine == null)
                return;

            const int Remembered = 100;
            _commands.Add(commandLine);

            while (_commands.Count > Remembered)
                _commands.RemoveAt(0);
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