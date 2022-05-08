// Abdelghani BOUZIANE    
// 2021

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace WinASM65
{
    class MultiSegment
    {
        public static List<Segment> SegmentList { get; set; }
        public static void Assemble()
        {
            JsonSerializer serializer = new JsonSerializer();
            if (SegmentList.Count > 0)
            {
                // assemble segments
                foreach (Segment seg in SegmentList)
                {
                    Assembler.SourceFile = seg.FileName;
                    Assembler.ObjectFileName = !string.IsNullOrWhiteSpace(seg.OutputFile) ? seg.OutputFile : seg.FileName.Split('.')[0] + ".o";
                    Assembler.Assemble();
                }
                // resolve dependencies
                foreach (Segment seg in SegmentList)
                {
                    if (Listing.EnableListing)
                    {
                        Listing.ListingFile = seg.FileName;
                    }
                    string objectFile = !string.IsNullOrWhiteSpace(seg.OutputFile) ? seg.OutputFile : seg.FileName.Split('.')[0] + ".o";
                    Assembler.FileOutMemory = new List<byte>(File.ReadAllBytes(objectFile));

                    Assembler.InitLexicalScope();
                    LexicalScopeData globalScope = Assembler.LexicalScope.LexicalScopeDataList[0];
                    string unsolvedFile = $"{seg.FileName.Split('.')[0]}.Unsolved";
                    if (File.Exists(unsolvedFile))
                    {
                        using (StreamReader file = File.OpenText(unsolvedFile))
                        {
                            globalScope.UnsolvedSymbols = (Dictionary<string, UnresolvedSymbol>)serializer.Deserialize(file, typeof(Dictionary<string, UnresolvedSymbol>));
                        }
                    }
                    else
                    {
                        Console.WriteLine($"{seg.FileName} - No Symbol to resolve");
                        Listing.GenerateListing();
                        continue;
                    }
                    string unsolvedExpr = $"{seg.FileName.Split('.')[0]}.UnsolvedExpr";
                    if (File.Exists(unsolvedExpr))
                    {
                        using (StreamReader file = File.OpenText(unsolvedExpr))
                        {
                            Assembler.UnsolvedExprList = (Dictionary<ushort, UnresolvedExpr>)serializer.Deserialize(file, typeof(Dictionary<ushort, UnresolvedExpr>));
                        }
                    }
                    else
                    {
                        Console.WriteLine($"{seg.FileName} - No Expression to resolve");
                        Listing.GenerateListing();
                        continue;
                    }
                    Assembler.FilePtr = new FileInfo() { SourceFile = unsolvedFile, CurrentLineNumber = -1 };
                    foreach (string dependence in seg.Dependencies)
                    {
                        string symbolTableFile = $"{dependence.Split('.')[0]}.symb";
                        using (StreamReader file = File.OpenText(symbolTableFile))
                        {
                            globalScope.SymbolTable = (Dictionary<string, dynamic>)serializer.Deserialize(file, typeof(Dictionary<string, dynamic>));
                        }
                        Assembler.ResolveSymbols();
                        if (Assembler.UnsolvedExprList.Count == 0)
                        {
                            File.Delete(unsolvedExpr);
                        }
                        if (globalScope.UnsolvedSymbols.Count == 0)
                        {
                            File.Delete(unsolvedFile);
                            break;
                        }
                    }
                    using (BinaryWriter writer = new BinaryWriter(File.Open(objectFile, FileMode.Create)))
                    {
                        writer.Write(Assembler.FileOutMemory.ToArray());
                    }
                    Listing.GenerateListing();
                }
            }
        }
    }
    class Segment
    {
        public string FileName { get; set; }
        public string OutputFile { get; set; }
        public string[] Dependencies { get; set; }
    }
}