// WinASM65 - String and character literals

using System.Collections.Generic;
using System.Globalization;

namespace WinASM65.Core
{
    /// <summary>
    /// Decodes the body of a string or character literal into bytes.
    /// <para>
    /// Escapes: <c>\n \r \t \0 \\ \" \' \xNN</c>. Any other character after a backslash
    /// is an error rather than a guess, so a typo does not silently become data.
    /// Characters above $FF cannot be a byte and are refused for the same reason.
    /// </para>
    /// </summary>
    public static class StringLiteral
    {
        /// <summary>
        /// Decodes a quoted token (<c>"…"</c> or <c>'c'</c>, quotes included).
        /// </summary>
        public static bool TryDecode(string token, out byte[] bytes, out string error)
        {
            bytes = null;
            error = null;
            if (string.IsNullOrEmpty(token) || token.Length < 2)
            {
                error = ErrorCodes.UNTERMINATED_STRING;
                return false;
            }

            char quote = token[0];
            if ((quote != '"' && quote != '\'') || token[token.Length - 1] != quote || token.Length == 1)
            {
                error = ErrorCodes.UNTERMINATED_STRING;
                return false;
            }

            return TryDecodeBody(token.Substring(1, token.Length - 2), out bytes, out error);
        }

        public static bool TryDecodeBody(string body, out byte[] bytes, out string error)
        {
            bytes = null;
            error = null;
            List<byte> result = new List<byte>();
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (c != '\\')
                {
                    if (c > 0xFF)
                    {
                        error = string.Format(ErrorCodes.INVALID_ESCAPE, c);
                        return false;
                    }
                    result.Add((byte)c);
                    continue;
                }

                if (i + 1 >= body.Length)
                {
                    error = string.Format(ErrorCodes.INVALID_ESCAPE, "\\");
                    return false;
                }

                char e = body[++i];
                switch (e)
                {
                    case 'n': result.Add(0x0A); break;
                    case 'r': result.Add(0x0D); break;
                    case 't': result.Add(0x09); break;
                    case '0': result.Add(0x00); break;
                    case '\\': result.Add((byte)'\\'); break;
                    case '"': result.Add((byte)'"'); break;
                    case '\'': result.Add((byte)'\''); break;
                    case 'x':
                    case 'X':
                        int value;
                        if (i + 2 < body.Length
                            && int.TryParse(body.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                        {
                            result.Add((byte)value);
                            i += 2;
                            break;
                        }
                        error = string.Format(ErrorCodes.INVALID_ESCAPE, "\\" + e);
                        return false;
                    default:
                        error = string.Format(ErrorCodes.INVALID_ESCAPE, "\\" + e);
                        return false;
                }
            }
            bytes = result.ToArray();
            return true;
        }
    }
}
