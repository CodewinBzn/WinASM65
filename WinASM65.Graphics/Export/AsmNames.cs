using System.Text;

namespace WinASM65.Graphics.Export
{
    /// <summary>
    /// The names generated sources define: labels in lower snake case
    /// (<c>prefix + name</c>), constants in upper case.
    ///
    /// The assembler compares names without regard to case, so two assets whose
    /// names differ only in case or punctuation would define the same label; the
    /// validators compare names through <see cref="Snake"/> for that reason.
    /// </summary>
    public static class AsmNames
    {
        /// <summary>
        /// <c>Ship Left</c>, <c>shipLeft</c> and <c>ship-left</c> all give <c>ship_left</c>:
        /// words split at spaces, punctuation and lower-to-upper changes, joined by one
        /// underscore. A name that would start with a digit gets a leading underscore.
        /// </summary>
        public static string Snake(string name)
        {
            StringBuilder text = new StringBuilder();
            string source = name ?? string.Empty;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (char.IsLetterOrDigit(c) && c < 128)
                {
                    bool boundary = char.IsUpper(c) && i > 0
                        && (char.IsLower(source[i - 1]) || char.IsDigit(source[i - 1])
                            || (char.IsUpper(source[i - 1]) && i + 1 < source.Length && char.IsLower(source[i + 1])));
                    if (boundary && text.Length > 0 && text[text.Length - 1] != '_')
                        text.Append('_');
                    text.Append(char.ToLowerInvariant(c));
                }
                else if (text.Length > 0 && text[text.Length - 1] != '_')
                {
                    text.Append('_');
                }
            }
            string snake = text.ToString().TrimEnd('_');
            if (snake.Length > 0 && char.IsDigit(snake[0]))
                snake = "_" + snake;
            return snake;
        }

        /// <summary>A prefix as written, lower case, with anything a name cannot hold made an underscore.</summary>
        public static string Prefix(string prefix)
        {
            StringBuilder text = new StringBuilder();
            foreach (char c in prefix ?? string.Empty)
                text.Append((char.IsLetterOrDigit(c) && c < 128) || c == '_' ? char.ToLowerInvariant(c) : '_');
            return text.ToString();
        }

        /// <summary>A label: the prefix, then the name in snake case.</summary>
        public static string Label(string prefix, string name)
        {
            string label = Prefix(prefix) + Snake(name);
            return label.Length > 0 && char.IsDigit(label[0]) ? "_" + label : label;
        }

        /// <summary>A constant: the prefix, then the name in snake case, all upper case.</summary>
        public static string Constant(string prefix, string name)
        {
            return Label(prefix, name).ToUpperInvariant();
        }
    }
}
