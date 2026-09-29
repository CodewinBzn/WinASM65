// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Symbol Resolver Interface (Dependency Inversion Principle)

using WinASM65.Core;

namespace WinASM65.Expressions
{
    public interface ISymbolResolver
    {
        bool TryResolveSymbol(string name, out Value value);
    }
}
