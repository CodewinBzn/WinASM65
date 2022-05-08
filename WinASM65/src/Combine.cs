// Abdelghani BOUZIANE    
// 2021

using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace WinASM65
{
    class Combine
    {
        private static CombineConf _config;
        public static CombineConf ConfigFile
        {
            get
            {
                return _config;
            }
            set
            {
                _config = value;
            }
        }
        public static List<byte> OutMemory { get; set; }
        public static void Process()
        {
            OutMemory = new List<byte>();
            using (BinaryWriter writer = new BinaryWriter(File.Open(_config.ObjectFile, FileMode.Create)))
            {
                foreach (FileConf fileConf in _config.Files)
                {
                    List<byte> bytesOut = new List<byte>(File.ReadAllBytes(fileConf.FileName));
                    writer.Write(bytesOut.ToArray());
                    if (fileConf.Size != null)
                    {
                        int size = int.Parse(fileConf.Size.Trim().Replace("$", string.Empty), NumberStyles.HexNumber);
                        if (bytesOut.Count < size)
                        {
                            int delta = size - bytesOut.Count;
                            for (int i = 0; i < delta; i++)
                            {
                                writer.Write((byte)0);
                            }
                        }
                    }
                }
            }
        }
    }

    class FileConf
    {
        public string FileName { get; set; }
        public string Size { get; set; }
    }

    class CombineConf
    {
        public string ObjectFile { get; set; }
        public FileConf[] Files { get; set; }
    }
}
