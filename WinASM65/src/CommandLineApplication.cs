using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.Modules;
using WinASM65.Segments;
using WinASM65.Targets;

namespace WinASM65
{
    public interface IConsoleOutput
    {
        void WriteLine(string value);
        void WriteError(string value);
    }

    public class SystemConsoleOutput : IConsoleOutput
    {
        public void WriteLine(string value) { Console.WriteLine(value); }
        public void WriteError(string value) { Console.Error.WriteLine(value); }
    }

    public class CommandLineApplication
    {
        private readonly IAssemblerFactory _assemblerFactory;
        private readonly IConfigurationReader _configurationReader;
        private readonly IBinaryCombiner _binaryCombiner;
        private readonly IConsoleOutput _console;
        private readonly IExecutablePublisher _executablePublisher;

        public CommandLineApplication(IAssemblerFactory assemblerFactory, IConfigurationReader configurationReader,
            IBinaryCombiner binaryCombiner, IConsoleOutput console, IExecutablePublisher executablePublisher)
        {
            if (assemblerFactory == null) throw new ArgumentNullException("assemblerFactory");
            if (configurationReader == null) throw new ArgumentNullException("configurationReader");
            if (binaryCombiner == null) throw new ArgumentNullException("binaryCombiner");
            if (console == null) throw new ArgumentNullException("console");
            if (executablePublisher == null) throw new ArgumentNullException("executablePublisher");
            _assemblerFactory = assemblerFactory;
            _configurationReader = configurationReader;
            _binaryCombiner = binaryCombiner;
            _console = console;
            _executablePublisher = executablePublisher;
        }

        public int Run(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                DisplayHelp();
                return 0;
            }

            string sourceFile = null;
            string objectFile = null;
            ConfigFile config = null;
            bool enableListing = false;
            string cliSystem = null;
            string cliCpu = null;
            string cliFormat = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-f": if (i + 1 < args.Length) sourceFile = args[++i]; break;
                    case "-o": if (i + 1 < args.Length) objectFile = args[++i]; break;
                    case "-l": enableListing = true; break;
                    case "-t":
                    case "-target":
                        if (i + 1 < args.Length) cliSystem = args[++i];
                        break;
                    case "-cpu":
                        if (i + 1 < args.Length) cliCpu = args[++i];
                        break;
                    case "-format":
                        if (i + 1 < args.Length) cliFormat = args[++i];
                        break;
                    case "-h":
                    case "-help": DisplayHelp(); return 0;
                    case "-c":
                        if (i + 1 >= args.Length) return ReportConfigurationError("Configuration file path is required.");
                        try { config = _configurationReader.Read(args[++i]); }
                        catch (Exception ex)
                        { return ReportConfigurationError(ex.Message); }
                        break;
                }
            }

            if (string.Equals(cliSystem, "list", StringComparison.OrdinalIgnoreCase))
            {
                _console.WriteLine(SystemCatalog.Describe());
                return 0;
            }

            ResolvedTarget target;
            ICpuInstructionSet cpu;
            try
            {
                target = TargetResolver.Resolve(config != null ? config.Target : null, cliSystem, cliCpu, cliFormat);
                cpu = CpuFactory.Create(target.CpuName);
            }
            catch (ArgumentException ex)
            {
                return ReportConfigurationError(ex.Message);
            }

            IDictionary<string, long> symbols = target.DefineHardwareSymbols ? target.HardwareSymbols : null;

            MemoryMap memoryMap;
            if (!TryBuildMemoryMap(config, out memoryMap))
                return 1;
            if (memoryMap != null && memoryMap.Regions.Count > 0)
            {
                Dictionary<string, long> merged = new Dictionary<string, long>();
                if (symbols != null)
                    foreach (KeyValuePair<string, long> pair in symbols)
                        merged[pair.Key] = pair.Value;
                foreach (KeyValuePair<string, long> pair in memoryMap.BuildPredefinedSymbols())
                    merged[pair.Key] = pair.Value;
                symbols = merged;
            }

            using (MemoryMapScope.Activate(memoryMap))
            {
                if (!string.IsNullOrEmpty(sourceFile))
                {
                    if (string.IsNullOrEmpty(objectFile)) objectFile = string.Format("{0}.o", sourceFile.Split('.')[0]);
                    AssemblerOptions options = new AssemblerOptions
                    {
                        EnableListing = enableListing,
                        Cpu = cpu,
                        PredefinedSymbols = symbols,
                        DefaultOrigin = target.LoadAddress
                    };
                    AssemblyResult assembly = _assemblerFactory.Create(options).Assemble(sourceFile, objectFile);
                    if (!assembly.Success) { DisplayDiagnostics(assembly.Diagnostics); return 1; }

                    if (IsModuleFormat(target))
                    {
                        if (assembly.Module == null)
                        {
                            DisplayDiagnostics(new List<Diagnostic> { new Diagnostic(
                                new SourceLocation(sourceFile, 0),
                                "Format 'w65' needs a module: the source declares no .export and no .import.") });
                            return 1;
                        }
                        OperationResult written = W65Format.Write(objectFile, assembly.Module);
                        if (!written.Success) { DisplayDiagnostics(written.Diagnostics); return 1; }
                    }
                    else if (!IsRawFormat(target))
                    {
                        target.OriginAddress = assembly.OriginAddress;
                        OperationResult published = _executablePublisher.Publish(objectFile, assembly.OutputBytes, target);
                        if (!published.Success) { DisplayDiagnostics(published.Diagnostics); return 1; }
                    }
                }

                if (config != null && config.Input != null && config.Input.Count > 0)
                {
                    Func<IAssembler> asmFactory = () => _assemblerFactory.Create(new AssemblerOptions
                    {
                        Cpu = cpu,
                        PredefinedSymbols = symbols,
                        EnableListing = enableListing,
                        DefaultOrigin = target.LoadAddress
                    });
                    MultiSegmentResult segments = new MultiSegmentOrchestrator(asmFactory).AssembleSegments(config.Input);
                    if (!segments.Success) { DisplayDiagnostics(segments.Diagnostics); return 1; }
                }
            }

            if (config != null && config.Output != null)
            {
                if (!IsRawFormat(target))
                {
                    byte[] payload;
                    OperationResult concat = _binaryCombiner.Concatenate(config.Output, out payload);
                    if (!concat.Success) { DisplayDiagnostics(concat.Diagnostics); return 1; }
                    OperationResult published = _executablePublisher.Publish(config.Output.ObjectFile, payload, target);
                    if (!published.Success) { DisplayDiagnostics(published.Diagnostics); return 1; }
                }
                else
                {
                    OperationResult combine = _binaryCombiner.Combine(config.Output);
                    if (!combine.Success) { DisplayDiagnostics(combine.Diagnostics); return 1; }
                }
            }
            return 0;
        }

        private static bool IsRawFormat(ResolvedTarget target)
        {
            return string.IsNullOrWhiteSpace(target.FormatName)
                || target.FormatName.Trim().Equals("bin", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True for the module object format. A module is not an executable: it is
        /// unplaced segments plus relocations, and it is produced by the linker
        /// rather than burned.
        /// </summary>
        private static bool IsModuleFormat(ResolvedTarget target)
        {
            return !string.IsNullOrWhiteSpace(target.FormatName)
                && target.FormatName.Trim().Equals("w65", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds the declared memory map. A configuration with no Regions section
        /// yields a null map, which is the pre-T6 behaviour: nothing to validate
        /// against, so nothing changes. An incoherent region is a configuration
        /// error, reported before any source is read.
        /// </summary>
        private bool TryBuildMemoryMap(ConfigFile config, out MemoryMap map)
        {
            map = null;
            if (config == null || config.Target == null || config.Target.Regions == null || config.Target.Regions.Length == 0)
                return true;

            IReadOnlyList<Diagnostic> errors;
            if (!MemoryMap.TryBuild(config.Target.Regions, out map, out errors))
            {
                DisplayDiagnostics(errors);
                return false;
            }
            return true;
        }

        private int ReportConfigurationError(string message) { _console.WriteError(message); return 1; }
        private void DisplayDiagnostics(IReadOnlyList<Diagnostic> diagnostics)
        {
            foreach (Diagnostic diagnostic in diagnostics) _console.WriteError(diagnostic.ToString());
        }
        private void DisplayHelp()
        {
            _console.WriteLine(string.Format("WinASM65 {0}", Assembly.GetExecutingAssembly().GetName().Version));
            _console.WriteLine("Usage: WinASM65 [-f source] [-o object] [-t system] [-cpu cpu] [-format fmt] [-l] [-c config] [-h|-help]");
            _console.WriteLine("  -t <system>   Target system (nes, c64, c128, vic20, apple2, apple2e, atari8, atari800, atari2600, bbc, bbcmicro, electron, oric, x16, lynx). Use 'list' to enumerate all.");
            _console.WriteLine("  -cpu <cpu>    CPU override (6502 or 65c02). Defaults to target system CPU.");
            _console.WriteLine("  -format <fmt> Output format override (bin, nes, ines, prg, xex, a2bin, rom, o65, ihex, srec, w65). 'w65' writes a linkable module instead of an executable. Defaults to target system format.");
        }
    }
}
