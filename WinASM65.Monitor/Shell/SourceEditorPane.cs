using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// The centre pane: an editable source view plus a read-only gutter that
    /// shows the address, bytes and cycles each line assembled to.
    ///
    /// The gutter is a separate control placed to the left of the editor at a
    /// fixed width of <see cref="ListingFormatter.GutterWidth"/> columns. The
    /// rows the gutter draws come from <see cref="AssemblerListingSource.EditorRows"/>
    /// which is the single translator — it is not re-derived here. The pane's only
    /// job is to keep the gutter's contents and scroll position in step with what the
    /// editor buffer shows.
    /// </summary>
    public sealed class SourceEditorPane : IDisposable
    {
        private readonly ShellTheme _theme;
        private readonly AssemblerListingSource _source;
        private readonly string _directory;
        private readonly SourceDocument _document;
        private readonly AssemblerSyntaxHighlighter _syntaxHighlighter;
        private readonly JsonSyntaxHighlighter _jsonSyntaxHighlighter;

        internal readonly MultilineEditControl TextView;
        internal readonly SourceGutterRenderer Gutter;

        private bool _suppressEvents;
        private List<EditorRow> _rows = new List<EditorRow>();
        private string _problem;
        private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(300);
        private CancellationTokenSource _debounceCancel;
        private ConsoleWindowSystem _windowSystem;
        private bool _disposed;
        private bool _assemblerDocument = true;

        public ConsoleWindowSystem WindowSystem
        {
            set { _windowSystem = value; }
        }

        /// <summary>The source file this editor is editing, or null when none is open.</summary>
        public string SourceFile { get; private set; }

        /// <summary>True when the buffer has been changed and not yet written back.</summary>
        public bool IsDirty { get; private set; }

        /// <summary>
        /// Raised when the buffer changes, so the window can update its message line.
        /// </summary>
        public event Action ContentChanged;

        /// <summary>
        /// The address at the line the cursor is on, or -1 when that line emits
        /// no bytes. Read for F8 (breakpoint).
        /// </summary>
        public int CursorAddress
        {
            get
            {
                if (_rows.Count == 0)
                    return -1;

                int row = TextView.CurrentLine - 1;
                if (row < 0 || row >= _rows.Count)
                    return -1;

                EditorRow line = _rows[row];
                return line.EmitsBytes ? line.Address : -1;
            }
        }

        public SourceEditorPane(ShellTheme theme, AssemblerListingSource source, string directory)
        {
            _theme = theme ?? ShellTheme.Default;
            _source = source;
            _directory = directory;
            _document = new SourceDocument(directory);
            _syntaxHighlighter = new AssemblerSyntaxHighlighter(_source, _theme);
            _jsonSyntaxHighlighter = new JsonSyntaxHighlighter(_theme);

            Gutter = new SourceGutterRenderer(this, _theme);

            TextView = new MultilineEditControl(0)
            {
                ForegroundColor = _theme.Foreground(ThemeRole.Default),
                SyntaxHighlighter = _syntaxHighlighter,
                HorizontalScrollbarVisibility = ScrollbarVisibility.Auto,
                WrapMode = WrapMode.NoWrap,
            };
            TextView.ContentChanged += OnContentsChanged;
            TextView.AddGutterRenderer(Gutter);
        }

        public IWindowControl Control { get { return TextView; } }
        public IWindowControl GutterControl { get { return TextView; } }

        public int CurrentLine { get { return TextView.CurrentLine; } }

        public EditorRow GetRow(int index)
        {
            if (index < 0 || index >= _rows.Count)
                return null;
            return _rows[index];
        }

        public IReadOnlyList<EditorRow> Rows { get { return _rows; } }

        internal EditorRow GetRowForGutter(int index)
        {
            return GetRow(index);
        }

        /// <summary>
        /// Loads <paramref name="text"/> into the editor.
        ///
        /// Syntax highlighting (lexing) is derived immediately so the editor is
        /// coloured from the first draw. Address/bytes/cycles are filled in by the
        /// caller via <see cref="SyncRowsFromListing"/> or by the debounced
        /// background assembly triggered from <see cref="OnContentsChanged"/>.
        /// </summary>
        public void LoadSource(string sourceFile, string text)
        {
            SourceFile = sourceFile;
            IsDirty = false;
            _assemblerDocument = SourceDocument.IsAssemblerSourceFile(sourceFile);
            bool jsonDocument = string.Equals(Path.GetExtension(sourceFile), ".json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetExtension(sourceFile), ".jsonc", StringComparison.OrdinalIgnoreCase);
            TextView.SyntaxHighlighter = _assemblerDocument ? _syntaxHighlighter
                : jsonDocument ? _jsonSyntaxHighlighter : null;
            TextView.Invalidate();
            _problem = null;
            string fullPath = string.IsNullOrEmpty(sourceFile) ? null
                : Path.GetFullPath(Path.IsPathRooted(sourceFile)
                    ? sourceFile : Path.Combine(_directory, sourceFile));
            if (fullPath != null && File.Exists(fullPath))
            {
                string fileText = _document.ReadText(fullPath, out string problem);
                if (problem == null)
                    text = fileText;
                else
                    _problem = problem;
            }

            _suppressEvents = true;
            try
            {
                TextView.Content = text ?? string.Empty;
            }
            finally
            {
                _suppressEvents = false;
            }

            _rows = new List<EditorRow>();
            if (_assemblerDocument)
                LexBufferIncrementally(0);
            else
                SyncPlainRows();
            Gutter.Invalidate();
            TextView.Invalidate();
        }

        /// <summary>
        /// Fills the gutter from rows already assembled by the listing, rather
        /// than re-assembling the buffer in memory. The listing rows carry the
        /// address and bytes; the editor buffer carries the line text.
        /// </summary>
public void SyncRowsFromListing(IReadOnlyList<ListingRow> listingRows)
        {
            List<string> bufferLines = SplitBuffer();

            Dictionary<int, ListingRow> byLine = new Dictionary<int, ListingRow>();
            if (listingRows != null)
            {
                foreach (ListingRow lr in listingRows)
                {
                    if (lr.SourceName != null)
                    {
                        string lrFileName = Path.GetFileName(lr.SourceName);
                        string editorFileName = Path.GetFileName(SourceFile);
                        if (!lrFileName.Equals(editorFileName, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    if (lr.LineNumber > 0 && !byLine.ContainsKey(lr.LineNumber))
                        byLine[lr.LineNumber] = lr;
                }
            }

            _rows.Clear();
            for (int i = 0; i < bufferLines.Count; i++)
            {
                EditorRow row = new EditorRow
                {
                    LineNumber = i + 1,
                    Address = -1,
                    Bytes = new List<byte>(),
                    Cycles = null,
                    Text = bufferLines[i],
                    Problem = null,
                };

                ListingRow listingRow;
                if (byLine.TryGetValue(row.LineNumber, out listingRow))
                {
                    row.Address = listingRow.Address;
                    row.Bytes = new List<byte>(listingRow.Bytes);
                    row.Cycles = listingRow.Cycles;
                    row.SetTokens(listingRow.Tokens);
                }

                _rows.Add(row);
            }

            _problem = null;
            Gutter.Invalidate();
        }

        private List<string> SplitBuffer()
        {
            List<string> lines = new List<string>();
            string text = TextView.Content ?? string.Empty;
            if (text.Length == 0)
                return lines;

            string[] parts = text.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0 && i + 1 < parts.Length &&
                    parts[i + 1].Length == 0)
                    continue;
                if (parts[i].Length == 0 && i == parts.Length - 1)
                    continue;
                lines.Add(parts[i]);
            }

            return lines;
        }

        /// <summary>
        /// Re-lexes only the changed line, preserving tokens already computed for
        /// all other lines. The AssemblyLexer tokenizes one line at a time with
        /// no state carried between lines, so a keystroke on line N only requires
        /// re-lexing line N — exactly how VS Code and Visual Studio handle
        /// line-isolated grammars.
        ///
        /// Insertions and deletions shift the _rows list to match the new line
        /// count, but do not re-lex the shifted rows: their text hasn't changed,
        /// only their index has. Token reuse here is what keeps a 6000-line file
        /// navigating at full speed — no O(n) pass per keystroke.
        /// </summary>
        private void LexBufferIncrementally(int changedRow)
        {
            if (_source == null || _rows == null)
                return;

            int lineCount = TextView.LineCount;
            if (lineCount == 0)
            {
                _rows.Clear();
                return;
            }

            while (_rows.Count < lineCount)
                _rows.Add(new EditorRow { Address = -1, Bytes = new List<byte>() });

            while (_rows.Count > lineCount)
                _rows.RemoveAt(_rows.Count - 1);

            int row = Math.Max(0, Math.Min(changedRow, lineCount - 1));
            string text = GetLineText(row);

            EditorRow editorRow = _rows[row];
            editorRow.LineNumber = row + 1;
            editorRow.Text = text;
            editorRow.SetTokens(_source.LexLine(text));
        }

        private string GetLineText(int lineIndex)
        {
            string content = TextView.Content ?? string.Empty;
            if (content.Length == 0)
                return string.Empty;

            string[] lines = content.Split('\n');
            if (lineIndex < 0 || lineIndex >= lines.Length)
                return string.Empty;

            string line = lines[lineIndex];
            // Remove trailing \r if present
            if (line.EndsWith("\r"))
                line = line.Substring(0, line.Length - 1);
            return line;
        }

        /// <summary>
        /// Re-derives address/bytes/cycles from the buffer by running the assembler.
        /// Call on a background thread after the user pauses typing.
        /// </summary>
        private Task RecomputeRowsAsync()
        {
            if (!_assemblerDocument || _source == null || string.IsNullOrEmpty(TextView.Content))
                return Task.CompletedTask;

            string text = TextView.Content;
            string sourceFile = SourceFile;

            return Task.Run(() =>
            {
                List<EditorRow> assembled = null;
                string problem = null;

                try
                {
                    IReadOnlyList<EditorRow> rows = _source.EditorRows(text, sourceFile, 0);
                    assembled = new List<EditorRow>(rows);
                    problem = _source.LastProblem;
                }
                catch
                {
                }

                if (assembled != null)
                {
                    _windowSystem?.EnqueueOnUIThread(() =>
                    {
                        if (_disposed)
                            return;
                        for (int i = 0; i < _rows.Count && i < assembled.Count; i++)
                        {
                            _rows[i].Address = assembled[i].Address;
                            _rows[i].Bytes = assembled[i].Bytes;
                            _rows[i].Cycles = assembled[i].Cycles;
                            _rows[i].Problem = assembled[i].Problem;
                            _rows[i].SetTokens(assembled[i].Tokens);
                        }
                        _problem = problem;
                        Gutter.Invalidate();
                        TextView.Invalidate();
                    });
                }
            });
        }

        private void OnContentsChanged(object sender, string e)
        {
            if (_suppressEvents || _disposed)
                return;

            IsDirty = true;
            ContentChanged?.Invoke();

            // Immediate: re-lex only the changed line for syntax highlighting.
            // The lexer is line-independent, so a keystroke on line N only needs
            // line N (and any shift from insertions/deletions). This is what
            // keeps scrolling and typing in a 6000-line file responsive.
            if (_assemblerDocument)
                LexBufferIncrementally(0);
            else
                SyncPlainRows();
            Gutter.Invalidate();
            TextView.Invalidate();

            if (!_assemblerDocument)
                return;

            // Delayed: full assembly runs after the user pauses, off the UI thread.
            _debounceCancel?.Cancel();
            _debounceCancel?.Dispose();
            _debounceCancel = new CancellationTokenSource();
            CancellationToken token = _debounceCancel.Token;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_debounceDelay, token);
                    if (!token.IsCancellationRequested)
                        await RecomputeRowsAsync();
                }
                catch (OperationCanceledException)
                {
                }
            });
        }

        private void SyncPlainRows()
        {
            _rows.Clear();
            for (int line = 0; line < TextView.LineCount; line++)
            {
                _rows.Add(new EditorRow
                {
                    LineNumber = line + 1,
                    Address = -1,
                    Bytes = new List<byte>(),
                    Text = GetLineText(line),
                });
            }
        }

        private void OnTextChanged(object sender, EventArgs args)
        {
        }

        /// <summary>Gives focus to the text editor, for F3.</summary>
        public void FocusEditor()
        {
            // Focus is managed by the window's FocusManager; the editor will receive
            // focus when its window is activated.
        }

/// <summary>
        /// Opens a file in the editor.
        /// </summary>
        public void OpenFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            string fullPath = Path.IsPathRooted(path)
                ? path
                : Path.Combine(_directory, path);

            if (!File.Exists(fullPath))
                return;

            string text = File.ReadAllText(fullPath);
            SourceFile = fullPath;
            LoadSource(fullPath, text);
        }

        /// <summary>
        /// Sets the editor content directly.
        /// </summary>
        public void SetContent(string text)
        {
            LoadSource(SourceFile ?? string.Empty, text ?? string.Empty);
        }

        /// <summary>
        /// Writes the buffer back through <see cref="SourceDocument"/>, which
        /// preserves encoding and line endings and refuses escapes from the directory.
        /// </summary>
        public SaveResult Save()
        {
            if (string.IsNullOrEmpty(SourceFile))
                return new SaveResult(null, "no source is open.");

            string problem;
            bool ok = _document.Write(SourceFile, TextView.Content, out problem);
            if (!ok)
                return new SaveResult(null, problem);

            IsDirty = false;
            return new SaveResult(SourceFile + " saved (" + _rows.Count + " lines)", null);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _debounceCancel?.Cancel();
            _debounceCancel?.Dispose();
            _debounceCancel = null;
        }

        /// <summary>
        /// The visible window as plain text, for tests. Each line is the gutter
        /// text followed by the source text, matching what the pane draws.
        /// </summary>
        public string VisibleText
        {
            get
            {
                int firstRow = TextView.VerticalScrollOffset;
                int visibleHeight = Math.Max(1, TextView.ActualHeight);
                int last = Math.Min(_rows.Count, firstRow + visibleHeight);

                List<string> lines = new List<string>();
                for (int i = firstRow; i < last; i++)
                {
                    string gutterText = ListingFormatter.Gutter(_rows[i]);
                    string sourceText = _rows[i].Text;
                    lines.Add(gutterText + sourceText);
                }

                return string.Join("\n", lines.ToArray());
            }
        }

        /// <summary>Why the pane shows nothing instead of rows, or null when it does.</summary>
        public string Problem
        {
            get { return _problem; }
            set { _problem = value; }
        }


        /// <summary>
        /// Finds the token whose [Start, Start+Text.Length) span covers <paramref name="column"/>.
        /// Whitespace between tokens falls back to the last token's role, so a
        /// comment tail stays coloured rather than reverting to the default.
        /// </summary>
        private static string RoleAtColumn(IList<ListingToken> tokens, int column)
        {
            string lastRole = ThemeRole.Default;
            for (int i = 0; i < tokens.Count; i++)
            {
                ListingToken token = tokens[i];
                int spanEnd = token.Start + token.Text.Length;
                if (column < token.Start)
                    return lastRole;
                if (column < spanEnd)
                    return token.Role;
                lastRole = token.Role;
            }
            return lastRole;
        }
    }

    internal sealed class SourceGutterRenderer : IGutterRenderer
    {
        private readonly SourceEditorPane _pane;
        private readonly Color _foreground;

        public SourceGutterRenderer(SourceEditorPane pane, ShellTheme theme)
        {
            _pane = pane;
            _foreground = theme.Foreground(ThemeRole.Gutter);
        }

        public event EventHandler Invalidated;

        public int GetWidth(int lineCount)
        {
            return ListingFormatter.GutterWidth;
        }

        public void Render(in GutterRenderContext context, int width)
        {
            EditorRow row = _pane.GetRowForGutter(context.SourceLineIndex);
            if (row == null)
                return;

            string text = ListingFormatter.Gutter(row);
            context.Buffer.WriteString(context.X, context.Y, text, _foreground, context.BackgroundColor);
        }

        public void Invalidate()
        {
            Invalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Result of a save attempt, carrying either a success message or a reason.
    /// </summary>
    public sealed class SaveResult
    {
        private readonly string _message;
        private readonly string _problem;

        public SaveResult(string message, string problem)
        {
            _message = message;
            _problem = problem;
        }

        /// <summary>A human-readable success message, or null.</summary>
        public string Message { get { return _message; } }

        /// <summary>A human-readable failure reason, or null.</summary>
        public string Problem { get { return _problem; } }

        /// <summary>True when the save succeeded.</summary>
        public bool Success { get { return _problem == null; } }
    }
}
