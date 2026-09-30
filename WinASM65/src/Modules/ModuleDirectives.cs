// WinASM65 - .export and .import directives
//
// A DLL publishes names, not addresses. These two directives are what turn a
// plain assembly unit into a module the linker can resolve against others.

using System;
using System.Collections.Generic;
using WinASM65.Core;
using WinASM65.Directives;
using WinASM65.Symbols;

namespace WinASM65.Modules
{
    /// <summary>Names this unit publishes to whoever links against it.</summary>
    public class ExportDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".export"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            if (context == null || string.IsNullOrWhiteSpace(argument))
                return;

            ModuleDirectiveState state = ModuleDirectiveState.For(context);
            if (state == null)
                return;

            List<string> names = ModuleDirectiveState.SplitNames(argument);
            for (int i = 0; i < names.Count; i++)
            {
                string symbol = names[i];

                // An export must name a symbol this unit actually defines. Catching
                // it here, at assembly time, beats letting the linker discover a
                // dangling export much later.
                Value address;
                if (!context.ScopeManager.TryResolveSymbol(symbol, out address))
                {
                    context.Diagnostics.ReportError(context.CurrentLocation,
                        "Exported symbol '" + symbol + "' is not defined in this unit.");
                    continue;
                }

                state.Exports.Add(symbol);
            }
        }
    }

    /// <summary>Names this unit expects another module to provide.</summary>
    public class ImportDirectiveHandler : IDirectiveHandler
    {
        public string Name { get { return ".import"; } }

        public void Execute(string argument, IAssemblyContext context)
        {
            if (context == null || string.IsNullOrWhiteSpace(argument))
                return;

            ModuleDirectiveState state = ModuleDirectiveState.For(context);
            if (state == null)
                return;

            // "Symbol" alone, or "Symbol, Module" / "Symbol Module" when the
            // expected provider is known. The module name is advisory: it makes a
            // diagnostic precise, resolution is still by symbol name.
            List<string> parts = ModuleDirectiveState.SplitNames(argument);
            if (parts.Count == 0)
                return;

            string symbol = parts[0];
            string module = parts.Count > 1 ? parts[1] : string.Empty;
            state.Imports.Add(new DeclaredImport(symbol, module));
        }
    }
}
