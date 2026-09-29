using System;
using System.Collections.Generic;
using WinASM65.Core;

namespace WinASM65.Targets
{
    public class InesFormat : IExecutableFormat
    {
        public const int PrgBankSize = 16384;
        public const int ChrBankSize = 8192;

        public string Name { get { return "ines"; } }

        public OperationResult Write(string path, byte[] payload, ResolvedTarget target)
        {
            payload = payload ?? new byte[0];
            if (HasInesHeader(payload))
                return ExecutableFile.WriteBytes(path, payload);

            target = target ?? new ResolvedTarget();
            int prgBanks = target.InesPrgBanks <= 0 ? 1 : target.InesPrgBanks;
            int chrBanks = target.InesChrBanks < 0 ? 0 : target.InesChrBanks;
            int prgSize = prgBanks * PrgBankSize;
            int chrSize = chrBanks * ChrBankSize;
            if (payload.Length > prgSize + chrSize)
            {
                List<Diagnostic> diagnostics = new List<Diagnostic>();
                diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0),
                    "Payload is larger than iNES PRG+CHR size."));
                return new OperationResult(false, diagnostics);
            }

            byte[] rom = new byte[16 + prgSize + chrSize];
            rom[0] = (byte)'N';
            rom[1] = (byte)'E';
            rom[2] = (byte)'S';
            rom[3] = 0x1A;
            rom[4] = (byte)prgBanks;
            rom[5] = (byte)chrBanks;
            rom[6] = BuildFlags6(target);
            rom[7] = (byte)((target.InesMapper & 0xF0));
            int copy = Math.Min(payload.Length, prgSize + chrSize);
            Array.Copy(payload, 0, rom, 16, copy);
            return ExecutableFile.WriteBytes(path, rom);
        }

        public static bool HasInesHeader(byte[] payload)
        {
            return payload != null && payload.Length >= 16
                && payload[0] == (byte)'N'
                && payload[1] == (byte)'E'
                && payload[2] == (byte)'S'
                && payload[3] == 0x1A;
        }

        private static byte BuildFlags6(ResolvedTarget target)
        {
            int flags = (target.InesMapper & 0x0F) << 4;
            if (target.InesBattery)
                flags |= 0x02;
            string mirroring = (target.InesMirroring ?? "vertical").Trim().ToLowerInvariant();
            if (mirroring == "four" || mirroring == "4" || mirroring == "fourscreen")
                flags |= 0x08;
            else if (mirroring == "vertical" || mirroring == "v")
                flags |= 0x01;
            return (byte)flags;
        }
    }
}
