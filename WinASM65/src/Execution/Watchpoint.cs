using System;
using System.Globalization;

namespace WinASM65.Execution
{
    /// <summary>
    /// The kind of access a watchpoint reports.
    /// </summary>
    public enum WatchpointKind
    {
        /// <summary>Report the address when the processor reads it.</summary>
        Read = 0,

        /// <summary>Report the address when the processor writes it.</summary>
        Write
    }

    /// <summary>
    /// A range of addresses the machine reports every read or every write on.
    /// <para>
    /// The range is straight: both ends included, and the start must not be above
    /// the end. A range that wraps around the top of the address space, such as
    /// "$FF80-$0010", is two watchpoints rather than one, because a wrapped range
    /// would have to be tested on every access in a way that is easy to get
    /// backwards and hard to notice.
    /// </para>
    /// </summary>
    public sealed class Watchpoint
    {
        public Watchpoint(WatchpointKind kind, ushort start, ushort end)
        {
            if (!Enum.IsDefined(typeof(WatchpointKind), kind))
                throw new ArgumentException("unknown watchpoint kind: " + kind);
            if (start > end)
                throw new ArgumentException("the start address is above the end address");

            Kind = kind;
            Start = start;
            End = end;
        }

        public WatchpointKind Kind { get; private set; }

        public ushort Start { get; private set; }

        /// <summary>Included, like <see cref="Start"/>.</summary>
        public ushort End { get; private set; }

        public bool Contains(ushort address)
        {
            return address >= Start && address <= End;
        }

        public override string ToString()
        {
            string kind = Kind == WatchpointKind.Read ? "read" : "write";
            if (Start == End)
                return string.Format(CultureInfo.InvariantCulture, "{0} ${1:X4}", kind, Start);
            return string.Format(CultureInfo.InvariantCulture, "{0} ${1:X4}-${2:X4}", kind, Start, End);
        }
    }
}
