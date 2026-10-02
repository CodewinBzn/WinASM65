using System.Globalization;

namespace WinASM65.Execution
{
    /// <summary>
    /// Why a run stopped.
    /// <para>
    /// A debugger asks a machine that is going to stop two things: how far it
    /// got, and why. Enumerating the reasons instead of returning a bool means a
    /// caller has to handle the case it did not expect, which is the point: a
    /// run that ends for a reason nobody modelled is a different situation from a
    /// run that ended where it was asked to.
    /// </para>
    /// </summary>
    public enum ExecutionStopReason
    {
        /// <summary>The requested instructions were executed. Nothing else intervened.</summary>
        StepComplete = 0,

        /// <summary>The program counter reached an address the debugger asked to stop at.</summary>
        Breakpoint,

        /// <summary>The processor read an address covered by a read watchpoint.</summary>
        ReadWatchpoint,

        /// <summary>The processor wrote an address covered by a write watchpoint.</summary>
        WriteWatchpoint,

        /// <summary>
        /// The instruction budget ran out before any stop condition was met.
        /// <para>
        /// This is a stop like the others and not an error: a program that never
        /// reaches a breakpoint is a program the user is watching, and the honest
        /// answer is where it was, not an exception.
        /// </para>
        /// </summary>
        StepBudgetExhausted
    }

    /// <summary>
    /// Why execution stopped and where the processor was when it did.
    /// <para>
    /// <see cref="Address"/> is the address the stop is about, which is not always
    /// the program counter. A breakpoint is the program counter; a watchpoint is
    /// the address that was touched. A caller that displays the program counter
    /// from <c>ProcessorState</c> and this reason side by side gets the same story
    /// the machine actually had.
    /// </para>
    /// </summary>
    public sealed class ExecutionStop
    {
        public ExecutionStop(ExecutionStopReason reason, ushort address)
        {
            Reason = reason;
            Address = address;
        }

        public ExecutionStopReason Reason { get; private set; }

        /// <summary>
        /// The address the stop is about: the program counter for a step or a
        /// breakpoint, the touched address for a watchpoint.
        /// </summary>
        public ushort Address { get; private set; }

        public static ExecutionStop StepComplete(ushort programCounter)
        {
            return new ExecutionStop(ExecutionStopReason.StepComplete, programCounter);
        }

        public static ExecutionStop BreakpointReached(ushort address)
        {
            return new ExecutionStop(ExecutionStopReason.Breakpoint, address);
        }

        public static ExecutionStop WatchpointHit(WatchpointKind kind, ushort address)
        {
            return new ExecutionStop(
                kind == WatchpointKind.Read
                    ? ExecutionStopReason.ReadWatchpoint
                    : ExecutionStopReason.WriteWatchpoint,
                address);
        }

        public static ExecutionStop BudgetExhausted(ushort programCounter)
        {
            return new ExecutionStop(ExecutionStopReason.StepBudgetExhausted, programCounter);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} at ${1:X4}", Reason, Address);
        }
    }
}
