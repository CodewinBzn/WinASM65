using System.Collections.Generic;
using WinASM65.Core;

namespace WinASM65.Targets
{
    public class AtariXexFormat : IExecutableFormat
    {
        public string Name { get { return "xex"; } }

        public OperationResult Write(string path, byte[] payload, ResolvedTarget target)
        {
            payload = payload ?? new byte[0];
            ushort start = ResolveStartAddress(target);
            int endValue = start + payload.Length - 1;
            if (payload.Length == 0)
                endValue = start;
            if (endValue > 0xFFFF)
            {
                List<Diagnostic> diagnostics = new List<Diagnostic>();
                diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0), "Atari XEX segment exceeds $FFFF."));
                return new OperationResult(false, diagnostics);
            }
            ushort end = (ushort)endValue;

            System.IO.MemoryStream stream = new System.IO.MemoryStream();
            stream.WriteByte(0xFF);
            stream.WriteByte(0xFF);
            WriteWord(stream, start);
            WriteWord(stream, end);
            stream.Write(payload, 0, payload.Length);

            if (target != null && target.RunAddress.HasValue)
            {
                WriteWord(stream, 0x02E0);
                WriteWord(stream, 0x02E1);
                WriteWord(stream, target.RunAddress.Value);
            }

            return ExecutableFile.WriteBytes(path, stream.ToArray());
        }

        private static ushort ResolveStartAddress(ResolvedTarget target)
        {
            const ushort Fallback = 0x0600;
            if (target == null)
                return Fallback;
            if (target.OriginAddress.HasValue)
                return target.OriginAddress.Value;
            if (target.LoadAddress.HasValue)
                return target.LoadAddress.Value;
            return Fallback;
        }

        private static void WriteWord(System.IO.MemoryStream stream, ushort value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
        }
    }
}
