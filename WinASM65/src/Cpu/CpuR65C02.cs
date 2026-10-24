// WinASM65 - Rockwell R65C02 and WDC W65C02S

using System;
using System.Collections.Generic;

namespace WinASM65.Cpu
{
    /// <summary>
    /// The Rockwell R65C02: the 65C02 plus the bit instructions on a zero-page
    /// byte. RMBn/SMBn clear or set bit n; BBRn/BBSn branch when bit n is reset
    /// or set, with two operands, <c>BBR0 zp,target</c>.
    /// </summary>
    public class CpuR65C02 : Cpu65C02
    {
        private static readonly Dictionary<string, Dictionary<AddressingMode, byte>> RockwellMap = CreateOpcodeMap();

        private static readonly HashSet<string> RockwellBitBranches = CreateBitBranches();

        protected override Dictionary<string, Dictionary<AddressingMode, byte>> OpcodeMap
        {
            get { return RockwellMap; }
        }

        protected override HashSet<string> BitBranchMnemonics
        {
            get { return RockwellBitBranches; }
        }

        protected static Dictionary<string, Dictionary<AddressingMode, byte>> CreateOpcodeMap()
        {
            Dictionary<string, Dictionary<AddressingMode, byte>> table = Copy(CmosOpcodeMap);
            AddBitInstructions(table);
            return table;
        }

        protected static void AddBitInstructions(Dictionary<string, Dictionary<AddressingMode, byte>> table)
        {
            for (int bit = 0; bit < 8; bit++)
            {
                Set(table, "RMB" + bit, AddressingMode.ZeroPage, (byte)(0x07 + bit * 0x10));
                Set(table, "SMB" + bit, AddressingMode.ZeroPage, (byte)(0x87 + bit * 0x10));
                Set(table, "BBR" + bit, AddressingMode.ZeroPageRelative, (byte)(0x0F + bit * 0x10));
                Set(table, "BBS" + bit, AddressingMode.ZeroPageRelative, (byte)(0x8F + bit * 0x10));
            }
        }

        private static HashSet<string> CreateBitBranches()
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int bit = 0; bit < 8; bit++)
            {
                set.Add("BBR" + bit);
                set.Add("BBS" + bit);
            }
            return set;
        }
    }

    /// <summary>
    /// The WDC W65C02S: the Rockwell set plus STP (stop the clock) and WAI (wait
    /// for an interrupt). This is the 65C02 of current production and of the
    /// Commander X16.
    /// </summary>
    public class CpuW65C02 : CpuR65C02
    {
        private static readonly Dictionary<string, Dictionary<AddressingMode, byte>> WdcMap = CreateWdcMap();

        protected override Dictionary<string, Dictionary<AddressingMode, byte>> OpcodeMap
        {
            get { return WdcMap; }
        }

        private static Dictionary<string, Dictionary<AddressingMode, byte>> CreateWdcMap()
        {
            Dictionary<string, Dictionary<AddressingMode, byte>> table = CreateOpcodeMap();
            Set(table, "WAI", AddressingMode.Implicit, 0xCB);
            Set(table, "STP", AddressingMode.Implicit, 0xDB);
            return table;
        }
    }
}
