// WinASM65 - NMOS 6502 with its stable undocumented opcodes

using System.Collections.Generic;

namespace WinASM65.Cpu
{
    /// <summary>
    /// The NMOS 6502 plus the undocumented opcodes that behave the same on every
    /// NMOS part, as used on the NES and the C64 (names follow ca65's 6502X).
    /// <para>
    /// Left out on purpose: the unstable opcodes whose result depends on the chip
    /// or on analog effects (ANE/XAA $8B, LXA $AB, SHA, SHX, SHY, TAS), and the
    /// duplicate encodings of an instruction that already has one (SBC $EB, ANC
    /// $2B, the other NOP and JAM codes): each mnemonic and mode keeps one opcode,
    /// so the disassembler shows a duplicate as <c>.byte</c>.
    /// </para>
    /// </summary>
    public class Cpu6502X : Cpu6502
    {
        private static readonly Dictionary<string, Dictionary<AddressingMode, byte>> IllegalMap = CreateOpcodeMap();

        protected override Dictionary<string, Dictionary<AddressingMode, byte>> OpcodeMap
        {
            get { return IllegalMap; }
        }

        private static Dictionary<string, Dictionary<AddressingMode, byte>> CreateOpcodeMap()
        {
            Dictionary<string, Dictionary<AddressingMode, byte>> table = Copy(FromRows(NmosOpcodeTable));

            // Read-modify-write combined with an ALU operation: one row per family,
            // zp, zp,x, abs, abs,x, abs,y, (zp,x), (zp),y.
            Family(table, "SLO", 0x07, 0x17, 0x0F, 0x1F, 0x1B, 0x03, 0x13);
            Family(table, "RLA", 0x27, 0x37, 0x2F, 0x3F, 0x3B, 0x23, 0x33);
            Family(table, "SRE", 0x47, 0x57, 0x4F, 0x5F, 0x5B, 0x43, 0x53);
            Family(table, "RRA", 0x67, 0x77, 0x6F, 0x7F, 0x7B, 0x63, 0x73);
            Family(table, "DCP", 0xC7, 0xD7, 0xCF, 0xDF, 0xDB, 0xC3, 0xD3);
            Family(table, "ISC", 0xE7, 0xF7, 0xEF, 0xFF, 0xFB, 0xE3, 0xF3);

            Set(table, "SAX", AddressingMode.ZeroPage, 0x87);
            Set(table, "SAX", AddressingMode.ZeroPageY, 0x97);
            Set(table, "SAX", AddressingMode.Absolute, 0x8F);
            Set(table, "SAX", AddressingMode.IndirectX, 0x83);

            Set(table, "LAX", AddressingMode.ZeroPage, 0xA7);
            Set(table, "LAX", AddressingMode.ZeroPageY, 0xB7);
            Set(table, "LAX", AddressingMode.Absolute, 0xAF);
            Set(table, "LAX", AddressingMode.AbsoluteY, 0xBF);
            Set(table, "LAX", AddressingMode.IndirectX, 0xA3);
            Set(table, "LAX", AddressingMode.IndirectY, 0xB3);

            Set(table, "ANC", AddressingMode.Immediate, 0x0B);
            Set(table, "ALR", AddressingMode.Immediate, 0x4B);
            Set(table, "ARR", AddressingMode.Immediate, 0x6B);
            Set(table, "AXS", AddressingMode.Immediate, 0xCB);
            Set(table, "LAS", AddressingMode.AbsoluteY, 0xBB);

            // NOP with an operand: it reads it and does nothing with it.
            Set(table, "NOP", AddressingMode.Immediate, 0x80);
            Set(table, "NOP", AddressingMode.ZeroPage, 0x04);
            Set(table, "NOP", AddressingMode.ZeroPageX, 0x14);
            Set(table, "NOP", AddressingMode.Absolute, 0x0C);
            Set(table, "NOP", AddressingMode.AbsoluteX, 0x1C);

            // Halts the processor until reset.
            Set(table, "JAM", AddressingMode.Implicit, 0x02);

            return table;
        }

        private static void Family(Dictionary<string, Dictionary<AddressingMode, byte>> table, string mnemonic,
            byte zp, byte zpX, byte abs, byte absX, byte absY, byte indX, byte indY)
        {
            Set(table, mnemonic, AddressingMode.ZeroPage, zp);
            Set(table, mnemonic, AddressingMode.ZeroPageX, zpX);
            Set(table, mnemonic, AddressingMode.Absolute, abs);
            Set(table, mnemonic, AddressingMode.AbsoluteX, absX);
            Set(table, mnemonic, AddressingMode.AbsoluteY, absY);
            Set(table, mnemonic, AddressingMode.IndirectX, indX);
            Set(table, mnemonic, AddressingMode.IndirectY, indY);
        }
    }
}
