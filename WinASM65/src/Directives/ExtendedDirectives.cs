// WinASM65 - Data, layout and diagnostic directives shared with other 6502 assemblers

using System.Collections.Generic;
using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.Expressions;
using WinASM65.Output;

namespace WinASM65.Directives
{
    /// <summary>Helpers shared by the directives below.</summary>
    internal static class DirectiveArguments
    {
        /// <summary>
        /// A count or an address that must be known now, because it decides how many
        /// bytes are emitted: a forward reference cannot be patched into a length.
        /// </summary>
        public static bool TryResolveNow(IAssemblyContext context, string expression, string directive, string what, out long value)
        {
            value = 0;
            ExpressionResult res = context.ResolveExpression(expression ?? string.Empty);
            if (res.HasError)
                return false;
            if (!res.IsResolved)
            {
                context.Diagnostics.ReportError(context.CurrentLocation,
                    string.Format(ErrorCodes.DIRECTIVE_ARGUMENTS, directive, what + " known at this point (undefined: " + string.Join(", ", res.UndefinedSymbols) + ")"));
                return false;
            }
            value = res.Value.AsInteger;
            return true;
        }

        /// <summary>The fill byte of .dsb, .pad and .align: the optional second argument, 0 by default.</summary>
        public static bool TryFillByte(IAssemblyContext context, List<string> args, int index, string directive, out byte fill)
        {
            fill = 0;
            if (args.Count <= index || string.IsNullOrWhiteSpace(args[index]))
                return true;
            long value;
            if (!TryResolveNow(context, args[index], directive, "a fill value", out value))
                return false;
            if (!Value.InByteRange(value))
            {
                context.Diagnostics.ReportError(context.CurrentLocation, string.Format(ErrorCodes.VALUE_OUT_OF_RANGE_BYTE, value));
                return false;
            }
            fill = (byte)(value & 0xFF);
            return true;
        }

        /// <summary>A message argument: a string literal, decoded, or the text as written.</summary>
        public static string Message(string argument)
        {
            string text = (argument ?? string.Empty).Trim();
            byte[] bytes;
            string error;
            if (text.StartsWith("\"") && StringLiteral.TryDecode(text, out bytes, out error))
                return System.Text.Encoding.Latin1.GetString(bytes);
            return text;
        }
    }

    /// <summary><c>.asciiz "text"[, …]</c>: the bytes of its arguments, then a zero.</summary>
    public class AsciizDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".asciiz"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            ushort start = context.Emitter.CurrentAddress;
            foreach (string data in SourceText.SplitArguments(argument))
            {
                if (!DataEmitter.TryEmitString(context, data))
                    DataEmitter.Emit(context, data, 1);
            }
            context.Emitter.EmitByte(0);
            context.ListingService.PrintLine(LineType.INST, context.Emitter.CurrentAddress - start);
        }
    }

    /// <summary>
    /// <c>.lobytes e, …</c> and <c>.hibytes e, …</c>: one byte per expression, its low or
    /// high byte, which is how a table of addresses is split into two tables.
    /// </summary>
    public class ByteTableDirectiveHandler : IDirectiveHandler
    {
        private readonly string _name;
        private readonly string _selector;

        public ByteTableDirectiveHandler(string name, string selector)
        {
            _name = name;
            _selector = selector;
        }

        public string Name { get { return _name; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            List<string> entries = SourceText.SplitArguments(argument);
            foreach (string data in entries)
                DataEmitter.Emit(context, _selector + "(" + data + ")", 1);
            context.ListingService.PrintLine(LineType.INST, entries.Count);
        }
    }

    /// <summary>
    /// <c>.dsb n[, fill]</c> (also <c>.ds</c>, <c>.fill</c>) and <c>.dsw n[, fill]</c>:
    /// n bytes or words of a fill value, 0 by default. Unlike <c>.res</c>, these emit.
    /// </summary>
    public class FillDirectiveHandler : IDirectiveHandler
    {
        private readonly string _name;
        private readonly int _width;

        public FillDirectiveHandler(string name, int width)
        {
            _name = name;
            _width = width;
        }

        public string Name { get { return _name; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            List<string> args = SourceText.SplitArguments(argument);
            if (args.Count == 0 || args.Count > 2)
            {
                context.Diagnostics.ReportError(context.CurrentLocation, string.Format(ErrorCodes.DIRECTIVE_ARGUMENTS, _name, "a count and an optional fill value"));
                return;
            }

            long count;
            if (!DirectiveArguments.TryResolveNow(context, args[0], _name, "a count", out count))
                return;
            if (count < 0 || count * _width > 0x10000)
            {
                context.Diagnostics.ReportError(context.CurrentLocation, string.Format(ErrorCodes.DIRECTIVE_ARGUMENTS, _name, "a count between 0 and the size of memory"));
                return;
            }

            long fill = 0;
            if (args.Count == 2 && !DirectiveArguments.TryResolveNow(context, args[1], _name, "a fill value", out fill))
                return;
            bool fits = _width == 1 ? Value.InByteRange(fill) : Value.InWordRange(fill);
            if (!fits)
            {
                context.Diagnostics.ReportError(context.CurrentLocation, string.Format(
                    _width == 1 ? ErrorCodes.VALUE_OUT_OF_RANGE_BYTE : ErrorCodes.VALUE_OUT_OF_RANGE_WORD, fill));
                return;
            }

            for (long i = 0; i < count; i++)
            {
                if (_width == 1)
                    context.Emitter.EmitByte((byte)(fill & 0xFF));
                else
                    context.Emitter.EmitWord((ushort)(fill & 0xFFFF));
            }
            context.ListingService.PrintLine(LineType.INST, (int)(count * _width));
        }
    }

    /// <summary>
    /// <c>.res n</c> without a label: advances the reservation area (see <c>.memarea</c>)
    /// without naming the space, and emits nothing, like the labelled form.
    /// </summary>
    public class ReserveDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".res"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            long size;
            if (!DirectiveArguments.TryResolveNow(context, argument, ".res", "a size", out size))
                return;
            if (size < 0 || size > 0x10000)
            {
                context.Diagnostics.ReportError(context.CurrentLocation, string.Format(ErrorCodes.DIRECTIVE_ARGUMENTS, ".res", "a size between 0 and the size of memory"));
                return;
            }
            ushort memArea = context.ScopeManager.CurrentScope.MemArea;
            context.ListingService.PrintLine(LineType.RES, memArea);
            context.ScopeManager.CurrentScope.MemArea = (ushort)(memArea + size);
        }
    }

    /// <summary><c>.pad address[, fill]</c>: fills up to an address, which must not be behind.</summary>
    public class PadDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".pad"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            List<string> args = SourceText.SplitArguments(argument);
            if (args.Count == 0 || args.Count > 2)
            {
                context.Diagnostics.ReportError(context.CurrentLocation, string.Format(ErrorCodes.DIRECTIVE_ARGUMENTS, ".pad", "an address and an optional fill value"));
                return;
            }

            long target;
            byte fill;
            if (!DirectiveArguments.TryResolveNow(context, args[0], ".pad", "an address", out target)
                || !DirectiveArguments.TryFillByte(context, args, 1, ".pad", out fill))
                return;

            int current = context.Emitter.CurrentAddress;
            if (target < current || target > 0x10000)
            {
                context.Diagnostics.ReportError(context.CurrentLocation, string.Format(ErrorCodes.PAD_BACKWARDS, target, current));
                return;
            }
            for (long a = current; a < target; a++)
                context.Emitter.EmitByte(fill);
            context.ListingService.PrintLine(LineType.INST, (int)(target - current));
        }
    }

    /// <summary><c>.align n[, fill]</c>: fills up to the next multiple of n, a power of two.</summary>
    public class AlignDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".align"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            List<string> args = SourceText.SplitArguments(argument);
            if (args.Count == 0 || args.Count > 2)
            {
                context.Diagnostics.ReportError(context.CurrentLocation, string.Format(ErrorCodes.DIRECTIVE_ARGUMENTS, ".align", "a power of two and an optional fill value"));
                return;
            }

            long boundary;
            byte fill;
            if (!DirectiveArguments.TryResolveNow(context, args[0], ".align", "a boundary", out boundary)
                || !DirectiveArguments.TryFillByte(context, args, 1, ".align", out fill))
                return;
            if (boundary <= 0 || boundary > 0x10000 || (boundary & (boundary - 1)) != 0)
            {
                context.Diagnostics.ReportError(context.CurrentLocation, string.Format(ErrorCodes.ALIGN_INVALID, boundary));
                return;
            }

            int current = context.Emitter.CurrentAddress;
            long padding = (boundary - current % boundary) % boundary;
            for (long i = 0; i < padding; i++)
                context.Emitter.EmitByte(fill);
            context.ListingService.PrintLine(LineType.INST, (int)padding);
        }
    }

    /// <summary><c>.error "message"</c>: stops the build with the message, where it is written.</summary>
    public class ErrorDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".error"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            context.Diagnostics.ReportError(context.CurrentLocation, string.Format(ErrorCodes.USER_ERROR, DirectiveArguments.Message(argument)));
        }
    }

    /// <summary><c>.warning "message"</c>: reports the message and lets the build go on.</summary>
    public class WarningDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".warning"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            context.Diagnostics.ReportWarning(context.CurrentLocation, string.Format(ErrorCodes.USER_WARNING, DirectiveArguments.Message(argument)));
        }
    }

    /// <summary>
    /// <c>.assert expr[, "message"]</c>: an error when the expression is false. One that
    /// reads a symbol defined later is checked once every symbol is known.
    /// </summary>
    public class AssertDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".assert"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            List<string> args = SourceText.SplitArguments(argument);
            if (args.Count == 0 || args.Count > 2 || string.IsNullOrWhiteSpace(args[0]))
            {
                context.Diagnostics.ReportError(context.CurrentLocation, string.Format(ErrorCodes.DIRECTIVE_ARGUMENTS, ".assert", "an expression and an optional message"));
                return;
            }

            string message = args.Count == 2 ? DirectiveArguments.Message(args[1]) : args[0];
            ExpressionResult res = context.ResolveExpression(args[0], AddressingMode.None, true);
            if (res.HasError)
                return;
            if (!res.IsResolved)
            {
                context.DeferAssertion(res.Expression, message);
                return;
            }
            if (!res.Value.ToBoolean())
                context.Diagnostics.ReportError(context.CurrentLocation, string.Format(ErrorCodes.ASSERTION_FAILED, message));
        }
    }
}
