using System;
using WinASM65.Core;
using WinASM65.Segments;

namespace WinASM65.Targets
{
    public static class TargetResolver
    {
        public static ResolvedTarget Resolve(TargetConf config, string cliSystem, string cliCpu, string cliFormat = null)
        {
            ResolvedTarget target = new ResolvedTarget();
            string systemId = !string.IsNullOrWhiteSpace(cliSystem)
                ? cliSystem
                : (config != null ? config.System : null);

            ResolvedTarget preset;
            if (SystemCatalog.TryGet(systemId, out preset))
                target = preset;
            else if (!string.IsNullOrWhiteSpace(systemId))
                throw new ArgumentException("Unknown system '" + systemId + "'. Use -t list.");

            if (config != null)
            {
                if (!string.IsNullOrWhiteSpace(config.Cpu))
                    target.CpuName = config.Cpu;
                if (!string.IsNullOrWhiteSpace(config.Format))
                    target.FormatName = config.Format;
                ushort load;
                if (NumericLiteral.TryParseUInt16(config.LoadAddress, out load))
                    target.LoadAddress = load;
                ushort run;
                if (NumericLiteral.TryParseUInt16(config.RunAddress, out run))
                    target.RunAddress = run;
                int rom;
                if (NumericLiteral.TryParseInteger(config.RomSize, out rom) && rom > 0)
                    target.RomSize = rom;
                if (config.DefineHardwareSymbols.HasValue)
                    target.DefineHardwareSymbols = config.DefineHardwareSymbols.Value;
                ApplyInes(target, config.Ines);
            }

            if (!string.IsNullOrWhiteSpace(cliCpu))
                target.CpuName = cliCpu;

            if (!string.IsNullOrWhiteSpace(cliFormat))
                target.FormatName = cliFormat;

            return target;
        }

        private static void ApplyInes(ResolvedTarget target, InesConf ines)
        {
            if (ines == null)
                return;
            if (ines.PrgBanks.HasValue)
                target.InesPrgBanks = ines.PrgBanks.Value;
            if (ines.ChrBanks.HasValue)
                target.InesChrBanks = ines.ChrBanks.Value;
            if (ines.Mapper.HasValue)
                target.InesMapper = ines.Mapper.Value;
            if (!string.IsNullOrWhiteSpace(ines.Mirroring))
                target.InesMirroring = ines.Mirroring;
            target.InesBattery = ines.Battery;
        }
    }
}
