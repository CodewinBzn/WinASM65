using System;
using System.Collections.Generic;
using SharpConsoleUI;
using SharpConsoleUI.Controls;

namespace WinASM65.Monitor.Shell
{
    /// <summary>Highlights JSON keys, strings, numbers, literals, and punctuation.</summary>
    public sealed class JsonSyntaxHighlighter : ISyntaxHighlighter
    {
        private readonly ShellTheme _theme;

        public JsonSyntaxHighlighter(ShellTheme theme)
        {
            _theme = theme ?? ShellTheme.Default;
        }

        ValueTuple<IReadOnlyList<SyntaxToken>, SyntaxLineState> ISyntaxHighlighter.Tokenize(
            string line, int lineIndex, SyntaxLineState startState)
        {
            List<SyntaxToken> tokens = new List<SyntaxToken>();
            if (string.IsNullOrEmpty(line))
                return (tokens, SyntaxLineState.Initial);

            int index = 0;
            while (index < line.Length)
            {
                char current = line[index];
                if (char.IsWhiteSpace(current))
                {
                    index++;
                    continue;
                }

                if (current == '"')
                {
                    int start = index++;
                    while (index < line.Length)
                    {
                        if (line[index] == '\\')
                        {
                            index = Math.Min(index + 2, line.Length);
                            continue;
                        }
                        if (line[index++] == '"')
                            break;
                    }

                    int lookahead = index;
                    while (lookahead < line.Length && char.IsWhiteSpace(line[lookahead]))
                        lookahead++;
                    string role = lookahead < line.Length && line[lookahead] == ':'
                        ? ThemeRole.Label
                        : ThemeRole.Operand;
                    tokens.Add(Token(start, index - start, role));
                    continue;
                }

                if (current == '-' || char.IsDigit(current))
                {
                    int start = index++;
                    while (index < line.Length
                        && (char.IsDigit(line[index]) || line[index] == '.'
                            || line[index] == 'e' || line[index] == 'E'
                            || line[index] == '+' || line[index] == '-'))
                    {
                        index++;
                    }
                    tokens.Add(Token(start, index - start, ThemeRole.Number));
                    continue;
                }

                if (char.IsLetter(current))
                {
                    int start = index++;
                    while (index < line.Length && char.IsLetter(line[index]))
                        index++;
                    string literal = line.Substring(start, index - start);
                    string role = literal == "true" || literal == "false" || literal == "null"
                        ? ThemeRole.Directive
                        : ThemeRole.Error;
                    tokens.Add(Token(start, index - start, role));
                    continue;
                }

                tokens.Add(Token(index, 1, ThemeRole.Chrome));
                index++;
            }

            return (tokens, SyntaxLineState.Initial);
        }

        private SyntaxToken Token(int start, int length, string role)
        {
            return new SyntaxToken(start, length, _theme.Foreground(role));
        }
    }
}
