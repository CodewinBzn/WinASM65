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
using System.IO;

namespace WinASM65
{
    class MultiSegment
    {
        public static string configFile { get; set; }
        private static List<Segment> segments = new List<Segment>();
        public static void Assemble()
        {
            JsonSerializer serializer;
            using (StreamReader file = File.OpenText(configFile))
            {
                serializer = new JsonSerializer();
                segments = (List<Segment>)serializer.Deserialize(file, typeof(List<Segment>));
            }
            if (segments.Count > 0)
            {
                // assemble segments
                foreach (Segment seg in segments)
                {
                    Assembler.sourceFile = seg.FileName;
                    Assembler.objectFileName = seg.FileName.Split('.')[0] + ".o";
                    Assembler.Assemble();
                }
                // resolve dependencies
                foreach (Segment seg in segments)
                {
                    string unsolvedFile = seg.FileName.Split('.')[0] + ".o_Unsolved.txt";
                    if (File.Exists(unsolvedFile))
                    {
                        using (StreamReader file = File.OpenText(unsolvedFile))
                        {
                            Assembler.unsolvedSymbols = (Dictionary<string, List<TokenInfo>>)serializer.Deserialize(file, typeof(Dictionary<string, List<TokenInfo>>));
                        }
                    }
                    else
                    {
                        Console.WriteLine($"{seg.FileName} - No Symbol to resolve");
                        continue;
                    }
                    Assembler.file = new FileInfo() { sourceFile = unsolvedFile, currentLineNumber = -1};
                    string objectFile = seg.FileName.Split('.')[0] + ".o";
                    Assembler.fileOutMemory = new List<byte>(File.ReadAllBytes(objectFile));
                    foreach (string dependence in seg.Dependencies)
                    {
                        string symbolTableFile = dependence.Split('.')[0] + ".o_Symbol.txt";
                        using (StreamReader file = File.OpenText(symbolTableFile))
                        {
                            Assembler.symbolTable = (Dictionary<string, Symbol>)serializer.Deserialize(file, typeof(Dictionary<string, Symbol>));
                        }
                        Assembler.ResolveSymbols();
                        if (Assembler.unsolvedSymbols.Count == 0)
                        {
                            File.Delete(unsolvedFile);
                            break;
                        }
                    }
                    using (BinaryWriter writer = new BinaryWriter(File.Open(objectFile, FileMode.Create)))
                    {
                        writer.Write(Assembler.fileOutMemory.ToArray());
                    }
                }
            }


        }
    }
    class Segment
    {
        public string FileName { get; set; }
        public string[] Dependencies { get; set; }
    }
}