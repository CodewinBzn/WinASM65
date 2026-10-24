// WinASM65 - Cycle counts of every instruction form, for documentation and editors

using System;
using System.Collections.Generic;
using WinASM65.Execution;

namespace WinASM65.Cpu
{
    /// <summary>
    /// How many cycles an instruction form takes, and when it takes more.
    /// <para>
    /// The documented NMOS opcodes take their count from the execution core's own
    /// table (<see cref="Cpu6502CycleTable"/>), which a test checks against the
    /// published figures, so the editor and the emulator cannot disagree. The forms
    /// the core does not execute (65C02 additions, undocumented opcodes, Rockwell and
    /// WDC bit instructions) come from the manufacturers' data sheets and the NESdev
    /// tables, listed below.
    /// </para>
    /// </summary>
    public static class CycleTimings
    {
        public const string PageCrossNote = "+1 if a page is crossed";
        public const string BranchNote = "+1 if taken, +2 if taken across a page";
        public const string DecimalNote = "+1 in decimal mode";

        // Reads whose indexed address may cross a page: they take one more cycle then.
        private static readonly HashSet<string> PageCrossReads = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ADC", "AND", "CMP", "EOR", "LDA", "LDX", "LDY", "ORA", "SBC", "LAX", "LAS", "NOP", "BIT"
        };

        private static readonly HashSet<string> ReadModifyWrite = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SLO", "RLA", "SRE", "RRA", "DCP", "ISC"
        };

        /// <summary>
        /// The base cycle count of a form, null when it has none (JAM halts), and a
        /// note saying when it takes longer, null when never.
        /// </summary>
        public static void Of(ICpuInstructionSet cpu, string mnemonic, AddressingMode mode, byte opcode,
            out int? cycles, out string note)
        {
            string m = (mnemonic ?? string.Empty).ToUpperInvariant();
            bool cmos = cpu is Cpu65C02;
            cycles = BaseOf(cmos, m, mode, opcode);
            note = NoteOf(cmos, m, mode);
        }

        private static int? BaseOf(bool cmos, string m, AddressingMode mode, byte opcode)
        {
            // CMOS changes to NMOS opcodes.
            if (cmos)
            {
                if (m == "JMP" && mode == AddressingMode.Indirect)
                    return 6;
                if ((m == "ASL" || m == "LSR" || m == "ROL" || m == "ROR") && mode == AddressingMode.AbsoluteX)
                    return 6;
            }

            int extra;
            if (TryExtra(m, mode, out extra))
                return extra == 0 ? (int?)null : extra;

            int documented = Cpu6502CycleTable.Base(opcode);
            return documented > 0 ? documented : (int?)null;
        }

        /// <summary>Forms the execution core does not implement.</summary>
        private static bool TryExtra(string m, AddressingMode mode, out int cycles)
        {
            cycles = -1;
            if (ReadModifyWrite.Contains(m))
            {
                switch (mode)
                {
                    case AddressingMode.ZeroPage: cycles = 5; break;
                    case AddressingMode.ZeroPageX: cycles = 6; break;
                    case AddressingMode.Absolute: cycles = 6; break;
                    case AddressingMode.AbsoluteX:
                    case AddressingMode.AbsoluteY: cycles = 7; break;
                    case AddressingMode.IndirectX:
                    case AddressingMode.IndirectY: cycles = 8; break;
                }
                return cycles >= 0;
            }

            switch (m)
            {
                case "SAX":
                    cycles = mode == AddressingMode.ZeroPage ? 3 : mode == AddressingMode.IndirectX ? 6 : 4;
                    return true;
                case "LAX":
                    cycles = mode == AddressingMode.ZeroPage ? 3
                        : mode == AddressingMode.IndirectX ? 6
                        : mode == AddressingMode.IndirectY ? 5 : 4;
                    return true;
                case "ANC":
                case "ALR":
                case "ARR":
                case "AXS":
                    cycles = 2;
                    return true;
                case "LAS":
                    cycles = 4;
                    return true;
                case "JAM":
                    cycles = 0; // halts: no count
                    return true;
                case "NOP":
                    if (mode == AddressingMode.Implicit)
                        return false;
                    cycles = mode == AddressingMode.Immediate ? 2 : mode == AddressingMode.ZeroPage ? 3 : 4;
                    return true;
                case "BRA":
                    cycles = 3;
                    return true;
                case "PHX":
                case "PHY":
                    cycles = 3;
                    return true;
                case "PLX":
                case "PLY":
                    cycles = 4;
                    return true;
                case "STZ":
                    cycles = mode == AddressingMode.ZeroPage ? 3 : mode == AddressingMode.AbsoluteX ? 5 : 4;
                    return true;
                case "TRB":
                case "TSB":
                    cycles = mode == AddressingMode.ZeroPage ? 5 : 6;
                    return true;
                case "WAI":
                case "STP":
                    cycles = 3;
                    return true;
            }

            if (m.Length == 4 && (m.StartsWith("RMB") || m.StartsWith("SMB") || m.StartsWith("BBR") || m.StartsWith("BBS")))
            {
                cycles = 5;
                return true;
            }

            switch (mode)
            {
                case AddressingMode.ZeroPageIndirect: cycles = 5; return true;      // (zp), 65C02
                case AddressingMode.AbsoluteIndexedIndirect: cycles = 6; return true; // JMP (abs,X)
            }

            if (m == "BIT")
            {
                // The 65C02 forms; the NMOS ones are in the core's table.
                if (mode == AddressingMode.Immediate) { cycles = 2; return true; }
                if (mode == AddressingMode.ZeroPageX || mode == AddressingMode.AbsoluteX) { cycles = 4; return true; }
            }
            if ((m == "INC" || m == "DEC") && mode == AddressingMode.Accumulator)
            {
                cycles = 2;
                return true;
            }
            return false;
        }

        private static string NoteOf(bool cmos, string m, AddressingMode mode)
        {
            List<string> notes = new List<string>();

            if (m == "BRA")
                notes.Add("+1 if it crosses a page");
            else if (mode == AddressingMode.Relative || mode == AddressingMode.ZeroPageRelative)
                notes.Add(BranchNote);

            bool indexedRead = mode == AddressingMode.AbsoluteX || mode == AddressingMode.AbsoluteY || mode == AddressingMode.IndirectY;
            if (indexedRead && PageCrossReads.Contains(m))
                notes.Add(PageCrossNote);
            if (cmos && mode == AddressingMode.AbsoluteX && (m == "ASL" || m == "LSR" || m == "ROL" || m == "ROR"))
                notes.Add(PageCrossNote);

            if (cmos && (m == "ADC" || m == "SBC"))
                notes.Add(DecimalNote);
            if (m == "JAM")
                notes.Add("halts the processor until reset");

            return notes.Count == 0 ? null : string.Join(", ", notes);
        }
    }
}
