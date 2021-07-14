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
                    Assembler.InitLexicalScope();
                    LexicalScopeData globalScope = Assembler.lexicalScope.lexicalScopeDataList[0];
                    string unsolvedFile = seg.FileName.Split('.')[0] + ".o_Unsolved.txt";
                    if (File.Exists(unsolvedFile))
                    {
                        using (StreamReader file = File.OpenText(unsolvedFile))
                        {
                            globalScope.unsolvedSymbols = (Dictionary<string, UnresolvedSymbol>)serializer.Deserialize(file, typeof(Dictionary<string, UnresolvedSymbol>));
                        }
                    }
                    else
                    {
                        Console.WriteLine($"{seg.FileName} - No Symbol to resolve");
                        continue;
                    }
                    string unsolvedExpr = seg.FileName.Split('.')[0] + ".o_UnsolvedExpr.txt";
                    if (File.Exists(unsolvedExpr))
                    {
                        using (StreamReader file = File.OpenText(unsolvedExpr))
                        {
                            Assembler.unsolvedExprList = (Dictionary<ushort, UnresolvedExpr>)serializer.Deserialize(file, typeof(Dictionary<ushort, UnresolvedExpr>));
                        }
                    }
                    else
                    {
                        Console.WriteLine($"{seg.FileName} - No Expression to resolve");
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
                            globalScope.symbolTable = (Dictionary<string, Symbol>)serializer.Deserialize(file, typeof(Dictionary<string, Symbol>));
                        }
                        Assembler.ResolveSymbols();                        
                        if(Assembler.unsolvedExprList.Count == 0)
                        {
                            File.Delete(unsolvedExpr);
                        }
                        if (globalScope.unsolvedSymbols.Count == 0)
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