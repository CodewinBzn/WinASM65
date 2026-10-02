// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Source listing API

using System.Collections.Generic;
using System.IO;
using WinASM65.Core;

namespace WinASM65.Output
{
    /// <summary>
    /// Lists a source in memory through the ordinary assembler, so a user
    /// interface gets rows without a source file, an object file or a
    /// <c>.lst</c> ever being written.
    /// </summary>
    public class SourceListingService : ISourceListingService
    {
        private readonly IAssemblerFactory _assemblerFactory;

        /// <summary>The name reported when the caller gives none.</summary>
        public const string DefaultSourceName = AssemblerEngine.DefaultSourceName;

        public SourceListingService()
            : this(new AssemblerFactory())
        {
        }

        public SourceListingService(IAssemblerFactory assemblerFactory)
        {
            _assemblerFactory = assemblerFactory ?? new AssemblerFactory();
        }

        public SourceListing ListSourceText(string sourceText)
        {
            return ListSourceText(sourceText, DefaultSourceName);
        }

        public SourceListing ListSourceText(string sourceText, string sourceName)
        {
            string name = string.IsNullOrEmpty(sourceName) ? DefaultSourceName : sourceName;
            InMemoryListingService sink = new InMemoryListingService();
            IAssembler assembler = _assemblerFactory.Create(new AssemblerOptions { ListingService = sink });

            AssemblyResult result = assembler.AssembleSource(sourceText ?? string.Empty, null, name);
            return new SourceListing(name, result, sink.Rows);
        }

        public SourceListing ListSourceFile(string sourceFilePath)
        {
            if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath))
            {
                // Reported the way the assembler reports a missing source, so a
                // caller sees one shape of failure and not two.
                List<Diagnostic> problems = new List<Diagnostic>
                {
                    new Diagnostic(new SourceLocation(sourceFilePath, 0), ErrorCodes.FILE_NOT_EXISTS)
                };
                return new SourceListing(sourceFilePath ?? string.Empty, new AssemblyResult(false, null, problems), null);
            }

            InMemoryListingService sink = new InMemoryListingService();
            IAssembler assembler = _assemblerFactory.Create(new AssemblerOptions { ListingService = sink });

            // Read whole rather than line by line: re-joining lines with a separator loses
            // the trailing terminator, and a source whose last line is empty
            // would then list one line short of the file it came from.
            string source = File.ReadAllText(sourceFilePath);
            AssemblyResult result = assembler.AssembleSource(source, null, sourceFilePath);
            return new SourceListing(sourceFilePath, result, sink.Rows);
        }
    }
}