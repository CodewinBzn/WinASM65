// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Standard Directive Handlers (Single Responsibility Principle)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.Expressions;
using WinASM65.Output;
using WinASM65.Segments;
using WinASM65.Symbols;

namespace WinASM65.Directives
{
    /// <summary>
    /// Records the relocation of a <c>.byte</c> or <c>.word</c> field whose value
    /// depends on a symbol. A literal field records nothing.
    /// </summary>
    internal static class DataRelocation
    {
        public static void Record(IAssemblyContext context, ExpressionResult res, int bufferOffset, byte width, long value)
        {
            if (res == null || res.IsConstant)
                return;

            List<string> symbols = new List<string>();
            foreach (SymbolReference reference in res.UsedSymbols)
            {
                if (!symbols.Contains(reference.Name))
                    symbols.Add(reference.Name);
            }

            RelocationRecord record = new RelocationRecord(
                context.Emitter.SegmentIndex,
                context.Emitter.SegmentName,
                bufferOffset,
                (ushort)(context.Emitter.CurrentAddress - width),
                width,
                RelocationRecord.TypeFor(res.Role, width, res.Selector),
                symbols,
                res.Location,
                res.Expression);

            record.MarkResolved(value);
            context.Emitter.RecordRelocation(record);
        }
    }

    /// <summary>
    /// Reports a data field whose value does not fit the field it is written into.
    /// <para>
    /// The message is the one the instruction path already used, so a value that is
    /// too wide reads the same whether it was written with <c>.byte</c> or with
    /// <c>LDX #</c>. Two widths, two bounds, and both the directive and the
    /// instruction path go through <see cref="Value.InByteRange"/> and
    /// <see cref="Value.InWordRange"/>, so the two cannot drift apart.
    /// </para>
    /// </summary>
    internal static class DataRange
    {
        public static void Report(IAssemblyContext context, long value, bool byteSized)
        {
            string template = byteSized ? ErrorCodes.VALUE_OUT_OF_RANGE_BYTE : ErrorCodes.VALUE_OUT_OF_RANGE_WORD;
            context.Diagnostics.ReportError(context.CurrentLocation,
                string.Format(CultureInfo.InvariantCulture, template, value));
        }
    }

    public class OrgDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".org"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            ExpressionResult res = context.ResolveExpression(argument);
            if (res.IsResolved)
            {
                ushort addr = res.Value.ToUInt16();
                // Cross-validation only, and only when the build declared regions: an
                // address outside every declared region is a diagnostic, and a bss
                // region never holds emitted bytes. A build that declares nothing has
                // a null map, so the .org lands exactly where it always did.
                MemoryMap regions = MemoryMapScope.Current;
                if (regions != null)
                    regions.ValidateOrigin(addr, context.CurrentLocation, context.Diagnostics);
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
                ushort addr = res.Value.ToUInt16();

                // A .memarea selects where the following .res start. It places
                // nothing and emits nothing, so only the overflow check applies --
                // unlike .org, which ValidateOrigin refuses outright inside a bss
                // region, because code there would produce file bytes in space the
                // target declared as holding none.
                MemoryMap regions = MemoryMapScope.Current;
                if (regions != null)
                    regions.ValidateReservation(addr, 0, context.CurrentLocation, context.Diagnostics);

                context.ScopeManager.CurrentScope.MemArea = addr;
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

            // Arguments are split on top-level commas only, so ".byte "a,b"" is one
            // string and ".byte ','" one character.
            ushort startAddr = context.Emitter.CurrentAddress;
            foreach (string data in SourceText.SplitArguments(argument))
            {
                if (!DataEmitter.TryEmitString(context, data))
                    DataEmitter.Emit(context, data, 1);
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

            System.Collections.Generic.List<string> entries = SourceText.SplitArguments(argument);
            context.ListingService.PrintLine(LineType.INST, entries.Count * 2);
            foreach (string data in entries)
                DataEmitter.Emit(context, data, 2);
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
