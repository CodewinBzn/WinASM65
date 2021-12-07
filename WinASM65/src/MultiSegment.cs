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
using System.Threading.Tasks;

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
                Task[] tasmArray = new Task[segments.Count];
                Task[] tResArray = new Task[segments.Count];
                // assemble segments
                for (int i = 0; i < segments.Count; i++)
                {
                    Segment segm = segments[i];
                    Assembler assembler = new Assembler(segm.FileName, segm.FileName.Split('.')[0] + ".o");
                    tasmArray[i] = new Task((object ele) =>
                    {
                        Assembler asm = (Assembler)ele;
                        asm.Assemble();
                    }, assembler);

                    tResArray[i] = new Task((object ele) =>
                    {
                        ResolverState resState = (ResolverState)ele;
                        Assembler asm = resState.Asm;
                        Segment seg = resState.Seg;
                        asm.InitLexicalScope();
                        LexicalScopeData globalScope = asm.lexicalScope.lexicalScopeDataList[0];
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
                            return;
                        }
                        string unsolvedExpr = seg.FileName.Split('.')[0] + ".o_UnsolvedExpr.txt";
                        if (File.Exists(unsolvedExpr))
                        {
                            using (StreamReader file = File.OpenText(unsolvedExpr))
                            {
                                asm.unsolvedExprList = (Dictionary<ushort, UnresolvedExpr>)serializer.Deserialize(file, typeof(Dictionary<ushort, UnresolvedExpr>));
                            }
                        }
                        else
                        {
                            Console.WriteLine($"{seg.FileName} - No Expression to resolve");
                            return;
                        }
                        asm.file = new FileInfo() { sourceFile = unsolvedFile, currentLineNumber = -1 };
                        string objectFile = seg.FileName.Split('.')[0] + ".o";
                        asm.fileOutMemory = new List<byte>(File.ReadAllBytes(objectFile));
                        foreach (string dependence in seg.Dependencies)
                        {
                            string symbolTableFile = dependence.Split('.')[0] + ".o_Symbol.txt";
                            using (StreamReader file = File.OpenText(symbolTableFile))
                            {
                                globalScope.symbolTable = (Dictionary<string, Symbol>)serializer.Deserialize(file, typeof(Dictionary<string, Symbol>));
                            }
                            asm.ResolveSymbols();
                            if (asm.unsolvedExprList.Count == 0)
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
                            writer.Write(asm.fileOutMemory.ToArray());
                        }
                    }, new ResolverState { Asm = assembler, Seg = segm });
                }
                foreach (Task t in tasmArray) t.Start();
                Task.WaitAll(tasmArray);
                foreach (Task t in tResArray) t.Start();
                Task.WaitAll(tResArray);
            }
        }
    }
    class Segment
    {
        public string FileName { get; set; }
        public string[] Dependencies { get; set; }
    }

    class ResolverState
    {
        public Segment Seg { get; set; }
        public Assembler Asm { get; set; }
    }
}