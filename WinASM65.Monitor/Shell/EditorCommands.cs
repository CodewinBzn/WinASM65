using System.Globalization;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// The command lines the editor keys issue.
    ///
    /// One class, holding nothing but strings, because the invariant this shell keeps
    /// is that every action is a command line into <see cref="MonitorSession"/>: the
    /// TUI adds no behaviour the REPL cannot express. A key handler that assembled its
    /// own line out of parts would be a second dialect, and a user who could not type
    /// what F5 did would be debugging the shell rather than the program.
    ///
    /// These are built here rather than written at the call site so that a test can
    /// assert the exact line a key issues, and so a change to one is a change to one
    /// place rather than to every handler that wanted the same verb.
    /// </summary>
    public static class EditorCommands
    {
        /// <summary>The slot F7 would save to.</summary>
        public const string DefaultStateSlot = "0";

        /// <summary>
        /// <c>ASSEMBLE &lt;source file&gt;</c>.
        ///
        /// The same verb, and the same relative path resolution, as a user typing it at
        /// the prompt: the session resolves against the directory it was given, so a
        /// listing and an assembly of the same name are the same file or the same
        /// failure.
        /// </summary>
        public static string Assemble(string sourceFile)
        {
            return "ASSEMBLE " + (sourceFile ?? string.Empty);
        }

        /// <summary><c>BREAK SET &lt;kind&gt; $addr</c>.</summary>
        public static string SetBreakpoint(string kind, int address)
        {
            return "BREAK SET " + (kind ?? string.Empty) + " " + Hex(address);
        }

        /// <summary><c>BREAK REMOVE &lt;kind&gt; $addr</c>.</summary>
        public static string RemoveBreakpoint(string kind, int address)
        {
            return "BREAK REMOVE " + (kind ?? string.Empty) + " " + Hex(address);
        }

        /// <summary>
        /// <c>BREAK CLEAR</c>: every breakpoint the emulator holds is released.
        ///
        /// Needed because the session refuses a single removal — Mesen exposes no way
        /// to read a callback handle back, so there is no one-callback removal to
        /// offer. A toggle that clears therefore has to clear and re-set, which is
        /// exactly what a REPL user would type.
        /// </summary>
        public static string ClearBreakpoints()
        {
            return "BREAK CLEAR";
        }

        /// <summary><c>RESUME</c>.</summary>
        public static string Resume()
        {
            return "RESUME";
        }

        /// <summary><c>STEP</c>.</summary>
        public static string Step()
        {
            return "STEP";
        }

        /// <summary>
        /// <c>STATE SAVE &lt;slot&gt;</c>.
        ///
        /// F7 is not bound in this build: <c>WinASM65.Monitor.Shell.StateCommands</c>,
        /// which owns the slot vocabulary, does not exist yet. When it lands,
        /// <c>StateCommands.Save(slot)</c> returns this line and the only change here
        /// is to delegate to it, so the slot naming stays in one place.
        /// </summary>
        public static string SaveState(string slot)
        {
            return "STATE SAVE " + (slot ?? DefaultStateSlot);
        }

        /// <summary>
        /// <c>STATE LOAD &lt;slot&gt;</c>. As <see cref="SaveState"/>: written out
        /// here, and to be replaced by <c>StateCommands.Load(slot)</c> when that type
        /// exists.
        /// </summary>
        public static string LoadState(string slot)
        {
            return "STATE LOAD " + (slot ?? DefaultStateSlot);
        }

        /// <summary>The monitor's own spelling of an address: <c>$C000</c>.</summary>
        public static string Hex(int address)
        {
            return "$" + address.ToString("X4", CultureInfo.InvariantCulture);
        }
    }
}
