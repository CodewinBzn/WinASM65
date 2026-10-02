using System;
using System.Collections.Generic;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// How the shell arranges its panes at a given terminal width.
    ///
    /// The names say what happens, not how wide a pane is: a pane's width is a
    /// consequence of the mode, so a new mode cannot contradict the widths it
    /// implies.
    /// </summary>
    public enum ShellLayoutMode
    {
        /// <summary>
        /// Too small for anything honest. The shell says so and stops, rather than
        /// drawing a layout that overflows.
        /// </summary>
        Refuse,

        /// <summary>
        /// Editor and listing take the whole width. The side panes are hidden
        /// because a 79-column terminal cannot carry them without squeezing the
        /// code, and code that wraps is worse than code that is off screen.
        /// </summary>
        FullWidth,

        /// <summary>
        /// The side panes exist but are toggled overlays rather than columns. They
        /// can still be reached with F2 and F3; they are simply not stealing width
        /// from the editor while they are closed.
        /// </summary>
        Overlay,

        /// <summary>
        /// The file tree is a narrow drawer, the editor and the right pane are
        /// columns. Enough width for both panes of content, not enough for a
        /// readable tree beside them.
        /// </summary>
        CollapsedTree,

        /// <summary>
        /// Everything is a column at its full width: tree, editor, right pane.
        /// </summary>
        Full
    }

    /// <summary>
    /// The breakpoints, as a decision about a width and a height.
    ///
    /// This is a pure function of the terminal size, with no Terminal.Gui type in
    /// it, for two reasons. It is the part of the shell with the most arithmetic
    /// and the least tolerance for being wrong - a pane one column too wide wraps
    /// the code, and the code is the one thing that must never wrap - and it has
    /// to be assertable at exact widths, which a rendered window cannot be. The
    /// window asks this object where to put things; it does not decide.
    ///
    /// Widths, from the presentation specification:
    ///
    /// | columns | layout                                            |
    /// |---------|---------------------------------------------------|
    /// | &gt;= 120  | file tree (28) + editor + right pane (34)        |
    /// | 100-119 | tree collapsed to a 3-cell drawer               |
    /// | 80-99   | right pane becomes a toggled overlay            |
    /// | &lt; 80    | editor and listing full width                   |
    /// | &lt; 60    | minimum-width message, not a broken layout      |
    ///
    /// Heights follow the same logic. The status line is one row and the message
    /// line is one row, so a window below <see cref="StatusLines"/> rows has
    /// nothing left to draw in and is refused rather than rendered as a sliver.
    /// </summary>
    public sealed class ShellLayout
    {
        /// <summary>First width at which the tree, editor and right pane all fit.</summary>
        public const int WideMinimum = 120;

        /// <summary>First width at which the tree may be a drawer rather than a column.</summary>
        public const int MediumMinimum = 100;

        /// <summary>First width at which the side panes may be overlays.</summary>
        public const int NarrowMinimum = 80;

        /// <summary>Below this the shell explains itself instead of drawing.</summary>
        public const int AbsoluteMinimum = 60;

        /// <summary>Columns for the file tree in <see cref="ShellLayoutMode.Full"/>.</summary>
        public const int TreeColumnWidth = 28;

        /// <summary>Columns for the collapsed tree drawer.</summary>
        public const int CollapsedTreeWidth = 3;

        /// <summary>Columns for the right pane in <see cref="ShellLayoutMode.Full"/>.</summary>
        public const int RightColumnWidth = 34;

        /// <summary>Rows taken by the status line and the message line.</summary>
        public const int StatusLines = 2;

        /// <summary>
        /// Fewest rows at which the editor itself has room to be an editor. Below
        /// this the panes would each be a border and no content.
        /// </summary>
        public const int AbsoluteMinimumHeight = 6;

        private ShellLayout(int width, int height, ShellLayoutMode mode, int treeColumn, int rightColumn)
        {
            Width = width;
            Height = height;
            Mode = mode;
            TreeWidth = treeColumn;
            RightPaneWidth = rightColumn;
        }

        /// <summary>Terminal columns the decision was made for.</summary>
        public int Width { get; }

        /// <summary>Terminal rows the decision was made for.</summary>
        public int Height { get; }

        public ShellLayoutMode Mode { get; }

        /// <summary>Columns the tree occupies. Zero when the tree is hidden.</summary>
        public int TreeWidth { get; }

        /// <summary>Columns the right pane occupies. Zero when it is hidden.</summary>
        public int RightPaneWidth { get; }

        /// <summary>True when the shell must not draw panes at all.</summary>
        public bool IsRefused
        {
            get { return Mode == ShellLayoutMode.Refuse; }
        }

        /// <summary>True when the tree is a visible column rather than a drawer or hidden.</summary>
        public bool TreeIsColumn
        {
            get { return Mode == ShellLayoutMode.Full; }
        }

        /// <summary>True when the right pane is a visible column rather than an overlay or hidden.</summary>
        public bool RightPaneIsColumn
        {
            get { return Mode == ShellLayoutMode.Full; }
        }

        /// <summary>Columns left for the editor once both side panes have their share.</summary>
        public int EditorWidth
        {
            get
            {
                int remaining = Width - TreeWidth - RightPaneWidth;
                return remaining < 0 ? 0 : remaining;
            }
        }

        /// <summary>The decision for a terminal of this size.</summary>
        public static ShellLayout ForSize(int width, int height)
        {
            if (width < AbsoluteMinimum || height < AbsoluteMinimumHeight)
                return new ShellLayout(width, height, ShellLayoutMode.Refuse, 0, 0);

            if (width >= WideMinimum)
                return new ShellLayout(width, height, ShellLayoutMode.Full, TreeColumnWidth, RightColumnWidth);

            if (width >= MediumMinimum)
                return new ShellLayout(width, height, ShellLayoutMode.CollapsedTree, CollapsedTreeWidth, RightColumnWidth);

            if (width >= NarrowMinimum)
                return new ShellLayout(width, height, ShellLayoutMode.Overlay, 0, RightColumnWidth);

            return new ShellLayout(width, height, ShellLayoutMode.FullWidth, 0, 0);
        }

        /// <summary>The decision for a width only, at a height that always draws.</summary>
        public static ShellLayout ForWidth(int width)
        {
            return ForSize(width, AbsoluteMinimumHeight);
        }

        /// <summary>
        /// The message shown instead of a layout, when the terminal is too small.
        /// Says what is needed rather than only what is wrong, and is built to fit
        /// the width it will be drawn at.
        /// </summary>
        public IReadOnlyList<string> RefusalLines()
        {
            List<string> lines = new List<string>();

            if (Width >= AbsoluteMinimum)
            {
                lines.Add("Terminal too short: " + Height + " rows.");
                lines.Add("");
                lines.Add("WinASM65 needs at least " + AbsoluteMinimum + " rows, and");
                lines.Add(AbsoluteMinimumHeight + " with the panes open. Make the");
                lines.Add("window taller, or press F2 to close the panes.");
            }
            else
            {
                lines.Add("Terminal too narrow: " + Width + " columns.");
                lines.Add("");
                lines.Add("WinASM65 needs at least " + AbsoluteMinimum + " columns.");
                lines.Add("The panes need " + NarrowMinimum + " to be usable and");
                lines.Add(WideMinimum + " for all three at once.");
            }

            return lines;
        }

        public override string ToString()
        {
            return Mode + " at " + Width + "x" + Height
                + " (tree " + TreeWidth + ", right " + RightPaneWidth
                + ", editor " + EditorWidth + ")";
        }
    }
}