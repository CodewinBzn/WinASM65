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
        private static readonly Dictionary<string, byte[]> CmosOpcodeTable = CreateOpcodeTable();

        protected override Dictionary<string, byte[]> OpcodeTable
        {
            get { return CmosOpcodeTable; }
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
            {
                byte opc;
                TryGetOpcode(mnemonic, AddressingMode.AbsoluteIndexedIndirect, out opc);
                return new InstructionInfo(AddressingMode.AbsoluteIndexedIndirect, opc, 3, expr);
            }
            return base.ParseIndexedIndirectX(mnemonic, expr);
        }

        protected override InstructionInfo ParseIndirect(string mnemonic, string expr)
        {
            if (string.Equals(mnemonic, "JMP", StringComparison.OrdinalIgnoreCase))
                return base.ParseIndirect(mnemonic, expr);

            byte zpOpc;
            if (TryGetOpcode(mnemonic, AddressingMode.ZeroPageIndirect, out zpOpc))
                return new InstructionInfo(AddressingMode.ZeroPageIndirect, zpOpc, 2, expr);

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

        private static Dictionary<string, byte[]> CreateOpcodeTable()
        {
            Dictionary<string, byte[]> table = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, byte[]> pair in NmosOpcodeTable)
                table[pair.Key] = PadRow(pair.Value);

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

            table["BRA"] = EmptyRow();
            Set(table, "BRA", AddressingMode.Relative, 0x80);

            table["PHX"] = EmptyRow();
            Set(table, "PHX", AddressingMode.Implicit, 0xDA);
            table["PHY"] = EmptyRow();
            Set(table, "PHY", AddressingMode.Implicit, 0x5A);
            table["PLX"] = EmptyRow();
            Set(table, "PLX", AddressingMode.Implicit, 0xFA);
            table["PLY"] = EmptyRow();
            Set(table, "PLY", AddressingMode.Implicit, 0x7A);

            table["STZ"] = EmptyRow();
            Set(table, "STZ", AddressingMode.ZeroPage, 0x64);
            Set(table, "STZ", AddressingMode.ZeroPageX, 0x74);
            Set(table, "STZ", AddressingMode.Absolute, 0x9C);
            Set(table, "STZ", AddressingMode.AbsoluteX, 0x9E);

            table["TRB"] = EmptyRow();
            Set(table, "TRB", AddressingMode.ZeroPage, 0x14);
            Set(table, "TRB", AddressingMode.Absolute, 0x1C);
            table["TSB"] = EmptyRow();
            Set(table, "TSB", AddressingMode.ZeroPage, 0x04);
            Set(table, "TSB", AddressingMode.Absolute, 0x0C);

            return table;
        }

        private static byte[] PadRow(byte[] nmos)
        {
            byte[] row = EmptyRow();
            int count = Math.Min(nmos.Length, row.Length);
            for (int i = 0; i < count; i++)
                row[i] = nmos[i];
            return row;
        }

        private static byte[] EmptyRow()
        {
            return new byte[]
            {
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff
            };
        }

        private static void Set(Dictionary<string, byte[]> table, string mnemonic, AddressingMode mode, byte opcode)
        {
            byte[] row;
            if (!table.TryGetValue(mnemonic, out row))
            {
                row = EmptyRow();
                table[mnemonic] = row;
            }
            else if (row.Length < 15)
            {
                row = PadRow(row);
                table[mnemonic] = row;
            }
            row[(int)mode] = opcode;
        }
    }
}
