using System;

namespace WinASM65.Monitor.Abstractions
{
    /// <summary>
    /// What an execution adapter can actually do, declared rather than thrown.
    ///
    /// Every bit here exists because a measured backend sets it, and no bit exists
    /// that none of them does. The measurements are in <c>Bridge/bridge.lua</c>
    /// (MesenCE 2.2.1), <c>Bridge/bridge_mesen2.lua</c> (Mesen2 2.1.1) and
    /// <c>MameCapabilityTests</c> (MAME 0.289). They are not read from vendor
    /// documentation and they are not expected to hold for another build; a bit that
    /// no measured backend sets is a capability the host would grey out for everyone,
    /// which is a lie with no measurement behind it.
    ///
    /// Declared and not thrown because the user has to see it before pressing the
    /// key: the host greys out what the attached system cannot do, and this flag set
    /// is the data behind the published capability table. A capability discovered by
    /// catching <see cref="NotSupportedException"/> is a capability the user has
    /// already pressed.
    ///
    /// The three measured backends, for reference:
    ///
    /// <list type="bullet">
    /// <item>Mesen2 2.1.1 — <see cref="FullControl"/>.</item>
    /// <item>MesenCE 2.2.1 — <see cref="Memory"/> and <see cref="StateSaveLoad"/>, nothing else.</item>
    /// <item>MAME 0.289 — <see cref="Memory"/> and <see cref="CpuState"/>, nothing else.</item>
    /// </list>
    ///
    /// The last two are the reason this is a flag set rather than one interface with
    /// everything on it. MesenCE answers no execution control at all; MAME's
    /// <c>debug:step</c> returns without moving the program counter and its
    /// <c>debug:bpset</c> blocks indefinitely. Neither is a replacement for Mesen2
    /// and neither may present itself as one.
    /// </summary>
    [Flags]
    public enum ExecutionCapability
    {
        /// <summary>
        /// Nothing. Declared so "this backend does none of it" is expressible without
        /// a magic value, which is what a <c>(ExecutionCapability)0</c> cast would be.
        /// </summary>
        None = 0,

        /// <summary>
        /// Memory can be read. Set by all three measured backends: Mesen2 and MesenCE
        /// through <c>emu.read</c>, MAME through the program space, which holds the
        /// cartridge's PRG ROM at <c>$8000</c>.
        /// </summary>
        MemoryRead = 1 << 0,

        /// <summary>
        /// Memory can be written. Set by Mesen2 and by MAME, both verified by reading
        /// the bytes back after the write rather than trusting the return.
        ///
        /// Set for MesenCE too, and the asymmetry is deliberate: <c>emu.write</c>
        /// exists there and RAM accepts it, while a write into PRG ROM is dropped
        /// silently. Per-space writability is not a capability, it is a property of
        /// the region: see <see cref="MemoryRegion.IsWritable"/>.
        /// </summary>
        MemoryWrite = 1 << 1,

        /// <summary>
        /// The processor's registers and cycle counter can be read.
        ///
        /// Set by Mesen2, whose <c>emu.getState</c> returns a flat table keyed
        /// <c>cpu.pc</c> through <c>cpu.cycleCount</c>, and by MAME, whose PC, A, X,
        /// Y, SP and P are all readable.
        ///
        /// Clear for MesenCE: <c>emu.getCpuState</c> does not exist in 2.2.1. That is
        /// why the bridge answers a named refusal rather than zeros, since a frozen
        /// machine and a CPU that cannot be observed produce the same empty reading.
        /// </summary>
        CpuState = 1 << 2,

        /// <summary>
        /// The machine can be stopped. Set by Mesen2 alone, and it is what makes a
        /// Mesen2 read deterministic. Clear for MesenCE, where <c>emu.pause</c> does
        /// not exist, and clear for MAME, which was not measured for it.
        /// </summary>
        Pause = 1 << 3,

        /// <summary>
        /// The machine can be restarted after a pause. Set by Mesen2, whose
        /// <c>emu.resume</c> works from inside an event callback. Clear for MesenCE,
        /// where it refuses calls made outside a callback.
        /// </summary>
        Resume = 1 << 4,

        /// <summary>
        /// The machine can be reset. Set by Mesen2. Clear for MesenCE, where
        /// <c>emu.reset</c> changes global state with no callback context and the
        /// bridge reserves it for manual validation.
        /// </summary>
        Reset = 1 << 5,

        /// <summary>
        /// The machine can be advanced by one instruction.
        ///
        /// Set by Mesen2, where stepping is exact: nine cycles to <c>$C01A</c>, then
        /// three more to <c>$C01B</c>. Clear for MesenCE, where <c>emu.step</c> refuses
        /// calls made outside a callback, and clear for MAME, where <c>debug:step</c>
        /// returns without error while leaving the program counter where it was. That
        /// measurement is the reason MAME stays non-blocking and in no way a
        /// replacement for Mesen2.
        /// </summary>
        StepInstruction = 1 << 6,

        /// <summary>
        /// Execution can be broken at an address. Set by Mesen2: <c>BREAK SET exec
        /// $C01A</c> answers <c>OK</c> and <c>DISASM $C01A 4</c> then reads back
        /// <c>PHA/TXA/PHA/TYA</c> at that address. Clear for MesenCE, where
        /// <c>emu.addMemoryCallback</c> refuses every function including the named
        /// ones, and clear for MAME, where <c>debug:bpset</c> blocks indefinitely.
        /// </summary>
        BreakpointExecution = 1 << 7,

        /// <summary>
        /// Reads can be watched. Set by Mesen2, whose <c>emu.addMemoryCallback</c>
        /// accepts <c>callbackType.read</c>. Clear for both other backends: on MesenCE
        /// that call refuses every function, and MAME's watch API sits behind the same
        /// blocking debug interface that already stalled the breakpoint probe.
        ///
        /// A watchpoint must refuse to let the access change anything. A watchpoint
        /// that alters emulated state is not a watchpoint.
        /// </summary>
        WatchpointRead = 1 << 8,

        /// <summary>
        /// Writes can be watched. Set by Mesen2, whose
        /// <c>emu.addMemoryCallback</c> accepts <c>callbackType.write</c>. Clear for
        /// the same two reasons as <see cref="WatchpointRead"/>.
        /// </summary>
        WatchpointWrite = 1 << 9,

        /// <summary>
        /// The machine can be captured and restored. Set by Mesen2 through
        /// <c>emu.getState</c> and <c>emu.setState</c>, and by MesenCE through
        /// <c>emu.getState</c>, <c>emu.setState</c> and <c>emu.loadSavestate</c>.
        /// MesenCE is therefore memory-only for execution and still snapshottable,
        /// which is what its bridge actually does: it serves memory and snapshots.
        /// Clear for MAME, which was not measured for it.
        /// </summary>
        StateSaveLoad = 1 << 10,

        /// <summary>
        /// Memory and nothing else. The vocabulary for a host that can look at a
        /// machine but not drive it, which is MesenCE 2.2.1 and MAME 0.289 before
        /// their other bits are added.
        ///
        /// A composite, not a capability: it names a set so the host and the
        /// published table can say it, and no plugin sets it on its own.
        /// </summary>
        Memory = MemoryRead | MemoryWrite,

        /// <summary>
        /// Every capability above. Mesen2 2.1.1 and no other measured backend: it is
        /// the only host that pauses, resets, breaks, watches, steps and snapshots.
        ///
        /// A composite, not a capability. Declaring it is how a backend says "full
        /// control" in one word instead of listing eleven bits, and how the
        /// MesenCE-versus-Mesen2 difference stays visible as a difference.
        /// </summary>
        FullControl = MemoryRead | MemoryWrite | CpuState | Pause | Resume | Reset
            | StepInstruction | BreakpointExecution | WatchpointRead | WatchpointWrite
            | StateSaveLoad
    }
}