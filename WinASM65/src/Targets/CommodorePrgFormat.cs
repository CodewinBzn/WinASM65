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

            ushort load;
            if (!TryResolveLoadAddress(target, out load))
            {
                List<Diagnostic> diagnostics = new List<Diagnostic>();
                diagnostics.Add(new Diagnostic(new SourceLocation(path, 0),
                    "No load address for the " + ModelName(target) + ". The first two bytes of a PRG "
                    + "are where the machine puts the code, and this machine is banked, so there is "
                    + "nothing to default to. Set Target.LoadAddress, or put a .org in the source."));
                return new OperationResult(false, diagnostics);
            }

            byte[] prg = new byte[payload.Length + 2];
            prg[0] = (byte)(load & 0xFF);
            prg[1] = (byte)((load >> 8) & 0xFF);
            System.Array.Copy(payload, 0, prg, 2, payload.Length);
            return ExecutableFile.WriteBytes(path, prg);
        }

        private static bool TryResolveLoadAddress(ResolvedTarget target, out ushort load)
        {
            load = 0;
            if (target == null)
            {
                load = 0x0801;
                return true;
            }
            if (target.OriginAddress.HasValue)
            {
                load = target.OriginAddress.Value;
                return true;
            }
            if (target.RunAddress.HasValue)
            {
                load = target.RunAddress.Value;
                return true;
            }
            if (target.LoadAddress.HasValue)
            {
                load = target.LoadAddress.Value;
                return true;
            }

            // $0801 is the C64's, and every Commodore target that is not banked
            // declares its own. Guessing it for a machine that banks its memory
            // would put the code at an address nothing checks.
            if (target.Hardware == null)
            {
                load = 0x0801;
                return true;
            }

            return false;
        }

        private static string ModelName(ResolvedTarget target)
        {
            if (target == null || target.Hardware == null)
                return "target";
            return string.IsNullOrEmpty(target.Hardware.Model)
                ? "target"
                : target.Hardware.Model;
        }
    }
}
