using System;
using System.Collections.Generic;
using System.IO;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// Owns one editor pane per open source file and keeps the tab control as the
    /// only presentation surface. The active editor remains available to the shell
    /// so existing commands continue to operate on the selected document.
    /// </summary>
    public sealed class SourceEditorTabs : IDisposable
    {
        private readonly TabControl _tabs = new TabControl();
        private readonly List<SourceEditorPane> _editors = new List<SourceEditorPane>();
        private readonly List<string> _paths = new List<string>();
        private readonly AssemblerListingSource _source;
        private readonly string _directory;
        private readonly ShellTheme _theme;
        private readonly ConsoleWindowSystem _windowSystem;

        public SourceEditorTabs(ShellTheme theme, AssemblerListingSource source, string directory, ConsoleWindowSystem windowSystem)
        {
            _theme = theme ?? ShellTheme.Default;
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _directory = directory ?? Directory.GetCurrentDirectory();
            _windowSystem = windowSystem ?? throw new ArgumentNullException(nameof(windowSystem));
            _tabs.ShowTabScrollIndicators = true;
            _tabs.TabChanged += OnTabChanged;
            _tabs.TabCloseRequested += OnTabCloseRequested;
            AddBlankEditor();
        }

        public TabControl Control { get { return _tabs; } }
        public SourceEditorPane ActiveEditor { get; private set; }
        public int Count { get { return _editors.Count; } }
        public IReadOnlyList<string> Paths { get { return _paths; } }
        public event Action<SourceEditorPane> ActiveChanged;

        public SourceEditorPane AddBlankEditor()
        {
            SourceEditorPane editor = CreateEditor();
            _editors.Add(editor);
            _paths.Add(null);
            _tabs.AddTab("untitled", editor.Control, true);
            ActiveEditor = editor;
            _tabs.ActiveTabIndex = _editors.Count - 1;
            ActiveChanged?.Invoke(editor);
            return editor;
        }

        public SourceEditorPane Open(string path, string text)
        {
            string fullPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(_directory, path));

            for (int i = 0; i < _paths.Count; i++)
            {
                if (string.Equals(_paths[i], fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    ActiveEditor = _editors[i];
                    _tabs.ActiveTabIndex = i;
                    if (!string.IsNullOrEmpty(text))
                        ActiveEditor.LoadSource(fullPath, text);
                    ActiveChanged?.Invoke(ActiveEditor);
                    return ActiveEditor;
                }
            }

            SourceEditorPane editor = CreateEditor();
            editor.LoadSource(fullPath, text ?? string.Empty);
            _editors.Add(editor);
            _paths.Add(fullPath);
            _tabs.AddTab(Path.GetFileName(fullPath), editor.Control, true);
            ActiveEditor = editor;
            _tabs.ActiveTabIndex = _editors.Count - 1;
            ActiveChanged?.Invoke(editor);
            return editor;
        }

        public bool CloseActive()
        {
            if (ActiveEditor == null || _editors.Count <= 1)
                return false;

            int index = _editors.IndexOf(ActiveEditor);
            return Close(index);
        }

        public bool Close(int index)
        {
            if (index < 0 || index >= _editors.Count)
                return false;

            SourceEditorPane editor = _editors[index];
            if (editor.IsDirty && !ConfirmDiscard(editor.SourceFile))
                return false;

            _tabs.RemoveTab(_paths[index] ?? "untitled");
            _editors.RemoveAt(index);
            _paths.RemoveAt(index);

            if (index == _tabs.ActiveTabIndex)
            {
                int next = Math.Min(index, _editors.Count - 1);
                ActiveEditor = _editors[next];
                _tabs.ActiveTabIndex = next;
            }
            else if (index < _tabs.ActiveTabIndex)
            {
                _tabs.ActiveTabIndex--;
            }

            ActiveChanged?.Invoke(ActiveEditor);
            return true;
        }

        public void NextTab()
        {
            if (_tabs.TabCount > 1)
                _tabs.NextTab();
        }

        public void PreviousTab()
        {
            if (_tabs.TabCount > 1)
                _tabs.ActiveTabIndex = (_tabs.ActiveTabIndex - 1 + _tabs.TabCount) % _tabs.TabCount;
        }

        public bool SaveActive()
        {
            if (ActiveEditor == null)
                return false;
            return ActiveEditor.Save().Success;
        }

        public bool SaveAll()
        {
            bool success = true;
            foreach (SourceEditorPane editor in _editors)
            {
                if (editor.IsDirty && !editor.Save().Success)
                    success = false;
            }
            return success;
        }

        public void Dispose()
        {
            foreach (SourceEditorPane editor in _editors)
                editor.Dispose();
        }

        public string GetSourceFile(SourceEditorPane editor)
        {
            int index = _editors.IndexOf(editor);
            return index >= 0 ? _paths[index] : null;
        }

        private SourceEditorPane CreateEditor()
        {
            SourceEditorPane editor = new SourceEditorPane(_theme, _source, _directory);
            editor.WindowSystem = _windowSystem;
            return editor;
        }

        private void OnTabChanged(object sender, EventArgs e)
        {
            if (_tabs.ActiveTabIndex < 0 || _tabs.ActiveTabIndex >= _editors.Count)
                return;

            ActiveEditor = _editors[_tabs.ActiveTabIndex];
            ActiveChanged?.Invoke(ActiveEditor);
        }

        private void OnTabCloseRequested(object sender, TabEventArgs e)
        {
            int index = -1;
            for (int i = 0; i < _tabs.TabPages.Count; i++)
            {
                if (ReferenceEquals(_tabs.TabPages[i], e.TabPage))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
                Close(index);
        }

        private static bool ConfirmDiscard(string path)
        {
            return path == null || !path.StartsWith("\\\\", StringComparison.Ordinal);
        }
    }
}
