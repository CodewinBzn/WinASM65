using System.Globalization;

namespace WinASM65.Execution
{
    /// <summary>
    /// The registers and counters of a machine, read at one instant.
    /// <para>
    /// A snapshot, so a user interface can hold on to the state it was given and
    /// keep displaying it while the machine runs on. It carries exactly what a
    /// debugger panel shows: the six registers the processor keeps visible, the
    /// status byte, and the two counters that say the machine moved.
    /// </para>
    /// <para>
    /// <see cref="Ps"/> is the status byte a user reads, with the unused bit set and
    /// the break bit clear, so it is a value that can be pushed and compared rather
    /// than a bag of individual flags.
    /// </para>
    /// </summary>
    public sealed class ProcessorState
    {
        public ProcessorState(ushort pc, byte a, byte x, byte y, byte sp, byte ps, long cycles, long instructions)
        {
            Pc = pc;
            A = a;
            X = x;
            Y = y;
            Sp = sp;
            Ps = ps;
            Cycles = cycles;
            Instructions = instructions;
        }

        public ushort Pc { get; private set; }
        public byte A { get; private set; }
        public byte X { get; private set; }
        public byte Y { get; private set; }
        public byte Sp { get; private set; }

        /// <summary>The status register, as the processor would push it.</summary>
        public byte Ps { get; private set; }

        /// <summary>Clock cycles executed since the last reset.</summary>
        public long Cycles { get; private set; }

        /// <summary>Instructions executed since the last reset.</summary>
        public long Instructions { get; private set; }

        /// <summary>
        /// True for a machine sitting at $8000 after a handful of cycles, which is
        /// what a machine that has been reset and not run looks like.
        /// <para>
        /// It is the same reading a host that cannot advance its processor at all
        /// produces, and that is the reason it is worth computing here rather than
        /// left to each caller: a frozen machine and a cold machine are otherwise
        /// indistinguishable, and the difference is the user's next question.
        /// </para>
        /// </summary>
        public bool LooksPoweredDown
        {
            get { return Cycles <= 16 && Pc == 0x8000; }
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "PC=${0:X4}  A={1:X2}  X={2:X2}  Y={3:X2}  SP={4:X2}  PS={5:X2}  cycles={6}",
                Pc, A, X, Y, Sp, Ps, Cycles);
        }
    }
}
