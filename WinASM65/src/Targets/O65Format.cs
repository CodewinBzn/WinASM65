using System.Collections.Generic;
using WinASM65.Core;

namespace WinASM65.Targets
{
    public class O65Format : IExecutableFormat
    {
        public const int HeaderSize = 12;
        public const int MaxSegmentSize = 0xFFFF;

        public string Name { get { return "o65"; } }

        public OperationResult Write(string path, byte[] payload, ResolvedTarget target)
        {
            payload = payload ?? new byte[0];
            if (payload.Length > MaxSegmentSize)
            {
                List<Diagnostic> diagnostics = new List<Diagnostic>();
                diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0),
                    "O65 segment size exceeds 65535 bytes."));
                return new OperationResult(false, diagnostics);
            }

            ushort load = ResolveLoadAddress(target);

            byte[] file = new byte[HeaderSize + payload.Length];
            file[0] = (byte)'o';
            file[1] = (byte)'6';
            file[2] = (byte)'5';
            file[3] = 0x00;
            file[4] = 0x01;
            file[5] = 0x00;
            file[6] = 0x01;
            file[7] = (byte)((load >> 8) & 0xFF);
            file[8] = (byte)(load & 0xFF);
            file[9] = (byte)((payload.Length >> 8) & 0xFF);
            file[10] = (byte)(payload.Length & 0xFF);
            System.Array.Copy(payload, 0, file, HeaderSize, payload.Length);
            return ExecutableFile.WriteBytes(path, file);
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
    }
}
