using System.Collections.Generic;
using WinASM65.Cpu;
using WinASM65.Output;

namespace WinASM65.Core
{
    public class AssemblerOptions
    {
        public bool EnableListing { get; set; }
        public ICpuInstructionSet Cpu { get; set; }
        public IDictionary<string, long> PredefinedSymbols { get; set; }
        public ushort? DefaultOrigin { get; set; }

        /// <summary>
        /// Refuse a name that no file of the run ever defines.
        /// <para>
        /// Off by default, and it has to be: the same engine assembles a file on
        /// its own and assembles one phase of a multi-file build, where a name
        /// this file uses is quite legitimately defined by a file read later.
        /// The orchestrator checks the leftovers itself, once it has loaded every
        /// symbol file. A standalone assembly has no later file, so nothing would
        /// check, and the placeholder left in the buffer is a zero: the image
        /// builds clean and is wrong on the machine.
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
                listingService: new ListingService { IsEnabled = options.EnableListing },
                predefinedSymbols: options.PredefinedSymbols,
                defaultOrigin: options.DefaultOrigin,
                reportUndefinedSymbols: options.ReportUndefinedSymbols);
        }
    }
}
