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
        AbsoluteIndexedIndirect = 14     // 65C02 JMP (abs,X)
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

        public InstructionInfo WithExpression(string expr)
        {
            return new InstructionInfo(Mode, Opcode, Length, expr);
        }

        public InstructionInfo WithModeAndLength(AddressingMode mode, byte opcode, byte length)
        {
            return new InstructionInfo(mode, opcode, length, OperandExpression);
        }
    }
}
