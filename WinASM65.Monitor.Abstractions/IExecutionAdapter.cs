using System;
using System.Collections.Generic;

namespace WinASM65.Monitor.Abstractions
{
    /// <summary>
    /// A machine the host can inspect and drive: memory by region and bank, the
    /// processor's state, and the execution control this backend measured itself able
    /// to perform.
    ///
    /// This is the contract the plan calls load-bearing. An interface compiled into
    /// both the host and a plugin is two interfaces: two <see cref="Type"/> objects
    /// with one name, so the cast of a plugin instance to this interface throws
    /// <see cref="InvalidCastException"/> at runtime, from a cast the compiler
    /// accepted. The host therefore loads every plugin into its own
    /// <c>AssemblyLoadContext</c> with a resolver that redirects this assembly to the
    /// host's copy, and
    /// <c>WinASM65.Monitor.Abstractions.Tests/PluginContractLoadingTests.cs</c>
    /// fails if that redirect stops working.
    ///
    /// Every member past <see cref="Capabilities"/> is a member the host may only
    /// call because a bit says it can. An adapter that cannot pause declares
    /// <see cref="ExecutionCapability.Pause"/> clear and the host never calls
    /// <see cref="Pause"/>; it does not implement it by throwing, because a
    /// capability learned from an exception is a capability the user has already
    /// pressed, and the flag set is also what the published capability table is
    /// generated from.
    ///
    /// What is deliberately absent: a property saying whether the machine is running.
    /// A Mesen2 paused on purpose and a MesenCE that has never advanced look
    /// identical from outside, and an adapter asked "are you running" would have to
    /// guess. The host knows, because it asked for the pause and it read the flags.
    ///
    /// No member has a body. The adapter translates; nothing here decides.
    /// </summary>
    public interface IExecutionAdapter : IDisposable
    {
        /// <summary>
        /// Name and version of the backend, for the status line and for refusing a
        /// plugin built against a different measurement.
        ///
        /// Reported by the plugin rather than assumed by the host: hard-coding
        /// "Mesen2" would let the monitor run against a host it was never measured
        /// against.
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// What this adapter can do, as measured on the build it is attached to.
        /// See <see cref="ExecutionCapability"/> for the flag set and the measurement
        /// behind each bit.
        /// </summary>
        ExecutionCapability Capabilities { get; }

        /// <summary>
        /// The address space as this adapter exposes it. Comparable against
        /// <see cref="ISystemProfile.AddressSpace"/>, and the difference between the
        /// two is worth stating: a profile describes the machine, an adapter
        /// describes what it could reach today.
        /// </summary>
        IReadOnlyList<MemoryRegion> Regions { get; }

        /// <summary>
        /// Reads <paramref name="length"/> bytes at <paramref name="where"/>, refusing
        /// rather than truncating a read that runs past the end of the bank: a
        /// short answer would shift every following byte and display false data.
        /// </summary>
        byte[] Read(RegionAddress where, int length);

        /// <summary>
        /// Writes at <paramref name="where"/>, or refuses the whole write by name if
        /// any byte is outside a writable region.
        ///
        /// Never partial and never silently accepted: a write that does not come back
        /// is a fault, not a success. MesenCE drops a write into PRG ROM without
        /// complaint, and both bridges answer a refusal naming the address and the
        /// offset rather than letting the caller believe the byte landed.
        /// </summary>
        void Write(RegionAddress where, byte[] bytes);

        /// <summary>
        /// The processor's registers and cycle counter.
        ///
        /// Callable only when <see cref="ExecutionCapability.CpuState"/> is set. When
        /// it is clear the adapter must refuse by name, as MesenCE's bridge does for
        /// the <c>emu.getCpuState</c> that does not exist in 2.2.1 — returning zeros
        /// would be indistinguishable from a machine sitting at its reset vector, and
        /// those are different facts.
        /// </summary>
        CpuState ReadCpuState();

        /// <summary>Stops the machine. Callable only with <see cref="ExecutionCapability.Pause"/>.</summary>
        void Pause();

        /// <summary>Restarts a paused machine. Callable only with <see cref="ExecutionCapability.Resume"/>.</summary>
        void Resume();

        /// <summary>Resets the machine. Callable only with <see cref="ExecutionCapability.Reset"/>.</summary>
        void Reset();

        /// <summary>
        /// Advances by one instruction. Callable only with
        /// <see cref="ExecutionCapability.StepInstruction"/>, which is exactly what a
        /// host whose <c>debug:step</c> returns without moving the program counter
        /// must not claim.
        /// </summary>
        void Step();

        /// <summary>Breaks at an address. Callable only with <see cref="ExecutionCapability.BreakpointExecution"/>.</summary>
        void SetBreakpoint(int address);

        /// <summary>Removes every execution breakpoint this adapter set.</summary>
        void ClearBreakpoints();

        /// <summary>
        /// Watches an access without letting it change anything. Callable only with
        /// <see cref="ExecutionCapability.WatchpointRead"/> or
        /// <see cref="ExecutionCapability.WatchpointWrite"/>, matching
        /// <paramref name="kind"/>.
        /// </summary>
        void SetWatchpoint(RegionAddress where, MemoryAccessKind kind);

        /// <summary>Removes every watchpoint this adapter set.</summary>
        void ClearWatchpoints();

        /// <summary>
        /// Captures the machine, registers and RAM alike. Callable only with
        /// <see cref="ExecutionCapability.StateSaveLoad"/>, which Mesen2 and MesenCE
        /// both set.
        /// </summary>
        byte[] SaveState();

        /// <summary>Restores a capture from <see cref="SaveState"/>.</summary>
        void LoadState(byte[] state);
    }
}