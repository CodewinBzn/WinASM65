using System.Collections.Generic;
using System.Text;
using WinASM65.Core;

namespace WinASM65.Targets
{
    public class IntelHexFormat : IExecutableFormat
    {
        public const int BytesPerDataRecord = 16;

        public string Name { get { return "ihex"; } }

        public OperationResult Write(string path, byte[] payload, ResolvedTarget target)
        {
            payload = payload ?? new byte[0];
            ushort load = ResolveLoadAddress(target);
            ushort run = ResolveRunAddress(target, load);

            StringBuilder text = new StringBuilder();
            text.Append(Record(0, 0x03, new byte[] { 0x00, 0x00, (byte)(run & 0xFF), (byte)((run >> 8) & 0xFF) }));

            int offset = 0;
            int emittedUpper = 0;
            while (offset < payload.Length)
            {
                int linear = (load & 0xFFFF) + offset;
                int upper = (linear >> 16) & 0xFFFF;
                if (upper != emittedUpper)
                {
                    text.Append(Record(0, 0x04, new byte[] { (byte)((upper >> 8) & 0xFF), (byte)(upper & 0xFF) }));
                    emittedUpper = upper;
                }

                int count = System.Math.Min(BytesPerDataRecord, payload.Length - offset);
                byte[] data = new byte[count];
                System.Array.Copy(payload, offset, data, 0, count);
                text.Append(Record(linear & 0xFFFF, 0x00, data));
                offset += count;
            }

            text.Append(Record(0, 0x01, new byte[0]));
            return ExecutableFile.WriteText(path, text.ToString());
        }

        private static string Record(int address, int type, byte[] data)
        {
            data = data ?? new byte[0];
            int count = data.Length;
            StringBuilder record = new StringBuilder();
            record.Append(':');
            record.Append(ToHex((byte)count, 2));
            record.Append(ToHex((byte)((address >> 8) & 0xFF), 2));
            record.Append(ToHex((byte)(address & 0xFF), 2));
            record.Append(ToHex((byte)type, 2));
            for (int i = 0; i < data.Length; i++)
                record.Append(ToHex(data[i], 2));

            int sum = count + ((address >> 8) & 0xFF) + (address & 0xFF) + type;
            for (int i = 0; i < data.Length; i++)
                sum += data[i];
            record.Append(ToHex((byte)((0x100 - (sum & 0xFF)) & 0xFF), 2));
            record.Append('\n');
            return record.ToString();
        }

        private static string ToHex(byte value, int digits)
        {
            string text = value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture);
            while (text.Length < digits)
                text = "0" + text;
            return text;
        }

        private static ushort ResolveLoadAddress(ResolvedTarget target)
        {
            if (target == null)
                return 0;
            if (target.OriginAddress.HasValue)
                return target.OriginAddress.Value;
            if (target.LoadAddress.HasValue)
                return target.LoadAddress.Value;
            return 0;
        }

        private static ushort ResolveRunAddress(ResolvedTarget target, ushort load)
        {
            if (target != null && target.RunAddress.HasValue)
                return target.RunAddress.Value;
            return load;
        }
    }
}
