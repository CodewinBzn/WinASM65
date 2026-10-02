using System;

namespace WinASM65.Monitor
{
    /// <summary>
    /// Memory access and execution control for the emulated machine.
    ///
    /// This interface is the only contract between the monitor and an emulator. A
    /// real backend (the MesenCE Lua bridge) and a test backend implement the same
    /// thing, which lets the whole monitor be tested without launching an emulator.
    ///
    /// Absolute rule: every read goes through here. Emulators distinguish a read
    /// with side effects from a pure one, and using the wrong kind alters emulated
    /// state with no visible sign. A real backend must physically forbid the
    /// side-effecting variant rather than trusting its caller.
    /// </summary>
    public interface IMemoryBackend : IDisposable
    {
        /// <summary>Emulator name and version, for diagnostics and version checking.</summary>
        string EmulatorName { get; }

        /// <summary>Version identifier, compared against the version frozen in the plan.</summary>
        string EmulatorVersion { get; }

        bool IsRunning { get; }

        /// <summary>
        /// Reads <paramref name="length"/> bytes. Throws if the read runs past the
        /// valid range or the buffer is too small: a silently truncated read would
        /// produce a false display.
        /// </summary>
        byte[] Read(int address, int length);

        /// <summary>Atomic write, or an explicit refusal if any byte is out of range.</summary>
        void Write(int address, byte[] bytes);

        void Pause();

        void Resume();

        /// <summary>Advances by one instruction.</summary>
        void Step();

        void Reset();

        /// <summary>Sets a breakpoint. <paramref name="kind"/> is read, write or exec.</summary>
        void AddBreakpoint(int address, string kind);

        void ClearBreakpoints();

        /// <summary>Captures the full machine state, so it can be restored later.</summary>
        byte[] SaveState();

        void LoadState(byte[] state);
    }

    /// <summary>Breakpoint kind.</summary>
    public static class BreakpointKind
    {
        public const string Read = "read";
        public const string Write = "write";
        public const string Exec = "exec";
    }

    /// <summary>
    /// Monitor failures. Every failure must be named: the protocol forbids silent
    /// failures, on the same principle as operand range validation (P0).
    /// </summary>
    public class MonitorException : Exception
    {
        public MonitorException(string message) : base(message) { }
        public MonitorException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>An address outside the machine's memory space.</summary>
    public sealed class AddressRangeException : MonitorException
    {
        public int Address { get; private set; }
        public int Length { get; private set; }

        public AddressRangeException(int address, int length)
            : base("Access " + address + " + " + length + " is outside the memory space.")
        {
            Address = address;
            Length = length;
        }
    }
}