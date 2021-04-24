/**********************************************************************************/
/*                                                                                */
/*                                                                                */
/* 2021 Abdelghani BOUZIANE                                                       */
/*                                                                                */
/*                                                                                */
/**********************************************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
namespace WinASM65
{
    class Combine
    {
        public static string configFile { get; set; }
        public static List<byte> outMemory;
        public static void Process()
        {
            outMemory = new List<byte>();
            JsonSerializer serializer;
            CombineConf config;
            using (StreamReader file = File.OpenText(configFile))
            {
                serializer = new JsonSerializer();
                config = (CombineConf)serializer.Deserialize(file, typeof(CombineConf));
            }

            foreach (FileConf fileConf in config.Files)
            {
                List<byte>  bytesOut = new List<byte>(File.ReadAllBytes(fileConf.FileName));
                if(fileConf.Size != null)
                {
                    ushort size = ushort.Parse(fileConf.Size.Trim().Replace("$", string.Empty), NumberStyles.HexNumber);
                    if (bytesOut.Count < size){
                        int delta = size - bytesOut.Count;
                        for(short i = 0; i< delta; i++)
                        {
                            bytesOut.Add(0);
                        }
                    }
                }
                outMemory.AddRange(bytesOut);
            }
            using (BinaryWriter writer = new BinaryWriter(File.Open(config.ObjectFile, FileMode.Create)))
            {
                writer.Write(outMemory.ToArray());
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
