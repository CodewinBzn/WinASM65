using System.Text;
using WinASM65.Core;

namespace WinASM65.Targets
{
    public class MotorolaSrecFormat : IExecutableFormat
    {
        public const int BytesPerDataRecord = 16;

        public string Name { get { return "srec"; } }

        public OperationResult Write(string path, byte[] payload, ResolvedTarget target)
        {
            payload = payload ?? new byte[0];
            ushort load = ResolveLoadAddress(target);
            ushort run = ResolveRunAddress(target, load);

            StringBuilder text = new StringBuilder();
            text.Append(DataRecord("0", load, new byte[0]));

            int offset = 0;
            while (offset < payload.Length)
            {
                int count = System.Math.Min(BytesPerDataRecord, payload.Length - offset);
                byte[] data = new byte[count];
                System.Array.Copy(payload, offset, data, 0, count);
                int address = (load & 0xFFFF) + offset;
                text.Append(DataRecord("1", address, data));
                offset += count;
            }

            text.Append(DataRecord("9", run, new byte[0]));
            return ExecutableFile.WriteText(path, text.ToString());
        }

        private static string DataRecord(string type, int address, byte[] data)
        {
            data = data ?? new byte[0];
            int addressHigh = (address >> 8) & 0xFF;
            int addressLow = address & 0xFF;
            int count = data.Length + 3;

            StringBuilder record = new StringBuilder();
            record.Append('S').Append(type);
            record.Append(ToHex((byte)count, 2));
            record.Append(ToHex((byte)addressHigh, 2));
            record.Append(ToHex((byte)addressLow, 2));
            for (int i = 0; i < data.Length; i++)
                record.Append(ToHex(data[i], 2));

            int sum = count + addressHigh + addressLow;
            for (int i = 0; i < data.Length; i++)
                sum += data[i];
            record.Append(ToHex((byte)((~sum) & 0xFF), 2));
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
