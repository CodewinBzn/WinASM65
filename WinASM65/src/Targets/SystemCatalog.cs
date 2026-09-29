using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WinASM65.Targets
{
    public static class SystemCatalog
    {
        private static readonly Dictionary<string, ResolvedTarget> Systems = CreateSystems();

        public static IEnumerable<string> Names
        {
            get { return Systems.Keys.OrderBy(n => n); }
        }

        public static bool TryGet(string systemId, out ResolvedTarget target)
        {
            target = null;
            string key = Normalize(systemId);
            if (string.IsNullOrEmpty(key))
                return false;
            ResolvedTarget found;
            if (!Systems.TryGetValue(key, out found))
                return false;
            target = found.Clone();
            return true;
        }

        public static string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("Known 6502 systems (CPU + executable format):");
            foreach (string name in Names)
            {
                ResolvedTarget target = Systems[name];
                text.AppendFormat("  {0,-12} cpu={1,-6} format={2}", name, target.CpuName, target.FormatName);
                if (target.LoadAddress.HasValue)
                    text.AppendFormat(" load=${0:X4}", target.LoadAddress.Value);
                if (target.RomSize.HasValue)
                    text.AppendFormat(" rom={0}", target.RomSize.Value);
                text.AppendLine();
            }
            return text.ToString();
        }

        public static string Normalize(string systemId)
        {
            if (string.IsNullOrWhiteSpace(systemId))
                return string.Empty;
            return systemId.Trim().ToLowerInvariant().Replace("-", "").Replace("_", "");
        }

        private static Dictionary<string, ResolvedTarget> CreateSystems()
        {
            Dictionary<string, ResolvedTarget> map = new Dictionary<string, ResolvedTarget>(StringComparer.OrdinalIgnoreCase);
            Add(map, "raw", "6502", "bin", null, null, false, null);
            Add(map, "nes", "6502", "ines", null, NesSymbols(), true, t => { t.InesPrgBanks = 1; t.InesChrBanks = 1; });
            Add(map, "famicom", "6502", "ines", null, NesSymbols(), true, t => { t.InesPrgBanks = 1; t.InesChrBanks = 1; });
            Add(map, "c64", "6502", "prg", 0x0801, C64Symbols(), true, null);
            Add(map, "c128", "6502", "prg", 0x1C01, C64Symbols(), true, null);
            Add(map, "vic20", "6502", "prg", 0x1001, Vic20Symbols(), true, null);
            Add(map, "pet", "6502", "prg", 0x0401, null, true, null);
            Add(map, "plus4", "6502", "prg", 0x1001, null, true, null);
            Add(map, "c16", "6502", "prg", 0x1001, null, true, null);
            Add(map, "x16", "65c02", "prg", 0x0801, null, true, null);
            Add(map, "apple2", "6502", "a2bin", 0x0800, Apple2Symbols(), true, null);
            Add(map, "apple2e", "65c02", "a2bin", 0x0800, Apple2Symbols(), true, null);
            Add(map, "atari8", "6502", "xex", 0x0600, Atari8Symbols(), true, null);
            Add(map, "atari800", "6502", "xex", 0x0600, Atari8Symbols(), true, null);
            Add(map, "atari2600", "6502", "rom", null, Atari2600Symbols(), true, t => t.RomSize = 4096);
            Add(map, "vcs", "6502", "rom", null, Atari2600Symbols(), true, t => t.RomSize = 4096);
            Add(map, "bbc", "6502", "bin", 0xE00, BbcSymbols(), true, null);
            Add(map, "bbcmicro", "6502", "bin", 0xE00, BbcSymbols(), true, null);
            Add(map, "electron", "6502", "bin", 0xE00, null, true, null);
            Add(map, "oric", "6502", "bin", 0x0500, null, true, null);
            Add(map, "lynx", "65c02", "bin", null, null, false, null);
            return map;
        }

        private static void Add(Dictionary<string, ResolvedTarget> map, string id, string cpu, string format,
            ushort? load, Dictionary<string, long> symbols, bool defineSymbols, Action<ResolvedTarget> extra)
        {
            ResolvedTarget target = new ResolvedTarget
            {
                SystemId = id,
                CpuName = cpu,
                FormatName = format,
                LoadAddress = load,
                DefineHardwareSymbols = defineSymbols,
                HardwareSymbols = symbols ?? new Dictionary<string, long>()
            };
            if (extra != null)
                extra(target);
            map[id] = target;
        }

        private static Dictionary<string, long> NesSymbols()
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                { "PPUCTRL", 0x2000 }, { "PPUMASK", 0x2001 }, { "PPUSTATUS", 0x2002 },
                { "OAMADDR", 0x2003 }, { "OAMDATA", 0x2004 }, { "PPUSCROLL", 0x2005 },
                { "PPUADDR", 0x2006 }, { "PPUDATA", 0x2007 }, { "OAMDMA", 0x4014 },
                { "SQ1_VOL", 0x4000 }, { "DMC_FREQ", 0x4010 }, { "SND_CHN", 0x4015 },
                { "JOY1", 0x4016 }, { "JOY2", 0x4017 }
            };
        }

        private static Dictionary<string, long> C64Symbols()
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                { "VIC", 0xD000 }, { "SID", 0xD400 }, { "CIA1", 0xDC00 }, { "CIA2", 0xDD00 }
            };
        }

        private static Dictionary<string, long> Vic20Symbols()
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                { "VIC", 0x9000 }, { "VIA1", 0x9110 }, { "VIA2", 0x9120 }
            };
        }

        private static Dictionary<string, long> Apple2Symbols()
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                { "KBD", 0xC000 }, { "KBDSTRB", 0xC010 }, { "TXTCLR", 0xC050 }
            };
        }

        private static Dictionary<string, long> Atari8Symbols()
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                { "DMACTL", 0xD400 }, { "COLBK", 0xD01A }, { "CONSOL", 0xD01F }
            };
        }

        private static Dictionary<string, long> Atari2600Symbols()
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                { "VSYNC", 0x00 }, { "VBLANK", 0x01 }, { "WSYNC", 0x02 }, { "COLUBK", 0x09 },
                { "GRP0", 0x1B }, { "GRP1", 0x1C }, { "INTIM", 0x284 }, { "TIM64T", 0x296 }
            };
        }

        private static Dictionary<string, long> BbcSymbols()
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                { "OSWRCH", 0xFFEE }, { "OSBYTE", 0xFFF4 }, { "OSWORD", 0xFFF1 }
            };
        }
    }
}
