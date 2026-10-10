using System;
using System.Collections.Generic;
using System.Text;
using WinASM65.Monitor.Protocol;

namespace WinASM65.Monitor.Shell
{
    /// <summary>The three regions a 6502 programmer reads constantly.</summary>
    public enum RamRegion
    {
        /// <summary>$0000-$00FF. The 256 bytes almost every program addresses by name.</summary>
        ZeroPage,

        /// <summary>$0100-$01FF. Where the stack grows down from $01FF.</summary>
        Stack,

        /// <summary>Everything else in the window: ordinary RAM, no special meaning.</summary>
        Free
    }

    /// <summary>One line of the RAM pane: an address, its bytes, and their characters.</summary>
    public sealed class RamDumpRow
    {
        private readonly List<byte> _bytes = new List<byte>();

        public RamDumpRow(int address, RamRegion region)
        {
            Address = address;
            Region = region;
        }

        public int Address { get; }

        public RamRegion Region { get; }

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
        /// The row as drawn: address, bytes, then the printable characters.
        ///
        /// The character column is why a non-printable byte is a dot and not a
        /// control character: a dump that emits $07 to the terminal has replaced
        /// the display with a beep, and the address column is the only thing that
        /// survives it.
        /// </summary>
        public string Text
        {
            get
            {
                StringBuilder text = new StringBuilder();
                text.Append('$').Append(Address.ToString("X4"));
                text.Append("  ");

                foreach (byte value in _bytes)
                    text.Append(value.ToString("X2")).Append(' ');

                text.Append('|');
                foreach (byte value in _bytes)
                    text.Append(Printable(value));
                text.Append('|');

                return text.ToString();
            }
        }

        private static char Printable(byte value)
        {
            return value >= 0x20 && value < 0x7F ? (char)value : '.';
        }
    }

    /// <summary>
    /// Turns a memory snapshot into the rows the RAM pane draws.
    ///
    /// A pure function of bytes and an address, with no backend in it. That is the
    /// point: the interesting question about a hex dump — how the three regions are
    /// marked, what a non-printable byte becomes, what a short read does — can be
    /// asserted without a machine, and a pane that is correct against a fake is
    /// correct against a bridge.
    ///
    /// The window is $0000-$3FFF. Below $0200 the zero page and the stack are
    /// marked, because an unlabelled dump hides the one address that matters most
    /// of the time; the rest is ordinary RAM.
    /// </summary>
    public static class RamDump
    {
        /// <summary>First address the pane shows.</summary>
        public const int WindowStart = 0x0000;

        /// <summary>Last address the pane shows, inclusive. Sixteen kilobytes: the
        /// first two pages are zero page and stack, and the rest is ordinary RAM
        /// up to a quarter of the 64 KiB space the bridge exposes.</summary>
        public const int WindowEnd = 0x3FFF;

        /// <summary>How many bytes the pane reads at once.</summary>
        public const int BytesPerRow = 16;

        public static int WindowLength
        {
            get { return WindowEnd - WindowStart + 1; }
        }

        /// <summary>
        /// Which region an address belongs to. A window outside $0000-$3FFF is all
        /// <see cref="RamRegion.Free"/>, which is the honest answer for an address
        /// the pane was told to show rather than one it invented.
        /// </summary>
        public static RamRegion RegionOf(int address)
        {
            if (address <= 0x00FF)
                return RamRegion.ZeroPage;
            if (address <= 0x01FF)
                return RamRegion.Stack;
            return RamRegion.Free;
        }

        /// <summary>True when the next address starts a new region.</summary>
        public static bool StartsRegion(int address)
        {
            return address == 0x0000 || address == 0x0100 || address == 0x0200;
        }

        public static string DescribeRegion(RamRegion region)
        {
            switch (region)
            {
                case RamRegion.ZeroPage:
                    return "zero page $0000-$00FF";
                case RamRegion.Stack:
                    return "stack $0100-$01FF";
                default:
                    return "free RAM $0200-$3FFF";
            }
        }

        /// <summary>
        /// Applies a push of byte changes to the rows that hold them, and returns
        /// the changed rows so the pane can redraw only those.
        ///
        /// A change outside the window is ignored: the pane shows $0000-$3FFF and
        /// has no row for an address above it, so the change is simply not drawn.
        /// The pane keeps its existing rows for the addresses that did not change,
        /// which is the point of pushing rather than polling — a poll redraws
        /// 1024 rows for a one-byte change, this redraws one.
        /// </summary>
        public static IReadOnlyList<RamDumpRow> ApplyChanges(
            IReadOnlyList<RamDumpRow> rows,
            IReadOnlyList<(int Address, byte Value)> changes)
        {
            List<RamDumpRow> changed = new List<RamDumpRow>();
            if (rows == null || changes == null || changes.Count == 0)
                return changed;

            // Group changes by row, so a row that received several bytes is
            // redrawn once rather than once per byte.
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

                RamDumpRow row = (RamDumpRow)rows[rowIndex];
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
        ///
        /// A snapshot shorter than a full row is drawn as the short row it is, with
        /// no invented padding: a trailing run of $00 that the machine never sent
        /// would be indistinguishable from real zeroes.
        /// </summary>
        public static IReadOnlyList<RamDumpRow> Rows(byte[] memory, int address)
        {
            List<RamDumpRow> rows = new List<RamDumpRow>();
            if (memory == null)
                return rows;

            for (int offset = 0; offset < memory.Length; offset += BytesPerRow)
            {
                int rowAddress = address + offset;
                if (rowAddress > WindowEnd)
                    break;

                RamDumpRow row = new RamDumpRow(rowAddress, RegionOf(rowAddress));
                int count = Math.Min(BytesPerRow, memory.Length - offset);
                for (int i = 0; i < count; i++)
                    row.Bytes.Add(memory[offset + i]);

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// The rows for the whole window, from a reader that may refuse.
        ///
        /// The reader is a delegate rather than an <c>IMemoryBackend</c> so this
        /// stays assertable with a lambda, and so the pane can decide its own read
        /// chunking. A refusal becomes a null result, which the pane renders as a
        /// fault in place; it never becomes an exception that leaves the shell.
        /// </summary>
        public static IReadOnlyList<RamDumpRow> Window(Func<int, int, byte[]> reader)
        {
            if (reader == null)
                return new List<RamDumpRow>();

            // One read per chunk, not one per row. The reader is a synchronous
            // round trip to the bridge, and this window is 1024 rows: asking row by
            // row cost 1024 round trips on the thread that draws, so the shell froze
            // for the length of the read. That is what made the pane look stuck and
            // scrolling feel like wading — nothing was wrong with the memory.
            //
            // The protocol takes an address and a length, so the pane asks for what
            // it is about to draw in one piece, capped by the same bound the bridge
            // enforces so a wider window chunks instead of being refused.
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
                // The span was refused outright, which is what a machine smaller than
                // the window does. Fall back to asking row by row so the pane still
                // shows the part that is real instead of losing all 1024 rows to one
                // refusal — the behaviour the per-row reader always had.
                got = 0;
            }

            List<RamDumpRow> rows = new List<RamDumpRow>();

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
                        rows.Add(new RamDumpRow(address, RegionOf(address)));
                    else
                        rows.AddRange(Rows(memory, address));
                }

                return rows;
            }

            // A short answer is not a failure either: the rows past what came back are
            // marked unreadable rather than taking the pane down with them.
            for (int address = WindowStart; address <= WindowEnd; address += BytesPerRow)
            {
                int offset = address - WindowStart;

                if (offset >= got)
                {
                    rows.Add(new RamDumpRow(address, RegionOf(address)));
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