namespace WinASM65.Targets
{
    public class Apple2BinaryFormat : IExecutableFormat
    {
        public string Name { get { return "a2bin"; } }

        public Core.OperationResult Write(string path, byte[] payload, ResolvedTarget target)
        {
            payload = payload ?? new byte[0];
            ushort load = ResolveLoadAddress(target, (ushort)0x0800);
            ushort length = (ushort)payload.Length;
            byte[] file = new byte[payload.Length + 4];
            file[0] = (byte)(load & 0xFF);
            file[1] = (byte)((load >> 8) & 0xFF);
            file[2] = (byte)(length & 0xFF);
            file[3] = (byte)((length >> 8) & 0xFF);
            System.Array.Copy(payload, 0, file, 4, payload.Length);
            return ExecutableFile.WriteBytes(path, file);
        }

        private static ushort ResolveLoadAddress(ResolvedTarget target, ushort fallback)
        {
            if (target == null)
                return fallback;
            if (target.OriginAddress.HasValue)
                return target.OriginAddress.Value;
            if (target.LoadAddress.HasValue)
                return target.LoadAddress.Value;
            return fallback;
        }
    }
}
