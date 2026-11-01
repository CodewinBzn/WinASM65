// Abdelghani BOUZIANE
// WinASM65 - WDC 65C02 instruction set (Apple IIe, BBC Master, Commander X16, ...)

using System;
using System.Collections.Generic;

namespace WinASM65.Cpu
{
    public class Cpu65C02 : Cpu6502
    {
        private static readonly HashSet<string> CmosRelativeBranchMnemonics = CreateRelativeBranches();
        private static readonly HashSet<string> CmosAccumulatorMnemonics = CreateAccumulatorMnemonics();
        protected static readonly Dictionary<string, Dictionary<AddressingMode, byte>> CmosOpcodeMap = CreateCmosOpcodeMap();

        protected override Dictionary<string, Dictionary<AddressingMode, byte>> OpcodeMap
        {
            get { return CmosOpcodeMap; }
        }

        protected override HashSet<string> RelativeBranchMnemonics
        {
            get { return CmosRelativeBranchMnemonics; }
        }

        protected override HashSet<string> AccumulatorMnemonics
        {
            get { return CmosAccumulatorMnemonics; }
        }

        protected override InstructionInfo ParseIndexedIndirectX(string mnemonic, string expr)
        {
            if (string.Equals(mnemonic, "JMP", StringComparison.OrdinalIgnoreCase))
                return Form(mnemonic, AddressingMode.AbsoluteIndexedIndirect, 3, expr);
            return base.ParseIndexedIndirectX(mnemonic, expr);
        }

        protected override InstructionInfo ParseIndirect(string mnemonic, string expr)
        {
            if (string.Equals(mnemonic, "JMP", StringComparison.OrdinalIgnoreCase))
                return base.ParseIndirect(mnemonic, expr);

            byte zpOpc;
            if (TryGetOpcode(mnemonic, AddressingMode.ZeroPageIndirect, out zpOpc))
                return new InstructionInfo(AddressingMode.ZeroPageIndirect, zpOpc, 2, expr, true);

            return base.ParseIndirect(mnemonic, expr);
        }

        private static HashSet<string> CreateRelativeBranches()
        {
            HashSet<string> set = new HashSet<string>(NmosRelativeBranchMnemonics, StringComparer.OrdinalIgnoreCase);
            set.Add("BRA");
            return set;
        }

        private static HashSet<string> CreateAccumulatorMnemonics()
        {
            HashSet<string> set = new HashSet<string>(NmosAccumulatorMnemonics, StringComparer.OrdinalIgnoreCase);
            set.Add("INC");
            set.Add("DEC");
            return set;
        }

        private static Dictionary<string, Dictionary<AddressingMode, byte>> CreateCmosOpcodeMap()
        {
            Dictionary<string, Dictionary<AddressingMode, byte>> table = Copy(FromRows(NmosOpcodeTable));

            Set(table, "ADC", AddressingMode.ZeroPageIndirect, 0x72);
            Set(table, "AND", AddressingMode.ZeroPageIndirect, 0x32);
            Set(table, "CMP", AddressingMode.ZeroPageIndirect, 0xD2);
            Set(table, "EOR", AddressingMode.ZeroPageIndirect, 0x52);
            Set(table, "LDA", AddressingMode.ZeroPageIndirect, 0xB2);
            Set(table, "ORA", AddressingMode.ZeroPageIndirect, 0x12);
            Set(table, "SBC", AddressingMode.ZeroPageIndirect, 0xF2);
            Set(table, "STA", AddressingMode.ZeroPageIndirect, 0x92);
            Set(table, "JMP", AddressingMode.AbsoluteIndexedIndirect, 0x7C);

            Set(table, "BIT", AddressingMode.Immediate, 0x89);
            Set(table, "BIT", AddressingMode.ZeroPageX, 0x34);
            Set(table, "BIT", AddressingMode.AbsoluteX, 0x3C);

            Set(table, "INC", AddressingMode.Accumulator, 0x1A);
            Set(table, "DEC", AddressingMode.Accumulator, 0x3A);

            Set(table, "BRA", AddressingMode.Relative, 0x80);

            Set(table, "PHX", AddressingMode.Implicit, 0xDA);
            Set(table, "PHY", AddressingMode.Implicit, 0x5A);
            Set(table, "PLX", AddressingMode.Implicit, 0xFA);
            Set(table, "PLY", AddressingMode.Implicit, 0x7A);

            Set(table, "STZ", AddressingMode.ZeroPage, 0x64);
            Set(table, "STZ", AddressingMode.ZeroPageX, 0x74);
            Set(table, "STZ", AddressingMode.Absolute, 0x9C);
            Set(table, "STZ", AddressingMode.AbsoluteX, 0x9E);

            Set(table, "TRB", AddressingMode.ZeroPage, 0x14);
            Set(table, "TRB", AddressingMode.Absolute, 0x1C);
            Set(table, "TSB", AddressingMode.ZeroPage, 0x04);
            Set(table, "TSB", AddressingMode.Absolute, 0x0C);

            return table;
        }
    }
}
