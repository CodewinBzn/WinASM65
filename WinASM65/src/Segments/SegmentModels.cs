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
    }

    public class ConfigFile
    {
        public TargetConf Target { get; set; }
        public List<Segment> Input { get; set; }
        public CombineConf Output { get; set; }
    }
}
