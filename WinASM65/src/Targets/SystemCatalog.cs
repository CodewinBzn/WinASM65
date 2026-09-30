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
            Add(map, "bbc", "6502", "bbc", 0xE00, BbcSymbols(), true,
                t => t.BbcFileType = "code");
            Add(map, "bbcmicro", "6502", "bbc", 0xE00, BbcSymbols(), true,
                t => t.BbcFileType = "code");
            Add(map, "tube", "6502", "tube", null, TubeSymbols(), true, null);
            Add(map, "bbc2p", "6502", "tube", null, TubeSymbols(), true, null);
            // The processor in the second processor is a 6502B according to the
            // user guide and a 65C02 according to the service manual and the
            // chips on surviving boards. Both exist, so both are offered and
            // neither is called the truth.
            Add(map, "tube65c02", "65c02", "tube", null, TubeSymbols(), true, null);
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

        /// <summary>
        /// The main machine's map, from the MOS 1.20 memory map. Two things here
        /// are not what their names suggest, which is why they are named after
        /// the map rather than after the device: the 6522 register order is the
        /// BBC's own, and the "1 MHz counter" is the system VIA's timers.
        /// </summary>
        private static Dictionary<string, long> BbcSymbols()
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                // OS entry points, $FFA7 to $FFD7
                { "OSWRCH", 0xFFEE }, { "OSWORD", 0xFFF1 }, { "OSBYTE", 0xFFF4 },
                { "OSCLI", 0xFFF7 }, { "OSNEWL", 0xFFE7 }, { "OSASCI", 0xFFE3 },
                { "OSRDCH", 0xFFE0 }, { "OSRDRM", 0xFFB9 }, { "OSBPUT", 0xFFD4 },
                { "OSBGET", 0xFFD7 }, { "OSGBPB", 0xFFD1 }, { "OSFIND", 0xFFCE },
                { "NVRDCH", 0xFFC8 }, { "NVWRCH", 0xFFCB }, { "OSARGS", 0xFFDA },
                { "OSFILE", 0xFFDD }, { "OSRDLINE", 0xFFA7 },

                // CRTC and the video ULA. There is one video ULA control register
                // and one palette register, not a set of interrupt registers as
                // on machines with a programmable raster chip.
                { "CRTC", 0xFE00 }, { "ACIA", 0xFE08 }, { "ACIADATA", 0xFE09 },
                { "SERIALULA", 0xFE10 }, { "VIDULA", 0xFE20 }, { "PALETTE", 0xFE21 },
                { "ROMSEL", 0xFE30 },

                // System VIA. Timer 1 is the OS 100 Hz clock and belongs to the
                // OS; a program that writes it stops the machine.
                { "SYSEOR_ORB", 0xFE40 }, { "SYSEOR_ORA", 0xFE41 },
                { "SYSEOR_DDRB", 0xFE42 }, { "SYSEOR_DDRA", 0xFE43 },
                { "SYSEOR_T1L", 0xFE44 }, { "SYSEOR_T1H", 0xFE45 },
                { "SYSEOR_T1LL", 0xFE46 }, { "SYSEOR_T1LH", 0xFE47 },
                { "SYSEOR_T2L", 0xFE48 }, { "SYSEOR_T2H", 0xFE49 },
                { "SYSEOR_SR", 0xFE4A }, { "SYSEOR_ACR", 0xFE4B },
                { "SYSEOR_PCR", 0xFE4C }, { "SYSEOR_IFR", 0xFE4D },
                { "SYSEOR_IER", 0xFE4E }, { "SYSEOR_PORT", 0xFE4F },

                // User VIA: the printer on A, the user port on B.
                { "USROR_ORB", 0xFE60 }, { "USROR_ORA", 0xFE61 },
                { "USROR_DDRB", 0xFE62 }, { "USROR_DDRA", 0xFE63 },
                { "USROR_T1L", 0xFE64 }, { "USROR_T1H", 0xFE65 },
                { "USROR_T1LL", 0xFE66 }, { "USROR_T1LH", 0xFE67 },
                { "USROR_T2L", 0xFE68 }, { "USROR_T2H", 0xFE69 },
                { "USROR_SR", 0xFE6A }, { "USROR_ACR", 0xFE6B },
                { "USROR_PCR", 0xFE6C }, { "USROR_IFR", 0xFE6D },
                { "USROR_IER", 0xFE6E }, { "USROR_PORT", 0xFE6F },

                // Analogue converter, and the tube as the main processor sees it
                { "ADSTART", 0xFEC0 }, { "ADHIGH", 0xFEC1 }, { "ADLOW", 0xFEC2 },
                { "TUBESTATUS", 0xFEE0 }, { "TUBEDATA3", 0xFEE5 },

                // The 1 MHz bus, reachable by the main processor only
                { "FRED", 0xFC00 }, { "JIM", 0xFD00 }
            };
        }

        /// <summary>
        /// A 6502 second processor is 64 KB of RAM with no ROM of its own in the
        /// way and, more to the point, no I/O: the main processor does all of
        /// that over the tube. So there is nothing here to name except the parts
        /// of the map that are spoken for, which is exactly what a program needs
        /// in order not to overwrite the system it is running on.
        /// </summary>
        private static Dictionary<string, long> TubeSymbols()
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                { "PAGE", 0x0800 }, { "HIMEM", 0x8000 },
                { "LANGUAGE", 0x8000 }, { "SPA_OS", 0xF800 },
                { "ZP_FREE_END", 0x00EE }, { "OS_PAGE2", 0x0200 },
                { "OS_ERRORS", 0x0300 }
            };
        }
    }
}
