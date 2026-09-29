// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Symbol and Scope Types

using System.Collections.Generic;
using WinASM65.Cpu;

namespace WinASM65.Symbols
{
    public enum SymbolType
    {
        Byte = 0,
        Word = 1
    }

    public class UnresolvedSymbol
    {
        public List<string> DependingList { get; set; }
        public List<ushort> ExprList { get; set; }
        public string Expr { get; set; }
        public int NbrUndefinedSymb { get; set; }

        public UnresolvedSymbol()
        {
            DependingList = new List<string>();
            ExprList = new List<ushort>();
        }
    }

    public class UnresolvedExpr
    {
        public string Expr { get; set; }
        public int NbrUndefinedSymb { get; set; }
        public ushort Position { get; set; }
        public SymbolType Type { get; set; }
        public AddressingMode AddrMode { get; set; }

        public UnresolvedExpr()
        {
        }
    }

    public class LexicalScopeData
    {
        public Dictionary<string, long> SymbolTable { get; set; }
        public Dictionary<string, UnresolvedSymbol> UnsolvedSymbols { get; set; }
        public ushort MemArea { get; set; }

        public LexicalScopeData()
        {
            SymbolTable = new Dictionary<string, long>();
            UnsolvedSymbols = new Dictionary<string, UnresolvedSymbol>();
            MemArea = 0;
        }
    }
}
