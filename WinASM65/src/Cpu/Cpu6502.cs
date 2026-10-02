// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - 6502 Instruction Set Definition (Pure OOP, SOLID)

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WinASM65.Core;

namespace WinASM65.Cpu
{
    public interface ICpuInstructionSet
    {
        bool IsInstruction(string mnemonic);
        bool IsRelativeBranch(string mnemonic);
        bool IsAccumulatorInstruction(string mnemonic);
        bool TryGetOpcode(string mnemonic, AddressingMode mode, out byte opcode);
        IReadOnlyDictionary<string, byte[]> InstructionTable { get; }
        InstructionInfo ParseOperand(string mnemonic, string operand);
        bool TryOptimizeZeroPage(string mnemonic, AddressingMode currentMode, long value, out AddressingMode optimizedMode, out byte opcode, out byte length);
        bool TryCalculateRelativeOffset(long targetAddress, long instructionAddress, out byte offset, out string error);
    }

    public class Cpu6502 : ICpuInstructionSet
    {
        protected static readonly HashSet<string> NmosRelativeBranchMnemonics = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BCC", "BCS", "BEQ", "BMI", "BNE", "BPL", "BVC", "BVS"
        };

        protected static readonly HashSet<string> NmosAccumulatorMnemonics = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ASL", "LSR", "ROL", "ROR"
        };

        // Complete 6502 Opcode Matrix [Mnemonic -> Array of 13 bytes for each AddressingMode enum value 0..12]
        // 0xFF means unsupported mode.
        protected static readonly Dictionary<string, byte[]> NmosOpcodeTable = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            // Mode index: IMP=0, ACC=1, IMM=2, ABS=3, ABX=4, ABY=5, ZPG=6, ZPX=7, ZPY=8, IND=9, INX=10, INY=11, REL=12
            { "ADC", new byte[] {0xff, 0xff, 0x69, 0x6d, 0x7d, 0x79, 0x65, 0x75, 0xff, 0xff, 0x61, 0x71, 0xff } },
            { "AND", new byte[] {0xff, 0xff, 0x29, 0x2d, 0x3d, 0x39, 0x25, 0x35, 0xff, 0xff, 0x21, 0x31, 0xff } },
            { "ASL", new byte[] {0xff, 0x0a, 0xff, 0x0e, 0x1e, 0xff, 0x06, 0x16, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "BCC", new byte[] {0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x90 } },
            { "BCS", new byte[] {0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xb0 } },
            { "BEQ", new byte[] {0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xf0 } },
            { "BIT", new byte[] {0xff, 0xff, 0xff, 0x2c, 0xff, 0xff, 0x24, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "BMI", new byte[] {0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x30 } },
            { "BNE", new byte[] {0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xd0 } },
            { "BPL", new byte[] {0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x10 } },
            { "BRK", new byte[] {0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "BVC", new byte[] {0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x50 } },
            { "BVS", new byte[] {0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x70 } },
            { "CLC", new byte[] {0x18, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "CLD", new byte[] {0xd8, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "CLI", new byte[] {0x58, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "CLV", new byte[] {0xb8, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "CMP", new byte[] {0xff, 0xff, 0xc9, 0xcd, 0xdd, 0xd9, 0xc5, 0xd5, 0xff, 0xff, 0xc1, 0xd1, 0xff } },
            { "CPX", new byte[] {0xff, 0xff, 0xe0, 0xec, 0xff, 0xff, 0xe4, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "CPY", new byte[] {0xff, 0xff, 0xc0, 0xcc, 0xff, 0xff, 0xc4, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "DEC", new byte[] {0xff, 0xff, 0xff, 0xce, 0xde, 0xff, 0xc6, 0xd6, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "DEX", new byte[] {0xca, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "DEY", new byte[] {0x88, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "EOR", new byte[] {0xff, 0xff, 0x49, 0x4d, 0x5d, 0x59, 0x45, 0x55, 0xff, 0xff, 0x41, 0x51, 0xff } },
            { "INC", new byte[] {0xff, 0xff, 0xff, 0xee, 0xfe, 0xff, 0xe6, 0xf6, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "INX", new byte[] {0xe8, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "INY", new byte[] {0xc8, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "JMP", new byte[] {0xff, 0xff, 0xff, 0x4c, 0xff, 0xff, 0xff, 0xff, 0xff, 0x6c, 0xff, 0xff, 0xff } },
            { "JSR", new byte[] {0xff, 0xff, 0xff, 0x20, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "LDA", new byte[] {0xff, 0xff, 0xa9, 0xad, 0xbd, 0xb9, 0xa5, 0xb5, 0xff, 0xff, 0xa1, 0xb1, 0xff } },
            { "LDX", new byte[] {0xff, 0xff, 0xa2, 0xae, 0xff, 0xbe, 0xa6, 0xff, 0xb6, 0xff, 0xff, 0xff, 0xff } },
            { "LDY", new byte[] {0xff, 0xff, 0xa0, 0xac, 0xbc, 0xff, 0xa4, 0xb4, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "LSR", new byte[] {0xff, 0x4a, 0xff, 0x4e, 0x5e, 0xff, 0x46, 0x56, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "NOP", new byte[] {0xea, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "ORA", new byte[] {0xff, 0xff, 0x09, 0x0d, 0x1d, 0x19, 0x05, 0x15, 0xff, 0xff, 0x01, 0x11, 0xff } },
            { "PHA", new byte[] {0x48, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "PHP", new byte[] {0x08, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "PLA", new byte[] {0x68, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "PLP", new byte[] {0x28, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "ROL", new byte[] {0xff, 0x2a, 0xff, 0x2e, 0x3e, 0xff, 0x26, 0x36, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "ROR", new byte[] {0xff, 0x6a, 0xff, 0x6e, 0x7e, 0xff, 0x66, 0x76, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "RTI", new byte[] {0x40, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "RTS", new byte[] {0x60, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "SBC", new byte[] {0xff, 0xff, 0xe9, 0xed, 0xfd, 0xf9, 0xe5, 0xf5, 0xff, 0xff, 0xe1, 0xf1, 0xff } },
            { "SEC", new byte[] {0x38, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "SED", new byte[] {0xf8, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "SEI", new byte[] {0x78, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "STA", new byte[] {0xff, 0xff, 0xff, 0x8d, 0x9d, 0x99, 0x85, 0x95, 0xff, 0xff, 0x81, 0x91, 0xff } },
            { "STX", new byte[] {0xff, 0xff, 0xff, 0x8e, 0xff, 0xff, 0x86, 0xff, 0x96, 0xff, 0xff, 0xff, 0xff } },
            { "STY", new byte[] {0xff, 0xff, 0xff, 0x8c, 0xff, 0xff, 0x84, 0x94, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "TAX", new byte[] {0xaa, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "TAY", new byte[] {0xa8, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "TSX", new byte[] {0xba, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "TXA", new byte[] {0x8a, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "TXS", new byte[] {0x9a, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } },
            { "TYA", new byte[] {0x98, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff } }
        };

        protected virtual Dictionary<string, byte[]> OpcodeTable
        {
            get { return NmosOpcodeTable; }
        }

        /// <summary>
        /// Vue en lecture seule de la table d'opcodes de ce CPU. Exposee pour le
        /// désassembleur, qui doit connaître le couplage exact mnémonique/mode/opcode,
        /// y compris les extensions 65C02 absentes de la table NMOS.
        /// </summary>
        public System.Collections.Generic.IReadOnlyDictionary<string, byte[]> InstructionTable
        {
            get { return OpcodeTable; }
        }

        protected virtual HashSet<string> RelativeBranchMnemonics
        {
            get { return NmosRelativeBranchMnemonics; }
        }

        protected virtual HashSet<string> AccumulatorMnemonics
        {
            get { return NmosAccumulatorMnemonics; }
        }

        public bool IsInstruction(string mnemonic)
        {
            if (string.IsNullOrEmpty(mnemonic))
                return false;
            return OpcodeTable.ContainsKey(mnemonic);
        }

        public bool IsRelativeBranch(string mnemonic)
        {
            if (string.IsNullOrEmpty(mnemonic))
                return false;
            return RelativeBranchMnemonics.Contains(mnemonic);
        }

        public bool IsAccumulatorInstruction(string mnemonic)
        {
            if (string.IsNullOrEmpty(mnemonic))
                return false;
            return AccumulatorMnemonics.Contains(mnemonic);
        }

        public bool TryGetOpcode(string mnemonic, AddressingMode mode, out byte opcode)
        {
            opcode = 0xFF;
            if (string.IsNullOrEmpty(mnemonic))
                return false;

            byte[] table;
            if (OpcodeTable.TryGetValue(mnemonic, out table))
            {
                int index = (int)mode;
                if (index >= 0 && index < table.Length)
                {
                    opcode = table[index];
                    return opcode != 0xFF;
                }
            }
            return false;
        }

        public InstructionInfo ParseOperand(string mnemonic, string operand)
        {
            mnemonic = (mnemonic ?? string.Empty).ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(operand))
            {
                if (IsAccumulatorInstruction(mnemonic))
                {
                    byte opc;
                    TryGetOpcode(mnemonic, AddressingMode.Accumulator, out opc);
                    return new InstructionInfo(AddressingMode.Accumulator, opc, 1, string.Empty);
                }
                else
                {
                    byte opc;
                    TryGetOpcode(mnemonic, AddressingMode.Implicit, out opc);
                    return new InstructionInfo(AddressingMode.Implicit, opc, 1, string.Empty);
                }
            }

            if (IsRelativeBranch(mnemonic))
            {
                byte opc;
                TryGetOpcode(mnemonic, AddressingMode.Relative, out opc);
                return new InstructionInfo(AddressingMode.Relative, opc, 2, operand.Trim());
            }

            string clean = Regex.Replace(operand, @"\s+", "");

            if (clean.StartsWith("#"))
            {
                string expr = clean.Substring(1);
                byte opc;
                TryGetOpcode(mnemonic, AddressingMode.Immediate, out opc);
                return new InstructionInfo(AddressingMode.Immediate, opc, 2, expr);
            }

            if (clean.StartsWith("(") && clean.EndsWith(",x)", StringComparison.OrdinalIgnoreCase))
            {
                string expr = clean.Substring(1, clean.Length - 4);
                return ParseIndexedIndirectX(mnemonic, expr);
            }

            if (clean.StartsWith("(") && clean.EndsWith("),y", StringComparison.OrdinalIgnoreCase))
            {
                string expr = clean.Substring(1, clean.Length - 4);
                byte opc;
                TryGetOpcode(mnemonic, AddressingMode.IndirectY, out opc);
                return new InstructionInfo(AddressingMode.IndirectY, opc, 2, expr);
            }

            if (clean.StartsWith("(") && clean.EndsWith(")"))
            {
                string expr = clean.Substring(1, clean.Length - 2);
                return ParseIndirect(mnemonic, expr);
            }

            if (clean.EndsWith(",x", StringComparison.OrdinalIgnoreCase))
            {
                string expr = clean.Substring(0, clean.Length - 2);
                byte opc;
                TryGetOpcode(mnemonic, AddressingMode.AbsoluteX, out opc);
                return new InstructionInfo(AddressingMode.AbsoluteX, opc, 3, expr);
            }

            if (clean.EndsWith(",y", StringComparison.OrdinalIgnoreCase))
            {
                string expr = clean.Substring(0, clean.Length - 2);
                byte opc;
                TryGetOpcode(mnemonic, AddressingMode.AbsoluteY, out opc);
                return new InstructionInfo(AddressingMode.AbsoluteY, opc, 3, expr);
            }

            // Default to Absolute
            byte absOpc;
            TryGetOpcode(mnemonic, AddressingMode.Absolute, out absOpc);
            return new InstructionInfo(AddressingMode.Absolute, absOpc, 3, clean);
        }

        protected virtual InstructionInfo ParseIndexedIndirectX(string mnemonic, string expr)
        {
            byte opc;
            TryGetOpcode(mnemonic, AddressingMode.IndirectX, out opc);
            return new InstructionInfo(AddressingMode.IndirectX, opc, 2, expr);
        }

        protected virtual InstructionInfo ParseIndirect(string mnemonic, string expr)
        {
            byte opc;
            TryGetOpcode(mnemonic, AddressingMode.Indirect, out opc);
            return new InstructionInfo(AddressingMode.Indirect, opc, 3, expr);
        }

        public bool TryOptimizeZeroPage(string mnemonic, AddressingMode currentMode, long value, out AddressingMode optimizedMode, out byte opcode, out byte length)
        {
            optimizedMode = currentMode;
            opcode = 0xFF;
            length = 3;

            // Only optimize if value fits in a byte (0..255) and current mode is absolute
            if (value >= 0 && value <= 255)
            {
                AddressingMode candidateMode = AddressingMode.None;
                if (currentMode == AddressingMode.Absolute)
                    candidateMode = AddressingMode.ZeroPage;
                else if (currentMode == AddressingMode.AbsoluteX)
                    candidateMode = AddressingMode.ZeroPageX;
                else if (currentMode == AddressingMode.AbsoluteY)
                    candidateMode = AddressingMode.ZeroPageY;

                if (candidateMode != AddressingMode.None)
                {
                    byte zpOpc;
                    if (TryGetOpcode(mnemonic, candidateMode, out zpOpc) && zpOpc != 0xFF)
                    {
                        optimizedMode = candidateMode;
                        opcode = zpOpc;
                        length = 2;
                        return true;
                    }
                }
            }
            return false;
        }

        public bool TryCalculateRelativeOffset(long targetAddress, long instructionAddress, out byte offset, out string error)
        {
            offset = 0;
            error = null;

            // In 6502, relative branch offset is calculated relative to PC AFTER the branch instruction:
            // PC after 2-byte branch instruction = instructionAddress + 2.
            long delta = targetAddress - (instructionAddress + 2);

            if (delta > 127 || delta < -128)
            {
                error = ErrorCodes.REL_JUMP;
                return false;
            }

            // Signed 8-bit offset to byte
            offset = (byte)(sbyte)delta;
            return true;
        }
    }
}
