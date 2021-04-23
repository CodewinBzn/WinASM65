/**********************************************************************************/
/*                                                                                */
/*                                                                                */
/* Copyright (c) 2021 Abdelghani BOUZIANE                                         */
/*                                                                                */
/* Permission is hereby granted, free of charge, to any person obtaining a copy   */
/* of this software and associated documentation files (the "Software"), to deal  */
/* in the Software without restriction, including without limitation the rights   */
/* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell      */
/* copies of the Software, and to permit persons to whom the Software is          */
/* furnished to do so, subject to the following conditions:                       */
/*                                                                                */
/* The above copyright notice and this permission notice shall be included in all */
/* copies or substantial portions of the Software.                                */
/*                                                                                */
/* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR     */
/* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,       */
/* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE    */
/* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER         */
/* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,  */
/* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE  */
/* SOFTWARE.                                                                      */
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
