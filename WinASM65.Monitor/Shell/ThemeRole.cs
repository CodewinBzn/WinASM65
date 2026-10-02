using System;
using System.Collections.Generic;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// The names the shell paints by.
    ///
    /// Roles, never inline hex. The palette is defined once, as a table of
    /// role -> colour, and the 16-colour fallback is a second column of the same
    /// table rather than a second hand-maintained theme. A role added here but
    /// not in a theme file falls back to the built-in default for that role
    /// alone, so one bad entry cannot take the whole palette down with it.
    ///
    /// Every name here appears in the shipped <c>theme.json</c>, and every role in
    /// that file is one of these names. Unknown names in a file are reported and
    /// ignored, so a typo is visible rather than silently unused.
    /// </summary>
    public static class ThemeRole
    {
        /// <summary>Body text, and anything with no more specific role.</summary>
        public const string Default = "default";

        /// <summary>Frame lines and pane titles.</summary>
        public const string Chrome = "chrome";

        /// <summary>Line numbers, address column, cycle column.</summary>
        public const string Gutter = "gutter";

        /// <summary>Opcode mnemonic.</summary>
        public const string Mnemonic = "mnemonic";

        /// <summary>Operand text and absolute addresses.</summary>
        public const string Operand = "operand";

        /// <summary>Assembler directives.</summary>
        public const string Directive = "directive";

        /// <summary>Labels and symbols.</summary>
        public const string Label = "label";

        /// <summary>Numeric literals.</summary>
        public const string Number = "number";

        /// <summary>Comments.</summary>
        public const string Comment = "comment";

        /// <summary>Anything that failed.</summary>
        public const string Error = "error";

        /// <summary>A breakpoint mark in the gutter.</summary>
        public const string Breakpoint = "breakpoint";

        /// <summary>Something the user should look at but that is not a failure.</summary>
        public const string Warning = "warning";

        private static readonly string[] All =
        {
            Default,
            Chrome,
            Gutter,
            Mnemonic,
            Operand,
            Directive,
            Label,
            Number,
            Comment,
            Error,
            Breakpoint,
            Warning,
        };

        /// <summary>Every role name, in the order the built-in palette lists them.</summary>
        public static IReadOnlyList<string> Names
        {
            get { return All; }
        }

        public static bool IsKnown(string name)
        {
            if (name == null)
                return false;

            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i], name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}