using System;
using System.Collections.Generic;

namespace WinASM65.Targets
{
    /// <summary>
    /// Builds a hardware symbol table out of a machine's options.
    /// <para>
    /// One table per family, then the differences applied. A C64C is a C64 with
    /// a different SID, not a second C64: writing both out would be two places
    /// to forget the same correction. What the options genuinely change is the
    /// I/O block, and that is the only thing the tables below differ in.
    /// </para>
    /// <para>
    /// Nothing here invents an address. Where a chip exists but the sources
    /// consulted could not place it, it is left out and the omission is
    /// recorded in the documentation rather than guessed at.
    /// </para>
    /// </summary>
    public static class HardwareProfile
    {
        /// <summary>
        /// The symbol table for a machine, or an empty table when the options do
        /// not describe a machine this file knows.
        /// </summary>
        public static Dictionary<string, long> Build(HardwareOptions options)
        {
            Dictionary<string, long> symbols = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (options == null)
                return symbols;

            string model = HardwareRules.Normalize(options.Model);
            string video = HardwareRules.Normalize(options.VideoChip);

            if (model == HardwareOptions.ModelC64 || model == HardwareOptions.ModelC64C
                || model == HardwareOptions.ModelC128 || model == HardwareOptions.ModelC128D)
            {
                Commodore64Family(symbols, model, video);
            }
            else if (model == HardwareOptions.ModelVvic20)
            {
                Vic20(symbols);
            }
            else if (model == HardwareOptions.ModelPlus4 || model == HardwareOptions.ModelC16)
            {
                Plus4(symbols);
            }
            else if (model == HardwareOptions.ModelPet2001 || model == HardwareOptions.ModelPet2001N)
            {
                Pet(symbols);
            }
            else if (model == HardwareOptions.ModelCbm2)
            {
                Cbm2(symbols, video);
            }
            else if (model == HardwareOptions.ModelNes)
            {
                Nes(symbols);
            }
            else if (model == null && video != null)
            {
                // No model named, but a chip was. The chip alone is enough to say
                // something true, and refusing would hide a mis-typed model.
                ChipOnly(symbols, video);
            }

            AddVideoStandard(symbols, options);
            return symbols;
        }

        /// <summary>
        /// The C64, the C64C and the C128 share one I/O block: VIC-II at $D000,
        /// SID at $D400, CIA 1 at $DC00, CIA 2 at $DD00, colour RAM at $D800.
        /// </summary>
        private static void Commodore64Family(Dictionary<string, long> symbols,
            string model, string video)
        {
            symbols["VIC"] = 0xD000;
            symbols["COLORRAM"] = 0xD800;
            symbols["SID"] = 0xD400;
            symbols["CIA1"] = 0xDC00;
            symbols["CIA2"] = 0xDD00;
            symbols["JOY1"] = 0xDC00;
            symbols["JOY2"] = 0xDC01;
            symbols["SCREEN"] = 0x0400;

            if (model == HardwareOptions.ModelC64C)
            {
                // The flat C64C has a 8562/8565 VIC-II at the same address and
                // does have colour RAM. What changes is the SID revision and
                // nothing that a program addresses, so nothing moves.
                symbols["MODEL"] = 0x0001;
            }

            if (model == HardwareOptions.ModelC128 || model == HardwareOptions.ModelC128D)
            {
                // The C128 adds the MMU, which is what decides whether the I/O
                // block is mapped in at all, and the VDC at $D600. CIA 2 keeps
                // $DD00 in both modes: nothing is multiplexed here.
                symbols["MMU"] = 0xD500;
                symbols["VDC"] = 0xD600;
                symbols["VDCDATA"] = 0xD601;
                symbols["MODEL"] = 0x0002;
            }

            if (video == "vdc")
            {
                symbols["VDC"] = 0xD600;
                symbols["VDCDATA"] = 0xD601;
            }
        }

        private static void Vic20(Dictionary<string, long> symbols)
        {
            symbols["VIC"] = 0x9000;
            symbols["VIA1"] = 0x9110;
            symbols["VIA2"] = 0x9120;
            symbols["COLORRAM"] = 0x9400;
            symbols["SCREEN"] = 0x1000;
            symbols["JOY1"] = 0x9110;
            symbols["JOY2"] = 0x9120;
        }

        private static void Plus4(Dictionary<string, long> symbols)
        {
            // No VIC-II. The TED holds the video registers, the colour memory is
            // inside the chip, and the whole I/O area sits at the top of memory
            // rather than at $D000. A C64 program built for this machine reads
            // RAM where it expects a register, and nothing warns it.
            symbols["TED"] = 0xFF00;
            symbols["TEDROM"] = 0xFF3E;
            symbols["TEDRAM"] = 0xFF3F;
            symbols["ACIA"] = 0xFD00;
            symbols["USERPORT"] = 0xFD10;
            symbols["SCREEN"] = 0x0C00;
            symbols["ATTRIBUTES"] = 0x0800;
        }

        /// <summary>
        /// The PET 2001 and 2001-N. No CRTC, no 6847, no ACIA: the video timing
        /// is discrete logic and the serial interface is two PIAs and a VIA. The
        /// 2001-N differs from the 2001 by RAM, a monitor and more ROM sockets,
        /// none of which is an address.
        /// </summary>
        private static void Pet(Dictionary<string, long> symbols)
        {
            symbols["PIA1"] = 0xE810;
            symbols["PIA2"] = 0xE820;
            symbols["VIA"] = 0xE840;
            symbols["SCREEN"] = 0x8000;
        }

        /// <summary>
        /// The CBM-II. What the 40/80 column choice changes is which chip answers
        /// at $D800: a CRTC on the B series, a VIC-II on the P series. That is
        /// the one case in this catalogue where a display option moves a
        /// register, and it is why the option exists at all.
        /// </summary>
        private static void Cbm2(Dictionary<string, long> symbols, string video)
        {
            symbols["CRTC"] = 0xD800;
            symbols["SID"] = 0xDA00;
            symbols["CIA"] = 0xDC00;
            symbols["ACIA"] = 0xDD00;
            symbols["TPIKEY"] = 0xDF00;
            symbols["TPIEEE"] = 0xDE00;
            symbols["VIDEO"] = 0xD000;
            symbols["CHARROM"] = 0xC000;
            symbols["EXECBANK"] = 0x0000;
            symbols["INDBANK"] = 0x0001;

            if (video == "vic")
            {
                symbols.Remove("CRTC");
                symbols["VIC"] = 0xD800;
                symbols["COLORRAM"] = 0xD400;
            }
        }

        private static void Nes(Dictionary<string, long> symbols)
        {
            symbols["PPUCTRL"] = 0x2000;
            symbols["PPUMASK"] = 0x2001;
            symbols["PPUSTATUS"] = 0x2002;
            symbols["OAMADDR"] = 0x2003;
            symbols["OAMDATA"] = 0x2004;
            symbols["PPUSCROLL"] = 0x2005;
            symbols["PPUADDR"] = 0x2006;
            symbols["PPUDATA"] = 0x2007;
            symbols["OAMDMA"] = 0x4014;
            symbols["SQ1_VOL"] = 0x4000;
            symbols["DMC_FREQ"] = 0x4010;
            symbols["SND_CHN"] = 0x4015;
            symbols["JOY1"] = 0x4016;
            symbols["JOY2"] = 0x4017;
        }

        private static void ChipOnly(Dictionary<string, long> symbols, string video)
        {
            if (video == "vic")
            {
                symbols["VIC"] = 0xD000;
                symbols["CIA1"] = 0xDC00;
                symbols["CIA2"] = 0xDD00;
            }
            else if (video == "vdc")
            {
                symbols["VDC"] = 0xD600;
                symbols["VDCDATA"] = 0xD601;
            }
            else if (video == "ted")
            {
                symbols["TED"] = 0xFF00;
                symbols["ACIA"] = 0xFD00;
            }
            else if (video == "crtc")
            {
                symbols["CRTC"] = 0xD800;
            }
        }

        /// <summary>
        /// PAL and NTSC change which chip revision is soldered and how it times.
        /// On every Commodore machine in this catalogue no register address moves
        /// with it. The one place a standard is worth recording is the NES, where
        /// the frame is 262 lines on NTSC and 312 on PAL, and the window in which
        /// a sprite DMA may be written differs. Those are the two numbers a
        /// program needs, and they are what this adds.
        /// </summary>
        private static void AddVideoStandard(Dictionary<string, long> symbols, HardwareOptions options)
        {
            string standard = HardwareRules.Normalize(options.VideoStandard);
            if (standard == null)
                return;

            if (symbols.ContainsKey("PPUCTRL"))
            {
                long lines = standard == HardwareOptions.StandardPal ? 312 : 262;
                long visible = standard == HardwareOptions.StandardPal ? 240 : 224;
                symbols["FRAME_LINES"] = lines;
                symbols["VISIBLE_LINES"] = visible;
                symbols["IS_PAL"] = standard == HardwareOptions.StandardPal ? 1 : 0;
            }

            // The Plus/4's TED keeps PAL and NTSC in a bit of one register rather
            // than in a different chip layout, so the option is worth carrying
            // as a value a program can test instead of a separate table.
            if (symbols.ContainsKey("TED") && standard != null)
            {
                symbols["IS_PAL"] = standard == HardwareOptions.StandardPal ? 0 : 1;
            }
        }
    }
}
