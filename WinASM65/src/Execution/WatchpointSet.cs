using System;
using System.Collections.Generic;

namespace WinASM65.Execution
{
    /// <summary>
    /// The watchpoints a machine is running under.
    /// <para>
    /// A set rather than a list of pairs, for the reason the monitor's breakpoint
    /// list is a set too: setting the same watchpoint twice must not consume a
    /// second check on every access, and the user has to be able to see that the
    /// second one changed nothing. A range that covers a narrower range already
    /// present is a duplicate as well, and is refused for the same reason.
    /// </para>
    /// <para>
    /// Matching keeps the order the watchpoints were added in, so a machine that
    /// touches two watched addresses in one instruction reports the first one
    /// asked about. Anything else would make the reported address depend on
    /// enumeration order.
    /// </para>
    /// </summary>
    public sealed class WatchpointSet
    {
        private readonly List<Watchpoint> _items = new List<Watchpoint>();

        /// <summary>The watchpoints, in the order they were added.</summary>
        public IList<Watchpoint> Items
        {
            get { return _items.AsReadOnly(); }
        }

        public int Count
        {
            get { return _items.Count; }
        }

        /// <summary>
        /// Adds a watchpoint. False when it was already there: that is a state the
        /// caller has already reached, not a failure.
        /// </summary>
        public bool Add(Watchpoint watchpoint)
        {
            if (watchpoint == null)
                throw new ArgumentNullException("watchpoint");
            if (Contains(watchpoint.Kind, watchpoint.Start, watchpoint.End))
                return false;

            _items.Add(watchpoint);
            return true;
        }

        /// <summary>Removes exactly this range. False when it was not there.</summary>
        public bool Remove(WatchpointKind kind, ushort start, ushort end)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Kind == kind && _items[i].Start == start && _items[i].End == end)
                {
                    _items.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public void Clear()
        {
            _items.Clear();
        }

        /// <summary>
        /// True when a watchpoint of this kind already covers this exact range,
        /// either as itself or as a wider range that includes it.
        /// </summary>
        public bool Contains(WatchpointKind kind, ushort start, ushort end)
        {
            if (start > end)
                return false;
            for (int i = 0; i < _items.Count; i++)
            {
                Watchpoint item = _items[i];
                if (item.Kind == kind && item.Start <= start && end <= item.End)
                    return true;
            }
            return false;
        }

        /// <summary>The first watchpoint of this kind covering the address, or null.</summary>
        public Watchpoint Match(WatchpointKind kind, ushort address)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Kind == kind && _items[i].Contains(address))
                    return _items[i];
            }
            return null;
        }
    }
}
