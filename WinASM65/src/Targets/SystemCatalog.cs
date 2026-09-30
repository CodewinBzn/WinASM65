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
            get
            {
                List<string> names = new List<string>();
                foreach (ResolvedTarget target in Systems.Values)
                    names.Add(target.SystemId);
                names.Sort(StringComparer.OrdinalIgnoreCase);
                return names;
            }
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
                ResolvedTarget target = null;
                foreach (ResolvedTarget candidate in Systems.Values)
                {
                    if (string.Equals(candidate.SystemId, name, StringComparison.OrdinalIgnoreCase))
                    {
                        target = candidate;
                        break;
                    }
                }
                if (target == null)
                    continue;
                text.AppendFormat("  {0,-12} cpu={1,-6} format={2}", name, target.CpuName, target.FormatName);
                if (target.LoadAddress.HasValue)
                    text.AppendFormat(" load=${0:X4}", target.LoadAddress.Value);
                if (target.RomSize.HasValue)
                    text.AppendFormat(" rom={0}", target.RomSize.Value);
                if (target.Hardware != null && !target.Hardware.IsEmpty)
                {
                    // The machine a build targeted is part of the build's record.
                    // A PAL and an NTSC build produce the same bytes here, so
                    // nothing else in the output would say which one it was.
                    text.Append(" hw=" + target.Hardware.Describe());
                }
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

            // The NES entries differ only by the video standard, which is exactly
            // what a named target should be: two words that stand for a
            // configuration, not two systems to maintain.
            AddNes(map, "nes", HardwareOptions.StandardNtsc);
            AddNes(map, "famicom", HardwareOptions.StandardNtsc);
            AddNes(map, "nes-pal", HardwareOptions.StandardPal);
            AddNes(map, "nes-ntsc", HardwareOptions.StandardNtsc);

            AddCommodore(map, "c64", 0x0801, HardwareOptions.ModelC64, null, null, HardwareOptions.Sid6581);
            AddCommodore(map, "c64c", 0x0801, HardwareOptions.ModelC64C, null, null, HardwareOptions.Sid8580);
            AddCommodore(map, "c64-pal", 0x0801, HardwareOptions.ModelC64, HardwareOptions.StandardPal, null, HardwareOptions.Sid6581);
            AddCommodore(map, "c64-ntsc", 0x0801, HardwareOptions.ModelC64, HardwareOptions.StandardNtsc, null, HardwareOptions.Sid6581);
            AddCommodore(map, "c128", 0x1C01, HardwareOptions.ModelC128, null, "vic", HardwareOptions.Sid6581);
            AddCommodore(map, "c128-vdc", 0x1C01, HardwareOptions.ModelC128, null, "vdc", HardwareOptions.Sid8580);
            AddCommodore(map, "c128d", 0x1C01, HardwareOptions.ModelC128D, null, "vdc", HardwareOptions.Sid8580);
            AddCommodore(map, "vic20", 0x1001, HardwareOptions.ModelVvic20, null, null, null);
            AddCommodore(map, "vic20-pal", 0x1001, HardwareOptions.ModelVvic20, HardwareOptions.StandardPal, null, null);
            AddCommodore(map, "vic20-ntsc", 0x1001, HardwareOptions.ModelVvic20, HardwareOptions.StandardNtsc, null, null);
            AddCommodore(map, "plus4", 0x1001, HardwareOptions.ModelPlus4, null, "ted", "ted");
            AddCommodore(map, "c16", 0x1001, HardwareOptions.ModelC16, null, "ted", "ted");
            AddCommodore(map, "pet2001", 0x0401, HardwareOptions.ModelPet2001, null, null, null);
            AddCommodore(map, "pet", 0x0401, HardwareOptions.ModelPet2001, null, null, null);
            AddCommodore(map, "pet2001n", 0x0401, HardwareOptions.ModelPet2001N, null, null, null);
            AddCommodore(map, "cbm2-80", null, HardwareOptions.ModelCbm2, null, "crtc", HardwareOptions.Sid6581);
            AddCommodore(map, "cbm2-40", null, HardwareOptions.ModelCbm2, null, "vic", HardwareOptions.Sid6581);
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

        private static void AddNes(Dictionary<string, ResolvedTarget> map, string id, string standard)
        {
            // No sound chip named: the NES has none to name. Its audio registers
            // belong to the PPU and are already in the table.
            HardwareOptions options = new HardwareOptions
            {
                Model = HardwareOptions.ModelNes,
                VideoStandard = standard
            };
            Add(map, id, "6502", "ines", null, HardwareProfile.Build(options), true, t =>
            {
                t.InesPrgBanks = 1;
                t.InesChrBanks = 1;
                t.Hardware = options.Clone();
            });
        }

        /// <summary>
        /// The symbol table of a machine, built from its options. The catalog
        /// holds no table of its own for these: a second table for the same
        /// machine is a second place to forget the same correction.
        /// </summary>
        private static Dictionary<string, long> Hardware(string model, string standard,
            string video, string sound)
        {
            HardwareOptions options = new HardwareOptions
            {
                Model = model,
                VideoStandard = standard,
                VideoChip = video,
                SoundChip = sound
            };
            return HardwareProfile.Build(options);
        }

        private static void AddCommodore(Dictionary<string, ResolvedTarget> map, string id, ushort? load,
            string model, string standard, string video, string sound)
        {
            HardwareOptions options = new HardwareOptions
            {
                Model = model,
                VideoStandard = standard,
                VideoChip = video,
                SoundChip = sound
            };
            Dictionary<string, long> symbols = HardwareProfile.Build(options);
            Add(map, id, "6502", "prg", load, symbols, true, t => t.Hardware = options.Clone());
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
            // Stored under the normalized id, not the one written here. Lookups
            // normalize too, so a key kept with its dash would be unreachable:
            // 'c64-pal' and 'c64pal' are the same target and only one can be found.
            map[Normalize(id)] = target;
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
