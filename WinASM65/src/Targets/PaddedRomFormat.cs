using System.Collections.Generic;
using WinASM65.Core;

namespace WinASM65.Targets
{
    public class PaddedRomFormat : IExecutableFormat
    {
        public string Name { get { return "rom"; } }

        public OperationResult Write(string path, byte[] payload, ResolvedTarget target)
        {
            payload = payload ?? new byte[0];
            int size = target != null && target.RomSize.HasValue ? target.RomSize.Value : NextPowerOfTwo(payload.Length);
            if (size < payload.Length)
            {
                List<Diagnostic> diagnostics = new List<Diagnostic>();
                diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0), "ROM size is smaller than the assembled payload."));
                return new OperationResult(false, diagnostics);
            }
            if (size == 0)
                size = payload.Length;
            return ExecutableFile.WriteBytes(path, ExecutableFile.Pad(payload, size));
        }

        private static int NextPowerOfTwo(int length)
        {
            if (length <= 0)
                return 0;
            int size = 1;
            while (size < length)
                size <<= 1;
            return size;
        }
    }
}
