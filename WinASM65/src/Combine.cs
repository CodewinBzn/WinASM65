// Abdelghani BOUZIANE    
// 2021

using Newtonsoft.Json;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace WinASM65
{
    class Combine
    {
        public static string ConfigFile { get; set; }
        public static List<byte> OutMemory { get; set; }
        public static void Process()
        {
            OutMemory = new List<byte>();
            JsonSerializer serializer;
            CombineConf config;
            using (StreamReader file = File.OpenText(ConfigFile))
            {
                serializer = new JsonSerializer();
                config = (CombineConf)serializer.Deserialize(file, typeof(CombineConf));
            }
            using (BinaryWriter writer = new BinaryWriter(File.Open(config.ObjectFile, FileMode.Append)))
            {                
                foreach (FileConf fileConf in config.Files)
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
