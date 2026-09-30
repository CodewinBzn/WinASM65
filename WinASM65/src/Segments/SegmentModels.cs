// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Multi-segment and Combine models

using System.Collections.Generic;

namespace WinASM65.Segments
{
    public class Segment
    {
        public string FileName { get; set; }
        public string OutputFile { get; set; }
        public string[] Dependencies { get; set; }

        public Segment()
        {
            Dependencies = new string[0];
        }
    }

    public class FileConf
    {
        public string FileName { get; set; }
        public string Size { get; set; }
    }

    public class CombineConf
    {
        public string ObjectFile { get; set; }
        public FileConf[] Files { get; set; }

        public CombineConf()
        {
            Files = new FileConf[0];
        }
    }

    public class InesConf
    {
        public int? PrgBanks { get; set; }
        public int? ChrBanks { get; set; }
        public int? Mapper { get; set; }
        public string Mirroring { get; set; }
        public bool Battery { get; set; }
    }

    /// <summary>
    /// A named region of the target memory, as declared in the configuration.
    ///
    /// <para>
    /// A region is <b>declarative</b>: it says what the target is expected to look
    /// like, and nothing else. It never places a byte, never replaces a
    /// <c>.org</c> and never changes the direct-burn output. Its only runtime
    /// effect is cross-validation: a <c>.org</c> that lands outside every declared
    /// region is a diagnostic instead of silence.
    /// </para>
    ///
    /// <para>
    /// Addresses and sizes are <b>strings</b> like every other address in the
    /// configuration, so <c>"$8000"</c> and <c>"32768"</c> are both accepted.
    /// <see cref="Address"/> is the base of the region and the address code runs
    /// at, so it is what a <c>.org</c> is checked against; <see cref="Load"/> is
    /// where the bytes are stored when that differs, which is the load/run split
    /// a relocating target needs.
    /// </para>
    /// </summary>
    public class RegionConf
    {
        /// <summary>Region name, used in diagnostics and to derive region symbols.</summary>
        public string Name { get; set; }

        /// <summary>Base address of the region, the address code runs at. Required.</summary>
        public string Address { get; set; }

        /// <summary>Size in bytes. When absent, the region runs to the top of the address space.</summary>
        public string Size { get; set; }

        /// <summary>Bank number, for targets whose memory is banked (NES PRG banks 0-15...).</summary>
        public int? Bank { get; set; }

        /// <summary>Region type: <c>ro</c> (default), <c>rw</c> or <c>bss</c>.</summary>
        public string Type { get; set; }

        /// <summary>Address the bytes are stored at, when it differs from where they run.</summary>
        public string Load { get; set; }
    }

    public class TargetConf
    {
        public string System { get; set; }
        public string Cpu { get; set; }
        public string Format { get; set; }
        public string LoadAddress { get; set; }
        public string RunAddress { get; set; }
        public string RomSize { get; set; }
        public bool? DefineHardwareSymbols { get; set; }
        public InesConf Ines { get; set; }

        /// <summary>
        /// Kind of BBC file to produce: <c>code</c> (with a code header),
        /// <c>text</c>, or <c>flat</c>. Null leaves the target's own choice,
        /// which is <c>code</c> for the BBC.
        /// </summary>
        public string BbcFileType { get; set; }

        /// <summary>Title carried by the BBC code header.</summary>
        public string Title { get; set; }

        /// <summary>Author carried by the BBC code header.</summary>
        public string Author { get; set; }

        /// <summary>
        /// Named memory regions. Empty when the section is absent, which is the case
        /// for every configuration written before regions existed: a configuration
        /// without regions behaves exactly as it did, because no declared region
        /// means nothing to cross-validate against.
        /// </summary>
        public RegionConf[] Regions { get; set; }

        public TargetConf()
        {
            Regions = new RegionConf[0];
        }
    }

    public class ConfigFile
    {
        public TargetConf Target { get; set; }
        public List<Segment> Input { get; set; }
        public CombineConf Output { get; set; }
    }
}
