// WinASM65 - Emitting data fields from expressions

using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.Expressions;
using WinASM65.Symbols;

namespace WinASM65.Directives
{
    /// <summary>
    /// Emits one data field of one or two bytes from an expression: the value when it
    /// is known, otherwise a zero placeholder that is patched once the symbols it
    /// waits for are defined. Shared by every data directive so that all of them
    /// refuse out-of-range values and record relocations the same way.
    /// </summary>
    public static class DataEmitter
    {
        public static void Emit(IAssemblyContext context, string expression, int width)
        {
            ExpressionResult res = context.ResolveExpression(expression, AddressingMode.None, false);
            bool isByte = width == 1;

            if (res.IsResolved)
            {
                // A value that does not fit is refused, not narrowed: ToByte() masks to
                // the low octet, so 300 became $2C and the image built clean while
                // behaving in a way the source never described. The placeholder is a
                // zero so every address after it stays where the source put it.
                bool fits = isByte ? Value.InByteRange(res.Value.AsInteger) : Value.InWordRange(res.Value.AsInteger);
                if (!fits)
                {
                    DataRange.Report(context, res.Value.AsInteger, isByte);
                    EmitZero(context, width);
                    return;
                }

                int fieldOffset = context.Emitter.Length;
                if (isByte)
                    context.Emitter.EmitByte(res.Value.ToByte());
                else
                    context.Emitter.EmitWord(res.Value.ToUInt16());
                DataRelocation.Record(context, res.WithRole(ExpressionRole.Data), fieldOffset, (byte)width, res.Value.AsInteger);
                return;
            }

            ushort position = (ushort)(context.Emitter.CurrentAddress - context.Emitter.OriginAddress);
            int bufferOffset = context.Emitter.Length;
            EmitZero(context, width);

            UnresolvedExpr pending = new UnresolvedExpr
            {
                Position = position,
                BufferOffset = bufferOffset,
                Type = isByte ? SymbolType.Byte : SymbolType.Word,
                AddrMode = AddressingMode.None,
                NbrUndefinedSymb = res.UndefinedSymbols.Count,
                Expr = res.Expression
            };
            context.ScopeManager.AddUnresolvedExpression(position, pending);

            DataRelocation.Record(context, res.WithRole(ExpressionRole.Data), bufferOffset, (byte)width, 0);

            foreach (string symbol in res.UndefinedSymbols)
            {
                UnresolvedSymbol unresolved = new UnresolvedSymbol();
                unresolved.ExprList.Add(position);
                context.ScopeManager.AddUnresolvedSymbol(symbol, unresolved);
            }
        }

        /// <summary>
        /// Emits a string argument (<c>"…"</c>, escapes decoded) and returns true, or
        /// returns false when the argument is not a string. An unterminated string or a
        /// bad escape is reported and emits nothing.
        /// </summary>
        public static bool TryEmitString(IAssemblyContext context, string argument)
        {
            if (!argument.StartsWith("\"", System.StringComparison.Ordinal))
                return false;

            byte[] bytes;
            string error;
            if (argument.Length < 2 || !argument.EndsWith("\"", System.StringComparison.Ordinal)
                || !SourceText.IsStringTerminated(argument, 0))
            {
                context.Diagnostics.ReportError(context.CurrentLocation, ErrorCodes.UNTERMINATED_STRING);
                return true;
            }
            if (!StringLiteral.TryDecode(argument, out bytes, out error))
            {
                context.Diagnostics.ReportError(context.CurrentLocation, error);
                return true;
            }
            foreach (byte b in bytes)
                context.Emitter.EmitByte(b);
            return true;
        }

        private static void EmitZero(IAssemblyContext context, int width)
        {
            if (width == 1)
                context.Emitter.EmitByte(0);
            else
                context.Emitter.EmitWord(0);
        }
    }
}
