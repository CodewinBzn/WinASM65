using System;
using System.Globalization;

namespace WinASM65.Monitor.Protocol
{
    /// <summary>
    /// Line-oriented text protocol between the monitor and the Lua bridge.
    ///
    /// Deliberately simple: the Lua bridge must stay trivial, so the protocol can
    /// be debugged over telnet or in a text editor.
    ///
    /// Every response starts with OK or ERR. Nothing fails silently: on the same
    /// principle as operand range validation (P0), an unnamed refusal is a bug,
    /// not caution.
    ///
    /// Mandatory bounds. The bridge is a synchronous Lua script: an unbounded
    /// request freezes the emulator, and the user thinks it crashed.
    /// </summary>
    public static class MonitorProtocol
    {
        public const string OkPrefix = "OK";
        public const string ErrPrefix = "ERR";

        // Beyond these, the Lua bridge blocks the emulator. These caps are covered
        // by tests: they are part of the contract, not a mere guard rail.
        public const int MaxReadLength = 4096;
        public const int MaxWriteLength = 4096;
        public const int MaxDisasmCount = 256;
        public const int MaxStateLength = 128 * 1024;

        /// <summary>
        /// Largest block a single LOAD may write. The bridge is synchronous Lua: a
        /// 64 KiB write in one message freezes the emulator long enough for the user
        /// to kill it. The bound is a contract, so it is tested like the others.
        /// </summary>
        public const int MaxLoadLength = 16 * 1024;

        /// <summary>
        /// Protocol address bound. The notion belongs to the machine's memory space
        /// (see <see cref="AddressSpace"/>); the protocol simply conforms to it.
        /// </summary>
        public const int MaxAddress = AddressSpace.MaxAddress;

        // One hex letter, in both cases: IndexOfAny is case sensitive, and "$FFF0"
        // must read as hex rather than as decimal 8000. 6502 opcodes are written in
        // upper case in listings.
        private static readonly char[] HexLetters = new char[]
        {
            'a', 'b', 'c', 'd', 'e', 'f', 'A', 'B', 'C', 'D', 'E', 'F'
        };

        /// <summary>
        /// Accepts "$1234", "0x1234", "d1234" (decimal) or a bare value read as hex.
        ///
        /// A bare value is read as hexadecimal, as every assembler does. That is a
        /// choice, and arbitrary without it: "8000" cannot mean both. Hex wins
        /// because it is the vocabulary of the domain, and because decimal stays
        /// reachable through the "d" prefix.
        ///
        /// A prefix is always authoritative. "$8000" and "0x8000" mean 32768 no
        /// matter what: reinterpreting them based on the presence of letters would
        /// be a quiet way to read the wrong address.
        /// </summary>
        public static bool TryParseAddress(string text, out int address)
        {
            address = 0;
            if (string.IsNullOrEmpty(text))
                return false;

            string value = text.Trim();
            if (value.Length == 0)
                return false;

            NumberStyles style;
            if (value[0] == '$')
            {
                value = value.Substring(1);
                style = NumberStyles.HexNumber;
            }
            else if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(2);
                style = NumberStyles.HexNumber;
            }
            else if (value[0] == 'd' || value[0] == 'D')
            {
                value = value.Substring(1);
                style = NumberStyles.Integer;
            }
            else
            {
                style = NumberStyles.HexNumber;
            }

            if (value.Length == 0)
                return false;

            int parsed;
            if (!int.TryParse(value, style, CultureInfo.InvariantCulture, out parsed))
                return false;

            if (parsed < 0 || parsed > MaxAddress)
                return false;

            address = parsed;
            return true;
        }

        public static bool TryParseHex(string text, out byte[] bytes)
        {
            bytes = null;
            if (string.IsNullOrEmpty(text))
                return false;

            string value = text.Replace(" ", string.Empty);
            if (value.Length == 0 || (value.Length % 2) != 0)
                return false;

            byte[] result = new byte[value.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                int hi = HexValue(value[i * 2]);
                int lo = HexValue(value[(i * 2) + 1]);
                if (hi < 0 || lo < 0)
                    return false;
                result[i] = (byte)((hi << 4) | lo);
            }

            bytes = result;
            return true;
        }

        public static string ToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;

            char[] chars = new char[bytes.Length * 2];
            const string digits = "0123456789ABCDEF";
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = digits[bytes[i] >> 4];
                chars[(i * 2) + 1] = digits[bytes[i] & 0x0F];
            }
            return new string(chars);
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9')
                return c - '0';
            if (c >= 'A' && c <= 'F')
                return c - 'A' + 10;
            if (c >= 'a' && c <= 'f')
                return c - 'a' + 10;
            return -1;
        }
    }
}