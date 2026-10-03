using System;
using WinASM65.Monitor.Protocol;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// Builds the command lines for state capture/restore. Strings, not calls:
    /// every shell action is a typed line into MonitorSession.Execute, so the TUI
    /// gains no behaviour the REPL cannot express.
    /// </summary>
    public static class StateCommands
    {
        /// <summary>
        /// Builds a STATE SAVE command line.
        /// </summary>
        /// <param name="slot">Slot name for the shell's local bookkeeping. Must not be null, empty, or whitespace.</param>
        /// <returns>The command line "STATE SAVE" if valid; otherwise an error string starting with "ERR ".</returns>
        public static string Save(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot))
                return MonitorProtocol.ErrPrefix + " slot name must not be empty";

            return "STATE SAVE";
        }

        /// <summary>
        /// Builds a STATE LOAD command line.
        /// </summary>
        /// <param name="slot">Hex-encoded state data previously returned by STATE SAVE. Must not be null, empty, or whitespace, and must be valid hex.</param>
        /// <returns>The command line "STATE LOAD <hex>" if valid; otherwise an error string starting with "ERR ".</returns>
        public static string Load(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot))
                return MonitorProtocol.ErrPrefix + " slot data must not be empty";

            byte[] dummy;
            if (!MonitorProtocol.TryParseHex(slot, out dummy))
                return MonitorProtocol.ErrPrefix + " slot data is not valid hex";

            return "STATE LOAD " + slot;
        }
    }
}