using System;
using System.Collections.Generic;
using System.Globalization;

namespace WinASM65.Cpu
{
    /// <summary>
    /// Couplage opcode -> (mnemonique, mode) obtenu par inversion de la table d'opcodes.
    /// </summary>
    public sealed class OpcodeInfo
    {
        public string Mnemonic { get; private set; }
        public AddressingMode Mode { get; private set; }
        public int Length { get; private set; }

        public OpcodeInfo(string mnemonic, AddressingMode mode)
        {
            Mnemonic = mnemonic;
            Mode = mode;
            Length = Disassembler.ModeLength(mode);
        }
    }

    /// <summary>
    /// Une instruction desassemblee. Pour un opcode inconnu, IsKnown vaut false et
    /// le rendu est ".byte $xx" : le desassembleur signale, il ne devine jamais.
    /// </summary>
    public sealed class DisassembledInstruction
    {
        public ushort Address { get; private set; }
        public byte Opcode { get; private set; }
        public bool IsKnown { get; private set; }
        public string Mnemonic { get; private set; }
        public AddressingMode Mode { get; private set; }
        public int Length { get; private set; }
        public ushort? TargetAddress { get; private set; }
        public string Text { get; private set; }
        public byte[] Bytes { get; private set; }

        internal DisassembledInstruction(ushort address, byte opcode, OpcodeInfo info, byte[] bytes, string text, ushort? target)
        {
            Address = address;
            Opcode = opcode;
            Bytes = bytes;
            Text = text;
            TargetAddress = target;

            if (info == null)
            {
                IsKnown = false;
                Mnemonic = null;
                Mode = AddressingMode.None;
                Length = 1;
            }
            else
            {
                IsKnown = true;
                Mnemonic = info.Mnemonic;
                Mode = info.Mode;
                Length = info.Length;
            }
        }
    }

    /// <summary>
    /// Desassembleur 6502 / 65C02 construit par inversion de la table d'opcodes du CPU.
    ///
    /// L'inversion est la seule source de verite : ce que l'assembleur emet est relu
    /// par la meme table. En particulier la forme zero-page est restituee telle quelle
    /// (l'assembleur optimise deja vers zero-page), ce qui rend
    /// desassembler puis reassembler idempotent.
    /// </summary>
    public sealed class Disassembler
    {
        private readonly Dictionary<byte, OpcodeInfo> _index;

        public Disassembler(ICpuInstructionSet cpu)
        {
            if (cpu == null)
                throw new ArgumentNullException("cpu");

            _index = BuildIndex(cpu.InstructionTable);
        }

        /// <summary>
        /// Inversion of the table. Throws if an opcode maps to two distinct
        /// mnemonic/mode pairs: silently disassembling wrong is worse than refusing
        /// to start.
        /// </summary>
        private static Dictionary<byte, OpcodeInfo> BuildIndex(IReadOnlyDictionary<string, byte[]> table)
        {
            Dictionary<byte, OpcodeInfo> index = new Dictionary<byte, OpcodeInfo>();
            foreach (KeyValuePair<string, byte[]> pair in table)
            {
                byte[] row = pair.Value;
                if (row == null)
                    continue;

                // La longueur de la rangee vaut 13 en NMOS et 15 en CMOS : les modes
                // ZeroPageIndirect et AbsoluteIndexedIndirect sont propres au 65C02.
                for (int mode = 0; mode < row.Length; mode++)
                {
                    byte opcode = row[mode];
                    if (opcode == 0xFF)
                        continue;

                    OpcodeInfo info = new OpcodeInfo(pair.Key.ToUpperInvariant(), (AddressingMode)mode);
                    OpcodeInfo existing;
                    if (index.TryGetValue(opcode, out existing))
                    {
                        if (!string.Equals(existing.Mnemonic, info.Mnemonic, StringComparison.Ordinal)
                            || existing.Mode != info.Mode)
                        {
                            throw new InvalidOperationException(
                                "Opcode $" + opcode.ToString("X2", CultureInfo.InvariantCulture)
                                + " ambiguous: " + existing.Mnemonic + "/" + existing.Mode
                                + " et " + info.Mnemonic + "/" + info.Mode + ".");
                        }
                        continue;
                    }

                    index[opcode] = info;
                }
            }
            return index;
        }

        public bool TryLookup(byte opcode, out OpcodeInfo info)
        {
            return _index.TryGetValue(opcode, out info);
        }

        /// <summary>
        /// Desassemble une instruction a <paramref name="offset"/> dans <paramref name="memory"/>.
        /// Si le tampon est trop court pour l'instruction complete, l'instruction est rendue
        /// avec les octets disponibles et Length reste celui de l'instruction : le manque
        /// est visible plutot que masque.
        /// </summary>
        public DisassembledInstruction Disassemble(ushort address, byte[] memory, int offset)
        {
            if (memory == null)
                throw new ArgumentNullException("memory");
            if (offset < 0 || offset >= memory.Length)
                throw new ArgumentOutOfRangeException("offset");

            byte opcode = memory[offset];
            OpcodeInfo info;
            if (!_index.TryGetValue(opcode, out info))
            {
                return new DisassembledInstruction(
                    address, opcode, null, new byte[] { opcode },
                    ".byte $" + opcode.ToString("X2", CultureInfo.InvariantCulture), null);
            }

            int length = info.Length;
            int available = Math.Min(length, memory.Length - offset);
            byte[] bytes = new byte[available];
            Array.Copy(memory, offset, bytes, 0, available);

            byte lo = available > 1 ? bytes[1] : (byte)0;
            byte hi = available > 2 ? bytes[2] : (byte)0;
            ushort word = (ushort)(lo | (hi << 8));

            string operand = RenderOperand(info.Mode, word, lo);
            ushort? target = null;
            if (info.Mode == AddressingMode.Relative)
            {
                sbyte delta = unchecked((sbyte)lo);
                target = (ushort)(address + 2 + delta);
                operand = "$" + target.Value.ToString("X4", CultureInfo.InvariantCulture);
            }

            string text = operand.Length == 0
                ? info.Mnemonic
                : info.Mnemonic + " " + operand;

            return new DisassembledInstruction(address, opcode, info, bytes, text, target);
        }

        /// <summary>
        /// Rendu de l'operande, symetrique exact de ParseOperand. Hexadecimal en
        /// majuscules comme le reste du projet.
        /// </summary>
        public static string RenderOperand(AddressingMode mode, ushort word, byte lo)
        {
            switch (mode)
            {
                case AddressingMode.Implicit:
                    return string.Empty;
                case AddressingMode.Accumulator:
                    // Volontairement vide : ParseOperand ne reconnait pas l'operande
                    // explicite "A" et retomberait sur Absolute (3 octets). Rendre "ASL A"
                    // produirait donc un reassemblage faux, de 3 octets au lieu de 1.
                    // ParseOperand(mnemonic, "") donne bien Accumulator pour les quatre
                    // instructions concernees. Sans operand, l'aller-retour est exact.
                    return string.Empty;
                case AddressingMode.Immediate:
                    return "#$" + Hex(lo, 2);
                case AddressingMode.Absolute:
                    return "$" + Hex(word, 4);
                case AddressingMode.AbsoluteX:
                    return "$" + Hex(word, 4) + ",x";
                case AddressingMode.AbsoluteY:
                    return "$" + Hex(word, 4) + ",y";
                case AddressingMode.ZeroPage:
                    return "$" + Hex(lo, 2);
                case AddressingMode.ZeroPageX:
                    return "$" + Hex(lo, 2) + ",x";
                case AddressingMode.ZeroPageY:
                    return "$" + Hex(lo, 2) + ",y";
                case AddressingMode.Indirect:
                    return "($" + Hex(word, 4) + ")";
                case AddressingMode.IndirectX:
                    return "($" + Hex(lo, 2) + ",x)";
                case AddressingMode.IndirectY:
                    return "($" + Hex(lo, 2) + "),y";
                case AddressingMode.ZeroPageIndirect:
                    return "($" + Hex(lo, 2) + ")";
                case AddressingMode.AbsoluteIndexedIndirect:
                    return "($" + Hex(word, 4) + ",x)";
                default:
                    return string.Empty;
            }
        }

        public static int ModeLength(AddressingMode mode)
        {
            switch (mode)
            {
                case AddressingMode.Implicit:
                case AddressingMode.Accumulator:
                    return 1;
                case AddressingMode.Immediate:
                case AddressingMode.ZeroPage:
                case AddressingMode.ZeroPageX:
                case AddressingMode.ZeroPageY:
                case AddressingMode.IndirectX:
                case AddressingMode.IndirectY:
                case AddressingMode.Relative:
                case AddressingMode.ZeroPageIndirect:
                    return 2;
                default:
                    return 3;
            }
        }

        private static string Hex(int value, int digits)
        {
            string text = ((ushort)value).ToString("X" + digits, CultureInfo.InvariantCulture);
            while (text.Length < digits)
                text = "0" + text;
            return text;
        }
    }
}