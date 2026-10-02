// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Instruction documentation derived from the CPU tables

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace WinASM65.Cpu
{
    /// <summary>
    /// What one assembler-accepted instruction form is: its addressing mode, the
    /// byte that encodes it and how many bytes it occupies in memory.
    /// <para>
    /// Every field is read from the CPU's own opcode table when the instance is
    /// built, so documentation cannot claim a mode the assembler will not emit.
    /// </para>
    /// </summary>
    public sealed class InstructionDocumentation
    {
        /// <summary>The mnemonic, upper case.</summary>
        public string Mnemonic { get; private set; }

        public AddressingMode Mode { get; private set; }

        /// <summary>The opcode byte the assembler emits for this form.</summary>
        public byte Opcode { get; private set; }

        /// <summary>
        /// Bytes occupied in memory, opcode included. This is
        /// <see cref="Disassembler.ModeLength"/> for the mode, which is the same
        /// function the disassembler uses to walk an image, so a documented
        /// length and a disassembled length cannot differ.
        /// </summary>
        public int Length { get; private set; }

        /// <summary>
        /// The operand shape, for a user interface to show: <c>#$nn</c>,
        /// <c>$nnnn,x</c>, <c>($nn),y</c> and so on. Empty for an implied
        /// operand.
        /// </summary>
        public string Syntax { get; private set; }

        /// <summary>
        /// Cycles taken, or null when unknown.
        /// <para>
        /// Always null today: no cycle data exists anywhere in this project, and
        /// a cycle count written here would be a second source of truth free of
        /// any check. It stays a nullable slot until the per-opcode cycle table
        /// the execution core needs arrives, at which point the agreement test
        /// in the suite is where it gets filled and verified.
        /// </para>
        /// </summary>
        public int? Cycles { get; private set; }

        public InstructionDocumentation(string mnemonic, AddressingMode mode, byte opcode, int length, string syntax)
        {
            Mnemonic = mnemonic;
            Mode = mode;
            Opcode = opcode;
            Length = length;
            Syntax = syntax;
        }

        public override string ToString()
        {
            return string.Format("{0} {1}  {2:X2}  {3} bytes", Mnemonic, Syntax, Opcode, Length);
        }
    }

    /// <summary>
    /// The instruction documentation an editor or a reference page reads.
    /// Nothing here is written out by hand: every entry is derived from the
    /// opcode table of the CPU it describes, and lengths come from the same
    /// function the disassembler walks an image with.
    /// </summary>
    public static class InstructionDocs
    {
        private static readonly ConditionalWeakTable<ICpuInstructionSet, IReadOnlyList<InstructionDocumentation>> Cache =
            new ConditionalWeakTable<ICpuInstructionSet, IReadOnlyList<InstructionDocumentation>>();

        // One instance per CPU, so that the no-argument forms hand back the same
        // table every call and a caller is free to hold on to it.
        private static readonly Cpu6502 DefaultCpu = new Cpu6502();
        private static readonly Cpu65C02 CmosCpu = new Cpu65C02();

        /// <summary>Every NMOS 6502 form the assembler accepts.</summary>
        public static IReadOnlyList<InstructionDocumentation> For6502
        {
            get { return ForCpu(DefaultCpu); }
        }

        /// <summary>Every 65C02 form, the NMOS ones included.</summary>
        public static IReadOnlyList<InstructionDocumentation> For65C02
        {
            get { return ForCpu(CmosCpu); }
        }

        /// <summary>
        /// Every form <paramref name="cpu"/> accepts, ordered by mnemonic and
        /// then by addressing mode, so a listing is stable between runs. One
        /// pass per CPU instance, kept until the instance goes away.
        /// </summary>
        public static IReadOnlyList<InstructionDocumentation> ForCpu(ICpuInstructionSet cpu)
        {
            if (cpu == null)
                return new List<InstructionDocumentation>();

            IReadOnlyList<InstructionDocumentation> cached;
            if (Cache.TryGetValue(cpu, out cached))
                return cached;

            List<InstructionDocumentation> docs = new List<InstructionDocumentation>();
            List<string> mnemonics = new List<string>(cpu.InstructionTable.Keys);
            mnemonics.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string mnemonic in mnemonics)
            {
                byte[] row = cpu.InstructionTable[mnemonic];
                for (int index = 0; index < row.Length; index++)
                {
                    byte opcode = row[index];
                    if (opcode == 0xFF)
                        continue;

                    AddressingMode mode = (AddressingMode)index;
                    docs.Add(new InstructionDocumentation(mnemonic.ToUpperInvariant(), mode, opcode,
                        Disassembler.ModeLength(mode), SyntaxFor(mode)));
                }
            }

            Cache.Add(cpu, docs);
            return docs;
        }

        /// <summary>
        /// The distinct mnemonics the NMOS 6502 accepts, upper case and sorted.
        /// This is what a highlighter classifies against when no CPU is named.
        /// </summary>
        public static IReadOnlyList<string> Mnemonics
        {
            get { return MnemonicsFor(DefaultCpu); }
        }

        /// <summary>
        /// The distinct mnemonics <paramref name="cpu"/> accepts, upper case and
        /// sorted. Null means the NMOS 6502, which is the CPU
        /// <see cref="AssemblerEngine"/> assembles for by default.
        /// </summary>
        public static IReadOnlyList<string> MnemonicsFor(ICpuInstructionSet cpu)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (InstructionDocumentation doc in ForCpu(cpu ?? DefaultCpu))
                names.Add(doc.Mnemonic);

            List<string> sorted = new List<string>(names);
            sorted.Sort(StringComparer.Ordinal);
            return sorted;
        }

        /// <summary>
        /// Whether <paramref name="token"/> is a mnemonic the assembler knows.
        /// Case-insensitive, as the assembler itself is. Null for
        /// <paramref name="cpu"/> asks about the NMOS 6502.
        /// </summary>
        public static bool IsMnemonic(string token, ICpuInstructionSet cpu = null)
        {
            return !string.IsNullOrEmpty(token) && new HashSet<string>(MnemonicsFor(cpu), StringComparer.OrdinalIgnoreCase).Contains(token);
        }

        /// <summary>
        /// The forms of one mnemonic, ordered by addressing mode. Empty when the
        /// assembler does not know the name.
        /// </summary>
        public static IReadOnlyList<InstructionDocumentation> ForMnemonic(string mnemonic, ICpuInstructionSet cpu = null)
        {
            List<InstructionDocumentation> found = new List<InstructionDocumentation>();
            if (string.IsNullOrEmpty(mnemonic))
                return found;

            IReadOnlyList<InstructionDocumentation> all = ForCpu(cpu ?? DefaultCpu);
            for (int i = 0; i < all.Count; i++)
            {
                if (string.Equals(all[i].Mnemonic, mnemonic, StringComparison.OrdinalIgnoreCase))
                    found.Add(all[i]);
            }

            return found;
        }

        /// <summary>
        /// One form by mnemonic and addressing mode, or false when the assembler
        /// accepts no such form. Looked up against the NMOS 6502 unless
        /// <paramref name="cpu"/> says otherwise; a 65C02 question needs the
        /// 65C02, because the answer is not always the same.
        /// </summary>
        public static bool TryGet(string mnemonic, AddressingMode mode, out InstructionDocumentation documentation,
            ICpuInstructionSet cpu = null)
        {
            documentation = null;
            if (string.IsNullOrEmpty(mnemonic))
                return false;

            IReadOnlyList<InstructionDocumentation> forms = ForMnemonic(mnemonic, cpu);
            for (int i = 0; i < forms.Count; i++)
            {
                if (forms[i].Mode == mode)
                {
                    documentation = forms[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The operand shape for a mode, as an editor shows it. Written per mode
        /// and not per instruction, because there are fifteen modes and fifty-odd
        /// mnemonics, and the two must not be able to disagree.
        /// </summary>
        public static string SyntaxFor(AddressingMode mode)
        {
            switch (mode)
            {
                case AddressingMode.Implicit:
                    return string.Empty;
                case AddressingMode.Accumulator:
                    return "A";
                case AddressingMode.Immediate:
                    return "#$nn";
                case AddressingMode.Absolute:
                    return "$nnnn";
                case AddressingMode.AbsoluteX:
                    return "$nnnn,x";
                case AddressingMode.AbsoluteY:
                    return "$nnnn,y";
                case AddressingMode.ZeroPage:
                    return "$nn";
                case AddressingMode.ZeroPageX:
                    return "$nn,x";
                case AddressingMode.ZeroPageY:
                    return "$nn,y";
                case AddressingMode.Indirect:
                    return "($nnnn)";
                case AddressingMode.IndirectX:
                    return "($nn,x)";
                case AddressingMode.IndirectY:
                    return "($nn),y";
                case AddressingMode.Relative:
                    return "target";
                case AddressingMode.ZeroPageIndirect:
                    return "($nn)";
                case AddressingMode.AbsoluteIndexedIndirect:
                    return "($nnnn,x)";
                default:
                    return string.Empty;
            }
        }
    }
}