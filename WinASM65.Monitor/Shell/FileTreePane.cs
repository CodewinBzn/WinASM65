using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// The file tree: what could be assembled, and what has been.
    ///
    /// A stock list control rather than a custom renderer. The rows are already
    /// text, and the only colour that carries meaning here is section versus
    /// content — which is one attribute per row, not a drawing pipeline.
    /// </summary>
    public sealed class FileTreePane
    {
        private readonly ListControl _list;
        private readonly IWindowControl _control;
        private readonly ShellTheme _theme;
        private readonly List<FileTreeRow> _rows = new List<FileTreeRow>();
        private int _viewportWidth = 1;

        public FileTreePane(ShellTheme theme)
        {
            _theme = theme ?? ShellTheme.Default;
            Title = "files";

            _list = new ListControl
            {
                IsSelectable = true,
                SelectOnRightClick = true,
                Title = Title,
            };
            _control = Controls.ScrollablePanel()
                .AddControl(_list)
                .WithVerticalScroll(ScrollMode.None)
                .WithHorizontalScroll(ScrollMode.Scroll)
                .WithScrollbar(true)
                .Build();

            _list.ItemActivated += OnActivated;
            _list.MouseRightClick += OnRightClick;
        }

        public IWindowControl Control { get { return _list; } }

        public IWindowControl ScrollableControl { get { return _control; } }

        public string Title { get; set; }

        public event EventHandler<FileTreeRow> Activated;

        public event EventHandler<MouseEventArgs> ContextRequested;

        public event EventHandler<MouseEventArgs> TreeContextRequested;

        public IReadOnlyList<FileTreeRow> Rows { get { return _rows; } }

        public int RowCount { get { return _rows.Count; } }

        public string RootName { get { return Title; } }

        public FileTreeRow Selected
        {
            get
            {
                int index = _list.SelectedIndex;
                return index >= 0 && index < _rows.Count ? _rows[index] : null;
            }
        }

        public void SetRows(IReadOnlyList<FileTreeRow> rows, string problem)
        {
            _rows.Clear();
            List<string> labels = new List<string>();

            if (problem != null)
                labels.Add("! " + problem);

            if (rows != null)
            {
                foreach (FileTreeRow row in rows)
                {
                    _rows.Add(row);
                    labels.Add(row.IsSelectable ? "  " + row.Label : row.Label);
                }
            }

            _list.StringItems = labels;
            _list.Width = Math.Max(_viewportWidth, labels.Count == 0
                ? _viewportWidth
                : labels.Max(label => label.Length) + 2);
            if (_list.Items.Count > 0 && _list.SelectedIndex < 0)
                _list.SelectedIndex = 0;
            _list.Invalidate();
        }

        public void SelectFirstSelectable()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].IsSelectable)
                {
                    _list.SelectedIndex = i;
                    return;
                }
            }
        }

        public void SetViewportWidth(int width)
        {
            _viewportWidth = Math.Max(1, width);
            _list.Width = Math.Max(_viewportWidth, _list.StringItems.Count == 0
                ? _viewportWidth
                : _list.StringItems.Max(label => label.Length) + 2);
        }

        private void OnActivated(object sender, ListItem e)
        {
            FileTreeRow row = Selected;
            if (row != null && row.IsSelectable && Activated != null)
                Activated(this, row);
        }

        private void OnRightClick(object sender, MouseEventArgs e)
        {
            FileTreeRow row = Selected;
            if (row != null && row.IsSelectable)
            {
                e.Handled = true;
                ContextRequested?.Invoke(this, e);
            }
            else
            {
                TreeContextRequested?.Invoke(this, e);
            }
        }
    }
}