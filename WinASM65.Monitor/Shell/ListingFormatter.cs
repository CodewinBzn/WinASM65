using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using WinASM65.Cpu;
using WinASM65.Monitor.Abstractions;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// Formats listing rows for display in the listing pane and the editor gutter.
    ///
    /// Pure formatting logic, no Terminal.Gui dependencies. The listing pane and the
    /// editor gutter both use this so they render identically — a row that looks one
    /// way in the listing looks the same in the gutter.
    /// </summary>
    public static class ListingFormatter
    {
        /// <summary>Width in columns reserved for the editor gutter.</summary>
        public const int GutterWidth = 24;

        private const string AddressFormat = "X4";
        private const string ByteFormat = "X2";
        private const string CycleFormat = "X2";

        /// <summary>
        /// Formats a listing row for the listing pane.
        ///
        /// Example: "$8000  A9 01      LDA #$01        2"
        ///          |addr| |bytes|    |mnemonic|     |cycles|
        /// </summary>
        public static string Row(ListingRow row)
        {
            if (row == null)
                return string.Empty;

            StringBuilder sb = new StringBuilder();

            if (row.Address >= 0)
            {
                sb.Append("$").Append(row.Address.ToString(AddressFormat, CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(new string(' ', AddressFormat.Length + 1));
            }

            sb.Append(' ');

            if (row.Bytes.Count > 0)
            {
                for (int i = 0; i < row.Bytes.Count; i++)
                {
                    sb.Append(row.Bytes[i].ToString(ByteFormat, CultureInfo.InvariantCulture));
                    if (i + 1 < row.Bytes.Count)
                        sb.Append(' ');
                }
            }

            sb.Append(' ', Math.Max(1, 12 - sb.Length));

            if (!string.IsNullOrEmpty(row.Source))
            {
                sb.Append(row.Source);
            }

            if (row.Cycles.HasValue)
            {
                sb.Append(' ', Math.Max(1, 30 - sb.Length));
                sb.Append(row.Cycles.Value.ToString(CycleFormat, CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formats the gutter text for a single editor row.
        ///
        /// The gutter is 24 columns: address (4), space, bytes (8 max), space, cycles (3).
        /// </summary>
        public static string Gutter(EditorRow row)
        {
            if (row == null)
                return string.Empty.PadRight(GutterWidth);

            StringBuilder sb = new StringBuilder();

            if (row.Address >= 0)
            {
                sb.Append("$").Append(row.Address.ToString(AddressFormat, CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(new string(' ', AddressFormat.Length + 1));
            }

            sb.Append(' ');

            if (row.Bytes.Count > 0)
            {
                for (int i = 0; i < row.Bytes.Count; i++)
                {
                    sb.Append(row.Bytes[i].ToString(ByteFormat, CultureInfo.InvariantCulture));
                    if (i + 1 < row.Bytes.Count)
                        sb.Append(' ');
                }
            }

            sb.Append(' ', Math.Max(1, GutterWidth - 2 - sb.Length));

            if (row.Cycles.HasValue)
            {
                sb.Append(row.Cycles.Value.ToString(CycleFormat, CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append("  ");
            }

            return sb.Length > GutterWidth ? sb.ToString(0, GutterWidth) : sb.ToString().PadRight(GutterWidth);
        }
    }
}