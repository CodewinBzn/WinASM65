// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Expression evaluation result

using System.Collections.Generic;
using WinASM65.Core;

namespace WinASM65.Expressions
{
    public class ExpressionResult
    {
        public Value Value { get; private set; }
        public IReadOnlyList<string> UndefinedSymbols { get; private set; }

        public bool IsResolved
        {
            get { return UndefinedSymbols == null || UndefinedSymbols.Count == 0; }
        }

        private ExpressionResult(Value value)
        {
            Value = value;
            UndefinedSymbols = new List<string>();
        }

        private ExpressionResult(IReadOnlyList<string> undefinedSymbols)
        {
            Value = default(Value);
            UndefinedSymbols = undefinedSymbols ?? new List<string>();
        }

        public static ExpressionResult Success(Value value)
        {
            return new ExpressionResult(value);
        }

        public static ExpressionResult WithUndefinedSymbols(IReadOnlyList<string> undefinedSymbols)
        {
            return new ExpressionResult(undefinedSymbols);
        }
    }
}
