// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Standard Directive Handlers (Single Responsibility Principle)

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.Expressions;
using WinASM65.Output;
using WinASM65.Symbols;

namespace WinASM65.Directives
{
    public class OrgDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".org"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            ExpressionResult res = context.ResolveExpression(argument);
            if (res.IsResolved)
            {
                ushort addr = res.Value.ToUInt16();
                context.Emitter.CurrentAddress = addr;
                context.Emitter.OriginAddress = addr;
                context.ListingService.PrintLine(LineType.ORG, addr);
            }
            else
            {
                context.Diagnostics.ReportError(context.CurrentLocation, ErrorCodes.UNDEFINED_SYMBOL);
            }
        }
    }

    public class MemAreaDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".memarea"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            ExpressionResult res = context.ResolveExpression(argument);
            if (res.IsResolved)
            {
                context.ScopeManager.CurrentScope.MemArea = res.Value.ToUInt16();
            }
            else
            {
                context.Diagnostics.ReportError(context.CurrentLocation, ErrorCodes.UNDEFINED_SYMBOL);
            }
        }
    }

    public class IncBinDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".incbin"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            string fileName = (argument ?? string.Empty).Replace("\"", string.Empty).Trim();
            string directoryName = Path.GetDirectoryName(context.CurrentLocation.FilePath);
            string toInclude = string.IsNullOrEmpty(directoryName) ? fileName : Path.Combine(directoryName, fileName);

            if (File.Exists(toInclude))
            {
                byte[] bytesToInc = File.ReadAllBytes(toInclude);
                context.Emitter.EmitBytes(bytesToInc);
                context.ListingService.PrintLine(LineType.INST, bytesToInc.Length);
            }
            else
            {
                context.Diagnostics.ReportError(context.CurrentLocation, ErrorCodes.FILE_NOT_EXISTS);
            }
        }
    }

    public class IncludeDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".include"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            string fileName = (argument ?? string.Empty).Replace("\"", string.Empty).Trim();
            string directoryName = Path.GetDirectoryName(context.CurrentLocation.FilePath);
            string toInclude = string.IsNullOrEmpty(directoryName) ? fileName : Path.Combine(directoryName, fileName);

            if (File.Exists(toInclude))
            {
                context.PushSourceFile(toInclude);
            }
            else
            {
                context.Diagnostics.ReportError(context.CurrentLocation, ErrorCodes.FILE_NOT_EXISTS);
            }
        }
    }

    public class ByteDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".byte"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            if (string.IsNullOrWhiteSpace(argument))
                return;

            string[] byteEntries = argument.Split(',');
            ushort startAddr = context.Emitter.CurrentAddress;

            foreach (string entry in byteEntries)
            {
                string data = entry.Trim();
                if (data.StartsWith("\"") && data.EndsWith("\"") && data.Length >= 2)
                {
                    string strContent = data.Substring(1, data.Length - 2);
                    foreach (char c in strContent)
                    {
                        context.Emitter.EmitByte(Convert.ToByte(c));
                    }
                }
                else
                {
                    ExpressionResult res = context.ResolveExpression(data);
                    if (res.IsResolved)
                    {
                        context.Emitter.EmitByte(res.Value.ToByte());
                    }
                    else
                    {
                        ushort position = (ushort)(context.Emitter.CurrentAddress - context.Emitter.OriginAddress);
                        context.Emitter.EmitByte(0);

                        UnresolvedExpr expr = new UnresolvedExpr
                        {
                            Position = position,
                            Type = SymbolType.Byte,
                            AddrMode = AddressingMode.None,
                            NbrUndefinedSymb = res.UndefinedSymbols.Count,
                            Expr = data
                        };
                        context.ScopeManager.AddUnresolvedExpression(position, expr);

                        foreach (string symb in res.UndefinedSymbols)
                        {
                            UnresolvedSymbol unResSymb = new UnresolvedSymbol();
                            unResSymb.ExprList.Add(position);
                            context.ScopeManager.AddUnresolvedSymbol(symb, unResSymb);
                        }
                    }
                }
            }

            int bytesCount = context.Emitter.CurrentAddress - startAddr;
            context.ListingService.PrintLine(LineType.INST, bytesCount);
        }
    }

    public class WordDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".word"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            if (string.IsNullOrWhiteSpace(argument))
                return;

            string[] wordEntries = argument.Split(',');
            context.ListingService.PrintLine(LineType.INST, wordEntries.Length * 2);

            foreach (string entry in wordEntries)
            {
                string data = entry.Trim();
                ExpressionResult res = context.ResolveExpression(data);
                if (res.IsResolved)
                {
                    context.Emitter.EmitWord(res.Value.ToUInt16());
                }
                else
                {
                    ushort position = (ushort)(context.Emitter.CurrentAddress - context.Emitter.OriginAddress);
                    context.Emitter.EmitWord(0);

                    UnresolvedExpr expr = new UnresolvedExpr
                    {
                        Position = position,
                        Type = SymbolType.Word,
                        AddrMode = AddressingMode.None,
                        NbrUndefinedSymb = res.UndefinedSymbols.Count,
                        Expr = data
                    };
                    context.ScopeManager.AddUnresolvedExpression(position, expr);

                    foreach (string symb in res.UndefinedSymbols)
                    {
                        UnresolvedSymbol unResSymb = new UnresolvedSymbol();
                        unResSymb.ExprList.Add(position);
                        context.ScopeManager.AddUnresolvedSymbol(symb, unResSymb);
                    }
                }
            }
        }
    }

    public class MacroDirectiveHandler : IDirectiveHandler
    {
        private static readonly Regex MacroDeclRegex = new Regex(@"^(\s*(?<label>[a-zA-Z_][a-zA-Z_0-9]*))(\s+(?<value>(.)+))?", RegexOptions.Compiled);

        public string Name { get { return ".macro"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            if (context.CurrentMacroBeingDefined != null)
            {
                context.Diagnostics.ReportError(context.CurrentLocation, ErrorCodes.NESTED_MACROS);
                return;
            }

            Match match = MacroDeclRegex.Match(argument ?? string.Empty);
            string macroName = match.Groups["label"].Value;
            string paramsPart = match.Groups["value"].Value;

            string[] paramList;
            if (!string.IsNullOrEmpty(paramsPart))
            {
                paramList = Regex.Replace(paramsPart, @"\s+", "").Split(',');
            }
            else
            {
                paramList = new string[0];
            }

            if (context.Macros.ContainsKey(macroName))
            {
                context.Diagnostics.ReportError(context.CurrentLocation, ErrorCodes.MACRO_EXISTS);
            }
            else
            {
                MacroDefinition def = new MacroDefinition(macroName, paramList);
                context.Macros[macroName] = def;
                context.CurrentMacroBeingDefined = def;
            }
        }
    }

    public class EndMacroDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".endmacro"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            if (context.CurrentMacroBeingDefined == null)
            {
                context.Diagnostics.ReportError(context.CurrentLocation, ErrorCodes.NO_MACRO);
                return;
            }
            context.CurrentMacroBeingDefined = null;
        }
    }

    public class IfDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".if"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            ConditionalState cAsm = context.ConditionState;
            if (cAsm.MaxDepthReached)
            {
                context.Diagnostics.ReportError(context.CurrentLocation, ErrorCodes.NESTED_CONDITIONAL_ASSEMBLY);
                return;
            }

            if (!cAsm.ShouldAssembleCurrentLine())
            {
                cAsm.Push(false);
                return;
            }

            ExpressionResult res = context.ResolveExpression(argument != null ? argument.Trim() : string.Empty, AddressingMode.None, true);
            if (!res.IsResolved)
            {
                context.Diagnostics.ReportError(context.CurrentLocation, ErrorCodes.UNDEFINED_SYMBOL);
                return;
            }

            cAsm.Push(res.Value.ToBoolean());
        }
    }

    public class IfDefDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".ifdef"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            ConditionalState cAsm = context.ConditionState;
            if (cAsm.MaxDepthReached)
            {
                context.Diagnostics.ReportError(context.CurrentLocation, ErrorCodes.NESTED_CONDITIONAL_ASSEMBLY);
                return;
            }

            if (!cAsm.ShouldAssembleCurrentLine())
            {
                cAsm.Push(false);
                return;
            }

            string label = (argument ?? string.Empty).Trim();
            Value val;
            bool exists = context.ScopeManager.TryResolveSymbol(label, out val);
            cAsm.Push(exists);
        }
    }

    public class IfnDefDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".ifndef"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            ConditionalState cAsm = context.ConditionState;
            if (cAsm.MaxDepthReached)
            {
                context.Diagnostics.ReportError(context.CurrentLocation, ErrorCodes.NESTED_CONDITIONAL_ASSEMBLY);
                return;
            }

            if (!cAsm.ShouldAssembleCurrentLine())
            {
                cAsm.Push(false);
                return;
            }

            string label = (argument ?? string.Empty).Trim();
            Value val;
            bool exists = context.ScopeManager.TryResolveSymbol(label, out val);
            cAsm.Push(!exists);
        }
    }

    public class ElseDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".else"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            ConditionalState cAsm = context.ConditionState;
            if (!cAsm.IsActive)
            {
                context.Diagnostics.ReportError(context.CurrentLocation, ErrorCodes.NO_CONDITIONAL_ASSEMBLY);
                return;
            }
            cAsm.FlipTop();
        }
    }

    public class EndIfDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".endif"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            ConditionalState cAsm = context.ConditionState;
            if (!cAsm.IsActive)
            {
                context.Diagnostics.ReportError(context.CurrentLocation, ErrorCodes.NO_CONDITIONAL_ASSEMBLY);
                return;
            }

            cAsm.Pop();
        }
    }

    public class RepDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".rep"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            RepeatBlockState rep = context.RepeatState;
            if (rep.IsInRepeatBlock)
            {
                context.Diagnostics.ReportError(context.CurrentLocation, ErrorCodes.NESTED_REP);
                return;
            }

            ExpressionResult res = context.ResolveExpression(argument != null ? argument.Trim() : string.Empty);
            if (!res.IsResolved)
            {
                context.Diagnostics.ReportError(context.CurrentLocation, ErrorCodes.UNDEFINED_SYMBOL);
                return;
            }

            rep.IsInRepeatBlock = true;
            rep.Lines.Clear();
            rep.Counter = res.Value.ToInt32();
        }
    }

    public class EndRepDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".endrep"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            RepeatBlockState rep = context.RepeatState;
            if (!rep.IsInRepeatBlock)
            {
                context.Diagnostics.ReportError(context.CurrentLocation, ErrorCodes.NO_REP);
                return;
            }

            context.ListingService.EndLine();
            rep.IsInRepeatBlock = false;

            List<string> linesToRepeat = new List<string>(rep.Lines);
            int count = rep.Counter;
            rep.Lines.Clear();

            for (int i = 0; i < count; i++)
            {
                foreach (string line in linesToRepeat)
                {
                    context.ListingService.PrintLine(line);
                    context.ParseLine(line, line);
                }
            }
        }
    }

    public class EndDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".end"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            context.StopAssembling();
        }
    }
}
