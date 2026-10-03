using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using WinASM65.Core;
using WinASM65.Symbols;

namespace WinASM65.Segments
{
    public class MultiSegmentResult : OperationResult
    {
        public MultiSegmentResult(bool success, IReadOnlyList<Diagnostic> diagnostics)
            : base(success, diagnostics) { }
    }

    public interface IMultiSegmentOrchestrator
    {
        MultiSegmentResult AssembleSegments(IReadOnlyList<Segment> segmentList);
    }

    public class MultiSegmentOrchestrator : IMultiSegmentOrchestrator
    {
        private readonly Func<IAssembler> _assemblerFactory;

        /// <summary>
        /// Where the side files of the build are, or null for beside their source.
        /// It has to be the directory the assembler was told to write them in,
        /// because these files are the only channel between one unit of a build and
        /// the next: <see cref="WinASM65.Core.AssemblerOptions.SideFileDirectory"/>.
        /// </summary>
        private readonly string _sideFileDirectory;

        public MultiSegmentOrchestrator(Func<IAssembler> assemblerFactory)
            : this(assemblerFactory, null)
        {
        }

        public MultiSegmentOrchestrator(Func<IAssembler> assemblerFactory, string sideFileDirectory)
        {
            if (assemblerFactory == null) throw new ArgumentNullException("assemblerFactory");
            _assemblerFactory = assemblerFactory;
            _sideFileDirectory = string.IsNullOrWhiteSpace(sideFileDirectory) ? null : sideFileDirectory;
        }

        public MultiSegmentResult AssembleSegments(IReadOnlyList<Segment> segmentList)
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            if (segmentList == null || segmentList.Count == 0)
                return new MultiSegmentResult(true, diagnostics);

            foreach (Segment segment in segmentList)
            {
                if (segment == null || string.IsNullOrWhiteSpace(segment.FileName))
                {
                    AddError(diagnostics, string.Empty, "Segment source file is required.");
                    continue;
                }
                AssemblyResult result = _assemblerFactory().Assemble(segment.FileName, GetObjectFile(segment));
                AddDiagnostics(diagnostics, result.Diagnostics);
            }
            if (diagnostics.Count > 0)
                return new MultiSegmentResult(false, diagnostics);

            JsonSerializer serializer = new JsonSerializer();
            foreach (Segment segment in segmentList)
            {
                if (!ResolveSegment(segment, serializer, diagnostics))
                    return new MultiSegmentResult(false, diagnostics);
            }
            return new MultiSegmentResult(true, diagnostics);
        }

        private bool ResolveSegment(Segment segment, JsonSerializer serializer, List<Diagnostic> diagnostics)
        {
            string baseName = SideFiles.BaseNameOf(segment.FileName);
            string objectFile = GetObjectFile(segment);
            string unsolvedFile = SideFile(baseName + ".Unsolved");
            string unsolvedExprFile = SideFile(baseName + ".UnsolvedExpr");
            if (!File.Exists(unsolvedFile)) return true;
            if (!File.Exists(unsolvedExprFile))
            {
                AddError(diagnostics, segment.FileName, "Unresolved symbol data has no expression data.");
                return false;
            }
            if (!File.Exists(objectFile))
            {
                AddError(diagnostics, segment.FileName, "Segment object file doesn't exist.");
                return false;
            }

            IAssembler assembler = _assemblerFactory();
            assembler.Emitter.EmitBytes(File.ReadAllBytes(objectFile));
            LexicalScopeData scope = assembler.ScopeManager.GlobalScope;
            try
            {
                using (StreamReader reader = File.OpenText(unsolvedFile))
                {
                    Dictionary<string, UnresolvedSymbol> symbols = (Dictionary<string, UnresolvedSymbol>)serializer.Deserialize(reader, typeof(Dictionary<string, UnresolvedSymbol>));
                    if (symbols == null) throw new InvalidDataException("Unresolved symbol data is invalid.");
                    foreach (KeyValuePair<string, UnresolvedSymbol> item in symbols) scope.UnsolvedSymbols[item.Key] = item.Value;
                }
                using (StreamReader reader = File.OpenText(unsolvedExprFile))
                {
                    Dictionary<ushort, UnresolvedExpr> expressions = (Dictionary<ushort, UnresolvedExpr>)serializer.Deserialize(reader, typeof(Dictionary<ushort, UnresolvedExpr>));
                    if (expressions == null) throw new InvalidDataException("Unresolved expression data is invalid.");
                    foreach (KeyValuePair<ushort, UnresolvedExpr> item in expressions) assembler.ScopeManager.AddUnresolvedExpression(item.Key, item.Value);
                }
            }
            catch (Exception ex)
            {
                AddError(diagnostics, segment.FileName, ex.Message);
                return false;
            }

            if (segment.Dependencies != null)
            {
                foreach (string dependency in segment.Dependencies)
                {
                    string symbolFile = SideFile(SideFiles.BaseNameOf(dependency) + ".symb");
                    if (!File.Exists(symbolFile))
                    {
                        AddError(diagnostics, segment.FileName, "Dependency symbol file doesn't exist: " + symbolFile);
                        return false;
                    }
                    using (StreamReader reader = File.OpenText(symbolFile))
                    {
                        Dictionary<string, long> symbols = (Dictionary<string, long>)serializer.Deserialize(reader, typeof(Dictionary<string, long>));
                        if (symbols != null)
                            foreach (KeyValuePair<string, long> item in symbols) scope.SymbolTable[item.Key] = item.Value;
                    }
                    assembler.ResolvePendingSymbols();
                }
            }

            if (scope.UnsolvedSymbols.Count > 0 || assembler.ScopeManager.UnsolvedExprList.Count > 0)
            {
                AddError(diagnostics, segment.FileName, ErrorCodes.UNDEFINED_SYMBOL);
                return false;
            }

            assembler.Emitter.SaveToFile(objectFile);
            File.Delete(unsolvedFile);
            File.Delete(unsolvedExprFile);
            return true;
        }

        private static string GetObjectFile(Segment segment)
        {
            // Beside the source by default, and named after it -- see SideFiles for
            // why the whole path with the extension off is the base, and why a name
            // without its directory would scatter the build across the disk.
            return !string.IsNullOrWhiteSpace(segment.OutputFile)
                ? segment.OutputFile
                : SideFiles.BaseNameOf(segment.FileName) + ".o";
        }

        /// <summary>
        /// Puts a side file where <see cref="AssemblerEngine"/> wrote it. The rule is
        /// <see cref="SideFiles"/>'s, so the two cannot drift apart.
        /// </summary>
        private string SideFile(string name)
        {
            return SideFiles.Locate(name, _sideFileDirectory);
        }
        private static void AddDiagnostics(List<Diagnostic> target, IReadOnlyList<Diagnostic> source)
        {
            if (source == null) return;
            foreach (Diagnostic diagnostic in source) target.Add(diagnostic);
        }
        private static void AddError(List<Diagnostic> diagnostics, string filePath, string message)
        {
            diagnostics.Add(new Diagnostic(new SourceLocation(filePath, 0), message));
        }
    }
}
