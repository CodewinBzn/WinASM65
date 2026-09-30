// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Symbol and Scope Types

using System;
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

        /// <summary>
        /// Offset of the field inside the emitted buffer. Unlike <see cref="Position"/>,
        /// which is an address made relative to the current OriginAddress, this does not
        /// move when a later <c>.org</c> changes the origin.
        /// </summary>
        public int BufferOffset { get; set; }

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
            // Case-insensitive, as on any 6502 assembler: "lda PPU_CTRL" has to
            // find PPU_CTRL. This used to be left to the operand being upper-cased
            // on its way in, which worked for the common case and then lost the
            // spelling the source actually used, so a symbol the source defined in
            // lower case stopped matching and an imported one stopped matching the
            // export table. Comparing the names as written, here, fixes both
            // without touching what reaches the relocation records.
            SymbolTable = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            UnsolvedSymbols = new Dictionary<string, UnresolvedSymbol>(StringComparer.OrdinalIgnoreCase);
            MemArea = 0;
        }
    }
}
