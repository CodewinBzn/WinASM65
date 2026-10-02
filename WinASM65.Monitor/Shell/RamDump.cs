using System;
using System.Collections.Generic;
using System.Text;

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
    /// The window is $0000-$07FF. Below $0200 the zero page and the stack are
    /// marked, because an unlabelled dump of $0000-$07FF hides the one address that
    /// matters most of the time.
    /// </summary>
    public static class RamDump
    {
        /// <summary>First address the pane shows.</summary>
        public const int WindowStart = 0x0000;

        /// <summary>Last address the pane shows.</summary>
        public const int WindowEnd = 0x07FF;

        /// <summary>How many bytes the pane reads at once.</summary>
        public const int BytesPerRow = 16;

        public static int WindowLength
        {
            get { return WindowEnd - WindowStart + 1; }
        }

        /// <summary>
        /// Which region an address belongs to. A window outside $0000-$07FF is all
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
                    return "free RAM $0200-$07FF";
            }
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

            List<RamDumpRow> rows = new List<RamDumpRow>();

            // Read in rows rather than in one 2 KiB block. A machine whose RAM is
            // smaller than the window then fails on the rows it does not have
            // instead of failing the whole pane, and the user still sees the part
            // that is real.
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
                {
                    rows.Add(new RamDumpRow(address, RegionOf(address)));
                    continue;
                }

                rows.AddRange(Rows(memory, address));
            }

            return rows;
        }
    }
}