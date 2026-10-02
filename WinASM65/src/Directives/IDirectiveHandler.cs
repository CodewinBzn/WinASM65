// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Directives interfaces and context

using System;
using System.Collections.Generic;
using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.Expressions;
using WinASM65.Output;
using WinASM65.Symbols;

namespace WinASM65.Directives
{
    public class MacroDefinition
    {
        public string Name { get; set; }
        public string[] Parameters { get; set; }
        public List<string> Lines { get; set; }

        public MacroDefinition(string name, string[] parameters)
        {
            Name = name;
            Parameters = parameters ?? new string[0];
            Lines = new List<string>();
        }
    }

    public class ConditionalState
    {
        private readonly Stack<bool> _stack = new Stack<bool>();

        /// <summary>True when at least one .if/.ifdef/.ifndef is open.</summary>
        public bool IsActive { get { return _stack.Count > 0; } }

        /// <summary>True when the nesting limit has been reached.</summary>
        public bool MaxDepthReached { get { return _stack.Count >= byte.MaxValue; } }

        /// <summary>Whether the current line should be assembled.</summary>
        public bool ShouldAssembleCurrentLine()
        {
            foreach (bool condition in _stack)
            {
                if (!condition)
                    return false;
            }
            return true;
        }

        /// <summary>Open a new conditional level with the given truth value.</summary>
        public void Push(bool condition)
        {
            _stack.Push(condition);
        }

        /// <summary>Flip the current level's truth value (used by .else).</summary>
        public void FlipTop()
        {
            if (_stack.Count == 0)
                return;
            bool top = _stack.Pop();
            _stack.Push(!top);
        }

        /// <summary>Close the current conditional level (used by .endif).</summary>
        public void Pop()
        {
            if (_stack.Count > 0)
                _stack.Pop();
        }

        /// <summary>Reset all conditional state (used between assemblies).</summary>
        public void Reset()
        {
            _stack.Clear();
        }
    }

    public class RepeatBlockState
    {
        public bool IsInRepeatBlock { get; set; }
        public int Counter { get; set; }
        public List<string> Lines { get; private set; }

        public RepeatBlockState()
        {
            IsInRepeatBlock = false;
            Counter = 0;
            Lines = new List<string>();
        }
    }

    public interface IAssemblyContext
    {
        IBinaryEmitter Emitter { get; }
        IScopeManager ScopeManager { get; }
        IExpressionEvaluator ExpressionEvaluator { get; }
        IListingService ListingService { get; }
        IDiagnosticReporter Diagnostics { get; }
        ICpuInstructionSet Cpu { get; }
        SourceLocation CurrentLocation { get; }

        ConditionalState ConditionState { get; }
        RepeatBlockState RepeatState { get; }
        Dictionary<string, MacroDefinition> Macros { get; }
        MacroDefinition CurrentMacroBeingDefined { get; set; }

        void PushSourceFile(string filePath);
        void StopAssembling();
        void ParseLine(string line, string originalLine);
        ExpressionResult ResolveExpression(string expr, AddressingMode addrMode = AddressingMode.None, bool isLogical = false);
    }

    public interface IDirectiveHandler
    {
        string Name { get; }
        void Execute(string argument, IAssemblyContext context);
    }

    public interface IDirectiveDispatcher
    {
        void Register(IDirectiveHandler handler);
        bool TryDispatch(string directiveName, string argument, IAssemblyContext context);
    }

    public class DirectiveDispatcher : IDirectiveDispatcher
    {
        private readonly Dictionary<string, IDirectiveHandler> _handlers =
            new Dictionary<string, IDirectiveHandler>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The registered names, as the handlers spell them.</summary>
        public IReadOnlyList<string> HandlerNames
        {
            get { return new List<string>(_handlers.Keys); }
        }

        public void Register(IDirectiveHandler handler)
        {
            if (handler == null)
                throw new ArgumentNullException("handler");
            _handlers[handler.Name] = handler;
        }

        public bool TryDispatch(string directiveName, string argument, IAssemblyContext context)
        {
            if (string.IsNullOrEmpty(directiveName))
                return false;

            IDirectiveHandler handler;
            if (_handlers.TryGetValue(directiveName, out handler))
            {
                handler.Execute(argument, context);
                return true;
            }
            return false;
        }
    }
}
