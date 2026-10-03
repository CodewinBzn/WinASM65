using System;
using WinASM65.Monitor.Protocol;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// In-place editing of the RAM pane's live window ($0000-$07FF).
    ///
    /// The editable address set is exactly the RAM pane window: $0000-$07FF
    /// (2 KiB). This includes the zero page ($0000-$00FF), the stack
    /// ($0100-$01FF), and ordinary RAM ($0200-$07FF). All three regions are
    /// fully writable; the region markers exist only for display.
    ///
    /// Every failure is a string, never an exception. A pane must not be able
    /// to take the shell down. A successful write is verified by reading the
    /// byte back; if the backend accepted the write but the value did not
    /// change, that is reported as a failure.
    /// </summary>
    public static class RamEditing
    {
        /// <summary>First editable address (inclusive), matches <see cref="RamDump.WindowStart"/>.</summary>
        public const int EditWindowStart = RamDump.WindowStart;

        /// <summary>Last editable address (inclusive), matches <see cref="RamDump.WindowEnd"/>.</summary>
        public const int EditWindowEnd = RamDump.WindowEnd;

        /// <summary>
        /// Writes a single byte to the live machine and verifies it took effect.
        /// </summary>
        /// <param name="backend">The memory backend to write through.</param>
        /// <param name="address">Address in the range $0000-$07FF.</param>
        /// <param name="value">Byte value to write.</param>
        /// <returns>
        /// "OK $XXXX" on success (XXXX is the address in hex).
        /// "ERR <reason>" on any validation or backend failure.
        /// </returns>
        public static string WriteByte(IMemoryBackend backend, int address, byte value)
        {
            if (backend == null)
                return MonitorProtocol.ErrPrefix + " backend is null";

            if (address < EditWindowStart || address > EditWindowEnd)
                return MonitorProtocol.ErrPrefix + " address $" + address.ToString("X4")
                    + " is outside the editable window $" + EditWindowStart.ToString("X4")
                    + "-$" + EditWindowEnd.ToString("X4");

            try
            {
                backend.Write(address, new byte[] { value });
            }
            catch (MonitorException ex)
            {
                return MonitorProtocol.ErrPrefix + " " + ex.Message;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return MonitorProtocol.ErrPrefix + " " + ex.Message;
            }

            byte[] readBack;
            try
            {
                readBack = backend.Read(address, 1);
            }
            catch (MonitorException ex)
            {
                return MonitorProtocol.ErrPrefix + " write accepted but read-back failed: " + ex.Message;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return MonitorProtocol.ErrPrefix + " write accepted but read-back failed: " + ex.Message;
            }

            if (readBack.Length != 1 || readBack[0] != value)
            {
                string got = readBack.Length == 1 ? readBack[0].ToString("X2") : "<no byte>";
                return MonitorProtocol.ErrPrefix + " write accepted but value not observed at $" + address.ToString("X4")
                    + " (expected " + value.ToString("X2") + ", got " + got + ")";
            }

            return MonitorProtocol.OkPrefix + " $" + address.ToString("X4");
        }

        /// <summary>
        /// Writes multiple consecutive bytes to the live machine and verifies they took effect.
        /// </summary>
        /// <param name="backend">The memory backend to write through.</param>
        /// <param name="address">Starting address in the range $0000-$07FF.</param>
        /// <param name="values">Bytes to write. Must not be null or empty.</param>
        /// <returns>
        /// "OK $XXXX N byte(s)" on success.
        /// "ERR <reason>" on any validation or backend failure.
        /// </returns>
        public static string WriteBytes(IMemoryBackend backend, int address, byte[] values)
        {
            if (backend == null)
                return MonitorProtocol.ErrPrefix + " backend is null";

            if (values == null || values.Length == 0)
                return MonitorProtocol.ErrPrefix + " no bytes to write";

            int endAddress = address + values.Length - 1;
            if (address < EditWindowStart || endAddress > EditWindowEnd)
                return MonitorProtocol.ErrPrefix + " range $" + address.ToString("X4")
                    + "-$" + endAddress.ToString("X4") + " exceeds the editable window $"
                    + EditWindowStart.ToString("X4") + "-$" + EditWindowEnd.ToString("X4");

            try
            {
                backend.Write(address, values);
            }
            catch (MonitorException ex)
            {
                return MonitorProtocol.ErrPrefix + " " + ex.Message;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return MonitorProtocol.ErrPrefix + " " + ex.Message;
            }

            byte[] readBack;
            try
            {
                readBack = backend.Read(address, values.Length);
            }
            catch (MonitorException ex)
            {
                return MonitorProtocol.ErrPrefix + " write accepted but read-back failed: " + ex.Message;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return MonitorProtocol.ErrPrefix + " write accepted but read-back failed: " + ex.Message;
            }

            if (readBack.Length != values.Length)
            {
                return MonitorProtocol.ErrPrefix + " write accepted but read-back length mismatch at $"
                    + address.ToString("X4") + " (expected " + values.Length + ", got " + readBack.Length + ")";
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (readBack[i] != values[i])
                {
                    return MonitorProtocol.ErrPrefix + " write accepted but value not observed at $"
                        + (address + i).ToString("X4") + " (expected " + values[i].ToString("X2")
                        + ", got " + readBack[i].ToString("X2") + ")";
                }
            }

            return MonitorProtocol.OkPrefix + " $" + address.ToString("X4") + " " + values.Length + " byte(s)";
        }
    }
}