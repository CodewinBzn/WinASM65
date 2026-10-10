using System;
using System.Collections.Generic;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using WinASM65.Monitor.Abstractions;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// Adapts the lexer already used by the listing pane to SharpConsoleUI's
    /// token renderer. The lexer remains the single source of syntax rules; this
    /// class only translates its role tags into foreground colours.
    /// </summary>
    public sealed class AssemblerSyntaxHighlighter : ISyntaxHighlighter
    {
        private readonly AssemblerListingSource _source;
        private readonly ShellTheme _theme;

        public AssemblerSyntaxHighlighter(AssemblerListingSource source, ShellTheme theme)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _theme = theme ?? ShellTheme.Default;
        }

        ValueTuple<IReadOnlyList<SyntaxToken>, SyntaxLineState> ISyntaxHighlighter.Tokenize(
            string line, int lineIndex, SyntaxLineState startState)
        {
            List<SyntaxToken> tokens = new List<SyntaxToken>();
            if (!string.IsNullOrEmpty(line))
            {
                foreach (ListingToken token in _source.LexLine(line))
                {
                    tokens.Add(new SyntaxToken(
                        token.Start,
                        token.Text.Length,
                        _theme.Foreground(token.Role)));
                }
            }

            return (tokens, SyntaxLineState.Initial);
        }
    }
}
