using System;

namespace WinASM65.Cpu
{
    public static class CpuFactory
    {
        public static ICpuInstructionSet Create(string cpuName)
        {
            string normalized = Normalize(cpuName);
            switch (normalized)
            {
                case "":
                case "6502":
                case "nmos":
                case "nmos6502":
                    return new Cpu6502();
                case "6502x":
                case "nmos6502x":
                case "6502illegal":
                    return new Cpu6502X();
                case "65c02":
                case "cmos":
                    return new Cpu65C02();
                case "r65c02":
                case "rockwell":
                    return new CpuR65C02();
                case "w65c02":
                case "w65c02s":
                case "wdc65c02":
                    return new CpuW65C02();
                default:
                    throw new ArgumentException("Unknown CPU '" + cpuName + "'. Use " + string.Join(", ", Names) + ".");
            }
        }

        /// <summary>The canonical CPU names, one per instruction set.</summary>
        public static readonly string[] Names = { "6502", "6502x", "65c02", "r65c02", "w65c02" };

        /// <summary>One instance of every instruction set, by canonical name.</summary>
        public static System.Collections.Generic.IReadOnlyDictionary<string, ICpuInstructionSet> All
        {
            get
            {
                System.Collections.Generic.Dictionary<string, ICpuInstructionSet> all =
                    new System.Collections.Generic.Dictionary<string, ICpuInstructionSet>();
                foreach (string name in Names)
                    all[name] = Create(name);
                return all;
            }
        }

        public static string Normalize(string cpuName)
        {
            if (string.IsNullOrWhiteSpace(cpuName))
                return string.Empty;
            return cpuName.Trim().ToLowerInvariant().Replace("-", "").Replace(" ", "");
        }
    }
}
