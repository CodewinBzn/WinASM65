using System.Collections.Generic;

namespace WinASM65.Targets
{
    public class ResolvedTarget
    {
        public string SystemId { get; set; }
        public string CpuName { get; set; }
        public string FormatName { get; set; }
        public ushort? LoadAddress { get; set; }
        public ushort? RunAddress { get; set; }
        public ushort? OriginAddress { get; set; }
        public int? RomSize { get; set; }
        public bool DefineHardwareSymbols { get; set; }
        public int InesPrgBanks { get; set; }
        public int InesChrBanks { get; set; }
        public int InesMapper { get; set; }
        public string InesMirroring { get; set; }
        public bool InesBattery { get; set; }
        public Dictionary<string, long> HardwareSymbols { get; set; }

        public ResolvedTarget()
        {
            SystemId = "raw";
            CpuName = "6502";
            FormatName = "bin";
            InesPrgBanks = 1;
            InesMirroring = "vertical";
            HardwareSymbols = new Dictionary<string, long>();
        }

        public ResolvedTarget Clone()
        {
            return new ResolvedTarget
            {
                SystemId = SystemId,
                CpuName = CpuName,
                FormatName = FormatName,
                LoadAddress = LoadAddress,
                RunAddress = RunAddress,
                OriginAddress = OriginAddress,
                RomSize = RomSize,
                DefineHardwareSymbols = DefineHardwareSymbols,
                InesPrgBanks = InesPrgBanks,
                InesChrBanks = InesChrBanks,
                InesMapper = InesMapper,
                InesMirroring = InesMirroring,
                InesBattery = InesBattery,
                HardwareSymbols = HardwareSymbols == null
                    ? new Dictionary<string, long>()
                    : new Dictionary<string, long>(HardwareSymbols)
            };
        }
    }
}
