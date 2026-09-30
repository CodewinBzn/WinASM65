// WinASM65 - Per-assembly state collected by .export and .import

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using WinASM65.Directives;

namespace WinASM65.Modules
{
    /// <summary>An import as declared in source, before it reaches a ModuleImage.</summary>
    public sealed class DeclaredImport
    {
        public string Symbol { get; private set; }
        public string ModuleName { get; private set; }

        public DeclaredImport(string symbol, string moduleName)
        {
            Symbol = symbol ?? string.Empty;
            ModuleName = moduleName ?? string.Empty;
        }
    }

    /// <summary>
    /// Holds the exports and imports declared by one assembly run. The
    /// directives receive an <see cref="IAssemblyContext"/>, which has no room
    /// for this, so the state is parked in a conditional-weak table keyed on the
    /// context. It is not held strongly: a context that is collected takes its
    /// declarations with it instead of leaking.
    /// </summary>
    internal sealed class ModuleDirectiveState
    {
        private static readonly ConditionalWeakTable<IAssemblyContext, ModuleDirectiveState> States =
            new ConditionalWeakTable<IAssemblyContext, ModuleDirectiveState>();

        internal readonly List<string> Exports = new List<string>();
        internal readonly List<DeclaredImport> Imports = new List<DeclaredImport>();

        /// <summary>Retrieves the state for a context, creating it on first use.</summary>
        internal static ModuleDirectiveState For(IAssemblyContext context)
        {
            ModuleDirectiveState state;
            if (context == null)
                return null;
            if (!States.TryGetValue(context, out state))
            {
                state = new ModuleDirectiveState();
                States.Add(context, state);
            }
            return state;
        }

        /// <summary>
        /// Finds the state of a context without creating one, for the assembly
        /// engine to read at the end of a run.
        /// </summary>
        internal static ModuleDirectiveState Peek(IAssemblyContext context)
        {
            ModuleDirectiveState state;
            if (context == null || !States.TryGetValue(context, out state))
                return null;
            return state;
        }

        internal bool IsEmpty
        {
            get { return Exports.Count == 0 && Imports.Count == 0; }
        }

        /// <summary>
        /// Splits a directive argument on commas and whitespace. ".export A, B" and
        /// ".export A B" both yield two names; ".import Foo, Bar" yields the symbol
        /// and the expected module, which is why the parts are kept separate rather
        /// than joined.
        /// </summary>
        internal static List<string> SplitNames(string argument)
        {
            List<string> parts = new List<string>();
            if (string.IsNullOrWhiteSpace(argument))
                return parts;

            string[] tokens = argument.Split(new char[] { ',', ' ', '\t', ';' },
                StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim();
                if (token.Length == 0)
                    continue;
                parts.Add(token);
            }
            return parts;
        }
    }
}
