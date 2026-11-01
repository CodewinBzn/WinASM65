// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - 6502 CPU Addressing Modes and Instruction Information

namespace WinASM65.Cpu
{
    public enum AddressingMode
    {
        None = -1,
        Implicit = 0,        // OPC
        Accumulator = 1,     // OPC A
        Immediate = 2,       // OPC #byte
        Absolute = 3,        // OPC word
        AbsoluteX = 4,       // OPC word,X
        AbsoluteY = 5,       // OPC word,Y
        ZeroPage = 6,        // OPC byte
        ZeroPageX = 7,       // OPC byte,X
        ZeroPageY = 8,       // OPC byte,Y
        Indirect = 9,        // OPC (word)
        IndirectX = 10,      // OPC (byte,X)
        IndirectY = 11,      // OPC (byte),Y
        Relative = 12,       // OPC byte
        ZeroPageIndirect = 13,           // 65C02 OPC (zp)
        AbsoluteIndexedIndirect = 14,    // 65C02 JMP (abs,X)
        ZeroPageRelative = 15            // R65C02 BBRn/BBSn zp,target
    }

    public struct InstructionInfo
    {
        public AddressingMode Mode { get; private set; }
        public byte Opcode { get; private set; }
        public byte Length { get; private set; }
        public string OperandExpression { get; private set; }

        public InstructionInfo(AddressingMode mode, byte opcode, byte length, string operandExpression = null)
            : this()
        {
            Mode = mode;
            Opcode = opcode;
            Length = length;
            OperandExpression = operandExpression;
        }

        /// <summary>
        /// Whether the CPU has this mnemonic in this addressing mode. When false,
        /// <see cref="Opcode"/> means nothing: every byte value is an opcode somewhere,
        /// so no byte can stand for "unsupported".
        /// </summary>
        public bool HasOpcode { get; private set; }

        /// <summary>The branch target of <c>BBRn zp,target</c>; null for one-operand forms.</summary>
        public string SecondOperandExpression { get; private set; }

        public InstructionInfo WithSecondOperand(string expression)
        {
            InstructionInfo copy = this;
            copy.SecondOperandExpression = expression;
            return copy;
        }

        public InstructionInfo(AddressingMode mode, byte opcode, byte length, string operandExpression, bool hasOpcode)
            : this(mode, opcode, length, operandExpression)
        {
            HasOpcode = hasOpcode;
        }

        public InstructionInfo WithExpression(string expr)
        {
            return new InstructionInfo(Mode, Opcode, Length, expr, HasOpcode);
        }

        public InstructionInfo WithModeAndLength(AddressingMode mode, byte opcode, byte length)
        {
            return new InstructionInfo(mode, opcode, length, OperandExpression, true);
        }
    }

    public static class AddressingModes
    {
        /// <summary>The name a diagnostic uses for a mode, as the 6502 literature spells it.</summary>
        public static string Describe(AddressingMode mode)
        {
            switch (mode)
            {
                case AddressingMode.Implicit: return "implied";
                case AddressingMode.Accumulator: return "accumulator";
                case AddressingMode.Immediate: return "immediate (#)";
                case AddressingMode.Absolute: return "absolute";
                case AddressingMode.AbsoluteX: return "absolute,X";
                case AddressingMode.AbsoluteY: return "absolute,Y";
                case AddressingMode.ZeroPage: return "zero page";
                case AddressingMode.ZeroPageX: return "zero page,X";
                case AddressingMode.ZeroPageY: return "zero page,Y";
                case AddressingMode.Indirect: return "indirect ()";
                case AddressingMode.IndirectX: return "indexed indirect (zp,X)";
                case AddressingMode.IndirectY: return "indirect indexed (zp),Y";
                case AddressingMode.Relative: return "relative";
                case AddressingMode.ZeroPageIndirect: return "zero page indirect (zp)";
                case AddressingMode.AbsoluteIndexedIndirect: return "absolute indexed indirect (abs,X)";
                case AddressingMode.ZeroPageRelative: return "zero page, relative (zp,target)";
                default: return mode.ToString();
            }
        }

        /// <summary>The zero-page form of an absolute mode, or None.</summary>
        public static AddressingMode ZeroPageOf(AddressingMode mode)
        {
            switch (mode)
            {
                case AddressingMode.Absolute: return AddressingMode.ZeroPage;
                case AddressingMode.AbsoluteX: return AddressingMode.ZeroPageX;
                case AddressingMode.AbsoluteY: return AddressingMode.ZeroPageY;
                default: return AddressingMode.None;
            }
        }
    }
}
