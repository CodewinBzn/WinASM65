// WinASM65 - Source text that knows about strings and character literals

using System.Collections.Generic;
using System.Text;

namespace WinASM65.Core
{
    /// <summary>
    /// Line-level text operations that must not look inside string or character
    /// literals: a <c>;</c> or a <c>,</c> in <c>"a;b,c"</c> or in <c>';'</c> is data,
    /// not a comment or a separator.
    /// <para>
    /// A <c>'</c> only opens a character literal when the literal is complete at that
    /// position (<c>'c'</c> or <c>'\c'</c>). A lone apostrophe, as in the comment
    /// <c>; wait for 2 VBlank's</c>, is an ordinary character, so old sources keep
    /// their comments.
    /// </para>
    /// </summary>
    public static class SourceText
    {
        /// <summary>
        /// The line without its comments: everything from the first <c>;</c> outside a
        /// literal, and every <c>/* ... */</c>. A <c>/*</c> left open runs to the end of
        /// the line.
        /// </summary>
        public static string StripComment(string line)
        {
            bool inBlock = false;
            bool opened;
            return StripComments(line, ref inBlock, out opened);
        }

        /// <summary>
        /// The line without its comments, for a block comment that spans lines:
        /// <paramref name="inBlock"/> says whether the line starts inside a
        /// <c>/* ... */</c>, and on return whether the next one does.
        /// <paramref name="opened"/> is true when the comment still open at the end
        /// was opened on this line. A block comment is replaced by a space, so
        /// <c>lda/*x*/#1</c> keeps its two words apart; <c>/*</c> and <c>;</c> inside a
        /// string or a character literal are data.
        /// </summary>
        public static string StripComments(string line, ref bool inBlock, out bool opened)
        {
            opened = false;
            if (string.IsNullOrEmpty(line))
                return line ?? string.Empty;

            StringBuilder kept = new StringBuilder(line.Length);
            int i = 0;
            while (i < line.Length)
            {
                if (inBlock)
                {
                    int close = line.IndexOf("*/", i, System.StringComparison.Ordinal);
                    if (close < 0)
                        return kept.ToString();
                    inBlock = false;
                    opened = false;
                    kept.Append(' ');
                    i = close + 2;
                    continue;
                }
                char c = line[i];
                if (c == '"')
                {
                    int end = SkipString(line, i);
                    kept.Append(line, i, end - i);
                    i = end;
                    continue;
                }
                if (c == '\'')
                {
                    int end = CharLiteralEnd(line, i);
                    if (end > 0)
                    {
                        kept.Append(line, i, end - i);
                        i = end;
                        continue;
                    }
                }
                if (c == ';')
                    break;
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '*')
                {
                    inBlock = true;
                    opened = true;
                    i += 2;
                    continue;
                }
                kept.Append(c);
                i++;
            }
            return kept.ToString();
        }

        /// <summary>
        /// Splits on commas that are outside literals and parentheses, trimming each part.
        /// An empty input gives no parts; <c>a,,b</c> gives an empty middle part.
        /// </summary>
        public static List<string> SplitArguments(string text)
        {
            List<string> parts = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return parts;

            int depth = 0;
            int start = 0;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '"')
                {
                    i = SkipString(text, i);
                    continue;
                }
                if (c == '\'')
                {
                    int end = CharLiteralEnd(text, i);
                    if (end > 0)
                    {
                        i = end;
                        continue;
                    }
                }
                if (c == '(')
                    depth++;
                else if (c == ')' && depth > 0)
                    depth--;
                else if (c == ',' && depth == 0)
                {
                    parts.Add(text.Substring(start, i - start).Trim());
                    start = i + 1;
                }
                i++;
            }
            parts.Add(text.Substring(start).Trim());
            return parts;
        }

        /// <summary>The text without whitespace, except inside literals: <c>lda # ' '</c> keeps its space.</summary>
        public static string RemoveWhitespaceOutsideQuotes(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            StringBuilder result = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '"')
                {
                    int end = SkipString(text, i);
                    result.Append(text, i, end - i);
                    i = end;
                    continue;
                }
                if (c == '\'')
                {
                    int end = CharLiteralEnd(text, i);
                    if (end > 0)
                    {
                        result.Append(text, i, end - i);
                        i = end;
                        continue;
                    }
                }
                if (!char.IsWhiteSpace(c))
                {
                    result.Append(c);
                    i++;
                    continue;
                }

                // A run of whitespace between two words is kept as one space, so
                // "TRUE or FALSE" does not become the single name "TRUEorFALSE".
                int next = i;
                while (next < text.Length && char.IsWhiteSpace(text[next]))
                    next++;
                if (result.Length > 0 && next < text.Length
                    && IsWordChar(result[result.Length - 1]) && IsWordChar(text[next]))
                    result.Append(' ');
                i = next;
            }
            return result.ToString();
        }

        private static bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '@';
        }

        /// <summary>Whether a <c>"</c> at <paramref name="start"/> is closed on the line.</summary>
        public static bool IsStringTerminated(string text, int start)
        {
            int end = SkipString(text, start);
            return end <= text.Length && end > start + 1 && text[end - 1] == '"' && !EndsEscaped(text, start, end - 1);
        }

        /// <summary>Index just past the string opening at <paramref name="start"/>, or the end of the text.</summary>
        private static int SkipString(string text, int start)
        {
            int i = start + 1;
            while (i < text.Length)
            {
                if (text[i] == '\\' && i + 1 < text.Length)
                {
                    i += 2;
                    continue;
                }
                if (text[i] == '"')
                    return i + 1;
                i++;
            }
            return text.Length;
        }

        private static bool EndsEscaped(string text, int start, int quote)
        {
            int backslashes = 0;
            for (int i = quote - 1; i > start && text[i] == '\\'; i--)
                backslashes++;
            return backslashes % 2 == 1;
        }

        /// <summary>
        /// Index just past a complete character literal at <paramref name="start"/>:
        /// <c>'c'</c>, <c>'\c'</c> or <c>'\xNN'</c>. Zero when there is none.
        /// </summary>
        public static int CharLiteralEnd(string text, int start)
        {
            if (start + 2 < text.Length && text[start + 1] != '\\' && text[start + 2] == '\'')
                return start + 3;
            if (start + 3 < text.Length && text[start + 1] == '\\')
            {
                if (text[start + 3] == '\'')
                    return start + 4;
                if ((text[start + 2] == 'x' || text[start + 2] == 'X') && start + 5 < text.Length && text[start + 5] == '\'')
                    return start + 6;
            }
            return 0;
        }
    }
}
