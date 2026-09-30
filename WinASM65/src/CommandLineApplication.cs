using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.Modules;
using WinASM65.Linking;
using WinASM65.Output;
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

            // The linker is a separate verb: it consumes .w65 modules and produces a
            // burnable image, so it must not fall through to the assemble path.
            if (string.Equals(args[0], "link", StringComparison.OrdinalIgnoreCase))
            {
                string[] rest = new string[args.Length - 1];
                Array.Copy(args, 1, rest, 0, rest.Length);
                return RunLink(rest);
            }

            // Archives are a verb too, for the same reason: they take .w65 modules
            // as input, which the assemble path has no way to accept.
            if (string.Equals(args[0], "archive", StringComparison.OrdinalIgnoreCase))
            {
                string[] rest = new string[args.Length - 1];
                Array.Copy(args, 1, rest, 0, rest.Length);
                return RunArchive(rest);
            }

            if (string.Equals(args[0], "view", StringComparison.OrdinalIgnoreCase))
            {
                string[] rest = new string[args.Length - 1];
                Array.Copy(args, 1, rest, 0, rest.Length);
                return RunView(rest);
            }

            if (string.Equals(args[0], "geos", StringComparison.OrdinalIgnoreCase))
            {
                string[] rest = new string[args.Length - 1];
                Array.Copy(args, 1, rest, 0, rest.Length);
                return RunGeos(rest);
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
                        DefaultOrigin = target.LoadAddress,
                        ReportUndefinedSymbols = true
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

        private int RunView(string[] args)
        {
            string input = null;
            string output = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-f": if (i + 1 < args.Length) input = args[++i]; break;
                    case "-o": if (i + 1 < args.Length) output = args[++i]; break;
                    case "-h":
                    case "-help":
                        _console.WriteLine("Usage: WinASM65 view -f <module.w65|archive.w65a> [-o <report.html>]");
                        _console.WriteLine("  Writes an offline HTML view of segments, symbols and relocations.");
                        return 0;
                    default:
                        if (input == null && !args[i].StartsWith("-", StringComparison.Ordinal))
                            input = args[i];
                        break;
                }
            }

            if (string.IsNullOrEmpty(input))
            {
                _console.WriteError("Nothing to view: give a .w65 module or a .w65a archive.");
                return 1;
            }

            if (string.IsNullOrEmpty(output))
                output = Path.ChangeExtension(input, ".html");

            OperationResult written = ModuleViewer.Write(output, input);
            if (!written.Success)
            {
                DisplayDiagnostics(written.Diagnostics);
                return 1;
            }

            _console.WriteLine("Wrote " + output);
            return 0;
        }

        /// <summary>
        /// Links .w65 modules and writes the result as a GEOS application on a D64.
        /// <para>
        /// One application, whose records are the linked segments in placement
        /// order. The first record is the one GEOS loads when the application is
        /// opened, so it has to be the segment holding the start address; the
        /// linker places by origin, so that is the segment whose origin is lowest.
        /// </para>
        /// </summary>
        private int RunGeos(string[] args)
        {
            List<string> inputs = new List<string>();
            string output = "out.d64";
            string diskName = "GEOS DISK";
            string diskId = "01";
            string appName = "APPLICATION";
            string author = string.Empty;
            string description = string.Empty;
            int shift = 0;
            int start = -1;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-o": if (i + 1 < args.Length) output = args[++i]; break;
                    case "-disk": if (i + 1 < args.Length) diskName = args[++i]; break;
                    case "-id": if (i + 1 < args.Length) diskId = args[++i]; break;
                    case "-name": if (i + 1 < args.Length) appName = args[++i]; break;
                    case "-author": if (i + 1 < args.Length) author = args[++i]; break;
                    case "-description": if (i + 1 < args.Length) description = args[++i]; break;
                    case "-shift": if (i + 1 < args.Length) int.TryParse(args[++i], out shift); break;
                    case "-start": if (i + 1 < args.Length) int.TryParse(args[++i], out start); break;
                    case "-h":
                    case "-help":
                        _console.WriteLine("Usage: WinASM65 geos <module.w65>... -o <disk.d64> [-name <n>] [-disk <n>] [-id <nn>] [-start <addr>]");
                        _console.WriteLine("  Links the modules into one GEOS application and writes a D64 image.");
                        _console.WriteLine("  The placed segments become the records of the application, and the");
                        _console.WriteLine("  relocation table of the absolute references is appended to them.");
                        return 0;
                    default:
                        if (!args[i].StartsWith("-", StringComparison.Ordinal))
                            inputs.Add(args[i]);
                        break;
                }
            }

            if (inputs.Count == 0)
            {
                _console.WriteError("Nothing to build: give at least one .w65 module.");
                return 1;
            }

            List<ModuleImage> modules = new List<ModuleImage>();
            foreach (string input in inputs)
            {
                if (IsArchive(input))
                {
                    ArchiveResolution resolution;
                    OperationResult resolved = ArchiveFormat.Resolve(input, out resolution);
                    if (!resolved.Success)
                    {
                        DisplayDiagnostics(resolved.Diagnostics);
                        return 1;
                    }
                    for (int a = 0; a < resolution.Archives.Count; a++)
                        for (int m = 0; m < resolution.Archives[a].Members.Count; m++)
                            modules.Add(resolution.Archives[a].Members[m].Module);
                    continue;
                }

                ModuleImage module;
                string moduleName;
                OperationResult read = W65Format.TryRead(input, out module, out moduleName);
                if (!read.Success)
                {
                    DisplayDiagnostics(read.Diagnostics);
                    return 1;
                }
                if (string.IsNullOrEmpty(module.ModuleName))
                    module.ModuleName = moduleName;
                modules.Add(module);
            }

            LinkerOptions options = new LinkerOptions();
            options.AddressShift = shift;
            LinkedImage image;
            OperationResult linked = new Linker().Link(modules, options, out image);
            if (!linked.Success)
            {
                DisplayDiagnostics(linked.Diagnostics);
                return 1;
            }

            GeosApplication application = new GeosApplication();
            application.Name = appName;
            application.Author = author;
            application.Description = description;
            // Stamped when the disk is written, which is what GEOS shows in the
            // file list. The library keeps 1900-01-01 so that a test building a
            // disk twice gets the same bytes.
            application.Timestamp = DateTime.Today;
            application.LoadAddress = image.OriginAddress;

            // Placed order, which is origin order, so record 0 is the one GEOS
            // loads first and it is the start of the program.
            for (int i = 0; i < image.Segments.Count; i++)
            {
                PlacedSegment segment = image.Segments[i];
                application.Records.Add(new GeosRecord(new byte[segment.Size]));
                byte[] record = application.Records[application.Records.Count - 1].Data;
                for (int b = 0; b < segment.Size; b++)
                {
                    int at = segment.Address + b - image.OriginAddress;
                    record[b] = at >= 0 && at < image.Data.Length ? image.Data[at] : (byte)0x00;
                }
            }

            // The relocation table goes after the code, in its own record, and the
            // information sector says where it is. The application is started at
            // -start, or at the first byte of the code when that was not said.
            ushort entry = start >= 0 ? (ushort)start : image.OriginAddress;
            GeosRelocationTable table = GeosRelocationTable.Build(image, entry);
            application.StartAddress = entry;
            application.EndAddress = (ushort)(image.OriginAddress + image.Data.Length);
            application.BaseAddress = table.BaseAddress;
            application.TableAddress = (ushort)(image.OriginAddress + image.Data.Length);
            application.TableEntryCount = (ushort)table.Entries.Count;
            application.Records.Add(new GeosRecord(table.Data));

            D64Builder disk = new D64Builder();
            disk.DiskName = diskName;
            disk.DiskId = diskId;
            disk.Applications.Add(application);

            OperationResult written = disk.Write(output);
            if (!written.Success)
            {
                DisplayDiagnostics(written.Diagnostics);
                return 1;
            }

            _console.WriteLine(string.Format("Linked {0} segment(s) into a GEOS application -> {1}",
                image.Segments.Count, output));
            _console.WriteLine("  " + appName + " load $" + image.OriginAddress.ToString("X4")
                + " start $" + entry.ToString("X4")
                + " end $" + application.EndAddress.ToString("X4")
                + ", " + (image.Segments.Count + 1) + " record(s)");
            _console.WriteLine("  relocation table at $" + application.TableAddress.ToString("X4")
                + ", " + table.Entries.Count + " reference(s) for base $"
                + table.BaseAddress.ToString("X4"));
            return 0;
        }

        /// <summary>
        /// Builds a .w65a archive out of .w65 modules.
        /// <para>
        /// -ref names another archive this one needs. The reference is stored as
        /// given and resolved relative to the archive being written, so a library
        /// keeps working when it is moved along with its dependents.
        /// </para>
        /// </summary>
        private int RunArchive(string[] args)
        {
            List<string> inputs = new List<string>();
            List<string> references = new List<string>();
            string output = "lib.w65a";

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-o": if (i + 1 < args.Length) output = args[++i]; break;
                    case "-ref":
                    case "-reference":
                        if (i + 1 < args.Length) references.Add(args[++i]);
                        break;
                    case "-h":
                    case "-help":
                        _console.WriteLine("Usage: WinASM65 archive <module.w65>... -o <lib.w65a> [-ref <other.w65a>...]");
                        _console.WriteLine("  Packs modules into an archive with a symbol index over them.");
                        _console.WriteLine("  -ref records an archive this one needs; resolution is transitive.");
                        return 0;
                    default:
                        if (!args[i].StartsWith("-", StringComparison.Ordinal))
                            inputs.Add(args[i]);
                        break;
                }
            }

            if (inputs.Count == 0)
            {
                _console.WriteError("Nothing to archive: give at least one .w65 module.");
                return 1;
            }

            ModuleArchive archive = new ModuleArchive();
            for (int i = 0; i < inputs.Count; i++)
            {
                ModuleImage module;
                string moduleName;
                OperationResult read = W65Format.TryRead(inputs[i], out module, out moduleName);
                if (!read.Success)
                {
                    DisplayDiagnostics(read.Diagnostics);
                    return 1;
                }
                archive.Members.Add(new ArchiveMember(
                    string.IsNullOrEmpty(moduleName) ? "module" + i : moduleName, module));
            }

            for (int i = 0; i < references.Count; i++)
                archive.References.Add(references[i]);

            OperationResult written = ArchiveFormat.Write(output, archive);
            if (!written.Success)
            {
                DisplayDiagnostics(written.Diagnostics);
                return 1;
            }

            _console.WriteLine(string.Format("Archived {0} module(s) -> {1}", archive.Members.Count, output));
            for (int i = 0; i < archive.Members.Count; i++)
                _console.WriteLine("  " + archive.Members[i].Name);
            for (int i = 0; i < archive.References.Count; i++)
                _console.WriteLine("  needs " + archive.References[i]);
            return 0;
        }

        private static bool IsArchive(string path)
        {
            try
            {
                return ArchiveFormat.HasMagic(File.ReadAllBytes(path));
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Runs the linker verb. Every input is a .w65 module; the output is the flat
        /// image in the format named by -format, or raw.
        /// </summary>
        private int RunLink(string[] args)
        {
            List<string> inputs = new List<string>();
            string output = "out.bin";
            string format = "bin";
            string cpu = null;
            int shift = 0;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-o": if (i + 1 < args.Length) output = args[++i]; break;
                    case "-format": if (i + 1 < args.Length) format = args[++i]; break;
                    case "-cpu": if (i + 1 < args.Length) cpu = args[++i]; break;
                    case "-shift": if (i + 1 < args.Length) int.TryParse(args[++i], out shift); break;
                    case "-h":
                    case "-help":
                        _console.WriteLine("Usage: WinASM65 link <module.w65>... -o <image> [-format <fmt>] [-shift <n>]");
                        _console.WriteLine("  Resolves imports against exports, places segments, applies");
                        _console.WriteLine("  relocations and writes a burnable image.");
                        return 0;
                    default:
                        if (!args[i].StartsWith("-", StringComparison.Ordinal))
                            inputs.Add(args[i]);
                        break;
                }
            }

            if (inputs.Count == 0)
            {
                _console.WriteError("Nothing to link: give at least one .w65 module.");
                return 1;
            }

            List<ModuleImage> modules = new List<ModuleImage>();
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            foreach (string input in inputs)
            {
                // An archive stands for every module it holds plus everything its
                // references pull in, so it is expanded here rather than being one
                // more module: the linker never needs to know archives existed.
                if (IsArchive(input))
                {
                    ArchiveResolution resolution;
                    OperationResult resolved = ArchiveFormat.Resolve(input, out resolution);
                    if (!resolved.Success)
                    {
                        DisplayDiagnostics(resolved.Diagnostics);
                        return 1;
                    }

                    for (int a = 0; a < resolution.Archives.Count; a++)
                    {
                        ModuleArchive archive = resolution.Archives[a];
                        _console.WriteLine("archive " + archive.Name + ": "
                            + archive.Members.Count + " module(s)");
                        for (int m = 0; m < archive.Members.Count; m++)
                        {
                            ModuleImage member = archive.Members[m].Module;
                            if (member != null && string.IsNullOrEmpty(member.ModuleName))
                                member.ModuleName = archive.Members[m].Name;
                            modules.Add(member);
                        }
                    }
                    continue;
                }

                ModuleImage module;
                string moduleName;
                OperationResult read = W65Format.TryRead(input, out module, out moduleName);
                if (!read.Success)
                {
                    DisplayDiagnostics(read.Diagnostics);
                    return 1;
                }
                if (string.IsNullOrEmpty(module.ModuleName))
                    module.ModuleName = moduleName;
                modules.Add(module);
            }

            LinkerOptions options = new LinkerOptions();
            options.AddressShift = shift;

            LinkedImage image;
            OperationResult linked = new Linker().Link(modules, options, out image);
            if (!linked.Success)
            {
                DisplayDiagnostics(linked.Diagnostics);
                return 1;
            }

            ResolvedTarget target;
            ICpuInstructionSet linkedCpu;
            try
            {
                target = TargetResolver.Resolve(null, null, cpu, format);
                linkedCpu = CpuFactory.Create(target.CpuName);
            }
            catch (ArgumentException ex)
            {
                return ReportConfigurationError(ex.Message);
            }

            target.LoadAddress = image.OriginAddress;
            OperationResult published = _executablePublisher.Publish(output, image.Data, target);
            if (!published.Success)
            {
                DisplayDiagnostics(published.Diagnostics);
                return 1;
            }

            _console.WriteLine("Linked " + modules.Count + " module(s) at $" +
                image.OriginAddress.ToString("X4") + ", " + image.Data.Length + " octets -> " + output);
            foreach (PlacedSegment segment in image.Segments)
                _console.WriteLine("  " + segment);
            return 0;
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
            _console.WriteLine("  -t <system>   Target system. Use 'list' to enumerate all, which includes bbc, tube (the BBC second processor), and the hardware variants.");
            _console.WriteLine("  -cpu <cpu>    CPU override (6502 or 65c02). Defaults to target system CPU.");
            _console.WriteLine("  -format <fmt> Output format override (bin, nes, ines, prg, xex, a2bin, rom, o65, ihex, srec, bbc, tube, prodos, w65). 'prodos' wraps the payload in a ProDOS load file on a volume, and 'w65' writes a linkable module instead of an executable. Defaults to target system format.");
            _console.WriteLine("");
                _console.WriteLine("Verbs:");
                _console.WriteLine("  link <module.w65>...   Link modules into a flat burnable image");
                _console.WriteLine("  archive <module.w65>... Pack modules into a .w65a archive");
                _console.WriteLine("  geos <module.w65>...   Link modules into a GEOS application on a D64");
                _console.WriteLine("  view -f <file>         Write an offline HTML view of a module or archive");
        }
    }
}
