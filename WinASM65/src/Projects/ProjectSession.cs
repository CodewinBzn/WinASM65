// WinASM65 - A whole project, as one unit a user interface can drive

using System;
using System.Collections.Generic;
using System.IO;
using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.Output;
using WinASM65.Segments;
using WinASM65.Symbols;
using WinASM65.Targets;

namespace WinASM65.Projects
{
    /// <summary>
    /// What a caller may vary about one build of a project.
    /// <para>
    /// Every option is at its default, and the defaults are the ones the command
    /// line uses, so <c>new ProjectBuildOptions()</c> and
    /// <c>WinASM65 -c config.json</c> build the same bytes.
    /// </para>
    /// </summary>
    public sealed class ProjectBuildOptions
    {
        /// <summary>Writes a <c>.lst</c> beside each source.</summary>
        public bool EnableListing { get; set; }

        /// <summary>
        /// Where the <c>.symb</c>, <c>.Unsolved</c> and <c>.UnsolvedExpr</c> files of
        /// the build go. Left null they go beside their source, which is where the
        /// assembler has always put them.
        /// <para>
        /// These three files are how one unit of a build is handed to the next, so
        /// naming the directory is what keeps two projects -- or two builds of the
        /// same project -- from sharing one set of them. An editor that keeps several
        /// projects open wants a scratch directory per session.
        /// </para>
        /// </summary>
        public string SideFileDirectory { get; set; }

        /// <summary>Overrides the configuration's system, as <c>-t</c> does.</summary>
        public string System { get; set; }

        /// <summary>Overrides the configuration's CPU, as <c>-cpu</c> does.</summary>
        public string Cpu { get; set; }

        /// <summary>Overrides the configuration's output format, as <c>-format</c> does.</summary>
        public string Format { get; set; }

        /// <summary>
        /// Whether the image is written to disk. Off by default: an interface
        /// pressing F5 wants the bytes, and a file it did not ask for is a file it
        /// has to clean up. The bytes are in <see cref="ProjectBuildResult.Image"/>
        /// either way.
        /// </summary>
        public bool WriteImage { get; set; }

        /// <summary>
        /// Where the image is written when <see cref="WriteImage"/> is set. Left
        /// null it is the configuration's <c>Output.ObjectFile</c>, resolved against
        /// the project directory.
        /// </summary>
        public string ImagePath { get; set; }
    }

    /// <summary>What one unit of a project contributed to a build.</summary>
    public sealed class ProjectUnitResult
    {
        /// <summary>Absolute path of the source that was assembled.</summary>
        public string SourceFile { get; private set; }

        /// <summary>Absolute path of the object file it was assembled into.</summary>
        public string ObjectFile { get; private set; }

        /// <summary>The sources this unit was allowed to take names from.</summary>
        public IReadOnlyList<string> Dependencies { get; private set; }

        /// <summary>
        /// Every name this unit defined, with the value it gave them. Empty when
        /// the unit produced no object file.
        /// </summary>
        public IDictionary<string, long> Symbols { get; private set; }

        public ProjectUnitResult(string sourceFile, string objectFile, IReadOnlyList<string> dependencies,
            IDictionary<string, long> symbols)
        {
            SourceFile = sourceFile ?? string.Empty;
            ObjectFile = objectFile ?? string.Empty;
            Dependencies = dependencies ?? new List<string>();
            Symbols = symbols ?? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The outcome of building a project: the image, what each unit contributed,
    /// and every diagnostic the build produced.
    /// </summary>
    public sealed class ProjectBuildResult
    {
        /// <summary>Whether the build produced an image and no error.</summary>
        public bool Success { get; private set; }

        /// <summary>
        /// The image the project's <c>Output</c> section describes, already in its
        /// target format. Empty when the build did not succeed.
        /// <para>
        /// This is the whole point of the session: a caller gets the ROM, not the
        /// name of a file it has to hope was written.
        /// </para>
        /// </summary>
        public byte[] Image { get; private set; }

        /// <summary>Where the image was written, when it was asked to be written.</summary>
        public string ImagePath { get; private set; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; private set; }

        /// <summary>One entry per declared input, in declaration order.</summary>
        public IReadOnlyList<ProjectUnitResult> Units { get; private set; }

        public ProjectBuildResult(bool success, byte[] image, string imagePath,
            IReadOnlyList<Diagnostic> diagnostics, IReadOnlyList<ProjectUnitResult> units)
        {
            Success = success;
            Image = image ?? new byte[0];
            ImagePath = imagePath ?? string.Empty;
            Diagnostics = diagnostics ?? new List<Diagnostic>();
            Units = units ?? new List<ProjectUnitResult>();
        }
    }

    /// <summary>
    /// A project opened for building and listing: a configuration plus the
    /// directory its names are relative to.
    /// <para>
    /// The multi-file build already existed and already worked; what it lacked was
    /// an address. Every path it touches was relative to the process's working
    /// directory, which is fine for a command line and useless for a user
    /// interface, because an interface pressing F5 has a working directory that
    /// has nothing to do with the project. This class supplies the missing piece
    /// -- an explicit base directory -- and hands the work to the pieces that
    /// already do it: <see cref="MultiSegmentOrchestrator"/> for the units and
    /// their cross-file names, <see cref="BinaryCombiner"/> for the image,
    /// <see cref="ExecutablePublisher"/> for its format.
    /// </para>
    /// <para>
    /// Nothing here assembles anything by itself. A name one file uses and another
    /// defines is resolved the way it always was, by the orchestrator handing over
    /// the symbol file of the unit that defines it, and a name no file defines is
    /// still reported.
    /// </para>
    /// <para>
    /// The side files of a build -- <c>.o</c>, <c>.symb</c>, <c>.Unsolved</c>,
    /// <c>.UnsolvedExpr</c>, <c>.lst</c> -- are files on disk, because that is how
    /// one unit of a build is handed to the next. Set
    /// <see cref="ProjectBuildOptions.WorkingDirectory"/> to keep the object files
    /// out of the source tree; the symbol files follow their source.
    /// </para>
    /// </summary>
    public sealed class ProjectSession
    {
        private readonly IConfigurationReader _configurationReader;
        private readonly IAssemblerFactory _assemblerFactory;
        private readonly IBinaryCombiner _binaryCombiner;
        private readonly IExecutablePublisher _publisher;

        private ProjectSession(ConfigFile configuration, string baseDirectory,
            IConfigurationReader configurationReader, IAssemblerFactory assemblerFactory,
            IBinaryCombiner binaryCombiner, IExecutablePublisher publisher)
        {
            Configuration = configuration ?? new ConfigFile();
            BaseDirectory = baseDirectory ?? string.Empty;
            _configurationReader = configurationReader;
            _assemblerFactory = assemblerFactory ?? new AssemblerFactory();
            _binaryCombiner = binaryCombiner ?? new BinaryCombiner();
            _publisher = publisher ?? new ExecutablePublisher();
            ProjectSymbols = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The configuration as it was read.</summary>
        public ConfigFile Configuration { get; private set; }

        /// <summary>
        /// The directory every relative name in the configuration is resolved
        /// against. It is the configuration's own directory, never the process's
        /// working directory.
        /// </summary>
        public string BaseDirectory { get; private set; }

        /// <summary>
        /// Every name the last successful build saw defined, across all its units.
        /// Empty before the first build, which is what a listing falls back on.
        /// </summary>
        public IDictionary<string, long> ProjectSymbols { get; private set; }

        /// <summary>
        /// The declared sources, absolute, in the order the configuration lists
        /// them. That order is the build order: a unit may use a name a unit
        /// declared as its dependency defines.
        /// </summary>
        public IReadOnlyList<string> SourceFiles
        {
            get
            {
                List<string> files = new List<string>();
                if (Configuration.Input != null)
                {
                    for (int i = 0; i < Configuration.Input.Count; i++)
                    {
                        if (Configuration.Input[i] != null && !string.IsNullOrWhiteSpace(Configuration.Input[i].FileName))
                            files.Add(Resolve(Configuration.Input[i].FileName));
                    }
                }
                return files;
            }
        }

        // ------------------------------------------------------------------ open

        /// <summary>
        /// Reads a configuration and returns the session that builds it. The
        /// project directory is the directory holding the configuration, so the
        /// names inside it mean what they meant when the project was written.
        /// </summary>
        public static ProjectSession Open(string configurationPath)
        {
            if (string.IsNullOrWhiteSpace(configurationPath))
                throw new ArgumentException("Configuration file path is required.", "configurationPath");
            if (!File.Exists(configurationPath))
                throw new FileNotFoundException("Configuration file not found.", configurationPath);

            IConfigurationReader reader = new JsonConfigurationReader();
            string full = Path.GetFullPath(configurationPath);
            return new ProjectSession(reader.Read(full), Path.GetDirectoryName(full),
                reader, null, null, null);
        }

        /// <summary>
        /// Builds a session from a configuration the caller already holds, against a
        /// directory it names. Used when the configuration did not come from a file,
        /// and by tests.
        /// </summary>
        public static ProjectSession Open(ConfigFile configuration, string baseDirectory)
        {
            return new ProjectSession(configuration, baseDirectory, null, null, null, null);
        }

        /// <summary>
        /// Builds a session with every collaborator supplied, so a caller can
        /// substitute one without a subclass.
        /// </summary>
        public static ProjectSession Create(ConfigFile configuration, string baseDirectory,
            IConfigurationReader configurationReader, IAssemblerFactory assemblerFactory,
            IBinaryCombiner binaryCombiner, IExecutablePublisher publisher)
        {
            return new ProjectSession(configuration, baseDirectory, configurationReader,
                assemblerFactory, binaryCombiner, publisher);
        }

        // ----------------------------------------------------------------- build

        /// <summary>Builds the project with the command line's own defaults.</summary>
        public ProjectBuildResult Build()
        {
            return Build(new ProjectBuildOptions());
        }

        /// <summary>Builds the project and returns the image in memory.</summary>
        public ProjectBuildResult Build(ProjectBuildOptions options)
        {
            options = options ?? new ProjectBuildOptions();
            List<Diagnostic> diagnostics = new List<Diagnostic>();

            ResolvedTarget target;
            ICpuInstructionSet cpu;
            try
            {
                target = TargetResolver.Resolve(Configuration.Target, options.System, options.Cpu, options.Format);
                cpu = CpuFactory.Create(target.CpuName);
            }
            catch (ArgumentException ex)
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0), ex.Message));
                return new ProjectBuildResult(false, null, null, diagnostics, null);
            }

            IDictionary<string, long> symbols = target.DefineHardwareSymbols ? target.HardwareSymbols : null;

            MemoryMap memoryMap;
            IReadOnlyList<Diagnostic> mapErrors;
            if (!TryBuildMemoryMap(out memoryMap, out mapErrors))
            {
                diagnostics.AddRange(mapErrors);
                return new ProjectBuildResult(false, null, null, diagnostics, null);
            }
            if (memoryMap != null && memoryMap.Regions.Count > 0)
            {
                Dictionary<string, long> merged = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                if (symbols != null)
                {
                    foreach (KeyValuePair<string, long> pair in symbols)
                        merged[pair.Key] = pair.Value;
                }
                foreach (KeyValuePair<string, long> pair in memoryMap.BuildPredefinedSymbols())
                    merged[pair.Key] = pair.Value;
                symbols = merged;
            }

            List<ProjectUnitResult> units = new List<ProjectUnitResult>();
            byte[] payload;

            using (MemoryMapScope.Activate(memoryMap))
            {
                List<Segment> segments = AbsoluteSegments(options);
                if (segments.Count == 0)
                {
                    diagnostics.Add(new Diagnostic(new SourceLocation(ConfigurationPath(), 0),
                        "Nothing to build: the configuration declares no input."));
                    return new ProjectBuildResult(false, null, null, diagnostics, units);
                }

                // The orchestrator builds one assembler per unit, and the symbol table
                // each one ends up holding is that unit's contribution to the project.
                // Holding on to them is what lets a listing know the names a sibling
                // file defines without re-reading the .symb files the build left.
                List<IAssembler> assembled = new List<IAssembler>();
                string sideFiles = SideFileDirectory(options);
                Func<IAssembler> factory = delegate
                {
                    IAssembler assembler = _assemblerFactory.Create(new AssemblerOptions
                    {
                        Cpu = cpu,
                        PredefinedSymbols = symbols,
                        EnableListing = options.EnableListing,
                        DefaultOrigin = target.LoadAddress,
                        SideFileDirectory = sideFiles,

                        // The one place a name may legitimately be unknown while a
                        // source is being read: this file is one unit of a build and a
                        // unit further down may define it. The orchestrator asks the
                        // question once every unit is in.
                        ReportUndefinedSymbols = false
                    });
                    assembled.Add(assembler);
                    return assembler;
                };

                MultiSegmentResult segmentsResult = new MultiSegmentOrchestrator(factory, sideFiles)
                    .AssembleSegments(segments);
                diagnostics.AddRange(segmentsResult.Diagnostics);

                for (int i = 0; i < segments.Count; i++)
                    units.Add(UnitOf(segments[i], assembled));

                if (!segmentsResult.Success)
                    return new ProjectBuildResult(false, null, null, diagnostics, units);

                OperationResult combined = Combine(target, options, out payload);
                if (!combined.Success)
                {
                    diagnostics.AddRange(combined.Diagnostics);
                    return new ProjectBuildResult(false, null, null, diagnostics, units);
                }
            }

            // Published only on request, and reported either way, so a caller that
            // asked for bytes is not told where to find them.
            string imagePath = null;
            if (options.WriteImage)
            {
                imagePath = ResolveImagePath(options);
                OperationResult published = _publisher.Publish(imagePath, payload, target);
                if (!published.Success)
                {
                    diagnostics.AddRange(published.Diagnostics);
                    return new ProjectBuildResult(false, null, imagePath, diagnostics, units);
                }
            }

            Remember(units);
            return new ProjectBuildResult(true, payload, imagePath, diagnostics, units);
        }

        // --------------------------------------------------------------- listing

        /// <summary>
        /// Lists a source of this project, with the names the project's other units
        /// define already known.
        /// <para>
        /// This is the listing an editor pane wants, and it is the whole of what
        /// makes a multi-file project editable one file at a time: a routine this
        /// file calls and another file defines is a name the editor has to resolve,
        /// and without the project's names it is reported as undefined on a line
        /// that is perfectly correct.
        /// </para>
        /// <para>
        /// A name the source itself defines wins over a predefined one, so listing
        /// never hides a redefinition, and a name no unit of the project defines is
        /// still reported -- the session widens what is known, it does not make
        /// silence correct.
        /// </para>
        /// <para>
        /// Before the first build, <see cref="ProjectSymbols"/> is empty and the
        /// listing is that of the file alone, which is what a project nobody has
        /// built yet can honestly say.
        /// </para>
        /// </summary>
        public SourceListing ListSourceFile(string sourceFilePath)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
            {
                List<Diagnostic> problems = new List<Diagnostic>
                {
                    new Diagnostic(new SourceLocation(sourceFilePath, 0), ErrorCodes.FILE_NOT_EXISTS)
                };
                return new SourceListing(sourceFilePath ?? string.Empty,
                    new AssemblyResult(false, null, problems), null);
            }

            // Read whole rather than line by line: a source whose last line is empty
            // must not list one line short of the file it came from.
            return ListSourceText(File.ReadAllText(sourceFilePath), sourceFilePath);
        }

        /// <summary>
        /// Lists source text against the project's names, under a name the caller
        /// chooses. Nothing is read from or written to that name; it is what
        /// diagnostics report and what an <c>.include</c> resolves against.
        /// </summary>
        public SourceListing ListSourceText(string sourceText, string sourceName)
        {
            string name = string.IsNullOrWhiteSpace(sourceName) ? AssemblerEngine.DefaultSourceName : sourceName;
            InMemoryListingService sink = new InMemoryListingService();
            IAssembler assembler = _assemblerFactory.Create(new AssemblerOptions
            {
                ListingService = sink,
                PredefinedSymbols = ProjectSymbols.Count == 0 ? null : ProjectSymbols
            });

            AssemblyResult result = assembler.AssembleSource(sourceText ?? string.Empty, null, name);
            return new SourceListing(name, result, sink.Rows);
        }

        // ---------------------------------------------------------------- private

        /// <summary>
        /// The directory the build's side files go to, or null for beside their
        /// source. The caller names one when it wants them out of the project.
        /// </summary>
        private string SideFileDirectory(ProjectBuildOptions options)
        {
            return options != null && !string.IsNullOrWhiteSpace(options.SideFileDirectory)
                ? Path.GetFullPath(options.SideFileDirectory)
                : null;
        }

        /// <summary>One declared input, with every path it is read through made absolute.</summary>
        private List<Segment> AbsoluteSegments(ProjectBuildOptions options)        {            List<Segment> segments = new List<Segment>();
            if (Configuration.Input == null)
                return segments;

            for (int i = 0; i < Configuration.Input.Count; i++)
            {
                Segment declared = Configuration.Input[i];
                if (declared == null || string.IsNullOrWhiteSpace(declared.FileName))
                    continue;

                string source = Resolve(declared.FileName);
                List<string> dependencies = new List<string>();
                if (declared.Dependencies != null)
                {
                    for (int d = 0; d < declared.Dependencies.Length; d++)
                    {
                        if (!string.IsNullOrWhiteSpace(declared.Dependencies[d]))
                            dependencies.Add(Resolve(declared.Dependencies[d]));
                    }
                }

                // Beside the source, which is where the configuration's Output section
                // expects to find it: a project names its object files by bare name, so
                // putting them anywhere else would desynchronise the two halves of the
                // configuration. An explicit OutputFile is honoured as written.
                string output = !string.IsNullOrWhiteSpace(declared.OutputFile)
                    ? Resolve(declared.OutputFile)
                    : ObjectFileFor(source);

                segments.Add(new Segment
                {
                    FileName = source,
                    OutputFile = output,
                    Dependencies = dependencies.ToArray()
                });
            }
            return segments;
        }

        private static string ObjectFileFor(string source)
        {
            return Path.Combine(Path.GetDirectoryName(source),
                Path.GetFileNameWithoutExtension(source) + ".o");
        }

        private ProjectUnitResult UnitOf(Segment segment, List<IAssembler> assembled)
        {
            // The orchestrator assembles the units in declaration order and creates
            // one assembler for each, so the tables line up. Nothing depends on the
            // pairing being right: a mismatch would cost a symbol table, not a
            // build, and a unit with no assembler simply reports no names.
            IDictionary<string, long> symbols = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (assembled.Count > 0)
            {
                IAssembler assembler = assembled[0];
                assembled.RemoveAt(0);
                LexicalScopeData scope = assembler.ScopeManager.GlobalScope;
                foreach (KeyValuePair<string, long> pair in scope.SymbolTable)
                    symbols[pair.Key] = pair.Value;
            }

            return new ProjectUnitResult(segment.FileName, segment.OutputFile, segment.Dependencies, symbols);
        }

        /// <summary>
        /// Keeps the union of what the units defined, so the next listing can see it.
        /// A name two units define is kept at the value the first one gave it, which
        /// is the same rule the assembler applies to a predefined name: the source
        /// is the authority on its own labels.
        /// </summary>
        private void Remember(IReadOnlyList<ProjectUnitResult> units)
        {
            Dictionary<string, long> merged = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < units.Count; i++)
            {
                foreach (KeyValuePair<string, long> pair in units[i].Symbols)
                {
                    if (!merged.ContainsKey(pair.Key))
                        merged[pair.Key] = pair.Value;
                }
            }
            ProjectSymbols = merged;
        }

        private bool TryBuildMemoryMap(out MemoryMap map, out IReadOnlyList<Diagnostic> errors)
        {
            map = null;
            errors = new List<Diagnostic>();
            if (Configuration.Target == null || Configuration.Target.Regions == null
                || Configuration.Target.Regions.Length == 0)
                return true;
            return MemoryMap.TryBuild(Configuration.Target.Regions, out map, out errors);
        }

        /// <summary>
        /// Builds the image the <c>Output</c> section describes, in memory. The
        /// file names are resolved against the project, so this is the command
        /// line's concatenation with the working directory taken out of it.
        /// </summary>
        private OperationResult Combine(ResolvedTarget target, ProjectBuildOptions options, out byte[] payload)
        {
            payload = new byte[0];
            if (Configuration.Output == null || Configuration.Output.Files == null
                || Configuration.Output.Files.Length == 0)
                return new OperationResult(true, new List<Diagnostic>());

            CombineConf absolute = new CombineConf
            {
                ObjectFile = ResolveImagePath(options),
                Files = new FileConf[Configuration.Output.Files.Length]
            };
            for (int i = 0; i < Configuration.Output.Files.Length; i++)
            {
                FileConf declared = Configuration.Output.Files[i];
                absolute.Files[i] = new FileConf
                {
                    FileName = Resolve(declared == null ? null : declared.FileName),
                    Size = declared == null ? null : declared.Size
                };
            }
            return _binaryCombiner.Concatenate(absolute, out payload);
        }

        private string ResolveImagePath(ProjectBuildOptions options)
        {
            if (options != null && !string.IsNullOrWhiteSpace(options.ImagePath))
                return Resolve(options.ImagePath);
            if (Configuration.Output != null && !string.IsNullOrWhiteSpace(Configuration.Output.ObjectFile))
                return Resolve(Configuration.Output.ObjectFile);
            return Resolve("out.bin");
        }

        /// <summary>Makes a name from the configuration absolute against the project.</summary>
        private string Resolve(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return name;
            if (Path.IsPathRooted(name))
                return Path.GetFullPath(name);

            string root = string.IsNullOrEmpty(BaseDirectory) ? Directory.GetCurrentDirectory() : BaseDirectory;
            return Path.GetFullPath(Path.Combine(root, name));
        }

        private string ConfigurationPath()
        {
            if (Configuration.Output == null || string.IsNullOrWhiteSpace(Configuration.Output.ObjectFile))
                return string.Empty;
            return Resolve(Configuration.Output.ObjectFile);
        }
    }
}
