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
        /// <summary>
        /// One open .if: whether the code around it is assembled, whether one of its
        /// branches has already been taken, whether the current branch is, and whether
        /// .else has been seen. A plain true/false per level could flip, but it could
        /// not say "an earlier branch was taken", which is what .elseif needs.
        /// </summary>
        private sealed class Frame
        {
            public bool ParentActive;
            public bool Taken;
            public bool Active;
            public bool SeenElse;
        }

        private readonly Stack<Frame> _stack = new Stack<Frame>();

        /// <summary>True when at least one .if/.ifdef/.ifndef is open.</summary>
        public bool IsActive { get { return _stack.Count > 0; } }

        /// <summary>True when the nesting limit has been reached.</summary>
        public bool MaxDepthReached { get { return _stack.Count >= byte.MaxValue; } }

        public bool ShouldAssembleCurrentLine()
        {
            return _stack.Count == 0 || _stack.Peek().Active;
        }

        /// <summary>Opens a level. A condition read inside a skipped branch counts as false.</summary>
        public void Push(bool condition)
        {
            bool parent = ShouldAssembleCurrentLine();
            Frame frame = new Frame { ParentActive = parent, Active = parent && condition };
            frame.Taken = frame.Active;
            _stack.Push(frame);
        }

        /// <summary>
        /// Whether an .elseif must evaluate its condition: only when the code around it
        /// is assembled and no earlier branch was taken. Its condition is not even read
        /// otherwise, so it may name a symbol the dead branch never defines.
        /// </summary>
        public bool ElseIfNeedsCondition
        {
            get { return _stack.Count > 0 && _stack.Peek().ParentActive && !_stack.Peek().Taken; }
        }

        /// <summary>Moves to an .elseif branch. False when .else was already seen.</summary>
        public bool ElseIf(bool condition)
        {
            if (_stack.Count == 0)
                return false;
            Frame frame = _stack.Peek();
            if (frame.SeenElse)
                return false;
            frame.Active = frame.ParentActive && !frame.Taken && condition;
            frame.Taken |= frame.Active;
            return true;
        }

        /// <summary>Moves to the .else branch. False when .else was already seen.</summary>
        public bool Else()
        {
            if (_stack.Count == 0)
                return false;
            Frame frame = _stack.Peek();
            if (frame.SeenElse)
                return false;
            frame.SeenElse = true;
            frame.Active = frame.ParentActive && !frame.Taken;
            frame.Taken = true;
            return true;
        }

        public void FlipTop()
        {
            Else();
        }

        public void Pop()
        {
            if (_stack.Count > 0)
                _stack.Pop();
        }

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

        /// <summary>
        /// Makes <paramref name="alias"/> run the handler registered as
        /// <paramref name="target"/>. The alias is a name like any other, so it is
        /// listed among the directive names and highlighted like the original.
        /// </summary>
        public void RegisterAlias(string alias, string target)
        {
            IDirectiveHandler handler;
            if (!_handlers.TryGetValue(target, out handler))
                throw new ArgumentException("no directive " + target + " to alias", "target");
            _handlers[alias] = handler;
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
