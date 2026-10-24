using System;

namespace WinASM65.Monitor
{
    /// <summary>Why the machine stopped, as the bridge reported it.</summary>
    public enum StopReason
    {
        Unknown,
        Breakpoint,
        Step,
        Pause
    }

    /// <summary>
    /// One stop of the machine: why, where, and, when the host can tell, which
    /// cartridge byte the program counter is executing.
    ///
    /// <see cref="PrgOffset"/> is what separates two banks mapped at the same CPU
    /// address. Null means the host did not say, not that the address is in RAM.
    /// </summary>
    public sealed class StopInfo
    {
        public StopInfo(StopReason reason, int pc, int? prgOffset)
        {
            Reason = reason;
            Pc = pc;
            PrgOffset = prgOffset;
        }

        public StopReason Reason { get; }
        public int Pc { get; }
        public int? PrgOffset { get; }

        public override string ToString()
        {
            return Reason.ToString().ToLowerInvariant() + " at $" + Pc.ToString("X4")
                + (PrgOffset.HasValue ? " (PRG $" + PrgOffset.Value.ToString("X5") + ")" : "");
        }
    }

    /// <summary>
    /// A backend that tells when the machine stops and runs again.
    ///
    /// Kept apart from <see cref="IMemoryBackend"/>, like <see cref="ICpuStateSource"/>:
    /// a host that can read memory is not thereby able to report its run state.
    ///
    /// Both events fire on the connection's receiver thread. A handler must not
    /// send a command from there: the receiver is the thread that delivers the
    /// reply, so a handler that waits for one waits forever. Queue the work instead.
    /// </summary>
    public interface IExecutionEvents
    {
        /// <summary>Whether the host reports run state at all.</summary>
        bool ReportsExecutionState { get; }

        bool IsStopped { get; }

        /// <summary>The current stop, or null while the machine runs.</summary>
        StopInfo LastStop { get; }

        event Action<StopInfo> Stopped;
        event Action Resumed;
    }

    /// <summary>A backend that can remove one breakpoint rather than all of them.</summary>
    public interface IBreakpointRemoval
    {
        void RemoveBreakpoint(int address, string kind);
    }
}
