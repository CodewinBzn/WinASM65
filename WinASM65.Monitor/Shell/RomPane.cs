using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using WinASM65.Monitor.Protocol;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// The ROM pane: $8000-$FFFF, showing cartridge/PRG-ROM.
    ///
    /// Read-only in this milestone. A pane that could not do it correctly yet is
    /// better as one that plainly does not do it yet.
    /// </summary>
    public sealed class RomPane
    {
        private readonly ListControl _list;
        private readonly IWindowControl _control;
        private readonly ShellTheme _theme;
        private readonly List<RomDumpRow> _rows = new List<RomDumpRow>();
        private readonly List<int> _faults = new List<int>();
        private readonly List<string> _labels = new List<string>();
        private int _contentWidth = 38;

        public RomPane(ShellTheme theme)
        {
            _theme = theme ?? ShellTheme.Default;
            Title = "ROM";

            _list = new ListControl
            {
                IsSelectable = true,
                Title = Title,
            };
            _control = Controls.ScrollablePanel()
                .AddControl(_list)
                .WithVerticalScroll(ScrollMode.None)
                .WithHorizontalScroll(ScrollMode.Scroll)
                .WithScrollbar(true)
                .Build();
        }

        public IWindowControl Control { get { return _list; } }

        public IWindowControl ScrollableControl { get { return _control; } }

        public string Title { get; set; }

        public int FaultCount { get { return _faults.Count; } }

        public IReadOnlyList<RomDumpRow> Rows { get { return _rows; } }

        public void Refresh(Func<int, int, byte[]> reader)
        {
            IReadOnlyList<RomDumpRow> rows = RomDump.Window(reader);
            RefreshRows(rows);
        }

        public void RefreshRows(IReadOnlyList<RomDumpRow> rows)
        {
            if (rows == null)
                rows = new List<RomDumpRow>();

            _rows.Clear();
            _faults.Clear();

            foreach (RomDumpRow row in rows)
            {
                if (row.Bytes.Count == 0)
                    _faults.Add(row.Address);

                _rows.Add(row);
            }

            _labels.Clear();
            foreach (RomDumpRow row in _rows)
                _labels.AddRange(FormatRows(row));

            _list.StringItems = new List<string>(_labels);
            int contentWidth = _labels.Count == 0
                ? _contentWidth
                : _labels.Max(label => label.Length) + 1;
            _list.Width = Math.Max(_contentWidth, contentWidth);
            _list.Invalidate();
        }

        public void SetContentWidth(int width)
        {
            _contentWidth = Math.Max(1, width);
            RefreshRows(_rows);
        }

        public void SetVisibleItems(int height)
        {
            _list.MaxVisibleItems = Math.Max(1, height);
        }

        public void InvalidateWindow(Window window)
        {
            window?.Invalidate(true);
        }

        private List<string> FormatRows(RomDumpRow row)
        {
            List<string> lines = new List<string>();
            if (row.Bytes.Count == 0)
            {
                lines.Add(row.Address.ToString("$X4", CultureInfo.InvariantCulture)
                    + "  unreadable");
                return lines;
            }

            StringBuilder text = new StringBuilder();
            text.Append('$').Append(row.Address.ToString("X4", CultureInfo.InvariantCulture)).Append("  ");

            // Single‑line representation of the ROM row
            // This mirrors the RamDumpRow.Text implementation.
            lines.Add(row.Text);
            return lines;
        }
    }

    /// <summary>One line of the ROM pane: an address and its bytes.</summary>
    public sealed class RomDumpRow
    {
        private readonly List<byte> _bytes = new List<byte>();

        public RomDumpRow(int address)
        {
            Address = address;
        }

        public int Address { get; }

        public IList<byte> Bytes
        {
            get { return _bytes; }
            set
            {
                _bytes.Clear();
                if (value != null)
                {
                    foreach (byte b in value)
                        _bytes.Add(b);
                }
            }
        }

            /// <summary>
            /// Single-line representation of the ROM row: address and hex bytes.
            /// </summary>
            public string Text
            {
                get
                {
                    var text = new StringBuilder();
                    text.Append('$').Append(Address.ToString("X4")).Append("  ");
                    foreach (byte b in _bytes)
                        text.Append(b.ToString("X2")).Append(' ');
                    return text.ToString();
                }
            }
        }


    /// <summary>
    /// Turns a memory snapshot into the rows the ROM pane draws.
    ///
    /// A pure function of bytes and an address, with no backend in it.
    /// The window is $8000-$FFFF (32 KiB for PRG-ROM).
    /// </summary>
    public static class RomDump
    {
        /// <summary>First address the pane shows.</summary>
        public const int WindowStart = 0x8000;

        /// <summary>Last address the pane shows, inclusive. 32 KiB: $8000-$FFFF.</summary>
        public const int WindowEnd = 0xFFFF;

        /// <summary>How many bytes the pane reads at once.</summary>
        public const int BytesPerRow = 16;

        public static int WindowLength
        {
            get { return WindowEnd - WindowStart + 1; }
        }

        /// <summary>
        /// Applies a push of byte changes to the rows that hold them, and returns
        /// the changed rows so the pane can redraw only those.
        /// </summary>
        public static IReadOnlyList<RomDumpRow> ApplyChanges(
            IReadOnlyList<RomDumpRow> rows,
            IReadOnlyList<(int Address, byte Value)> changes)
        {
            List<RomDumpRow> changed = new List<RomDumpRow>();
            if (rows == null || changes == null || changes.Count == 0)
                return changed;

            // Group changes by row
            Dictionary<int, Dictionary<int, byte>> byRow = new Dictionary<int, Dictionary<int, byte>>();
            foreach ((int address, byte value) in changes)
            {
                if (address < WindowStart || address > WindowEnd)
                    continue;

                int rowIndex = (address - WindowStart) / BytesPerRow;
                int col = (address - WindowStart) % BytesPerRow;

                Dictionary<int, byte> rowChanges;
                if (!byRow.TryGetValue(rowIndex, out rowChanges))
                {
                    rowChanges = new Dictionary<int, byte>();
                    byRow[rowIndex] = rowChanges;
                }

                rowChanges[col] = value;
            }

            foreach (int rowIndex in byRow.Keys)
            {
                if (rowIndex < 0 || rowIndex >= rows.Count)
                    continue;

                RomDumpRow row = (RomDumpRow)rows[rowIndex];
                Dictionary<int, byte> rowChanges = byRow[rowIndex];
                foreach (int col in rowChanges.Keys)
                {
                    while (row.Bytes.Count <= col)
                        row.Bytes.Add(0);

                    row.Bytes[col] = rowChanges[col];
                }

                changed.Add(row);
            }

            return changed;
        }

        /// <summary>
        /// The rows for a snapshot whose first byte is at <paramref name="address"/>.
        /// </summary>
        public static IReadOnlyList<RomDumpRow> Rows(byte[] memory, int address)
        {
            List<RomDumpRow> rows = new List<RomDumpRow>();
            if (memory == null)
                return rows;

            for (int offset = 0; offset < memory.Length; offset += BytesPerRow)
            {
                int rowAddress = address + offset;
                if (rowAddress > WindowEnd)
                    break;

                RomDumpRow row = new RomDumpRow(rowAddress);
                int count = Math.Min(BytesPerRow, memory.Length - offset);
                for (int i = 0; i < count; i++)
                    row.Bytes.Add(memory[offset + i]);

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// The rows for the whole window, from a reader that may refuse.
        /// </summary>
        public static IReadOnlyList<RomDumpRow> Window(Func<int, int, byte[]> reader)
        {
            if (reader == null)
                return new List<RomDumpRow>();

            int span = WindowEnd - WindowStart + 1;
            int chunk = MonitorProtocol.MaxReadLength < span ? MonitorProtocol.MaxReadLength : span;

            byte[] whole = new byte[span];
            int got = 0;

            try
            {
                while (got < span)
                {
                    byte[] part = reader(WindowStart + got, chunk);
                    if (part == null || part.Length == 0)
                        break;

                    int take = part.Length < span - got ? part.Length : span - got;
                    Array.Copy(part, 0, whole, got, take);
                    got += take;

                    if (part.Length < chunk)
                        break;
                }
            }
            catch (MonitorException)
            {
                got = 0;
            }

            List<RomDumpRow> rows = new List<RomDumpRow>();

            if (got == 0)
            {
                for (int address = WindowStart; address <= WindowEnd; address += BytesPerRow)
                {
                    int length = Math.Min(BytesPerRow, WindowEnd - address + 1);

                    byte[] memory;
                    try
                    {
                        memory = reader(address, length);
                    }
                    catch (MonitorException)
                    {
                        memory = null;
                    }

                    if (memory == null || memory.Length == 0)
                        rows.Add(new RomDumpRow(address));
                    else
                        rows.AddRange(Rows(memory, address));
                }

                return rows;
            }

            // A short answer is not a failure either
            for (int address = WindowStart; address <= WindowEnd; address += BytesPerRow)
            {
                int offset = address - WindowStart;

                if (offset >= got)
                {
                    rows.Add(new RomDumpRow(address));
                    continue;
                }

                int length = BytesPerRow;
                if (length > got - offset)
                    length = got - offset;

                byte[] row = new byte[length];
                Array.Copy(whole, offset, row, 0, length);
                rows.AddRange(Rows(row, address));
            }

            return rows;
        }
    }
}