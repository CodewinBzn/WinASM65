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
                defaultOrigin: options.DefaultOrigin);
        }
    }
}
