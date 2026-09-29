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
                case "65c02":
                case "cmos":
                case "wdc65c02":
                    return new Cpu65C02();
                default:
                    throw new ArgumentException("Unknown CPU '" + cpuName + "'. Use 6502 or 65c02.");
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
