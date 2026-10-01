// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Expression evaluation result

using System;
using System.Collections.Generic;
using WinASM65.Core;

namespace WinASM65.Expressions
{
    /// <summary>
    /// How the emitter must treat the value carried by an expression.
    /// This is the type that used to be lost on the way to the emitter: before it
    /// existed, <c>lda #$05</c> and <c>lda label</c> both came back resolved with
    /// a <c>Value</c> and nothing else.
    /// </summary>
    public enum ExpressionRole
    {
        /// <summary>No role assigned: the expression is not an instruction operand.</summary>
        None = 0,

        /// <summary>Immediate operand, <c>lda #expr</c>. One byte, a value, never an address.</summary>
        Immediate = 1,

        /// <summary>Address operand, <c>lda expr</c> / <c>sta $2000,x</c>. Carries an address.</summary>
        Address = 2,

        /// <summary>Relative branch target, <c>bne loop</c>. One signed byte from the next instruction.</summary>
        RelativeBranch = 3,

        /// <summary>Data field emitted by <c>.byte</c> or <c>.word</c>. A value, not necessarily an address.</summary>
        Data = 4
    }

    /// <summary>
    /// Which byte of the value the expression asked for.
    /// <para>
    /// <c>&lt;label</c> and <c>&gt;label</c> are not sugar the evaluator can finish
    /// on its own: the symbol they read is usually not known yet, so the selection
    /// has to survive the two passes and be applied by whoever writes the value.
    /// Without it the linker receives a whole address for a one-byte field.
    /// </para>
    /// </summary>
    public enum ByteSelector
    {
        None = 0,
        Low = 1,
        High = 2
    }

    /// <summary>
    /// One identifier actually read by an expression, with the source location it
    /// was read at. The provenance is what turns a relocation into something a
    /// diagnostic can point at.
    /// </summary>
    public sealed class SymbolReference : IEquatable<SymbolReference>
    {
        public string Name { get; private set; }
        public SourceLocation Location { get; private set; }

        public SymbolReference(string name, SourceLocation location)
        {
            Name = name ?? string.Empty;
            Location = location;
        }

        public bool Equals(SymbolReference other)
        {
            if (ReferenceEquals(other, null))
                return false;
            return string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase) && Location.Equals(other.Location);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SymbolReference);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Name.ToLowerInvariant().GetHashCode() * 397) ^ Location.GetHashCode();
            }
        }

        public override string ToString()
        {
            return string.Format("{0} ({1})", Name, Location);
        }
    }

    public class ExpressionResult
    {
        public Value Value { get; private set; }
        public IReadOnlyList<string> UndefinedSymbols { get; private set; }

        /// <summary>
        /// How the emitter must treat this value. Assigned by the caller from the
        /// decoded addressing mode, never guessed by the evaluator.
        /// </summary>
        public ExpressionRole Role { get; private set; }

        /// <summary>
        /// Every identifier the expression actually read, resolved or not, in
        /// order of appearance, with the file and line it was read at.
        /// </summary>
        public IReadOnlyList<SymbolReference> UsedSymbols { get; private set; }

        /// <summary>The source text of the expression, kept for relocation provenance.</summary>
        public string Expression { get; private set; }

        /// <summary>Where the expression was written.</summary>
        public SourceLocation Location { get; private set; }

        /// <summary>
        /// Which byte of the value the source asked for with <c>&lt;</c> or <c>&gt;</c>.
        /// Carried through the unresolved path on purpose: that is exactly the case
        /// where the value is not known and the selection cannot be applied yet.
        /// </summary>
        public ByteSelector Selector { get; private set; }

        public bool IsResolved
        {
            get { return UndefinedSymbols == null || UndefinedSymbols.Count == 0; }
        }

        /// <summary>
        /// True when the expression reads no symbol at all, so its value is fixed
        /// at assembly time and needs no relocation.
        /// </summary>
        public bool IsConstant
        {
            get { return UsedSymbols == null || UsedSymbols.Count == 0; }
        }

        /// <summary>The first symbol read, i.e. the one this site points at.</summary>
        public string TargetSymbol
        {
            get { return UsedSymbols != null && UsedSymbols.Count > 0 ? UsedSymbols[0].Name : null; }
        }

        private ExpressionResult(Value value, ExpressionRole role, IReadOnlyList<SymbolReference> usedSymbols,
            string expression, SourceLocation location, ByteSelector selector)
        {
            Value = value;
            UndefinedSymbols = new List<string>();
            Role = role;
            UsedSymbols = usedSymbols ?? new List<SymbolReference>();
            Expression = expression ?? string.Empty;
            Location = location;
            Selector = selector;
        }

        private ExpressionResult(IReadOnlyList<string> undefinedSymbols, ExpressionRole role,
            IReadOnlyList<SymbolReference> usedSymbols, string expression, SourceLocation location,
            ByteSelector selector)
        {
            Value = default(Value);
            UndefinedSymbols = undefinedSymbols ?? new List<string>();
            Role = role;
            UsedSymbols = usedSymbols ?? new List<SymbolReference>();
            Expression = expression ?? string.Empty;
            Location = location;
            Selector = selector;
        }

        public static ExpressionResult Success(Value value)
        {
            return Success(value, ExpressionRole.None, null, string.Empty, default(SourceLocation));
        }

        public static ExpressionResult Success(Value value, ExpressionRole role, IReadOnlyList<SymbolReference> usedSymbols,
            string expression, SourceLocation location)
        {
            return Success(value, role, usedSymbols, expression, location, ByteSelector.None);
        }

        public static ExpressionResult Success(Value value, ExpressionRole role, IReadOnlyList<SymbolReference> usedSymbols,
            string expression, SourceLocation location, ByteSelector selector)
        {
            return new ExpressionResult(value, role, usedSymbols, expression, location, selector);
        }

        public static ExpressionResult WithUndefinedSymbols(IReadOnlyList<string> undefinedSymbols)
        {
            return WithUndefinedSymbols(undefinedSymbols, ExpressionRole.None, null, string.Empty, default(SourceLocation));
        }

        public static ExpressionResult WithUndefinedSymbols(IReadOnlyList<string> undefinedSymbols, ExpressionRole role,
            IReadOnlyList<SymbolReference> usedSymbols, string expression, SourceLocation location)
        {
            return WithUndefinedSymbols(undefinedSymbols, role, usedSymbols, expression, location, ByteSelector.None);
        }

        public static ExpressionResult WithUndefinedSymbols(IReadOnlyList<string> undefinedSymbols, ExpressionRole role,
            IReadOnlyList<SymbolReference> usedSymbols, string expression, SourceLocation location, ByteSelector selector)
        {
            return new ExpressionResult(undefinedSymbols, role, usedSymbols, expression, location, selector);
        }

        /// <summary>
        /// Returns a copy carrying a different role. The role is a property of the
        /// site, not of the expression, so it is attached on the way out.
        /// </summary>
        public ExpressionResult WithRole(ExpressionRole role)
        {
            if (IsResolved)
                return new ExpressionResult(Value, role, UsedSymbols, Expression, Location, Selector);
            return new ExpressionResult(UndefinedSymbols, role, UsedSymbols, Expression, Location, Selector);
        }
    }
}
