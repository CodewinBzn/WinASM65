namespace WinASM65.Execution
{
    /// <summary>
    /// The cycles an NMOS 6502 instruction costs, by opcode.
    /// <para>
    /// The table is the published one, and it is the reason a user interface can
    /// show a machine that runs at a plausible speed instead of one whose counter
    /// only proves it is moving. Cycles are counted, not timed: nothing here reads
    /// a clock.
    /// </para>
    /// <para>
    /// Two things it deliberately does not claim. A page crossing on an indexed
    /// access costs one more cycle, and a branch that is taken costs one more, and
    /// neither is added, because a half-modelled timing is worse than a base count
    /// the reader knows the rule for. An opcode the core does not implement reads
    /// zero: the core refuses it before the count is ever consulted, and a zero
    /// here cannot be mistaken for an instruction that costs nothing.
    /// </para>
    /// <para>
    /// Two opcodes disagree with the published table on purpose. This core uses
    /// $FC and $FF, the two JSR forms whose target is only known at run time, and
    /// they cost what every other JSR costs.
    /// </para>
    /// </summary>
    internal static class Cpu6502CycleTable
    {
        private static readonly byte[] BaseCycles =
        {
            // 0x
            7, 6, 0, 0, 0, 3, 5, 0, 3, 2, 2, 0, 0, 4, 6, 0,
            // 1x
            2, 5, 0, 0, 0, 4, 6, 0, 2, 4, 0, 0, 0, 4, 7, 0,
            // 2x
            6, 6, 0, 0, 3, 3, 5, 0, 4, 2, 2, 0, 4, 4, 6, 0,
            // 3x
            2, 5, 0, 0, 0, 4, 6, 0, 2, 4, 0, 0, 0, 4, 7, 0,
            // 4x
            6, 6, 0, 0, 0, 3, 5, 0, 3, 2, 2, 0, 3, 4, 6, 0,
            // 5x
            2, 5, 0, 0, 0, 4, 6, 0, 2, 4, 0, 0, 0, 4, 7, 0,
            // 6x
            6, 6, 0, 0, 0, 3, 5, 0, 3, 2, 2, 0, 5, 4, 6, 0,
            // 7x
            2, 5, 0, 0, 0, 4, 6, 0, 2, 4, 0, 0, 0, 4, 7, 0,
            // 8x
            0, 6, 0, 0, 3, 3, 3, 0, 2, 0, 2, 0, 4, 4, 4, 0,
            // 9x
            2, 6, 0, 0, 4, 4, 4, 0, 2, 5, 2, 0, 0, 5, 0, 0,
            // Ax
            2, 6, 2, 0, 3, 3, 3, 0, 2, 2, 2, 0, 4, 4, 4, 0,
            // Bx
            2, 5, 0, 0, 4, 4, 4, 0, 2, 4, 2, 0, 4, 4, 4, 0,
            // Cx
            2, 6, 0, 0, 3, 3, 5, 0, 2, 2, 2, 0, 4, 4, 6, 0,
            // Dx
            2, 5, 0, 0, 0, 4, 6, 0, 2, 4, 0, 0, 0, 4, 7, 0,
            // Ex
            2, 6, 0, 0, 3, 3, 5, 0, 2, 2, 2, 0, 4, 4, 6, 0,
            // Fx
            2, 5, 0, 0, 0, 4, 6, 0, 2, 4, 0, 0, 0, 4, 7, 0
        };

        static Cpu6502CycleTable()
        {
            // JSR (abs,X) and JSR (zp),Y. Unimplemented on a stock NMOS part,
            // implemented here, and a call costs what a call costs.
            BaseCycles[0xFC] = 6;
            BaseCycles[0xFF] = 6;
        }

        /// <summary>The base cost of an opcode, zero when the core does not implement it.</summary>
        public static int Base(byte opcode)
        {
            return BaseCycles[opcode];
        }
    }
}
