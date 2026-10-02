using System;
using System.Collections.Generic;
using System.Text;

namespace WinASM65.Monitor
{
    /// <summary>
    /// The address space of the emulated machine.
    ///
    /// These constants live here rather than in the protocol because they describe
    /// the machine, not the transport: a backend enforces them just as much as the
    /// server that speaks the protocol.
    /// </summary>
    public static class AddressSpace
    {
        /// <summary>
        /// A 6502 address is 16 bits. Outside that range the input is rejected
        /// before it reaches the emulator: "invalid address" tells the user they
        /// mistyped, whereas "outside the memory space" would make them think the
        /// machine had failed.
        /// </summary>
        public const int MaxAddress = 0xFFFF;

        public const int Size = 0x10000;
    }

    /// <summary>A breakpoint that has been set, as recorded by the monitor.</summary>
    public sealed class Breakpoint
    {
        public Breakpoint(int address, string kind)
        {
            Address = address;
            Kind = kind;
        }

        public int Address { get; private set; }
        public string Kind { get; private set; }

        public override string ToString()
        {
            return Kind + " $" + Address.ToString("X4");
        }
    }

    /// <summary>
    /// The breakpoints the monitor has set, and their correspondence with the
    /// emulator.
    ///
    /// Mesen exposes <c>addMemoryCallback</c>, which returns a handle, but offers
    /// no way to read back the list of active callbacks. Without tracking on the
    /// .NET side, the monitor could not say what it had set: <c>BREAK LIST</c>
    /// would be impossible, and a breakpoint set by mistake would stay forever.
    /// This class holds that list.
    ///
    /// Two invariants, both covered by tests:
    ///
    /// 1. No duplicates. Replaying <c>BREAK SET exec $C000</c> must not register a
    ///    second callback: the second is invisible to the user but consumes a
    ///    resource on the emulator side, and only <c>CLEAR</c> frees it.
    /// 2. No divergence. The backend is called <em>before</em> the entry is
    ///    recorded. If the emulator refuses the address, the monitor must not keep
    ///    a phantom breakpoint: it would display a breakpoint as set that never
    ///    triggers anything.
    /// </summary>
    public sealed class BreakpointSet
    {
        private readonly IMemoryBackend _backend;
        private readonly List<Breakpoint> _items = new List<Breakpoint>();

        public BreakpointSet(IMemoryBackend backend)
        {
            if (backend == null)
                throw new ArgumentNullException("backend");
            _backend = backend;
        }

        /// <summary>Breakpoints in the order they were set.</summary>
        public IList<Breakpoint> Items
        {
            get { return _items.AsReadOnly(); }
        }

        public int Count
        {
            get { return _items.Count; }
        }

        public bool Contains(int address, string kind)
        {
            return IndexOf(address, kind) >= 0;
        }

        /// <summary>
        /// Sets a breakpoint. Returns false if it was already there: that is a
        /// reached state, not an error, and the protocol must be able to report it
        /// without requiring a <c>CLEAR</c> first.
        /// </summary>
        public bool Add(int address, string kind)
        {
            if (string.IsNullOrEmpty(kind))
                throw new MonitorException("empty breakpoint kind");
            if (!IsKnownKind(kind))
                throw new MonitorException("unknown kind: " + kind);
            if (address < 0 || address > AddressSpace.MaxAddress)
                throw new AddressRangeException(address, 1);

            if (Contains(address, kind))
                return false;

            // Backend first: if it fails, the exception propagates and nothing is
            // recorded, so divergence between the list and the real machine is
            // impossible.
            _backend.AddBreakpoint(address, kind);
            _items.Add(new Breakpoint(address, kind));
            return true;
        }

        /// <summary>Removes one specific breakpoint. False if it did not exist.</summary>
        public bool Remove(int address, string kind)
        {
            int index = IndexOf(address, kind);
            if (index < 0)
                return false;

            _items.RemoveAt(index);

            // The interface has no single-callback removal: CLEAR then re-setting
            // the rest is the only correct path on the emulator side.
            _backend.ClearBreakpoints();
            foreach (Breakpoint item in _items)
                _backend.AddBreakpoint(item.Address, item.Kind);

            return true;
        }

        public void Clear()
        {
            _backend.ClearBreakpoints();
            _items.Clear();
        }

        /// <summary>
        /// Rendering for <c>BREAK LIST</c>, in the very syntax of
        /// <c>BREAK SET</c>: a list rendered this way can be replayed verbatim to
        /// restore exactly the same breakpoints.
        /// </summary>
        public string Describe()
        {
            if (_items.Count == 0)
                return "0";

            StringBuilder text = new StringBuilder();
            text.Append(_items.Count);
            foreach (Breakpoint item in _items)
                text.Append(' ').Append(item);
            return text.ToString();
        }

        private int IndexOf(int address, string kind)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Address == address
                    && string.Equals(_items[i].Kind, kind, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static bool IsKnownKind(string kind)
        {
            return string.Equals(kind, BreakpointKind.Read, StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, BreakpointKind.Write, StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, BreakpointKind.Exec, StringComparison.OrdinalIgnoreCase);
        }
    }
}