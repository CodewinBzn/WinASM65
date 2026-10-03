namespace WinASM65.Monitor.Abstractions
{
    /// <summary>
    /// The processor's registers and the cycle counter, as read from the machine.
    ///
    /// The numbers only. Nothing here decides what they mean, and in particular there
    /// is no "looks powered down" test: telling a machine that has not run from a
    /// machine that cannot be observed is the host's judgement, made where the
    /// capability flags are known, and putting it here would make the contract
    /// behave.
    ///
    /// The cycle counter is here for one reason. MesenCE 2.2.1 exposes no way to
    /// advance the emulated CPU from a script, so its cycle count never moves and its
    /// program counter stays at the reset vector with all of RAM reading as zero —
    /// indistinguishable from a broken bridge if memory is all you can see. A static
    /// cycle count is therefore two different facts: a deliberate pause on Mesen2,
    /// and a host that cannot run the machine at all. Those are separated by
    /// <see cref="ExecutionCapability.StepInstruction"/> and by whether the host
    /// asked for the pause, not by anything this type computes.
    /// </summary>
    public sealed class CpuState
    {
        /// <summary>Creates a processor state from the values the machine reported.</summary>
        /// <param name="pc">Program counter.</param>
        /// <param name="a">Accumulator.</param>
        /// <param name="x">X index register.</param>
        /// <param name="y">Y index register.</param>
        /// <param name="sp">Stack pointer.</param>
        /// <param name="ps">Processor status.</param>
        /// <param name="cycleCount">Elapsed emulated cycles, which never advance on a host that cannot step.</param>
        public CpuState(int pc, int a, int x, int y, int sp, int ps, long cycleCount)
        {
            Pc = pc;
            A = a;
            X = x;
            Y = y;
            Sp = sp;
            Ps = ps;
            CycleCount = cycleCount;
        }

        /// <summary>Program counter.</summary>
        public int Pc { get; private set; }

        /// <summary>Accumulator.</summary>
        public int A { get; private set; }

        /// <summary>X index register.</summary>
        public int X { get; private set; }

        /// <summary>Y index register.</summary>
        public int Y { get; private set; }

        /// <summary>Stack pointer.</summary>
        public int Sp { get; private set; }

        /// <summary>Processor status.</summary>
        public int Ps { get; private set; }

        /// <summary>Elapsed emulated cycles.</summary>
        public long CycleCount { get; private set; }
    }
}