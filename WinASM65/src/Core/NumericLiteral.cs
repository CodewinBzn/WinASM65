using System;
using System.Globalization;

namespace WinASM65.Core
{
    public static class NumericLiteral
    {
        public static bool TryParseInteger(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string trimmed = text.Trim();
            if (trimmed.StartsWith("$"))
                return int.TryParse(trimmed.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(trimmed.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        public static bool TryParseUInt16(string text, out ushort value)
        {
            int parsed;
            if (!TryParseInteger(text, out parsed) || parsed < 0 || parsed > 0xFFFF)
            {
                value = 0;
                return false;
            }
            value = (ushort)parsed;
            return true;
        }
    }
}
