using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SharpConsoleUI;
using SharpConsoleUI.Controls;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// The status line, painted as one markup string with a colour per role run.
    ///
    /// One control rather than one per role, because the line is a single row of
    /// text and the only thing that varies along it is the role. Spectre markup
    /// carries the colour inline, so the palette is applied per segment without
    /// a control per segment — which is what a status line is: a sentence, not a
    /// grid of labels. Whole segments are hidden rather than truncated, for the
    /// reason given on ShellStatus.SelectFitting.
    /// </summary>
    public sealed class StatusLine
    {
        private readonly MarkupControl _label;
        private readonly ShellTheme _theme;
        private List<StatusSegment> _segments = new List<StatusSegment>();

        public StatusLine(ShellTheme theme)
        {
            _theme = theme ?? ShellTheme.Default;
            _label = new MarkupControl(new List<string> { string.Empty });
        }

        public MarkupControl Control { get { return _label; } }

        public IReadOnlyList<StatusSegment> ShownSegments { get { return _segments; } }

        public void Update(IReadOnlyList<StatusSegment> segments, int width)
        {
            List<StatusSegment> fitting = ShellStatus.SelectFitting(segments, width);
            _segments = fitting;
            _label.Text = BuildMarkup(fitting);
        }

        public string Line { get { return ShellStatus.Fit(_segments, int.MaxValue); } }

        private string BuildMarkup(IReadOnlyList<StatusSegment> segments)
        {
            if (segments == null || segments.Count == 0)
                return string.Empty;

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < segments.Count; i++)
            {
                if (i > 0)
                    builder.Append("  ");

                StatusSegment segment = segments[i];
                Color color = _theme.Foreground(segment.Role);
                string hex = HexTag(color);
                builder.Append(hex).Append(segment.Text).Append("[/]");
            }

            return builder.ToString();
        }

        private static string HexTag(Color color)
        {
            return "[#" + color.R.ToString("X2", CultureInfo.InvariantCulture)
                + color.G.ToString("X2", CultureInfo.InvariantCulture)
                + color.B.ToString("X2", CultureInfo.InvariantCulture) + "]";
        }
    }
}