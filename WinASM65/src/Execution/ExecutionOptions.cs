namespace WinASM65.Execution
{
    /// <summary>
    /// How a run is allowed to go on.
    /// <para>
    /// The budget is not decoration. A machine asked to run until a breakpoint is
    /// running code the debugger has not read, and if the program never reaches
    /// the breakpoint the run has to end somewhere: without a budget it ends by
    /// exhausting memory, or by freezing a user interface that is waiting for it.
    /// </para>
    /// <para>
    /// The two switches exist because "run to here" and "run with these
    /// watchpoints" are different questions. A caller watching a memory range it
    /// has just written, while resuming, would otherwise stop on its own write
    /// and appear to make no progress.
    /// </para>
    /// </summary>
    public sealed class ExecutionOptions
    {
        /// <summary>The budget the relocated test suite has always used.</summary>
        public const long DefaultMaxSteps = 200000;

        public ExecutionOptions()
        {
            MaxSteps = DefaultMaxSteps;
            CheckBreakpoints = true;
            CheckWatchpoints = true;
        }

        /// <summary>
        /// Instructions to execute before the run reports that the budget ran out.
        /// Zero or less runs nothing and stops at once.
        /// </summary>
        public long MaxSteps { get; set; }

        /// <summary>Stop when the program counter reaches a set breakpoint.</summary>
        public bool CheckBreakpoints { get; set; }

        /// <summary>Stop when the processor touches a watched address.</summary>
        public bool CheckWatchpoints { get; set; }

        public static ExecutionOptions Default
        {
            get { return new ExecutionOptions(); }
        }
    }
}
