using System.Collections.Generic;
using WinASM65.Cpu;
using WinASM65.Output;

namespace WinASM65.Core
{
    public class AssemblerOptions
    {
        /// <summary>
        /// Every option is at its default until a caller says otherwise, and a
        /// property initialiser runs after this constructor, so a caller that
        /// wants the permissive behaviour has to write it out.
        /// </summary>
        public AssemblerOptions()
        {
            ReportUndefinedSymbols = true;
        }

        public bool EnableListing { get; set; }
        public ICpuInstructionSet Cpu { get; set; }
        public IDictionary<string, long> PredefinedSymbols { get; set; }
        public ushort? DefaultOrigin { get; set; }

        /// <summary>
        /// Where the listing goes. Left null it is a <c>.lst</c> beside the
        /// source, driven by <see cref="EnableListing"/>; hand it an
        /// <see cref="InMemoryListingService"/> and nothing touches the disk,
        /// which is what a user interface assembling a buffer needs.
        /// </summary>
        public IListingService ListingService { get; set; }

        /// <summary>
        /// Refuse a name that no file of the run ever defines.
        /// <para>
        /// On by default, because an assembly that ends with an unknown name in it
        /// is not a partial result, it is a wrong one: the placeholder left in the
        /// buffer is a zero, so a typo in a label produces a ROM that branches to
        /// $0000 and reports nothing. There is no later pass that could catch it.
        /// </para>
        /// <para>
        /// The one caller that turns it off is a phase of a multi-file build, where
        /// a name this file uses is quite legitimately defined by a file read later.
        /// The orchestrator checks the leftovers itself, once it has loaded every
        /// symbol file, which is the only point at which that question has an answer.
        /// </para>
        /// </summary>
        public bool ReportUndefinedSymbols { get; set; }
    }

    public interface IAssemblerFactory
    {
        IAssembler Create(bool enableListing);
        IAssembler Create(AssemblerOptions options);
    }

    public class AssemblerFactory : IAssemblerFactory
    {
        public IAssembler Create(bool enableListing)
        {
            return Create(new AssemblerOptions { EnableListing = enableListing });
        }

        public IAssembler Create(AssemblerOptions options)
        {
            options = options ?? new AssemblerOptions();
            return new AssemblerEngine(
                cpu: options.Cpu,
                listingService: options.ListingService
                    ?? new ListingService { IsEnabled = options.EnableListing },
                predefinedSymbols: options.PredefinedSymbols,
                defaultOrigin: options.DefaultOrigin,
                reportUndefinedSymbols: options.ReportUndefinedSymbols);
        }
    }
}
