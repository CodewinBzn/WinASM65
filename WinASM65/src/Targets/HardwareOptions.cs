using System;
using System.Collections.Generic;
using System.Text;

namespace WinASM65.Targets
{
    /// <summary>
    /// What a target's machine is made of, as options rather than as separate
    /// systems.
    /// <para>
    /// A PAL VIC-II and an NTSC one are the same chip in a different market: same
    /// registers, different timing. A Plus/4 and a C64 are not: the Plus/4 has no
    /// VIC-II at all, so every register a C64 program knows at $D000 is plain RAM
    /// there. Collapsing both cases into a list of systems would either multiply
    /// near-identical entries or pretend the difference does not exist.
    /// </para>
    /// <para>
    /// So the options are separate, and they are not all the same kind. Some
    /// change a register address, some change only a chip revision that a build
    /// still has to record, and some change a bit inside a register.
    /// </para>
    /// </summary>
    public class HardwareOptions
    {
        /// <summary>Which machine, when that is what actually differs.</summary>
        public const string ModelPet2001 = "pet2001";
        public const string ModelPet2001N = "pet2001n";
        public const string ModelCbm2 = "cbm2";
        public const string ModelC64 = "c64";
        public const string ModelC64C = "c64c";
        public const string ModelC128 = "c128";
        public const string ModelC128D = "c128d";
        public const string ModelVvic20 = "vic20";
        public const string ModelPlus4 = "plus4";
        public const string ModelC16 = "c16";
        public const string ModelNes = "nes";

        public const string StandardPal = "pal";
        public const string StandardNtsc = "ntsc";

        /// <summary>The original SID, in every C64 and C128.</summary>
        public const string Sid6581 = "6581";

        /// <summary>The R4F / 8580, fitted to the later flat C64 and the C128.</summary>
        public const string Sid8580 = "8580";

        public string Model { get; set; }

        /// <summary>"pal" or "ntsc". Null means the machine's usual market.</summary>
        public string VideoStandard { get; set; }

        /// <summary>"vic", "vdc", "ted", "crtc", or null.</summary>
        public string VideoChip { get; set; }

        /// <summary>"6581", "8580", "ted", "vic", or null.</summary>
        public string SoundChip { get; set; }

        public HardwareOptions Clone()
        {
            return new HardwareOptions
            {
                Model = Model,
                VideoStandard = VideoStandard,
                VideoChip = VideoChip,
                SoundChip = SoundChip
            };
        }

        public bool IsEmpty
        {
            get
            {
                return string.IsNullOrEmpty(Model)
                    && string.IsNullOrEmpty(VideoStandard)
                    && string.IsNullOrEmpty(VideoChip)
                    && string.IsNullOrEmpty(SoundChip);
            }
        }

        /// <summary>A one line summary, for the catalogue listing and the build record.</summary>
        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            if (!string.IsNullOrEmpty(Model))
                text.Append(Model);
            if (!string.IsNullOrEmpty(VideoChip))
                Append(text, VideoChip);
            if (!string.IsNullOrEmpty(SoundChip))
                Append(text, SoundChip);
            if (!string.IsNullOrEmpty(VideoStandard))
                Append(text, VideoStandard);
            return text.ToString();
        }

        private static void Append(StringBuilder text, string part)
        {
            if (text.Length > 0)
                text.Append('/');
            text.Append(part);
        }
    }

    /// <summary>
    /// Why a set of options is refused, and what each machine can actually
    /// carry.
    /// </summary>
    public static class HardwareRules
    {
        /// <summary>
        /// Checks the combination and returns the reason it cannot be built, or
        /// null when it can.
        /// <summary>
        public static string Reject(HardwareOptions options)
        {
            if (options == null)
                return null;

            string model = Normalize(options.Model);
            string video = Normalize(options.VideoChip);
            string sound = Normalize(options.SoundChip);
            string standard = Normalize(options.VideoStandard);

            if (standard != null && standard != HardwareOptions.StandardPal
                && standard != HardwareOptions.StandardNtsc)
            {
                return "unknown video standard '" + options.VideoStandard + "'. Use pal or ntsc.";
            }
            if (sound != null && sound != HardwareOptions.Sid6581 && sound != HardwareOptions.Sid8580
                && sound != "ted" && sound != "vic")
            {
                return "unknown sound chip '" + options.SoundChip + "'.";
            }

            // The 2001 and 2001-N have no video controller chip at all: the
            // timing is discrete logic. Asking for one is not a warning, it is a
            // register address that answers with something else.
            if (video != null && (model == HardwareOptions.ModelPet2001
                || model == HardwareOptions.ModelPet2001N))
            {
                return "the " + model + " has no video controller chip. Its video timing is "
                    + "discrete logic; a CRTC or VDC address would name nothing.";
            }

            // The 8580 is a sound chip revision. It does not move a register,
            // and neither SID variant is fitted to a Plus/4.
            if (sound != null && sound != "ted" && sound != "vic"
                && (model == HardwareOptions.ModelPlus4 || model == HardwareOptions.ModelC16))
            {
                return "the " + model + " has no SID. It carries a TED, and its registers are at "
                    + "$FF00.";
            }

            if (video == "vdc" && model != null
                && model != HardwareOptions.ModelC128 && model != HardwareOptions.ModelC128D
                && model != HardwareOptions.ModelCbm2)
            {
                return "the 8563 VDC exists on the C128 and the CBM-II, not on the " + model + ".";
            }

            if (video == "ted" && model != null
                && model != HardwareOptions.ModelPlus4 && model != HardwareOptions.ModelC16)
            {
                return "the TED exists on the Plus/4 and the C16, not on the " + model + ".";
            }

            if (video == "crtc" && model != null
                && model != HardwareOptions.ModelCbm2)
            {
                return "the CRTC exists on the CBM-II, not on the " + model + ".";
            }

            return null;
        }

        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            return value.Trim().ToLowerInvariant();
        }
    }
}
