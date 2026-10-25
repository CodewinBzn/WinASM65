// WinASM65 - What each instruction does, in one sentence, and the flags it changes

using System;
using System.Collections.Generic;

namespace WinASM65.Cpu
{
    /// <summary>One instruction as a reference card shows it.</summary>
    public sealed class InstructionDescription
    {
        public InstructionDescription(string mnemonic, string name, string summary, string flags)
        {
            Mnemonic = mnemonic;
            Name = name;
            Summary = summary;
            Flags = flags;
        }

        public string Mnemonic { get; }

        /// <summary>The full name: "Load Accumulator".</summary>
        public string Name { get; }

        /// <summary>One sentence: what it does.</summary>
        public string Summary { get; }

        /// <summary>The status flags it changes ("N Z C"), empty when none.</summary>
        public string Flags { get; }
    }

    /// <summary>
    /// The name, a one-sentence summary and the affected flags of every mnemonic the
    /// assembler knows, on every CPU. The addressing modes, opcodes, lengths and
    /// cycles are not here: they come from the opcode tables (<see cref="InstructionDocs"/>).
    /// </summary>
    public static class InstructionDescriptions
    {
        private static readonly Dictionary<string, InstructionDescription> Table = Create();

        public static bool TryGet(string mnemonic, out InstructionDescription description)
        {
            description = null;
            if (string.IsNullOrEmpty(mnemonic))
                return false;
            string m = mnemonic.ToUpperInvariant();
            if (Table.TryGetValue(m, out description))
                return true;

            // The Rockwell bit instructions: one entry per family, the bit is the digit.
            if (m.Length == 4 && char.IsDigit(m[3]) && m[3] <= '7')
            {
                string bit = m.Substring(3);
                switch (m.Substring(0, 3))
                {
                    case "RMB":
                        description = new InstructionDescription(m, "Reset Memory Bit " + bit, "Clears bit " + bit + " of a zero-page byte.", "");
                        return true;
                    case "SMB":
                        description = new InstructionDescription(m, "Set Memory Bit " + bit, "Sets bit " + bit + " of a zero-page byte.", "");
                        return true;
                    case "BBR":
                        description = new InstructionDescription(m, "Branch on Bit " + bit + " Reset", "Branches to the target when bit " + bit + " of a zero-page byte is 0.", "");
                        return true;
                    case "BBS":
                        description = new InstructionDescription(m, "Branch on Bit " + bit + " Set", "Branches to the target when bit " + bit + " of a zero-page byte is 1.", "");
                        return true;
                }
            }
            return false;
        }

        private static Dictionary<string, InstructionDescription> Create()
        {
            Dictionary<string, InstructionDescription> t = new Dictionary<string, InstructionDescription>(StringComparer.OrdinalIgnoreCase);
            void Add(string m, string name, string summary, string flags) => t[m] = new InstructionDescription(m, name, summary, flags);

            // NMOS 6502, documented.
            Add("ADC", "Add with Carry", "Adds a byte and the carry to A.", "N V Z C");
            Add("AND", "Logical AND", "ANDs a byte into A.", "N Z");
            Add("ASL", "Arithmetic Shift Left", "Shifts A or a byte one bit left; bit 7 goes to the carry.", "N Z C");
            Add("BCC", "Branch if Carry Clear", "Branches when C = 0.", "");
            Add("BCS", "Branch if Carry Set", "Branches when C = 1.", "");
            Add("BEQ", "Branch if Equal", "Branches when Z = 1.", "");
            Add("BIT", "Bit Test", "ANDs A with a byte to set Z, and copies bits 7 and 6 of the byte to N and V.", "N V Z");
            Add("BMI", "Branch if Minus", "Branches when N = 1.", "");
            Add("BNE", "Branch if Not Equal", "Branches when Z = 0.", "");
            Add("BPL", "Branch if Plus", "Branches when N = 0.", "");
            Add("BRK", "Break", "Pushes PC+2 and the status, then jumps through the IRQ vector at $FFFE.", "B I");
            Add("BVC", "Branch if Overflow Clear", "Branches when V = 0.", "");
            Add("BVS", "Branch if Overflow Set", "Branches when V = 1.", "");
            Add("CLC", "Clear Carry", "Sets C to 0.", "C");
            Add("CLD", "Clear Decimal", "Sets D to 0: ADC and SBC work in binary.", "D");
            Add("CLI", "Clear Interrupt Disable", "Sets I to 0: IRQs are taken.", "I");
            Add("CLV", "Clear Overflow", "Sets V to 0.", "V");
            Add("CMP", "Compare A", "Compares A with a byte: sets the flags as A minus the byte would.", "N Z C");
            Add("CPX", "Compare X", "Compares X with a byte.", "N Z C");
            Add("CPY", "Compare Y", "Compares Y with a byte.", "N Z C");
            Add("DEC", "Decrement", "Subtracts 1 from a byte (or from A on the 65C02).", "N Z");
            Add("DEX", "Decrement X", "Subtracts 1 from X.", "N Z");
            Add("DEY", "Decrement Y", "Subtracts 1 from Y.", "N Z");
            Add("EOR", "Exclusive OR", "Exclusive-ORs a byte into A.", "N Z");
            Add("INC", "Increment", "Adds 1 to a byte (or to A on the 65C02).", "N Z");
            Add("INX", "Increment X", "Adds 1 to X.", "N Z");
            Add("INY", "Increment Y", "Adds 1 to Y.", "N Z");
            Add("JMP", "Jump", "Continues at an address, read from memory in the indirect forms.", "");
            Add("JSR", "Jump to Subroutine", "Pushes the return address minus one, then jumps.", "");
            Add("LDA", "Load A", "Loads A with a byte.", "N Z");
            Add("LDX", "Load X", "Loads X with a byte.", "N Z");
            Add("LDY", "Load Y", "Loads Y with a byte.", "N Z");
            Add("LSR", "Logical Shift Right", "Shifts A or a byte one bit right; bit 0 goes to the carry.", "N Z C");
            Add("NOP", "No Operation", "Does nothing (the undocumented forms read their operand).", "");
            Add("ORA", "Logical OR", "ORs a byte into A.", "N Z");
            Add("PHA", "Push A", "Pushes A on the stack.", "");
            Add("PHP", "Push Status", "Pushes the status register, with B set.", "");
            Add("PLA", "Pull A", "Pulls A from the stack.", "N Z");
            Add("PLP", "Pull Status", "Pulls the status register from the stack.", "N V D I Z C");
            Add("ROL", "Rotate Left", "Rotates A or a byte one bit left through the carry.", "N Z C");
            Add("ROR", "Rotate Right", "Rotates A or a byte one bit right through the carry.", "N Z C");
            Add("RTI", "Return from Interrupt", "Pulls the status, then the return address.", "N V D I Z C");
            Add("RTS", "Return from Subroutine", "Pulls the return address and continues after the JSR.", "");
            Add("SBC", "Subtract with Carry", "Subtracts a byte and the borrow (NOT C) from A.", "N V Z C");
            Add("SEC", "Set Carry", "Sets C to 1.", "C");
            Add("SED", "Set Decimal", "Sets D to 1: ADC and SBC work in BCD.", "D");
            Add("SEI", "Set Interrupt Disable", "Sets I to 1: IRQs are ignored.", "I");
            Add("STA", "Store A", "Stores A in memory.", "");
            Add("STX", "Store X", "Stores X in memory.", "");
            Add("STY", "Store Y", "Stores Y in memory.", "");
            Add("TAX", "Transfer A to X", "Copies A into X.", "N Z");
            Add("TAY", "Transfer A to Y", "Copies A into Y.", "N Z");
            Add("TSX", "Transfer SP to X", "Copies the stack pointer into X.", "N Z");
            Add("TXA", "Transfer X to A", "Copies X into A.", "N Z");
            Add("TXS", "Transfer X to SP", "Copies X into the stack pointer.", "");
            Add("TYA", "Transfer Y to A", "Copies Y into A.", "N Z");

            // 65C02 additions.
            Add("BRA", "Branch Always", "Branches unconditionally.", "");
            Add("PHX", "Push X", "Pushes X on the stack.", "");
            Add("PHY", "Push Y", "Pushes Y on the stack.", "");
            Add("PLX", "Pull X", "Pulls X from the stack.", "N Z");
            Add("PLY", "Pull Y", "Pulls Y from the stack.", "N Z");
            Add("STZ", "Store Zero", "Stores 0 in memory.", "");
            Add("TRB", "Test and Reset Bits", "Sets Z from A AND the byte, then clears in the byte the bits set in A.", "Z");
            Add("TSB", "Test and Set Bits", "Sets Z from A AND the byte, then sets in the byte the bits set in A.", "Z");
            Add("WAI", "Wait for Interrupt", "Stops the processor until an IRQ or NMI (W65C02S).", "");
            Add("STP", "Stop", "Stops the clock until a reset (W65C02S).", "");

            // NMOS undocumented, stable.
            Add("SLO", "Shift Left, OR", "ASL on the byte, then ORA with the result.", "N Z C");
            Add("RLA", "Rotate Left, AND", "ROL on the byte, then AND with the result.", "N Z C");
            Add("SRE", "Shift Right, EOR", "LSR on the byte, then EOR with the result.", "N Z C");
            Add("RRA", "Rotate Right, Add", "ROR on the byte, then ADC with the result.", "N V Z C");
            Add("SAX", "Store A AND X", "Stores A AND X in memory.", "");
            Add("LAX", "Load A and X", "Loads A and X with the same byte.", "N Z");
            Add("DCP", "Decrement, Compare", "DEC on the byte, then CMP with the result.", "N Z C");
            Add("ISC", "Increment, Subtract", "INC on the byte, then SBC with the result.", "N V Z C");
            Add("ANC", "AND, Carry from bit 7", "AND #imm, then copies N into C.", "N Z C");
            Add("ALR", "AND, Shift Right", "AND #imm, then LSR A.", "N Z C");
            Add("ARR", "AND, Rotate Right", "AND #imm, then ROR A, with C and V set from bits 6 and 5.", "N V Z C");
            Add("AXS", "A AND X minus immediate", "X = (A AND X) - #imm, without borrow; sets the flags as CMP would.", "N Z C");
            Add("LAS", "Load A, X and SP", "A, X and SP = the byte AND SP.", "N Z");
            Add("JAM", "Jam", "Halts the processor until a reset.", "");

            return t;
        }
    }
}
