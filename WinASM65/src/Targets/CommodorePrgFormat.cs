using System.Collections.Generic;
using WinASM65.Core;

namespace WinASM65.Targets
{
    public class CommodorePrgFormat : IExecutableFormat
    {
        public string Name { get { return "prg"; } }

        public OperationResult Write(string path, byte[] payload, ResolvedTarget target)
        {
            payload = payload ?? new byte[0];
            ushort load = ResolveLoadAddress(target, (ushort)0x0801);
            byte[] prg = new byte[payload.Length + 2];
            prg[0] = (byte)(load & 0xFF);
            prg[1] = (byte)((load >> 8) & 0xFF);
            System.Array.Copy(payload, 0, prg, 2, payload.Length);
            return ExecutableFile.WriteBytes(path, prg);
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
