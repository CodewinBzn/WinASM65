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

        /// <summary>
        /// Which kind of BBC file to write: <c>code</c> (the default, a file
        /// carrying a code header), <c>text</c>, or <c>flat</c>. Empty for every
        /// other target, which is what makes the field a no-op elsewhere instead
        /// of a BBC assumption leaking out.
        /// </summary>
        public string BbcFileType { get; set; }

        /// <summary>
        /// CPU id the BBC code header announces, so a client running the file on
        /// the wrong processor can say so. Two means 6502; one means the turbo
        /// variant.
        /// </summary>
        public byte BbcCpuType { get; set; }

        /// <summary>Title string the BBC code header carries. Null means a default.</summary>
        public string Title { get; set; }

        /// <summary>Author string the BBC code header carries. Null means a default.</summary>
        public string Author { get; set; }
        public Dictionary<string, long> HardwareSymbols { get; set; }

        /// <summary>
        /// What the machine is made of, for the targets where that varies. Null
        /// for a target whose machine does not vary, which is what keeps the
        /// options from leaking into targets they have nothing to say about.
        /// </summary>
        public HardwareOptions Hardware { get; set; }

        public ResolvedTarget()
        {
            SystemId = "raw";
            CpuName = "6502";
            FormatName = "bin";
            InesPrgBanks = 1;
            InesMirroring = "vertical";
            HardwareSymbols = new Dictionary<string, long>();
            BbcCpuType = BbcFormat.Cpu6502;
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
                BbcFileType = BbcFileType,
                BbcCpuType = BbcCpuType,
                Title = Title,
                Author = Author,
                HardwareSymbols = HardwareSymbols == null
                    ? new Dictionary<string, long>()
                    : new Dictionary<string, long>(HardwareSymbols),
                Hardware = Hardware == null ? null : Hardware.Clone()
            };
        }
    }
}
