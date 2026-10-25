using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Events;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Layout;
using WinASM65.Cpu;
using WinASM65.Monitor.Abstractions;
using WinASM65.Monitor.Protocol;
using WinASM65.Projects;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// The window: panes, status line, keys, and the rule that says which pane is
    /// where.
    ///
    /// Every action here is a typed line into MonitorSession, so nothing the shell
    /// can do is unreachable from the REPL. That is what keeps the TUI honest: the
    /// TUI adds no behaviour the REPL cannot express.
    ///
    /// The layout decision itself is not made in this class. ShellLayout decides
    /// it, purely, from a width and a height; this class only puts panes where it
    /// is told.
    /// </summary>
    public sealed class ShellWindow : IDisposable
    {
        public const int DefaultOrigin = 0x0800;
        public const int MaxRows = 4000;

        private readonly MonitorSession _session;
        private readonly ShellTheme _theme;
        private readonly IListingSource _singleFileListing;
        private readonly string _directory;
        private IListingSource _listing;
        private readonly ExecutionCapability _capabilities;
        private readonly StatusLine _status;
        private readonly MarkupControl _message;
        private readonly FileTreePane _tree;
        private readonly ListingPane _listingPane;
        private readonly SourceEditorTabs _sourceEditors;
        private readonly ToolbarControl _activityToolbar;
        private readonly SpinnerControl _loadingSpinner;
        private readonly RamPane _ram;
        private readonly RomPane _rom;
        private readonly MarkupControl _refusal;
        private readonly List<Shortcut> _shortcuts = new List<Shortcut>();
        private readonly List<string> _commands = new List<string>();
        private readonly List<int> _breakpoints = new List<int>();
        private ShellLayout _layout;
        private bool _treeOpen = true;
        private bool _rightPaneOpen = true;
        private bool _ramOpen = true;
        private bool _romOpen = true;
        private readonly Window _mainWindow;
        private readonly Window _treeWindow;
        private readonly Window _editorWindow;
        private readonly Window _ramWindow;
        private readonly Window _romWindow;
        private readonly Window _refusalWindow;
        private readonly ConsoleWindowSystem _windowSystem;
        private string _sourceFile;
        private string _messageText = string.Empty;
        private int _sourceLoadVersion;
        private string _lastBuiltRomPath;
        private DesktopPortal _contextMenuPortal;
        private readonly Action<IReadOnlyList<(int Address, byte Value)>> _ramChangedHandler;
        private readonly object _ramUpdateLock = new object();
        private readonly Dictionary<int, byte> _pendingRamChanges = new Dictionary<int, byte>();
        private bool _ramUpdateQueued;
        private bool _disposed;
        private bool _actionInProgress;
        private int _actionGeneration;

        public ShellWindow(MonitorSession session, ShellTheme theme, IListingSource listing, string directory, ConsoleWindowSystem windowSystem)
        {
            if (session == null) throw new ArgumentNullException("session");
            _session = session;
            _theme = theme ?? ShellTheme.Default;
            _windowSystem = windowSystem;
            _singleFileListing = listing ?? new AssemblerListingSource(new Cpu6502(), directory);
            _listing = _singleFileListing;
            _directory = directory;
            _capabilities = ExecutionCapabilities.Of(session.Backend);

            _tree = new FileTreePane(_theme);
            _listingPane = new ListingPane(_theme, _listing);
            AssemblerListingSource source = _singleFileListing as AssemblerListingSource
                ?? new AssemblerListingSource(new Cpu6502(), directory);
            _sourceEditors = new SourceEditorTabs(_theme, source, _directory, windowSystem);
            _sourceEditors.ActiveChanged += OnActiveEditorChanged;
            _loadingSpinner = new SpinnerControl { IsSpinning = false, Width = 1 };
            _ram = new RamPane(_theme);
            _rom = new RomPane(_theme);
            _tree.Activated += (sender, row) => OnTreeActivated(row);
            _tree.ContextRequested += OnTreeContextRequested;
            _tree.TreeContextRequested += OnTreeContextRequested;
            _sourceEditors.Control.MouseRightClick += OnEditorContextRequested;
            _ramChangedHandler = OnRamChanged;
            _session.Backend.RamChanged += _ramChangedHandler;
            _status = new StatusLine(_theme);
            _message = new MarkupControl(new List<string> { string.Empty })
            {
                Width = null,
                Height = 1,
                ForegroundColor = _theme.Foreground(ThemeRole.Warning),
            };
            _activityToolbar = Controls.Toolbar()
                .Add(_loadingSpinner)
                .Add(_message)
                .WithHeight(1)
                .WithSpacing(1)
                .WithWrap(false)
                .Build();
            _refusal = new MarkupControl(new List<string> { string.Empty })
            {
                // MarkupControl does not have a Visible property; the refusal window is hidden by being positioned off‑screen.
                ForegroundColor = _theme.Foreground(ThemeRole.Warning),
                Width = null,
                Height = null,
            };

            _mainWindow = new Window(windowSystem)
            {
                Title = "WinASM65 monitor",
                Left = 0,
                Top = 0,
                Width = 120,
                Height = 2,
                IsResizable = false,
                IsMovable = false,
                IsMaximizable = false,
                IsMinimizable = false,
                IsClosable = false,
                ShowTitle = true,
                ShowInTaskbar = false,
            };
            _mainWindow.AddControl(_status.Control);
            _mainWindow.AddControl(_activityToolbar);

            _treeWindow = new Window(windowSystem)
            {
                Title = "FILES",
                Left = 0,
                Top = 2,
                Width = 28,
                Height = 32,
                IsResizable = false,
                IsMovable = false,
                IsMaximizable = false,
                IsMinimizable = false,
                IsClosable = false,
                ShowTitle = true,
                ShowInTaskbar = false,
            };
            _treeWindow.AddControl(_tree.ScrollableControl);

            _editorWindow = new Window(windowSystem)
            {
                Title = "SOURCE & LISTING",
                Left = 28,
                Top = 2,
                Width = 58,
                Height = 32,
                IsResizable = false,
                IsMovable = false,
                IsMaximizable = false,
                IsMinimizable = false,
                IsClosable = false,
                ShowTitle = true,
                ShowInTaskbar = false,
            };
            _editorWindow.AddControl(_sourceEditors.Control);

            _ramWindow = new Window(windowSystem)
            {
                Title = "RAM ($0000-$3FFF)",
                Left = 86,
                Top = 2,
                Width = 34,
                Height = 32,
                IsResizable = false,
                IsMovable = false,
                IsMaximizable = false,
                IsMinimizable = false,
                IsClosable = false,
                ShowTitle = true,
                ShowInTaskbar = false,
            };
            _ramWindow.AddControl(_ram.ScrollableControl);

            _romWindow = new Window(windowSystem)
            {
                Title = "ROM ($8000-$FFFF)",
                Left = 86,
                Top = 2,
                Width = 34,
                Height = 32,
                IsResizable = false,
                IsMovable = false,
                IsMaximizable = false,
                IsMinimizable = false,
                IsClosable = false,
                ShowTitle = true,
                ShowInTaskbar = false,
            };
            _romWindow.AddControl(_rom.ScrollableControl);

            _refusalWindow = new Window(windowSystem)
            {
                Title = "Terminal Too Small",
                Left = -1000,
                Top = -1000,
                Width = 0,
                Height = 0,
                IsResizable = false,
                IsMovable = false,
                IsMaximizable = false,
                IsMinimizable = false,
                IsClosable = false,
                ShowTitle = true,
                ShowInTaskbar = false,
            };
            _refusalWindow.AddControl(_refusal);

            BindKeys();
        }

        private void TransferRomWithFeedback(string path)
        {
            if (_actionInProgress)
            {
                PostMessage("An operation is already in progress.");
                return;
            }
            RunWithProgress("Transferring ROM " + Path.GetFileName(path) + "...",
                progress =>
                {
                    var answer = _session.TransferRom(path);
                    var result = string.Join(" ", answer);

                    // Auto-reset emulator after ROM transfer to load new image
                    if (answer.Count > 0 && answer[0].StartsWith(MonitorProtocol.OkPrefix, StringComparison.Ordinal))
                    {
                        progress("ROM transferred; resetting emulator...");
                        try
                        {
                            _session.Execute("RESET");
                            result += " | Emulator reset";
                        }
                        catch (Exception ex)
                        {
                            result += " | Reset failed: " + ex.Message;
                        }
                    }
                    return result;
                });
        }

        /// <summary>
        /// Reconnects to the bridge. The backend must support reconnection; a
        /// backend that cannot reconnect refuses with its own words.
        /// </summary>
        public void ReconnectBridge()
        {
            if (_actionInProgress)
            {
                PostMessage("An operation is already in progress.");
                return;
            }

            string bridge = BridgeFilePicker.Pick();
            if (string.IsNullOrEmpty(bridge)) return;

            RunWithProgress("Starting selected bridge...", progress =>
            {
                if (_session.Launcher == null)
                    return "ERR start the monitor with an emulator before selecting a bridge";
                _session.Launcher.RestartWithBridge(bridge);
                IReadOnlyList<string> answer = _session.Execute("CONNECT");
                var result = string.Join(" ", answer);

                // Re-read the RAM pane after reconnecting, so it shows the
                // new bridge's memory rather than the old one's last push.
                if (answer.Count > 0 && answer[0].StartsWith(MonitorProtocol.OkPrefix, StringComparison.Ordinal))
                {
                    progress("Reconnected; refreshing panes...");
                    RefreshPanes();
                }
                return result;
            });
        }

        /// <summary>
        /// Launches the emulator from the monitor. The session must have a
        /// launcher configured; a session without one refuses with its own words.
        /// </summary>
        public void LaunchEmulator()
        {
            if (_actionInProgress)
            {
                PostMessage("An operation is already in progress.");
                return;
            }

            if (_session.Launcher == null)
            {
                PostMessage("no emulator is configured; give --emulator-path, or an Emulator section in config.json");
                return;
            }

            RunWithProgress("Starting emulator...", progress =>
            {
                IReadOnlyList<string> answer = _session.Execute("EMULATOR START");
                return string.Join(" ", answer);
            });
        }

        public Window Window { get { return _mainWindow; } }

        public void RegisterWindows(ConsoleWindowSystem ws)
        {
            if (ws == null) return;
            ws.AddWindow(_mainWindow);
            ws.AddWindow(_treeWindow);
            ws.AddWindow(_editorWindow, activateWindow: true);
            ws.AddWindow(_ramWindow);
            ws.AddWindow(_romWindow);
            ws.AddWindow(_refusalWindow);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _sourceLoadVersion++;
            _session.Backend.RamChanged -= _ramChangedHandler;
            DismissContextMenu();
            _sourceEditors.Dispose();
        }

        public ShellTheme Theme { get { return _theme; } }
        public object Breakpoints { get { return null; } }
        public ShellLayout CurrentLayout { get { return _layout; } }

        private readonly Dictionary<string, string> _stateSlots =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int CapturedStateCount { get { return _stateSlots.Count; } }

        public string SaveStateSlot(string slot)
        {
            string refusal = Refusal("F7", ExecutionCapability.StateSaveLoad);
            if (refusal != null)
            {
                _messageText = refusal;
                return refusal;
            }
            string name = string.IsNullOrWhiteSpace(slot) ? EditorCommands.DefaultStateSlot : slot;
            IReadOnlyList<string> answer = Run(EditorCommands.SaveState(name));
            string hex = ExtractStateHex(answer);
            if (hex == null)
            {
                _stateSlots.Remove(name);
                string msg = JoinAnswer(answer, "state not saved");
                _messageText = msg;
                return msg;
            }
            _stateSlots[name] = hex;
            string result = "state saved in slot " + name + " (" + (hex.Length / 2) + " bytes)";
            _messageText = result;
            return result;
        }

        public string LoadStateSlot(string slot)
        {
            string refusal = Refusal("F7", ExecutionCapability.StateSaveLoad);
            if (refusal != null) return refusal;
            string name = string.IsNullOrWhiteSpace(slot) ? EditorCommands.DefaultStateSlot : slot;
            string hex;
            if (!_stateSlots.TryGetValue(name, out hex))
                return "no state in slot " + name + ": press F7 to capture one first";
            return JoinAnswer(Run(EditorCommands.LoadState(hex)), "state restored from slot " + name);
        }

        private static string ExtractStateHex(IReadOnlyList<string> answer)
        {
            if (answer == null) return null;
            foreach (string line in answer)
            {
                if (line == null) continue;
                string trimmed = line.Trim();
                if (trimmed.StartsWith(MonitorProtocol.ErrPrefix, StringComparison.Ordinal)) return null;
                string[] parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[0] == MonitorProtocol.OkPrefix) return parts[1];
            }
            return null;
        }

        private static string JoinAnswer(IReadOnlyList<string> answer, string onSuccess)
        {
            if (answer == null || answer.Count == 0) return onSuccess;
            return string.Join(" ", new List<string>(answer).ToArray());
        }

        private void SaveCurrentState()
        {
            string message = SaveStateSlot(null);
            _messageText = message;
        }

        public const string ProjectRule =
            "F5 builds the project when a config.json in the open source's directory, or"
            + " in one above it up to the session directory, declares that source as an"
            + " input; otherwise it assembles that one file (BUILD config.json, or"
            + " ASSEMBLE <file>).";

        public static IReadOnlyList<ShellKey> Keys
        {
            get
            {
                return new List<ShellKey>
                {
                    new ShellKey("F1", "this help", true),
                    new ShellKey("F2", "file tree", true),
                    new ShellKey("F3", "focus editor", true),
                    new ShellKey("F4", "RAM pane", true),
                    new ShellKey("F5", "build and transfer project ROM", true),
                    new ShellKey("F6", "save all sources", true),
                    new ShellKey("F7", "save state", true, ExecutionCapability.StateSaveLoad),
                    new ShellKey("F8", "toggle breakpoint", true, ExecutionCapability.BreakpointExecution),
                    new ShellKey("F9", "run", true, ExecutionCapability.Resume),
                    new ShellKey("F10", "step", true, ExecutionCapability.StepInstruction),
                    new ShellKey("F12", "video pane", false),
                    new ShellKey("R", "reconnect to bridge", true),
                    new ShellKey("Ctrl+S", "save active source", true),
                    new ShellKey("Ctrl+A", "assistant", false),
                    new ShellKey("Ctrl+Q", "quit", true),
                };
            }
        }

        public ExecutionCapability Capabilities { get { return _capabilities; } }

        public string MachineName
        {
            get
            {
                IExecutionAdapter adapter = _session.Backend as IExecutionAdapter;
                if (adapter != null && !string.IsNullOrWhiteSpace(adapter.DisplayName))
                    return adapter.DisplayName;
                string name = _session.Backend.EmulatorName;
                string version = _session.Backend.EmulatorVersion;
                if (string.IsNullOrWhiteSpace(name)) return null;
                return string.IsNullOrWhiteSpace(version) ? name : name + " " + version;
            }
        }

        public string Message
        {
            get { return _messageText; }
            set { _messageText = value ?? string.Empty; }
        }

        public string StatusLine
        {
            get { return _status.Line; }
        }

        public IReadOnlyList<string> Commands { get { return _commands; } }

        public int BreakpointCount { get { return _breakpoints.Count; } }

        public IMemoryBackend Machine
        {
            get { return _session.Backend; }
        }

        public string ExecutionState
        {
            get { return _session.IsRunning ? "RUNNING" : "STOPPED"; }
        }

        public int ShortcutCount { get { return _shortcuts.Count; } }

        public bool HasShortcut(string keyName)
        {
            foreach (Shortcut shortcut in _shortcuts)
            {
                if (shortcut.Key == keyName)
                    return true;
            }
            return false;
        }

        public bool AreShortcutsVisible
        {
            get { return false; } // Shortcuts are bound to application, not visible as controls
        }

        public int FileTreeRowCount { get { return _tree.RowCount; } }

        public string FileTreeRootName { get { return _tree.RootName; } }

        public RamPane Ram { get { return _ram; } }

        public bool RamHasZeroPageMarker { get { return _ram.HasZeroPageMarker; } }

        public bool RamHasStackMarker { get { return _ram.HasStackMarker; } }

        public ListingPane Listing { get { return _listingPane; } }

        public SourceEditorPane SourceEditor { get { return _sourceEditors.ActiveEditor; } }

        public SourceEditorTabs SourceEditors { get { return _sourceEditors; } }

        public string SourceFile { get { return _sourceFile; } }

        public int ResumeCount { get { return _resumeCount; } }
        public int StepCount { get { return _stepCount; } }
        private int _resumeCount = 0;
        private int _stepCount = 0;

        public void OpenSource(string path)
        {
            OpenSourceFile(path);
            RefreshPanes();
        }

        public void OpenSourceAndList(string path, string sourceText = null)
        {
            _sourceFile = path;
            _sourceEditors.Open(path, sourceText);
            RefreshPanes();
        }

        public void LoadSourceAndList(string path, string sourceText = null)
        {
            OpenSourceAndList(path, sourceText);
        }

        public void AssembleCurrentSource()
        {
            if (string.IsNullOrEmpty(_sourceFile))
            {
                _messageText = "no source is open";
                return;
            }

            ProjectGovernance governance = ProjectConfigLocator.Locate(_sourceFile, _directory);
            if (governance.Governed)
            {
                BuildProject(governance.Configuration);
                return;
            }

            string sourcePath = Path.GetRelativePath(_directory, _sourceFile);
            string command = EditorCommands.Assemble(sourcePath);
            _commands.Add(command);
            IReadOnlyList<string> answer = _session.Execute(command);
            _messageText = "-- single file " + sourcePath + " " + string.Join(" ", answer);
        }

        public void BuildProject(string configFile)
        {
            _commands.Add("BUILD " + configFile);
            IReadOnlyList<string> answer = _session.Execute("BUILD " + configFile);
            _messageText = "-- project " + configFile + " " + string.Join(" ", answer);
            ProjectBuildResult result = _session.Projects.LastResult;
            _lastBuiltRomPath = result != null && result.Success ? result.ImagePath : null;
        }

        public void SaveCurrentSource()
        {
            if (string.IsNullOrEmpty(_sourceFile))
            {
                _messageText = "no source is open";
                return;
            }

            var result = SourceEditor.Save();
            if (result.Success)
            {
                _messageText = "saved " + Path.GetFileName(_sourceFile);
            }
            else
            {
                _messageText = "ERR " + result.Problem;
            }
        }

        public void SaveAllSources()
        {
            if (!_sourceEditors.SaveAll())
            {
                _messageText = "one or more sources could not be saved";
                return;
            }

            _messageText = "saved " + _sourceEditors.Count + " source file(s)";
        }

        public void BuildAndTransferCurrentProject()
        {
            if (string.IsNullOrEmpty(_sourceFile))
            {
                _messageText = "no source is open";
                return;
            }

            ProjectGovernance governance = ProjectConfigLocator.Locate(_sourceFile, _directory);
            if (!governance.Governed)
            {
                SaveAllSources();
                if (_messageText.StartsWith("one or more sources could not be saved", StringComparison.Ordinal))
                    return;
                AssembleCurrentSource();
                return;
            }

            SaveAllSources();
            if (_messageText.StartsWith("one or more sources could not be saved", StringComparison.Ordinal))
                return;
            ProjectBuildReport report = _session.Projects.Build(governance.Configuration);
            _commands.Add("BUILD " + governance.Configuration);
            _messageText = string.Join(" ", report.Answer);

            if (!report.Succeeded || string.IsNullOrEmpty(report.Result.ImagePath))
            {
                _lastBuiltRomPath = null;
                return;
            }

            _lastBuiltRomPath = report.Result.ImagePath;
            try
            {
                IReadOnlyList<string> transfer = _session.TransferRom(report.Result.ImagePath);
                _messageText = string.Join(" ", transfer);
            }
            catch (Exception ex)
            {
                _messageText = "ERR transferring ROM: " + ex.Message;
            }
        }

        private void StartBuildAndTransfer()
        {
            if (_actionInProgress)
            {
                PostMessage("An operation is already in progress.");
                return;
            }
            if (string.IsNullOrEmpty(_sourceFile))
            {
                PostMessage("ERR no source is open");
                return;
            }

            ProjectGovernance governance = ProjectConfigLocator.Locate(_sourceFile, _directory);
            if (!governance.Governed)
            {
                SaveAllSources();
                if (!_messageText.StartsWith("saved ", StringComparison.Ordinal))
                {
                    PostMessage("ERR " + _messageText);
                    return;
                }
                string sourcePath = Path.GetRelativePath(_directory, _sourceFile);
                string command = EditorCommands.Assemble(sourcePath);
                _commands.Add(command);
                RunWithProgress("Assembling " + sourcePath + "...",
                    progress => "-- single file " + sourcePath + " "
                        + string.Join(" ", _session.Execute(command)));
                return;
            }

            StartProjectBuild(governance.Configuration, true);
        }

        private void StartProjectBuild(string configFile, bool transferRom)
        {
            if (_actionInProgress)
            {
                PostMessage("An operation is already in progress.");
                return;
            }
            SaveAllSources();
            if (!_messageText.StartsWith("saved ", StringComparison.Ordinal))
            {
                PostMessage("ERR " + _messageText);
                return;
            }

            _commands.Add("BUILD " + configFile);
            string builtRomPath = null;
            RunWithProgress("Building project " + configFile + "...", progress =>
            {
                ProjectBuildReport report = _session.Projects.Build(configFile);
                if (!report.Succeeded || report.Result == null
                    || string.IsNullOrEmpty(report.Result.ImagePath))
                {
                    return string.Join(" ", report.Answer);
                }

                builtRomPath = report.Result.ImagePath;
                if (!transferRom)
                    return string.Join(" ", report.Answer);

                progress("Build complete; transferring ROM...");
                IReadOnlyList<string> answer = _session.TransferRom(builtRomPath);
                var result = string.Join(" ", answer);

                // Auto-reset emulator after ROM transfer to load new image
                if (answer.Count > 0 && answer[0].StartsWith(MonitorProtocol.OkPrefix, StringComparison.Ordinal))
                {
                    progress("ROM transferred; resetting emulator...");
                    try
                    {
                        _session.Execute("RESET");
                        result += " | Emulator reset";
                    }
                    catch (Exception ex)
                    {
                        result += " | Reset failed: " + ex.Message;
                    }
                }
                return result;
            }, () => _lastBuiltRomPath = builtRomPath);
        }

        private void StartAssembleCurrentSource()
        {
            if (_actionInProgress)
            {
                PostMessage("An operation is already in progress.");
                return;
            }
            if (string.IsNullOrEmpty(_sourceFile))
            {
                PostMessage("ERR no source is open");
                return;
            }

            SaveCurrentSource();
            if (Message.StartsWith("ERR ", StringComparison.Ordinal))
            {
                PostMessage(Message);
                return;
            }

            string sourcePath = Path.GetRelativePath(_directory, _sourceFile);
            string command = EditorCommands.Assemble(sourcePath);
            _commands.Add(command);
            RunWithProgress("Assembling " + sourcePath + "...",
                progress => "-- single file " + sourcePath + " "
                    + string.Join(" ", _session.Execute(command)));
        }

        private void RunWithProgress(string initialMessage, Func<Action<string>, string> action,
            Action completed = null)
        {
            if (_actionInProgress)
            {
                PostMessage("An operation is already in progress.");
                return;
            }

            _actionInProgress = true;
            int generation = ++_actionGeneration;
            _loadingSpinner.IsSpinning = true;
            _loadingSpinner.Invalidate();
            _activityToolbar.Invalidate();
            PostMessage(initialMessage);

            Task.Run(() => action(progress =>
            {
                if (_disposed)
                    return;
                _windowSystem.EnqueueOnUIThread(() =>
                {
                    if (!_disposed && generation == _actionGeneration)
                        PostMessage(progress);
                });
            })).ContinueWith(task =>
            {
                if (_disposed)
                    return;
                _windowSystem.EnqueueOnUIThread(() =>
                {
                    if (_disposed || generation != _actionGeneration)
                        return;

                    _actionInProgress = false;
                    _loadingSpinner.IsSpinning = false;
                    _loadingSpinner.Invalidate();
                    _activityToolbar.Invalidate();
                    if (task.IsFaulted)
                    {
                        PostMessage("ERR " + task.Exception.GetBaseException().Message);
                    }
                    else
                    {
                        completed?.Invoke();
                        PostMessage(task.Result);
                    }
                    RefreshPanes();
                });
            }, TaskScheduler.Default);
        }

        public void ResumeMachine()
        {
            string refusal = Refusal("F9", ExecutionCapability.Resume);
            if (refusal != null)
            {
                _messageText = refusal;
                return;
            }

            _resumeCount++;
            _commands.Add("RESUME");
            IReadOnlyList<string> answer = _session.Execute("RESUME");
            _messageText = string.Join(" ", answer);
        }

        public void StepMachine()
        {
            string refusal = Refusal("F10", ExecutionCapability.StepInstruction);
            if (refusal != null)
            {
                _messageText = refusal;
                return;
            }

            _stepCount++;
            _commands.Add("STEP");
            IReadOnlyList<string> answer = _session.Execute("STEP");
            _messageText = string.Join(" ", answer);
        }

        public void ToggleBreakpoint()
        {
            string refusal = Refusal("F8", ExecutionCapability.BreakpointExecution);
            if (refusal != null)
            {
                _messageText = refusal;
                return;
            }

            // Get current cursor position from editor
            int line = SourceEditor.CurrentLine - 1; // CurrentLine is 1-based, GetRow expects 0-based
            if (line >= 0)
            {
                EditorRow row = SourceEditor.GetRow(line);
                if (row != null && row.Address >= 0)
                {
                    int address = row.Address;
                    bool removing = _breakpoints.Contains(address);
                    string command = removing
                        ? EditorCommands.RemoveBreakpoint(BreakpointKind.Exec, address)
                        : EditorCommands.SetBreakpoint(BreakpointKind.Exec, address);
                    _commands.Add(command);
                    IReadOnlyList<string> answer = _session.Execute(command);
                    _messageText = string.Join(" ", answer);

                    // The local set follows what the emulator accepted, so a refused
                    // toggle leaves the gutter showing the truth.
                    if (answer.Count > 0 && answer[0].StartsWith(MonitorProtocol.OkPrefix, StringComparison.Ordinal))
                    {
                        if (removing)
                            _breakpoints.Remove(address);
                        else
                            _breakpoints.Add(address);
                    }
                }
                else
                {
                    _messageText = "F8 not on an address";
                }
            }
            else
            {
                _messageText = "F8 not on an address";
            }
        }

        public string ShowHelp()
        {
            List<string> lines = new List<string>();
            foreach (ShellKey key in Keys)
            {
                string line = "  " + key.Key + "  " + key.Description;
                if (!key.AvailableWhen(_capabilities))
                {
                    line += " (unavailable on " + MachineName + ")";
                }
                lines.Add(line);
            }
            lines.Add(string.Empty);
            lines.Add(ProjectRule);
            return string.Join(Environment.NewLine, lines);
        }

        private void ToggleTree()
        {
            _treeOpen = !_treeOpen;
            if (_layout != null)
                ApplyLayout(_layout);
        }

        private void FocusEditor()
        {
            SourceEditor?.FocusEditor();
        }

        private void ToggleRamPane()
        {
            _ramOpen = !_ramOpen;
            if (_layout != null)
                ApplyLayout(_layout);
        }

        private void ToggleRomPane()
        {
            _romOpen = !_romOpen;
            if (_layout != null)
                ApplyLayout(_layout);
        }

        private void SaveState()
        {
            _commands.Add("STATE SAVE");
            IReadOnlyList<string> answer = _session.Execute("STATE SAVE");
            _messageText = string.Join(" ", answer);
        }

        private void RequestQuit()
        {
            _mainWindow?.Close();
            _treeWindow?.Close();
            _editorWindow?.Close();
            _ramWindow?.Close();
            _refusalWindow?.Close();
            Dispose();
        }

        public void OpenSourceFile(string path)
        {
            _sourceFile = path;
            _sourceEditors.Open(path, null);
        }

        /// <summary>
        /// Resolves <paramref name="path"/> against <see cref="_directory"/> and returns
        /// the exit code: 0 when it opened, 4 when it could not.
        /// </summary>
        public int TryOpenSource(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return 0;

            string trimmed = path.Trim();
            string fullPath;
            try
            {
                fullPath = Path.IsPathRooted(trimmed)
                    ? trimmed
                    : Path.GetFullPath(Path.Combine(_directory, trimmed));
            }
            catch (ArgumentException)
            {
                return 4;
            }
            catch (PathTooLongException)
            {
                return 4;
            }
            catch (NotSupportedException)
            {
                return 4;
            }

            if (!File.Exists(fullPath))
                return 4;

            SourceDocument document = new SourceDocument(_directory);
            string problem;
            string text = document.ReadText(fullPath, out problem);
            if (problem != null)
            {
                PostMessage("ERR " + problem);
                return 4;
            }
            OpenSourceAndList(fullPath, text);
            PostMessage("Opened " + Path.GetFileName(fullPath));
            return 0;
        }

        public void OnResized(int width, int height)
        {
            if (_layout == null || _layout.Width != width || _layout.Height != height)
            {
                _layout = ShellLayout.ForSize(width, height);
                ApplyLayout(_layout);
            }
        }

        private void RefreshPanes()
        {
            // Refresh RAM pane with memory data
            _ram.Refresh((address, length) =>
            {
                try
                {
                    return _session.Backend.Read(address, length);
                }
                catch
                {
                    return new byte[length];
                }
            });
            _ram.InvalidateWindow(_ramWindow);

            // Refresh ROM pane with memory data
            _rom.Refresh((address, length) =>
            {
                try
                {
                    return _session.Backend.Read(address, length);
                }
                catch
                {
                    return new byte[length];
                }
            });
            _rom.InvalidateWindow(_romWindow);

            // Refresh listing pane with current listing
            if (_listing != null && SourceEditor != null
                && SourceDocument.IsAssemblerSourceFile(_sourceFile))
            {
                string sourceText = SourceEditor.TextView.Content;
                var request = new ListingRequest(_sourceFile ?? "", 0x8000, 0);
                request.SourceText = sourceText;
                var listingRows = _listing.Rows(request);
                _listingPane.SetRows(listingRows);

                // Sync editor rows with listing
                SourceEditor.SyncRowsFromListing(listingRows);
            }

            // Refresh file tree with current directory
            string problem;
            _tree.SetRows(FileTreeModel.Rows(_directory, _session.Library, out problem), problem);
        }

        public void ApplyLayout(ShellLayout layout)
        {
            _layout = layout;
            if (layout.IsRefused)
            {
                ShowRefusal(layout.RefusalMessage);
                return;
            }

            HideRefusal();

            int bodyTop = 3;
            int paneHeight = Math.Max(1, layout.Height - bodyTop);

            // Status & Message Window (top bar)
            _mainWindow.IsContentVisible = true;
            _mainWindow.Left = 0;
            _mainWindow.Top = 0;
            _mainWindow.Width = layout.Width;
            _mainWindow.Height = bodyTop;
            _status.Control.Width = layout.Width;
            _status.Update(ShellStatus.Read(_session.Backend, _breakpoints.Count), layout.Width);
            _message.Width = layout.Width;
            _activityToolbar.Width = layout.Width;

            // Tree Pane Window (left)
            if (_treeOpen && layout.TreeWidth > 0)
            {
                _treeWindow.IsContentVisible = true;
                _treeWindow.Left = 0;
                _treeWindow.Top = bodyTop;
                _treeWindow.Width = layout.TreeWidth;
                _treeWindow.Height = paneHeight;
                _tree.Control.Width = Math.Max(1, layout.TreeWidth - 2);
                _tree.Control.Height = Math.Max(1, paneHeight - 2);
                _tree.ScrollableControl.Width = Math.Max(1, layout.TreeWidth - 2);
                _tree.ScrollableControl.Height = Math.Max(1, paneHeight - 2);
                _tree.SetViewportWidth(Math.Max(1, layout.TreeWidth - 2));
            }
            else
            {
                _treeWindow.IsContentVisible = false;
            }

            // Source Editor Window (center)
            if (_rightPaneOpen && layout.EditorWidth > 0)
            {
                _editorWindow.IsContentVisible = true;
                _editorWindow.Left = layout.TreeWidth;
                _editorWindow.Top = bodyTop;
                _editorWindow.Width = layout.EditorWidth;
                _editorWindow.Height = paneHeight;

                _sourceEditors.Control.Visible = true;
                _sourceEditors.Control.Width = Math.Max(1, layout.EditorWidth - 2);
                _listingPane.SetViewportWidth(Math.Max(1, layout.EditorWidth - 4));
                int editorHeight = Math.Max(2, paneHeight - 2);
                _sourceEditors.Control.Height = editorHeight;
                ApplyActiveEditorLayout();
            }
            else
            {
                _editorWindow.IsContentVisible = false;
            }

            // RAM Pane Window (right)
            if (_ramOpen && layout.RamWidth > 0)
            {
                int ramHeight = _romOpen ? paneHeight / 2 : paneHeight;
                _ramWindow.IsContentVisible = true;
                _ramWindow.Left = layout.TreeWidth + layout.EditorWidth;
                _ramWindow.Top = bodyTop;
                _ramWindow.Width = layout.RamWidth;
                _ramWindow.Height = ramHeight;
                _ram.Control.Width = Math.Max(1, layout.RamWidth - 2);
                int ramContentHeight = Math.Max(1, ramHeight - 2);
                _ram.Control.Height = ramContentHeight;
                _ram.ScrollableControl.Width = Math.Max(1, layout.RamWidth - 2);
                _ram.ScrollableControl.Height = ramContentHeight;
                _ram.SetContentWidth(Math.Max(1, layout.RamWidth - 3));
                _ram.SetVisibleItems(ramContentHeight);
            }
            else
            {
                _ramWindow.IsContentVisible = false;
            }

            // ROM Pane Window (below RAM when both open)
            if (_romOpen && layout.RamWidth > 0)
            {
                int romTop = bodyTop;
                int romHeight = paneHeight;
                if (_ramOpen)
                {
                    romTop = bodyTop + paneHeight / 2;
                    romHeight = paneHeight - paneHeight / 2;
                }

                _romWindow.IsContentVisible = true;
                _romWindow.Left = layout.TreeWidth + layout.EditorWidth;
                _romWindow.Top = romTop;
                _romWindow.Width = layout.RamWidth;
                _romWindow.Height = romHeight;
                _rom.Control.Width = Math.Max(1, layout.RamWidth - 2);
                int romContentHeight = Math.Max(1, romHeight - 2);
                _rom.Control.Height = romContentHeight;
                _rom.ScrollableControl.Width = Math.Max(1, layout.RamWidth - 2);
                _rom.ScrollableControl.Height = romContentHeight;
                _rom.SetContentWidth(Math.Max(1, layout.RamWidth - 3));
                _rom.SetVisibleItems(romContentHeight);
            }
            else
            {
                _romWindow.IsContentVisible = false;
            }

            RefreshPanes();
        }

        private void ShowRefusal(string message)
        {
            _refusal.Text = message;
            _refusalWindow.IsContentVisible = true;
            _refusalWindow.Left = 0;
            _refusalWindow.Top = 0;
            _refusalWindow.Width = _layout != null ? _layout.Width : 60;
            _refusalWindow.Height = _layout != null ? _layout.Height : 10;

            _mainWindow.IsContentVisible = false;
            _treeWindow.IsContentVisible = false;
            _editorWindow.IsContentVisible = false;
            _ramWindow.IsContentVisible = false;
            _romWindow.IsContentVisible = false;
        }

        private void HideRefusal()
        {
            _refusalWindow.IsContentVisible = false;
        }

        public void PostMessage(string text)
        {
            _messageText = text ?? string.Empty;
            string role = MessageHasError(_messageText) ? ThemeRole.Error
                : MessageHasSuccess(_messageText) ? ThemeRole.Label : ThemeRole.Warning;
            SharpConsoleUI.Color color = _theme.Foreground(role);
            string tag = "[#" + color.R.ToString("X2", CultureInfo.InvariantCulture)
                + color.G.ToString("X2", CultureInfo.InvariantCulture)
                + color.B.ToString("X2", CultureInfo.InvariantCulture) + "]";
            string cue = role == ThemeRole.Error ? "[ERR] "
                : role == ThemeRole.Label ? "[OK] " : "[i] ";
            _message.Text = tag + cue + EscapeMarkup(_messageText) + "[/]";
            _message.Invalidate();
            _activityToolbar.Invalidate();
        }

        private void SetMessage(string text)
        {
            PostMessage(text);
        }

        private static bool MessageHasError(string text)
        {
            return text.StartsWith("ERR", StringComparison.OrdinalIgnoreCase)
                || text.IndexOf(" ERR ", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("could not", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool MessageHasSuccess(string text)
        {
            return text.StartsWith("OK", StringComparison.OrdinalIgnoreCase)
                || text.IndexOf("OK ", StringComparison.OrdinalIgnoreCase) >= 0
                || text.StartsWith("Opened ", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("saved ", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("state saved", StringComparison.OrdinalIgnoreCase)
                || text.IndexOf("ROM replaced", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("built", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string EscapeMarkup(string text)
        {
            return (text ?? string.Empty).Replace("[", "[[").Replace("]", "]]");
        }

        private void BindKeys()
        {
            _shortcuts.Clear();

            foreach (ShellKey key in Keys)
            {
                if (!key.Bound) continue;

                Action action = null;
                switch (key.Key)
                {
                    case "F1": action = () => PostMessage(ShowHelp()); break;
                    case "F2": action = () => ToggleTree(); break;
                    case "F3": action = () => FocusEditor(); break;
                    case "F4": action = () => ToggleRamPane(); break;
                    case "F5": action = () => StartBuildAndTransfer(); break;
                    case "F6": action = () => { SaveAllSources(); PostMessage(Message); }; break;
                    case "F7": action = () => { SaveState(); PostMessage(Message); }; break;
                    case "F8": action = () => { ToggleBreakpoint(); PostMessage(Message); }; break;
                    case "F9": action = () => { ResumeMachine(); PostMessage(Message); }; break;
                    case "F10": action = () => { StepMachine(); PostMessage(Message); }; break;
                    case "F12": action = () => ToggleRomPane(); break;
                    case "R": action = () => { ReconnectBridge(); PostMessage(Message); }; break;
                    case "Ctrl+S": action = () => { SaveCurrentSource(); PostMessage(Message); }; break;
                    case "Ctrl+Q": action = () => RequestQuit(); break;
                }

                if (action != null)
                {
                    _shortcuts.Add(new Shortcut(key.Key, action));
                }
            }
        }

        private void OnTreeActivated(FileTreeRow row)
        {
            if (row == null || (row.Kind != FileTreeKind.Source && row.Kind != FileTreeKind.TextFile))
                return;
            string path = row.Path;
            if (!File.Exists(path)) return;

            OpenSourceWithFeedback(path, null);
        }

        private void OnTreeContextRequested(object sender, MouseEventArgs e)
        {
            FileTreeRow row = _tree.Selected;
            if (row != null && (row.Kind == FileTreeKind.Source || row.Kind == FileTreeKind.Rom))
            {
                ShowSourceContextMenu(row, e.AbsolutePosition, _tree.ScrollableControl);
            }
            else
            {
                ShowTreeContextMenu(e.AbsolutePosition, _tree.ScrollableControl);
            }
        }

        private void OnEditorContextRequested(object sender, MouseEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_sourceFile))
                return;

            ShowSourceContextMenu(
                new FileTreeRow(FileTreeKind.Source, Path.GetFileName(_sourceFile), _sourceFile),
                e.AbsolutePosition, _sourceEditors.Control);
        }

        private void ShowSourceContextMenu(FileTreeRow row, Point position, IWindowControl owner)
        {
            DismissContextMenu();

            string path = row.Path;
            bool isSource = row.Kind == FileTreeKind.Source;
            bool isRom = row.Kind == FileTreeKind.Rom;
            ProjectGovernance governance = isSource
                ? ProjectConfigLocator.Locate(path, _directory)
                : null;
            MenuBuilder menuBuilder = Controls.Menu().Vertical().WithName("source-context-menu");
            if (isSource)
                menuBuilder.AddItem("Assemble source", () => RunSourceAction(path, StartAssembleCurrentSource));

            if (isSource && governance.Governed)
            {
                menuBuilder.AddItem("Build project", () => RunSourceAction(path, () =>
                {
                    StartProjectBuild(governance.Configuration, false);
                }));

                if (_session.Backend is IRomTransferTarget)
                {
                    menuBuilder.AddItem("Build + Transfer",
                        () => RunSourceAction(path, StartBuildAndTransfer));
                }
            }

            if (isRom && _session.Backend is IRomTransferTarget)
            {
                menuBuilder.AddItem("Transfer this ROM", () =>
                {
                    DismissContextMenu();
                    TransferRomWithFeedback(path);
                });
            }

            if (_lastBuiltRomPath != null && _session.Backend is IRomTransferTarget)
            {
                menuBuilder.AddSeparator();
                menuBuilder.AddItem("Transfer last ROM", () =>
                {
                    DismissContextMenu();
                    TransferRomWithFeedback(_lastBuiltRomPath);
                });
            }

            MenuControl menu = menuBuilder.Build();
            int width = Math.Max(24, Math.Min(36, Math.Max(
                "Assemble source".Length,
                _lastBuiltRomPath == null ? "Build + Transfer".Length : "Transfer last ROM".Length) + 4));
            int itemCount = (isSource ? 1 : 0)
                + (isSource && governance.Governed ? (_session.Backend is IRomTransferTarget ? 2 : 1) : 0)
                + (isRom && _session.Backend is IRomTransferTarget ? 1 : 0)
                + (_lastBuiltRomPath != null && _session.Backend is IRomTransferTarget ? 2 : 0)
                + 2;
            int height = itemCount;
            int left = Math.Max(0, Math.Min(position.X, _layout.Width - width));
            int top = Math.Max(0, Math.Min(position.Y, _layout.Height - height));
            _contextMenuPortal = _windowSystem.DesktopPortalService.CreatePortal(
                new DesktopPortalOptions(menu, new Rectangle(left, top, width, height),
                    true, true, false, () => _contextMenuPortal = null, owner,
                    null, null));
        }

        private void ShowTreeContextMenu(Point position, IWindowControl owner)
        {
            DismissContextMenu();

            MenuBuilder menuBuilder = Controls.Menu().Vertical().WithName("tree-context-menu");
            menuBuilder.AddItem("New source file", () => { DismissContextMenu(); _sourceEditors.AddBlankEditor(); });
            menuBuilder.AddItem("Open source file...", () => { DismissContextMenu(); /* TODO: file dialog */ });
            menuBuilder.AddSeparator();
            menuBuilder.AddItem("Refresh file tree", () => { DismissContextMenu(); RefreshPanes(); });

            // Launch emulator is always shown; LaunchEmulator() handles the
            // case where no launcher is configured by posting a message.
            menuBuilder.AddSeparator();
            menuBuilder.AddItem("Launch emulator", () => { DismissContextMenu(); LaunchEmulator(); });

            // Reconnect is always available.
            menuBuilder.AddItem("Reconnect to bridge", () => { DismissContextMenu(); ReconnectBridge(); });

            MenuControl menu = menuBuilder.Build();
            int width = Math.Max(24, Math.Min(36, "Refresh file tree".Length + 4));
            // Seven menu rows (including the two separators) plus the menu's
            // top and bottom frame.  The old seven-cell portal clipped the last
            // two actions, which made Launch and Reconnect appear intermittently
            // absent depending on the terminal driver.
            int height = 9;
            int left = Math.Max(0, Math.Min(position.X, _layout.Width - width));
            int top = Math.Max(0, Math.Min(position.Y, _layout.Height - height));
            _contextMenuPortal = _windowSystem.DesktopPortalService.CreatePortal(
                new DesktopPortalOptions(menu, new Rectangle(left, top, width, height),
                    true, true, false, () => _contextMenuPortal = null, owner,
                    null, null));
        }

        private void RunSourceAction(string path, Action action)
        {
            DismissContextMenu();
            try
            {
                string fullPath = Path.GetFullPath(path);
                if (SourceEditor != null && string.Equals(SourceEditor.SourceFile, fullPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    action();
                    PostMessage(Message);
                    return;
                }

                OpenSourceWithFeedback(fullPath, action);
            }
            catch (Exception ex)
            {
                PostMessage("ERR " + ex.Message);
            }
        }

        private void OpenSourceWithFeedback(string path, Action afterOpen)
        {
            int loadVersion = ++_sourceLoadVersion;
            _loadingSpinner.IsSpinning = true;
            PostMessage("Loading " + Path.GetFileName(path));

            Task.Run(() =>
            {
                SourceDocument document = new SourceDocument(_directory);
                string problem;
                string text = document.ReadText(path, out problem);
                return new Tuple<string, string>(text, problem);
            }).ContinueWith(task =>
            {
                _windowSystem.EnqueueOnUIThread(() =>
                {
                    if (loadVersion != _sourceLoadVersion)
                        return;

                    _loadingSpinner.IsSpinning = false;
                    if (task.IsFaulted)
                    {
                        PostMessage("ERR loading " + Path.GetFileName(path) + ": "
                            + task.Exception.GetBaseException().Message);
                        return;
                    }

                    try
                    {
                        if (task.Result.Item2 != null)
                        {
                            PostMessage("ERR loading " + Path.GetFileName(path) + ": " + task.Result.Item2);
                            return;
                        }
                        OpenSourceAndList(path, task.Result.Item1);
                        if (afterOpen != null)
                            afterOpen();
                        else
                            PostMessage("Opened " + Path.GetFileName(path));
                        PostMessage(Message);
                    }
                    catch (Exception ex)
                    {
                        PostMessage("ERR loading " + Path.GetFileName(path) + ": " + ex.Message);
                    }
                });
            }, TaskScheduler.Default);
        }

        private void AssembleSourceFromContext()
        {
            StartAssembleCurrentSource();
        }

        private void DismissContextMenu()
        {
            if (_contextMenuPortal == null)
                return;

            _windowSystem.DesktopPortalService.RemovePortal(_contextMenuPortal);
            _contextMenuPortal = null;
        }

        private void OnActiveEditorChanged(SourceEditorPane editor)
        {
            if (editor == null)
                return;

            ApplyActiveEditorLayout();
            _sourceFile = editor.SourceFile;
            if (SourceDocument.IsAssemblerSourceFile(_sourceFile))
            {
                IReadOnlyList<ListingRow> rows = _listing.Rows(new ListingRequest(_sourceFile, 0x8000, 0)
                {
                    SourceText = editor.TextView.Content,
                });
                _listingPane.SetRows(rows);
                editor.SyncRowsFromListing(rows);
            }
            else
            {
                _listingPane.SetRows(new List<ListingRow>());
            }
            RefreshPanes();
        }

        private void ApplyActiveEditorLayout()
        {
            if (_layout == null || _layout.IsRefused || SourceEditor == null)
                return;

            int paneHeight = Math.Max(1, _layout.Height - 3);
            int editorHeight = Math.Max(2, paneHeight - 2);
            IWindowControl control = SourceEditor.Control;
            control.Width = Math.Max(1, _layout.EditorWidth - 2);
            control.Height = editorHeight;
            ((MultilineEditControl)control).ViewportHeight = Math.Max(1, editorHeight - 2);
        }

        private void OnRamChanged(IReadOnlyList<(int Address, byte Value)> changes)
        {
            if (_disposed || changes == null || changes.Count == 0)
                return;
            lock (_ramUpdateLock)
            {
                if (_disposed)
                    return;
                foreach ((int address, byte value) in changes)
                    _pendingRamChanges[address] = value;
                if (_ramUpdateQueued)
                    return;
                _ramUpdateQueued = true;
            }

            _windowSystem.EnqueueOnUIThread(() =>
            {
                List<(int Address, byte Value)> latest;
                lock (_ramUpdateLock)
                {
                    latest = new List<(int Address, byte Value)>(_pendingRamChanges.Count);
                    foreach (KeyValuePair<int, byte> change in _pendingRamChanges)
                        latest.Add((change.Key, change.Value));
                    _pendingRamChanges.Clear();
                    _ramUpdateQueued = false;
                }
                if (!_disposed && latest.Count > 0)
                {
                    _ram.ApplyChanges(latest);
                    _ram.InvalidateWindow(_ramWindow);
                }
            });
        }

        private IReadOnlyList<string> Run(string command)
        {
            return _session.Execute(command);
        }

        private string Refusal(string keyName, ExecutionCapability capability)
        {
            if (_capabilities.HasFlag(capability)) return null;
            string host = _session.Backend.EmulatorName + " " + _session.Backend.EmulatorVersion;
            return ShellCapabilities.Refusal(keyName, host, capability);
        }
    }
}
