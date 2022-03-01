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
        public static string ConfigFile { get; set; }
        private static List<Segment> _segments = new List<Segment>();
        public static void Assemble()
        {
            JsonSerializer serializer;
            using (StreamReader file = File.OpenText(ConfigFile))
            {
                serializer = new JsonSerializer();
                _segments = (List<Segment>)serializer.Deserialize(file, typeof(List<Segment>));
            }
            if (_segments.Count > 0)
            {
                // assemble segments
                foreach (Segment seg in _segments)
                {
                    Assembler.SourceFile = seg.FileName;
                    Assembler.ObjectFileName = seg.FileName.Split('.')[0] + ".o";
                    Assembler.Assemble();
                }
                // resolve dependencies
                foreach (Segment seg in _segments)
                {
                    if (Listing.EnableListing)
                    {
                        Listing.ListingFile = seg.FileName;
                    }
                    string objectFile = seg.FileName.Split('.')[0] + ".o";
                    Assembler.FileOutMemory = new List<byte>(File.ReadAllBytes(objectFile));

                    Assembler.InitLexicalScope();
                    LexicalScopeData globalScope = Assembler.LexicalScope.LexicalScopeDataList[0];
                    string unsolvedFile = seg.FileName.Split('.')[0] + ".o_Unsolved.txt";
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
                    string unsolvedExpr = seg.FileName.Split('.')[0] + ".o_UnsolvedExpr.txt";
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
                        string symbolTableFile = dependence.Split('.')[0] + ".symb";
                        using (StreamReader file = File.OpenText(symbolTableFile))
                        {
                            globalScope.SymbolTable = (Dictionary<string, Symbol>)serializer.Deserialize(file, typeof(Dictionary<string, Symbol>));
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
        public string[] Dependencies { get; set; }
    }
}