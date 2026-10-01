// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Relocation records produced by the emitter

using System;
using System.Collections.Generic;
using WinASM65.Core;
using WinASM65.Expressions;

namespace WinASM65.Output
{
    /// <summary>
    /// What a linker has to write at a relocation site. The value is decided from
    /// the decoded addressing mode and from the symbols the expression actually
    /// used, never from the numeric value alone.
    /// </summary>
    public enum RelocationType
    {
        /// <summary>No relocation: the site does not depend on any symbol.</summary>
        None = 0,

        /// <summary>1-byte address in the zero page, or a 1-byte indirect pointer.</summary>
        Zp8 = 1,

        /// <summary>2-byte absolute address.</summary>
        Abs16 = 2,

        /// <summary>1-byte value loaded from a symbol, <c>lda #mask</c>. A value, not an address.</summary>
        Imm8 = 3,

        /// <summary>1-byte signed offset from the next instruction, <c>bne loop</c>.</summary>
        Rel8 = 4,

        /// <summary>1-byte data field, <c>.byte label</c>.</summary>
        Data8 = 5,

        /// <summary>2-byte data field, <c>.word label</c>.</summary>
        Data16 = 6,

        /// <summary>Address of a segment. Not produced yet: it needs the .w65 segments (T3).</summary>
        Seg = 7,

        /// <summary>Low byte of the resolved value, <c>lda #&lt;label</c>.</summary>
        LowByte = 8,

        /// <summary>High byte of the resolved value, <c>lda #&gt;label</c>.</summary>
        HighByte = 9
    }

    /// <summary>
    /// One site whose emitted value depends on a symbol. Provenance is part of the
    /// record on purpose: a relocation without a file and a line can only produce
    /// a number, and a number does not help anyone fix anything.
    /// </summary>
    public sealed class RelocationRecord
    {
        /// <summary>Index of the segment the site belongs to. 0 is the single implicit segment of direct-burn mode.</summary>
        public int SegmentIndex { get; private set; }

        /// <summary>Name of the segment. Empty in direct-burn mode, which has no named segments.</summary>
        public string SegmentName { get; private set; }

        /// <summary>Offset of the first byte of the field, relative to the segment base.</summary>
        public int Offset { get; private set; }

        /// <summary>Absolute address of the first byte of the field, as assembled.</summary>
        public ushort Address { get; private set; }

        /// <summary>Width of the field in bytes: 1 or 2. It follows the addressing mode, never a default.</summary>
        public byte Width { get; private set; }

        public RelocationType Type { get; private set; }

        /// <summary>Every symbol the expression read, in order of appearance. A site may use more than one.</summary>
        public IReadOnlyList<string> Symbols { get; private set; }

        /// <summary>The first symbol read, i.e. the one this site primarily points at.</summary>
        public string TargetSymbol { get; private set; }

        /// <summary>Source file the site was written in.</summary>
        public string SourceFile { get; private set; }

        /// <summary>Source line the site was written on.</summary>
        public int SourceLine { get; private set; }

        /// <summary>The source text of the operand, kept for diagnostics.</summary>
        public string Expression { get; private set; }

        /// <summary>False while the site still holds a placeholder pending the second pass.</summary>
        public bool IsResolved { get; private set; }

        /// <summary>The value written at the site, meaningful only once <see cref="IsResolved"/> is true.</summary>
        public long Value { get; private set; }

        public RelocationRecord(int segmentIndex, string segmentName, int offset, ushort address, byte width,
            RelocationType type, IReadOnlyList<string> symbols, SourceLocation location, string expression)
        {
            SegmentIndex = segmentIndex;
            SegmentName = segmentName ?? string.Empty;
            Offset = offset;
            Address = address;
            Width = width;
            Type = type;
            Symbols = symbols ?? new List<string>();
            TargetSymbol = Symbols.Count > 0 ? Symbols[0] : null;
            SourceFile = location.FilePath ?? string.Empty;
            SourceLine = location.LineNumber;
            Expression = expression ?? string.Empty;
            IsResolved = false;
            Value = 0;
        }

        /// <summary>
        /// Decides the relocation type from the role of the expression and the width
        /// actually emitted. <paramref name="operandWidth"/> must be the width left by
        /// the addressing mode decision, which is only final once the zero-page
        /// optimization has run: <c>Cpu6502.TryOptimizeZeroPage</c> can shrink an
        /// absolute operand to a single byte, and the type has to follow.
        /// </summary>
        public static RelocationType TypeFor(ExpressionRole role, byte operandWidth)
        {
            return TypeFor(role, operandWidth, ByteSelector.None);
        }

        /// <summary>
        /// Same decision, with the byte selector the source asked for.
        /// <para>
        /// <c>&lt;</c> and <c>&gt;</c> only make sense on a one-byte field: there the
        /// value written is half of an address, and keeping the whole address would
        /// make <c>lda #&gt;label</c> fail with "does not fit on one octet" on every
        /// label outside page zero. On a two-byte field the selection is dropped,
        /// because a narrowed value in a word field is a different mistake and not
        /// something to guess at here.
        /// </para>
        /// </summary>
        public static RelocationType TypeFor(ExpressionRole role, byte operandWidth, ByteSelector selector)
        {
            if (operandWidth == 1)
            {
                if (selector == ByteSelector.Low)
                    return RelocationType.LowByte;
                if (selector == ByteSelector.High)
                    return RelocationType.HighByte;
            }

            switch (role)
            {
                case ExpressionRole.RelativeBranch:
                    return RelocationType.Rel8;
                case ExpressionRole.Immediate:
                    return RelocationType.Imm8;
                case ExpressionRole.Address:
                    return operandWidth == 1 ? RelocationType.Zp8 : RelocationType.Abs16;
                case ExpressionRole.Data:
                    return operandWidth == 1 ? RelocationType.Data8 : RelocationType.Data16;
                default:
                    return RelocationType.None;
            }
        }

        public void MarkResolved(long value)
        {
            IsResolved = true;
            Value = value;
        }

        public override string ToString()
        {
            return string.Format("{0} {1}[{2}] w={3} {4} @${5:X4} ({6}:{7})",
                SegmentName, Type, TargetSymbol ?? "?", Width, Offset, Address, SourceFile, SourceLine);
        }
    }
}
