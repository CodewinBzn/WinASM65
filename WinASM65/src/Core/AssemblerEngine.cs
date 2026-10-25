// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Assembler Engine (Pure OOP, SOLID, KISS)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using WinASM65.Cpu;
using WinASM65.Directives;
using WinASM65.Expressions;
using WinASM65.Modules;
using WinASM65.Output;
using WinASM65.Segments;
using WinASM65.Symbols;

namespace WinASM65.Core
{
    public class AssemblyResult
    {
        public bool Success { get; private set; }
        public byte[] OutputBytes { get; private set; }
        public IReadOnlyList<Diagnostic> Diagnostics { get; private set; }

        public ushort OriginAddress { get; private set; }

        /// <summary>
        /// Every site whose emitted value depends on a symbol, with its width, its
        /// type, the symbols it reads and the source it was written in. In
        /// direct-burn mode these are already patched, so they are informational
        /// here; they are what the .w65 writer (T3) and the linker (T4) consume.
        /// </summary>
        public IReadOnlyList<RelocationRecord> Relocations { get; private set; }

        public AssemblyResult(bool success, byte[] outputBytes, IReadOnlyList<Diagnostic> diagnostics, ushort originAddress = 0,
            IReadOnlyList<RelocationRecord> relocations = null, ModuleImage module = null)
        {
            Success = success;
            OutputBytes = outputBytes ?? new byte[0];
            Diagnostics = diagnostics ?? new List<Diagnostic>();
            OriginAddress = originAddress;
            Relocations = relocations ?? new List<RelocationRecord>();
            Module = module;
        }

        /// <summary>
        /// The module this unit publishes, or null when the source declared no
        /// exports and no imports. A unit with no .export and no .import is an
        /// ordinary direct-burn assembly, and reporting a module for it would be
        /// misleading.
        /// </summary>
        public ModuleImage Module { get; private set; }
    }

    public interface IAssembler
    {
        IBinaryEmitter Emitter { get; }
        IScopeManager ScopeManager { get; }
        IListingService ListingService { get; }
        IDiagnosticReporter Diagnostics { get; }
        AssemblyResult Assemble(string sourceFile, string outputFile);

        /// <summary>
        /// Assembles source text held in memory. Same pipeline as
        /// <see cref="Assemble"/>, with the source arriving as text instead of
        /// as a path, so a user interface can assemble what it is holding
        /// without writing it to a file first.
        /// <para>
        /// <paramref name="outputFile"/> may be null or empty, in which case no
        /// object file is written; <paramref name="sourceName"/> is what
        /// diagnostics and <c>.include</c> resolve against, and nothing is read
        /// from or written to it.
        /// </para>
        /// </summary>
        AssemblyResult AssembleSource(string sourceText, string outputFile, string sourceName = null);

        void ResolvePendingSymbols();
    }

    public class AssemblerEngine : IAssembler, IAssemblyContext
    {
        /// <summary>The name reported for source assembled from memory with no name given.</summary>
        public const string DefaultSourceName = "source";

        private readonly ICpuInstructionSet _cpu;
        private readonly ITokenizer _tokenizer;
        private readonly IExpressionEvaluator _evaluator;
        private readonly IScopeManager _scopeManager;
        private readonly IBinaryEmitter _emitter;
        private readonly IListingService _listingService;
        private readonly IDiagnosticReporter _diagnostics;
        private readonly IDirectiveDispatcher _directiveDispatcher;
        private readonly IDictionary<string, long> _predefinedSymbols;

        /// <summary>
        /// Whether a name nobody defines is an error. See
        /// <see cref="AssemblerOptions.ReportUndefinedSymbols"/> for why this is
        /// a decision and not a constant.
        /// </summary>
        private readonly bool _reportUndefinedSymbols;

        /// <summary>
        /// Where the symbol files of a multi-file build are written, or null for
        /// the process's working directory. See
        /// <see cref="AssemblerOptions.SideFileDirectory"/>.
        /// </summary>
        private readonly string _sideFileDirectory;

        /// <summary>
        /// The names the target predefined and that no source has taken over yet.
        /// </summary>
        private readonly HashSet<string> _predefinedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly ushort? _defaultOrigin;

        private readonly Dictionary<string, MacroDefinition> _macros = new Dictionary<string, MacroDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly ConditionalState _conditionState = new ConditionalState();
        private readonly RepeatBlockState _repeatState = new RepeatBlockState();
        private readonly Stack<SourceFileState> _fileStack = new Stack<SourceFileState>();

        /// <summary>
        /// The labels this unit defined, in the order it defined them. They are kept
        /// apart from the symbol table because the symbol table also holds constants
        /// and predefined hardware names, which are values and not addresses: only a
        /// label can be the target of a relocation.
        /// </summary>
        private readonly List<SourceLabel> _sourceLabels = new List<SourceLabel>();

        private SourceFileState _currentFile;
        private bool _stopAssembling;
        private MacroDefinition _currentMacroBeingDefined;

        // Regex patterns for line matching
        private static readonly Regex StartLocalScopeRegex = new Regex(@"^\s*\{\s*", RegexOptions.Compiled);
        private static readonly Regex EndLocalScopeRegex = new Regex(@"^\s*\}\s*", RegexOptions.Compiled);
        // Anchored to a bare label: this must not fire on "Label: nop", which is an
        // instruction carrying a label. Unanchored, it swallowed the colon and left
        // the instruction with nothing to match, so the line was silently dropped
        // and only the label survived.
        private static readonly Regex LabelDeclareRegex = new Regex(@"^\s*(?<label>@?[a-zA-Z_][a-zA-Z_0-9]*):\s*$", RegexOptions.Compiled);
        // Anchored: a directive is the first thing on its line (a label before it is
        // LabelDirectiveRegex's case). Unanchored, ".word" anywhere in a line won the
        // dispatch, whatever came before it.
        private static readonly Regex DirectiveRegex = new Regex(@"^\s*(?<directive>\.[a-zA-Z][a-zA-Z0-9_]*)(\s+(?<value>(.)+))?$", RegexOptions.Compiled);
        private static readonly Regex InstructionRegex = new Regex(@"^(\s*(?<label>@?[a-zA-Z_][a-zA-Z_0-9]*)\s*:?\s+)?(?<opcode>[a-zA-Z]{3}[0-7]?)((\s+(?<operands>(.)+))|$)", RegexOptions.Compiled);
        private static readonly Regex ConstantRegex = new Regex(@"^\s*(?<label>@?[a-zA-Z_][a-zA-Z_0-9]*)\s*=\s*(?<value>(.)+)$", RegexOptions.Compiled);
        private static readonly Regex MemReserveRegex = new Regex(@"^\s*(?<label>@?[a-zA-Z_][a-zA-Z_0-9]*)\s+\.(RES|res)\s+(?<value>(.)+)$", RegexOptions.Compiled);
        private static readonly Regex MacroCallRegex = new Regex(@"^(\s*(?<label>[a-zA-Z_][a-zA-Z_0-9]*))(\s+(?<value>(.)+))?", RegexOptions.Compiled);

        /// <summary>
        /// A label that shares its line with a directive, as in <c>Data: .byte $AA</c>.
        /// <para>
        /// DirectiveRegex is not anchored, so on such a line it matches the directive
        /// part and wins the dispatch, which used to drop the label on the floor: the
        /// directive emitted its bytes and the name silently never existed, so a later
        /// reference resolved to $0000 with no diagnostic at all. This pattern is
        /// anchored and demands a directive after the label, which is what separates
        /// this line from a bare label, a constant and an instruction.
        /// </para>
        /// </summary>
        private static readonly Regex LabelDirectiveRegex = new Regex(@"^\s*(?<label>@?[a-zA-Z_][a-zA-Z_0-9]*)\s*:?\s+\.[a-zA-Z][a-zA-Z0-9_]*", RegexOptions.Compiled);

        #region Properties & IAssemblyContext

        public IBinaryEmitter Emitter { get { return _emitter; } }
        public IScopeManager ScopeManager { get { return _scopeManager; } }
        public IExpressionEvaluator ExpressionEvaluator { get { return _evaluator; } }
        public IListingService ListingService { get { return _listingService; } }
        public IDiagnosticReporter Diagnostics { get { return _diagnostics; } }
        public ICpuInstructionSet Cpu { get { return _cpu; } }

        public ConditionalState ConditionState { get { return _conditionState; } }
        public RepeatBlockState RepeatState { get { return _repeatState; } }
        public Dictionary<string, MacroDefinition> Macros { get { return _macros; } }

        public MacroDefinition CurrentMacroBeingDefined
        {
            get { return _currentMacroBeingDefined; }
            set { _currentMacroBeingDefined = value; }
        }

        public SourceLocation CurrentLocation
        {
            get
            {
                return _currentFile != null
                    ? new SourceLocation(_currentFile.FilePath, _currentFile.CurrentLineNumber)
                    : new SourceLocation(string.Empty, 0);
            }
        }

        #endregion

        public AssemblerEngine(
            ICpuInstructionSet cpu = null,
            ITokenizer tokenizer = null,
            IExpressionEvaluator evaluator = null,
            IScopeManager scopeManager = null,
            IBinaryEmitter emitter = null,
            IListingService listingService = null,
            IDiagnosticReporter diagnostics = null,
            IDirectiveDispatcher directiveDispatcher = null,
            IDictionary<string, long> predefinedSymbols = null,
            ushort? defaultOrigin = null,
            bool reportUndefinedSymbols = true,
            string sideFileDirectory = null)
        {
            _cpu = cpu ?? new Cpu6502();
            _tokenizer = tokenizer ?? new WinASM65.Expressions.Tokenizer();
            _evaluator = evaluator ?? new ExpressionEvaluator(_tokenizer);
            _scopeManager = scopeManager ?? new ScopeManager();
            ScopeManager concreteScopes = _scopeManager as ScopeManager;
            if (concreteScopes != null)
            {
                concreteScopes.LateError = (pending, message) =>
                    _diagnostics.ReportError(pending != null && pending.HasLocation ? pending.Location : CurrentLocation, message);
            }
            _emitter = emitter ?? new BinaryEmitter();
            _listingService = listingService ?? new ListingService();
            _diagnostics = diagnostics ?? new DiagnosticReporter();
            _directiveDispatcher = directiveDispatcher ?? CreateDefaultDispatcher();
            _predefinedSymbols = predefinedSymbols;
            _defaultOrigin = defaultOrigin;
            _reportUndefinedSymbols = reportUndefinedSymbols;
            _sideFileDirectory = string.IsNullOrWhiteSpace(sideFileDirectory) ? null : sideFileDirectory;
        }

        private static IDirectiveDispatcher CreateDefaultDispatcher()
        {
            DirectiveDispatcher dispatcher = new DirectiveDispatcher();
            dispatcher.Register(new OrgDirectiveHandler());
            dispatcher.Register(new MemAreaDirectiveHandler());
            dispatcher.Register(new IncBinDirectiveHandler());
            dispatcher.Register(new IncludeDirectiveHandler());
            dispatcher.Register(new ByteDirectiveHandler());
            dispatcher.Register(new WordDirectiveHandler());
            dispatcher.Register(new MacroDirectiveHandler());
            dispatcher.Register(new EndMacroDirectiveHandler());
            dispatcher.Register(new IfDirectiveHandler());
            dispatcher.Register(new IfDefDirectiveHandler());
            dispatcher.Register(new IfnDefDirectiveHandler());
            dispatcher.Register(new ElseDirectiveHandler());
            dispatcher.Register(new EndIfDirectiveHandler());
            dispatcher.Register(new RepDirectiveHandler());
            dispatcher.Register(new EndRepDirectiveHandler());
            dispatcher.Register(new ElseIfDirectiveHandler());
            dispatcher.Register(new AsciizDirectiveHandler());
            dispatcher.Register(new ByteTableDirectiveHandler(".lobytes", "<"));
            dispatcher.Register(new ByteTableDirectiveHandler(".hibytes", ">"));
            dispatcher.Register(new FillDirectiveHandler(".dsb", 1));
            dispatcher.Register(new FillDirectiveHandler(".dsw", 2));
            dispatcher.Register(new ReserveDirectiveHandler());
            dispatcher.Register(new PadDirectiveHandler());
            dispatcher.Register(new AlignDirectiveHandler());
            dispatcher.Register(new ErrorDirectiveHandler());
            dispatcher.Register(new WarningDirectiveHandler());
            dispatcher.Register(new AssertDirectiveHandler());
            dispatcher.Register(new NamedValueDirectiveHandler(".equ"));
            dispatcher.Register(new NamedValueDirectiveHandler(".set"));

            // The spellings other assemblers use for the same directives.
            dispatcher.RegisterAlias(".db", ".byte");
            dispatcher.RegisterAlias(".byt", ".byte");
            dispatcher.RegisterAlias(".dw", ".word");
            dispatcher.RegisterAlias(".addr", ".word");
            dispatcher.RegisterAlias(".endm", ".endmacro");
            dispatcher.RegisterAlias(".rept", ".rep");
            dispatcher.RegisterAlias(".endr", ".endrep");
            dispatcher.RegisterAlias(".ds", ".dsb");
            dispatcher.RegisterAlias(".fill", ".dsb");
            dispatcher.Register(new EndDirectiveHandler());
            dispatcher.Register(new ExportDirectiveHandler());
            dispatcher.Register(new ImportDirectiveHandler());
            return dispatcher;
        }

        /// <summary>
        /// The directive names this engine dispatches, dots included, on an
        /// engine with the default dispatcher.
        /// <para>
        /// <c>.res</c> is absent from the list because it is not a directive
        /// handler: it is a pattern the line dispatch matches before any handler
        /// is consulted.
        /// </para>
        /// <para>
        /// This exists so that anything enumerating directives for a user
        /// interface or a reference page can be checked against the engine
        /// instead of against a list someone typed out.
        /// </para>
        /// </summary>
        public static IReadOnlyList<string> DefaultDirectiveNames
        {
            get
            {
                DirectiveDispatcher dispatcher = (DirectiveDispatcher)CreateDefaultDispatcher();
                List<string> names = new List<string>(dispatcher.HandlerNames);
                names.Sort(StringComparer.Ordinal);
                return names;
            }
        }

        public AssemblyResult Assemble(string sourceFile, string outputFile)
        {
            Reset();
            ApplyDefaultOrigin();
            ApplyPredefinedSymbols();

            if (string.IsNullOrEmpty(sourceFile))
            {
                _diagnostics.ReportError(CurrentLocation, "undefined Source file");
                return new AssemblyResult(false, null, _diagnostics.Diagnostics);
            }

            if (string.IsNullOrEmpty(outputFile))
            {
                _diagnostics.ReportError(CurrentLocation, "undefined object file");
                return new AssemblyResult(false, null, _diagnostics.Diagnostics);
            }

            if (!File.Exists(sourceFile))
            {
                _diagnostics.ReportError(new SourceLocation(sourceFile, 0), ErrorCodes.FILE_NOT_EXISTS);
                return new AssemblyResult(false, null, _diagnostics.Diagnostics);
            }

            _listingService.Start(sourceFile);

            PushSourceLines(File.ReadAllLines(sourceFile), sourceFile);
            ProcessFileStack();

            return Complete(outputFile, sourceFile, true);
        }

        public AssemblyResult AssembleSource(string sourceText, string outputFile, string sourceName = null)
        {
            Reset();
            ApplyDefaultOrigin();
            ApplyPredefinedSymbols();

            string name = string.IsNullOrEmpty(sourceName) ? DefaultSourceName : sourceName;

            if (sourceText == null)
            {
                _diagnostics.ReportError(new SourceLocation(name, 0), "undefined Source file");
                return new AssemblyResult(false, null, _diagnostics.Diagnostics);
            }

            _listingService.Start(name);

            PushSourceLines(SplitLines(sourceText), name);
            ProcessFileStack();

            return Complete(outputFile, name, false);
        }

        /// <summary>
        /// The tail every assembly shares, once the source lines are in the
        /// stack. <paramref name="fromFile"/> says whether a symbol export is
        /// owed: text assembled from memory has no path to write next to, and
        /// dropping three files into whatever directory the process happens to
        /// be in is not what a caller listing a buffer asked for.
        /// </summary>
        private AssemblyResult Complete(string outputFile, string sourceName, bool fromFile)
        {
            ResolvePendingSymbols();
            CheckDeferredAssertions();

            // A name nobody ever defined leaves a placeholder in the buffer, and
            // the placeholder is zero. The image therefore builds clean, writes a
            // file, and is wrong on the target: the failure only shows up as a
            // machine that does not do what the source says. The multi-file path
            // already refuses this case; a single file must refuse it too, or
            // moving code to a target with a different hardware table silently
            // zeroes every register it names.
            ReportStillUndefinedSymbols();

            _emitter.SaveToFile(outputFile);
            _listingService.Finish(_emitter.ToArray());

            if (fromFile)
                ExportSymbolFiles(sourceName);

            return new AssemblyResult(!_diagnostics.HasErrors, _emitter.ToArray(), _diagnostics.Diagnostics,
                _emitter.OriginAddress, _emitter.Relocations, BuildModule(sourceName));
        }

        private static string[] SplitLines(string sourceText)
        {
            string[] lines = sourceText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            // A source that ends with a newline has no last line, which is what
            // reading the same text from a file would say. Without this, text and
            // file would differ by one blank listing row.
            if (lines.Length > 1 && lines[lines.Length - 1].Length == 0)
            {
                string[] trimmed = new string[lines.Length - 1];
                Array.Copy(lines, trimmed, lines.Length - 1);
                return trimmed;
            }

            return lines;
        }

        /// <summary>
        /// Builds the module this unit publishes, or null when it declared no
        /// exports and no imports. Exports are resolved to a segment and an offset
        /// here, never to an absolute address: the address is the linker's to
        /// decide, and baking one in would defeat the whole point of a module.
        /// <para>
        /// One segment per <c>.org</c>. The emitter holds a single flat buffer, so a
        /// unit with several origins has to be cut back along the offsets recorded
        /// at each <c>.org</c>. Publishing it as one segment would give every label
        /// past the second origin an offset relative to the first one -- a wrong
        /// address rather than a missing one, which is far harder to notice.
        /// </para>
        /// </summary>
        private ModuleImage BuildModule(string sourceFile)
        {
            ModuleDirectiveState state = ModuleDirectiveState.Peek(this);
            if (state == null || state.IsEmpty)
                return null;

            ModuleImage image = new ModuleImage();
            image.ModuleName = string.IsNullOrEmpty(sourceFile)
                ? string.Empty
                : Path.GetFileNameWithoutExtension(sourceFile);

            // A unit with no .org at all still has one block: the default origin.
            // Normalising here means the rest of the method deals with one shape.
            IReadOnlyList<EmitterOrigin> origins = _emitter.Origins;
            if (origins.Count == 0)
            {
                List<EmitterOrigin> single = new List<EmitterOrigin>();
                single.Add(new EmitterOrigin(0, _emitter.OriginAddress));
                origins = single;
            }

            byte[] buffer = _emitter.ToArray();

            // Segment names follow the source order, not the address order: a .org
            // may move backwards, and "the segment at $9000" is not a name.
            List<int> segmentIndexes = new List<int>();
            for (int s = 0; s < origins.Count; s++)
            {
                int from = origins[s].BufferOffset;
                int to = s + 1 < origins.Count ? origins[s + 1].BufferOffset : buffer.Length;
                if (to < from)
                    to = from;

                byte[] data = new byte[to - from];
                Array.Copy(buffer, from, data, 0, data.Length);

                string name = origins.Count == 1 && !string.IsNullOrEmpty(image.ModuleName)
                    ? image.ModuleName
                    : image.ModuleName + "_" + s.ToString(CultureInfo.InvariantCulture);

                segmentIndexes.Add(image.AddSegment(new ModuleSegment(name, data, SegmentKind.Ro, 1, 0)
                {
                    OriginAddress = origins[s].Address
                }));
            }

            for (int i = 0; i < state.Exports.Count; i++)
            {
                Value address;
                if (!_scopeManager.TryResolveSymbol(state.Exports[i], out address))
                    continue; // already diagnosed by the directive

                int placed;
                uint offset;
                if (TryPlace(address.AsInteger, origins, buffer.Length, out placed, out offset))
                    image.AddExport(new ModuleExport(state.Exports[i], placed, offset));
            }

            for (int i = 0; i < state.Imports.Count; i++)
            {
                image.AddImport(new ModuleImport(state.Imports[i].Symbol, state.Imports[i].ModuleName));
            }

            // The labels the unit wrote down, minus the ones it exports, which are
            // already in the table above. A label that falls outside every block --
            // a .org with no bytes between it and the next, say -- is left out
            // rather than clamped, and the reference fails by name at link time.
            for (int i = 0; i < _sourceLabels.Count; i++)
            {
                string name = _sourceLabels[i].Name;
                if (state.Exports.Contains(name) || name.StartsWith("__anon_", StringComparison.Ordinal))
                    continue;

                int placed;
                uint offset;
                if (TryPlace(_sourceLabels[i].Address, origins, buffer.Length, out placed, out offset))
                    image.AddSymbol(new ModuleSymbol(name, placed, offset));
            }

            // A relocation names the segment and the offset inside it. The emitter
            // only ever had one segment, so every record says segment 0 and carries a
            // buffer offset. Re-pointed at the block that buffer offset now falls in;
            // a site that cannot be placed is dropped, because a record pointing at a
            // segment that does not exist fails the whole link with a message about
            // a segment index rather than about the source line that caused it.
            for (int i = 0; i < _emitter.Relocations.Count; i++)
            {
                RelocationRecord record = _emitter.Relocations[i];

                int placed;
                uint offset;
                if (TryPlaceBufferOffset(record.Offset, origins, buffer.Length, out placed, out offset))
                {
                    image.AddRelocation(Repoint(record, segmentIndexes[placed], (int)offset));
                }
            }

            return image;
        }

        /// <summary>
        /// Builds a copy of <paramref name="record"/> pointing at another segment.
        /// The record carries its own file and line, which is the provenance a
        /// diagnostic needs and the only way the reader knows where to look.
        /// </summary>
        private static RelocationRecord Repoint(RelocationRecord record, int segmentIndex, int offset)
        {
            RelocationRecord repointed = new RelocationRecord(segmentIndex, string.Empty, offset,
                record.Address, record.Width, record.Type, record.Symbols,
                new SourceLocation(record.SourceFile, record.SourceLine), record.Expression);

            // Resolution state is carried over rather than re-derived. A site the
            // second pass already filled holds the right value, and losing that
            // would make the linker re-resolve it -- or, worse, believe it is
            // still a placeholder and treat the placeholder as an answer.
            if (record.IsResolved)
                repointed.MarkResolved(record.Value);
            return repointed;
        }

        /// <summary>
        /// Finds the block an address sits in, and its offset inside that block.
        /// <para>
        /// An address lands in a block when it is at or after the origin and before
        /// the end of the bytes that origin produced. Both halves matter: an address
        /// past the last origin but inside the buffer is not in a block, and neither
        /// is one that lands beyond the block it started in.
        /// </para>
        /// </summary>
        private static bool TryPlace(long address, IReadOnlyList<EmitterOrigin> origins, int bufferLength,
            out int segmentIndex, out uint offset)
        {
            segmentIndex = -1;
            offset = 0;

            long target = address & 0xFFFF;

            for (int s = 0; s < origins.Count; s++)
            {
                int from = origins[s].BufferOffset;
                int to = s + 1 < origins.Count ? origins[s + 1].BufferOffset : bufferLength;
                if (to < from)
                    to = from;

                long baseAddress = origins[s].Address;
                long end = baseAddress + (to - from);

                // The end is exclusive, and inclusive of the last byte only: a block
                // of one byte at $9000 contains $9000 and nothing else.
                if (target < baseAddress || target >= end)
                    continue;

                segmentIndex = s;
                offset = (uint)(target - baseAddress);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Same as <see cref="TryPlace"/>, for a site the emitter gave as a buffer
        /// offset rather than an address. The two differ because an offset is
        /// relative to the buffer while an address is absolute, and a .org in
        /// between makes them stop agreeing.
        /// </summary>
        private static bool TryPlaceBufferOffset(int bufferOffset, IReadOnlyList<EmitterOrigin> origins,
            int bufferLength, out int segmentIndex, out uint offset)
        {
            segmentIndex = -1;
            offset = 0;

            if (bufferOffset < 0 || bufferOffset >= bufferLength)
                return false;

            for (int s = 0; s < origins.Count; s++)
            {
                int from = origins[s].BufferOffset;
                int to = s + 1 < origins.Count ? origins[s + 1].BufferOffset : bufferLength;
                if (to < from)
                    to = from;

                if (bufferOffset < from || bufferOffset >= to)
                    continue;

                segmentIndex = s;
                offset = (uint)(bufferOffset - from);
                return true;
            }

            return false;
        }

        private void ApplyDefaultOrigin()
        {
            InMemoryListingService inMemory = _listingService as InMemoryListingService;
            if (inMemory != null)
                inMemory.StartAddress = _defaultOrigin ?? 0;

            if (!_defaultOrigin.HasValue)
                return;
            _emitter.CurrentAddress = _defaultOrigin.Value;
            _emitter.OriginAddress = _defaultOrigin.Value;
        }

        private void ApplyPredefinedSymbols()
        {
            if (_predefinedSymbols == null)
                return;
            foreach (KeyValuePair<string, long> pair in _predefinedSymbols)
            {
                string error;
                if (!_scopeManager.AddSymbol(pair.Key, pair.Value, true, out error) && !string.IsNullOrEmpty(error))
                    _diagnostics.ReportError(CurrentLocation, error);
                _predefinedNames.Add(pair.Key);
            }
        }

        /// <summary>
        /// Defines a symbol written by the source.
        /// <para>
        /// A name the target predefined is not a duplicate definition. Those values
        /// are a convenience, and a source is entitled to say what a name means in
        /// its own program: refusing "JOY1: TXA" because the NES catalog already
        /// defines a controller port at $4016 makes real code unbuildable, and the
        /// source is the authority on its own labels.
        /// </para>
        /// <para>
        /// The predefined name is consumed here, so writing the same label twice in
        /// the source is still a duplicate and still reported.
        /// </para>
        /// </summary>
        private bool AddSourceSymbol(string label, long value, out string error)
        {
            bool replace = _predefinedNames.Remove(label);
            if (!_scopeManager.AddSymbol(label, value, replace, out error))
                return false;

            // Recorded on success only: a rejected definition never held the address,
            // so writing it here would put a label in the module that the source does
            // not have. A redefinition replaces the entry, keeping the first position
            // so the order a reader sees does not depend on how often a name recurs.
            for (int i = 0; i < _sourceLabels.Count; i++)
            {
                if (string.Equals(_sourceLabels[i].Name, label, StringComparison.Ordinal))
                {
                    _sourceLabels[i] = new SourceLabel(label, value);
                    return true;
                }
            }
            _sourceLabels.Add(new SourceLabel(label, value));
            return true;
        }

        /// <summary>A label as the source wrote it: a name and the address it sits at.</summary>
        private sealed class SourceLabel
        {
            public string Name { get; private set; }
            public long Address { get; private set; }

            public SourceLabel(string name, long address)
            {
                Name = name;
                Address = address;
            }
        }

        public void ResolvePendingSymbols()
        {
            _scopeManager.ResolveSymbols(_evaluator, PatchResolvedExpression);
        }

        /// <summary>
        /// Reports the names that are still unresolved once every file has been
        /// read, and which no <c>.import</c> declared. A declared import is left
        /// alone on purpose: the linker is the one that resolves it, and refusing
        /// it here would make a module impossible to assemble on its own.
        /// </summary>
        private void ReportStillUndefinedSymbols()
        {
            Dictionary<string, UnresolvedSymbol> unsolved = _scopeManager.GlobalScope.UnsolvedSymbols;

            // A local or anonymous label cannot come from another unit, so one left
            // undefined is an error even in a multi-unit build.
            List<string> generated = new List<string>();
            foreach (string name in unsolved.Keys)
            {
                if (IsGeneratedName(name))
                    generated.Add(name);
            }
            generated.Sort(StringComparer.Ordinal);
            foreach (string name in generated)
                ReportUndefinedGeneratedName(name);

            if (!_reportUndefinedSymbols)
                return;
            if (unsolved.Count == 0 && _scopeManager.UnsolvedExprList.Count == 0)
                return;

            ModuleDirectiveState state = ModuleDirectiveState.Peek(this);
            List<string> declared = new List<string>();
            if (state != null)
            {
                for (int i = 0; i < state.Imports.Count; i++)
                    declared.Add(state.Imports[i].Symbol);
            }

            List<string> missing = new List<string>();
            foreach (string name in unsolved.Keys)
            {
                if (IsGeneratedName(name))
                    continue;
                if (declared.Count == 0 || !declared.Contains(name))
                    missing.Add(name);
            }
            missing.Sort(StringComparer.OrdinalIgnoreCase);

            // An expression can be pending because a name inside it is unknown.
            // That name is already in the unsolved symbol table, so it is not
            // counted twice here.
            for (int i = 0; i < missing.Count; i++)
            {
                SourceLocation where = LocationOf(missing[i]);
                _diagnostics.ReportError(where,
                    ErrorCodes.UNDEFINED_SYMBOL + ": " + missing[i]
                    + ". Define it, or declare it with .import when another module provides it.");
            }
        }

        /// <summary>
        /// Where a name was last written. The relocation table already carries
        /// that provenance, and an error without a line only says the name is
        /// wrong, not where.
        /// </summary>
        private SourceLocation LocationOf(string symbol)
        {
            IReadOnlyList<RelocationRecord> relocations = _emitter.Relocations;
            for (int i = 0; i < relocations.Count; i++)
            {
                RelocationRecord record = relocations[i];
                if (record != null
                    && string.Equals(record.TargetSymbol, symbol, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(record.SourceFile))
                {
                    return new SourceLocation(record.SourceFile, record.SourceLine);
                }
            }
            return CurrentLocation;
        }

        private sealed class DeferredAssertion
        {
            public string Expression;
            public string Message;
            public SourceLocation Location;
        }

        private readonly List<DeferredAssertion> _deferredAssertions = new List<DeferredAssertion>();

        public void DeferAssertion(string expression, string message)
        {
            _deferredAssertions.Add(new DeferredAssertion { Expression = expression, Message = message, Location = CurrentLocation });
        }

        /// <summary>
        /// The .assert lines that read a symbol defined later, now that the unit is
        /// complete. In a multi-unit build a name may still belong to another unit;
        /// that case is left to the build's own undefined-name check.
        /// </summary>
        private void CheckDeferredAssertions()
        {
            foreach (DeferredAssertion assertion in _deferredAssertions)
            {
                ExpressionResult res = _evaluator.Evaluate(assertion.Expression, _scopeManager, assertion.Location);
                if (res.HasError)
                    _diagnostics.ReportError(assertion.Location, res.Error);
                else if (!res.IsResolved)
                {
                    if (_reportUndefinedSymbols)
                        _diagnostics.ReportError(assertion.Location, ErrorCodes.UNDEFINED_SYMBOL + ": " + string.Join(", ", res.UndefinedSymbols));
                }
                else if (!res.Value.ToBoolean())
                    _diagnostics.ReportError(assertion.Location, string.Format(ErrorCodes.ASSERTION_FAILED, assertion.Message));
            }
            _deferredAssertions.Clear();
        }

        private void Reset()
        {
            _deferredAssertions.Clear();
            _currentGlobal = null;
            _expansions.Clear();
            _expansionCounter = 0;
            _backwardLabels.Clear();
            _forwardLabels.Clear();
            _emitter.Reset();
            _scopeManager.Reset();
            _diagnostics.Clear();
            _macros.Clear();
            _conditionState.Reset();
            _repeatState.IsInRepeatBlock = false;
            _repeatState.Counter = 0;
            _repeatState.Lines.Clear();
            _fileStack.Clear();
            _currentFile = null;
            _stopAssembling = false;
            _currentMacroBeingDefined = null;
        }

        public void PushSourceFile(string filePath)
        {
            if (_currentFile != null)
                _fileStack.Push(_currentFile);

            _currentFile = new SourceFileState(filePath);
        }

        /// <summary>
        /// Opens the lines of a source already held in memory. The name is what
        /// diagnostics and <c>.include</c> resolve against; for a source read
        /// from disk it is that path, for a buffer it is whatever the caller
        /// named the buffer.
        /// </summary>
        private void PushSourceLines(IReadOnlyList<string> lines, string name)
        {
            if (_currentFile != null)
                _fileStack.Push(_currentFile);

            _currentFile = new SourceFileState(name, lines);
        }

        public void StopAssembling()
        {
            _stopAssembling = true;
        }

        private void ProcessFileStack()
        {
            ISourceLineAwareListingService lineAware = _listingService as ISourceLineAwareListingService;

            while (_currentFile != null && !_stopAssembling)
            {
                if (lineAware != null)
                    lineAware.SetSource(_currentFile.FilePath);

                string rawLine;
                while (!_stopAssembling && (rawLine = _currentFile.ReadLine()) != null)
                {
                    string originalLine = rawLine;

                    // A listing that tracks source lines has to be told where the
                    // line it is about to receive starts; one that only renders
                    // never asks, so nothing is paid for it here. SetSource is
                    // called inside the inner loop as well, because .include
                    // swaps _currentFile without re-entering the outer loop.
                    if (lineAware != null)
                    {
                        lineAware.SetSource(_currentFile.FilePath);
                        lineAware.StartLine(_currentFile.CurrentLineNumber + 1);
                    }

                    _listingService.PrintLine(originalLine);

                    string trimmed = rawLine.Trim();
                    _currentFile.CurrentLineNumber++;

                    // Strip the comment: the first ';' that is not inside a string or a
                    // character literal, so ".byte "a;b"" and "cmp #';'" keep their data.
                    trimmed = SourceText.StripComment(trimmed).Trim();
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        _listingService.EndLine();
                        continue;
                    }

                    if (_currentMacroBeingDefined != null &&
                        !IsEndMacro(trimmed) &&
                        !trimmed.StartsWith(".macro", StringComparison.OrdinalIgnoreCase))
                    {
                        _currentMacroBeingDefined.Lines.Add(trimmed);
                        _listingService.EndLine();
                    }
                    else
                    {
                        ParseLine(trimmed, originalLine);
                    }
                }

                _currentFile.Close();
                _currentFile = _fileStack.Count > 0 ? _fileStack.Pop() : null;
            }
        }

        private static readonly Regex EndMacroRegex = new Regex(@"^\.(endmacro|endm)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex EndRepRegex = new Regex(@"^\.(endrep|endr)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ConditionalDirectiveRegex = new Regex(@"^\.(if|ifdef|ifndef|elseif|else|endif)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // NAME = value is redefinable (a counter in a .rep block needs that); NAME .set
        // value is the same thing. NAME EQU value and NAME .equ value are constants: a
        // second definition with another value is an error.
        private static readonly Regex EquRegex = new Regex(@"^\s*(?<label>@?[a-zA-Z_][a-zA-Z_0-9]*)\s*:?\s+\.?(?i:equ)\s+(?<value>.+)$", RegexOptions.Compiled);
        private static readonly Regex SetRegex = new Regex(@"^\s*(?<label>@?[a-zA-Z_][a-zA-Z_0-9]*)\s+(?i:\.set)\s+(?<value>.+)$", RegexOptions.Compiled);

        // *= expr sets the program counter, as .org does.
        private static readonly Regex OrgStarRegex = new Regex(@"^\s*\*\s*=\s*(?<value>.+)$", RegexOptions.Compiled);

        private static bool IsEndMacro(string line)
        {
            return EndMacroRegex.IsMatch(line);
        }

        private static bool IsEndRep(string line)
        {
            return EndRepRegex.IsMatch(line);
        }

        // The address of the line being assembled: what '*' means in an expression.
        private ushort _lineStartAddress;

        public void ParseLine(string line, string originalLine)
        {
            _lineStartAddress = _emitter.CurrentAddress;

            // Conditional assembly filtering
            if (_conditionState.IsActive &&
                !_conditionState.ShouldAssembleCurrentLine() &&
                !IsConditionalDirective(line))
            {
                _listingService.EndLine();
                return;
            }

            // Repeat block buffering
            if (_repeatState.IsInRepeatBlock &&
                !IsEndRep(line) &&
                !line.StartsWith(".rep", StringComparison.OrdinalIgnoreCase))
            {
                _repeatState.Lines.Add(line);
                _listingService.EndLine();
                return;
            }

            ParseLineBody(line);
            _listingService.EndLine();
        }

        // A line that is only "-", "--", "+", "++"... (an anonymous label), possibly
        // followed by the rest of a line: "- lda #1".
        private static readonly Regex AnonLabelRegex = new Regex(@"^(?<anon>\++|-+)(\s+(?<rest>.+))?$", RegexOptions.Compiled);

        private void ParseLineBody(string line)
        {
            Match anonymous = AnonLabelRegex.Match(line);
            if (anonymous.Success)
            {
                DefineAnonymous(anonymous.Groups["anon"].Value);
                string rest = anonymous.Groups["rest"].Value;
                if (!string.IsNullOrWhiteSpace(rest))
                    ParseLineBody(rest.Trim());
                return;
            }

            // Match line categories
            if (StartLocalScopeRegex.IsMatch(line))
            {
                string err;
                if (!_scopeManager.EnterLocalScope(out err))
                    _diagnostics.ReportError(CurrentLocation, err);
            }
            else if (EndLocalScopeRegex.IsMatch(line))
            {
                string err;
                if (!_scopeManager.ExitLocalScope(out err))
                    _diagnostics.ReportError(CurrentLocation, err);
            }
            else if (OrgStarRegex.IsMatch(line))
            {
                DispatchDirective(".org", OrgStarRegex.Match(line).Groups["value"].Value);
            }
            else if (EquRegex.IsMatch(line))
            {
                HandleConstant(EquRegex.Match(line), true);
            }
            else if (SetRegex.IsMatch(line))
            {
                HandleConstant(SetRegex.Match(line), false);
            }
            else if (MemReserveRegex.IsMatch(line))
            {
                HandleMemReserve(MemReserveRegex.Match(line));
            }
            else if (LabelDirectiveRegex.IsMatch(line))
            {
                // The label takes the address the directive is about to emit at, so
                // it is defined first and the directive then fills that address.
                Match labelMatch = LabelDirectiveRegex.Match(line);
                HandleLabel(labelMatch);
                Match labelled = DirectiveRegex.Match(line.Substring(labelMatch.Groups["label"].Index + labelMatch.Groups["label"].Length).TrimStart(':', ' ', '\t'));
                DispatchDirective(labelled.Groups["directive"].Value, labelled.Groups["value"].Value);
            }
            else if (DirectiveRegex.IsMatch(line))
            {
                Match match = DirectiveRegex.Match(line);
                DispatchDirective(match.Groups["directive"].Value, match.Groups["value"].Value);
            }
            else if (LabelDeclareRegex.IsMatch(line))
            {
                HandleLabel(LabelDeclareRegex.Match(line));
            }
            else if (ConstantRegex.IsMatch(line))
            {
                HandleConstant(ConstantRegex.Match(line), false);
            }
            else if (InstructionRegex.IsMatch(line))
            {
                HandleInstruction(InstructionRegex.Match(line));
            }
            else if (MacroCallRegex.IsMatch(line))
            {
                HandleMacroCall(MacroCallRegex.Match(line));
            }
            else
            {
                _diagnostics.ReportError(CurrentLocation, ErrorCodes.SYNTAX);
            }
        }

        // ---------------------------------------------------- local and anonymous labels

        // The last global label that took an address: the scope of "@name".
        private string _currentGlobal;

        // Open macro expansions and .rep iterations, innermost last: inside one, "@name"
        // belongs to the expansion, so the same macro can be used twice.
        private readonly Stack<int> _expansions = new Stack<int>();
        private int _expansionCounter;

        // How many anonymous labels of each run length have been defined so far:
        // "-" and "--" are different names, as in asm6.
        private readonly Dictionary<int, int> _backwardLabels = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _forwardLabels = new Dictionary<int, int>();

        public void BeginExpansion()
        {
            _expansions.Push(++_expansionCounter);
        }

        public void EndExpansion()
        {
            if (_expansions.Count > 0)
                _expansions.Pop();
        }

        private string LocalScope()
        {
            if (_expansions.Count > 0)
                return "__m" + _expansions.Peek().ToString(CultureInfo.InvariantCulture);
            return _currentGlobal;
        }

        /// <summary>
        /// The stored name of a definition. "@name" becomes "Scope@name"; a global
        /// label that takes an address opens a new scope for the locals after it.
        /// </summary>
        private string DefineName(string written, bool opensScope)
        {
            if (written.StartsWith("@", StringComparison.Ordinal))
            {
                string scope = LocalScope();
                if (scope == null)
                {
                    _diagnostics.ReportError(CurrentLocation, string.Format(ErrorCodes.LOCAL_LABEL_NO_SCOPE, written));
                    return written;
                }
                return scope + written;
            }
            if (opensScope)
                _currentGlobal = written;
            return written;
        }

        private static string AnonymousName(bool forward, int length, int index)
        {
            return "__anon_" + (forward ? "p" : "m") + length.ToString(CultureInfo.InvariantCulture)
                + "_" + index.ToString(CultureInfo.InvariantCulture);
        }

        private void DefineAnonymous(string run)
        {
            bool forward = run[0] == '+';
            Dictionary<int, int> counts = forward ? _forwardLabels : _backwardLabels;
            int index;
            counts.TryGetValue(run.Length, out index);
            counts[run.Length] = index + 1;

            string err;
            if (AddSourceSymbol(AnonymousName(forward, run.Length, index), _emitter.CurrentAddress, out err))
            {
                _listingService.PrintLine(LineType.LABEL, _emitter.CurrentAddress);
                _scopeManager.ResolveSymbols(_evaluator, PatchResolvedExpression);
            }
            else
            {
                _diagnostics.ReportError(CurrentLocation, err);
            }
        }

        private static readonly Regex AnonReferenceRegex = new Regex(@"^\s*(?<anon>\++|-+)\s*$", RegexOptions.Compiled);

        /// <summary>
        /// Rewrites the names an expression reads into the names they are stored under:
        /// "@loop" into "Scope@loop", and an operand that is only "-" or "+" runs into
        /// the anonymous label it designates.
        /// </summary>
        private string ExpandNames(string expr)
        {
            if (string.IsNullOrEmpty(expr))
                return expr;

            Match anonymous = AnonReferenceRegex.Match(expr);
            if (anonymous.Success)
            {
                string run = anonymous.Groups["anon"].Value;
                bool forward = run[0] == '+';
                int count;
                (forward ? _forwardLabels : _backwardLabels).TryGetValue(run.Length, out count);
                if (forward)
                    return AnonymousName(true, run.Length, count);
                if (count == 0)
                {
                    _diagnostics.ReportError(CurrentLocation, string.Format(ErrorCodes.ANON_LABEL_NOT_FOUND, run, "before this line"));
                    return "0";
                }
                return AnonymousName(false, run.Length, count - 1);
            }

            if (expr.IndexOf('@') < 0)
                return expr;

            string scope = LocalScope();
            IReadOnlyList<Token> tokens = _tokenizer.Tokenize(expr);
            System.Text.StringBuilder text = new System.Text.StringBuilder(expr);
            for (int i = tokens.Count - 1; i >= 0; i--)
            {
                if (tokens[i].Type != TokenType.Identifier || !tokens[i].Value.StartsWith("@", StringComparison.Ordinal))
                    continue;
                if (scope == null)
                {
                    _diagnostics.ReportError(CurrentLocation, string.Format(ErrorCodes.LOCAL_LABEL_NO_SCOPE, tokens[i].Value));
                    continue;
                }
                text.Insert(tokens[i].Index, scope);
            }
            return text.ToString();
        }

        /// <summary>A stored name as the source wrote it, for messages.</summary>
        private static bool IsGeneratedName(string name)
        {
            return name.StartsWith("__anon_", StringComparison.Ordinal) || name.IndexOf('@') >= 0;
        }

        private void ReportUndefinedGeneratedName(string name)
        {
            SourceLocation where = LocationOf(name);
            if (name.StartsWith("__anon_", StringComparison.Ordinal))
            {
                int length = int.Parse(name.Substring(8, name.LastIndexOf('_') - 8), CultureInfo.InvariantCulture);
                _diagnostics.ReportError(where, string.Format(ErrorCodes.ANON_LABEL_NOT_FOUND, new string('+', length), "after this line"));
                return;
            }
            int at = name.IndexOf('@');
            string scope = name.Substring(0, at);
            if (scope.StartsWith("__m", StringComparison.Ordinal))
                scope = "this macro or .rep expansion";
            _diagnostics.ReportError(where, string.Format(ErrorCodes.LOCAL_LABEL_UNDEFINED, name.Substring(at), scope));
        }

        /// <summary>
        /// Runs a directive, and says so when there is no such directive: an unknown
        /// one used to be skipped without a word, so ".db" or a typo left a hole in
        /// the image where the data should have been.
        /// </summary>
        private void DispatchDirective(string written, string argument)
        {
            if (_directiveDispatcher.TryDispatch(written.ToLowerInvariant(), argument, this))
                return;
            // ".loop" alone on its line is how other assemblers write a local label.
            if (string.IsNullOrWhiteSpace(argument))
                _diagnostics.ReportError(CurrentLocation, string.Format(ErrorCodes.UNKNOWN_DIRECTIVE_LABEL, written, written.TrimStart('.')));
            else
                _diagnostics.ReportError(CurrentLocation, string.Format(ErrorCodes.UNKNOWN_DIRECTIVE, written));
        }

        private static bool IsConditionalDirective(string line)
        {
            return ConditionalDirectiveRegex.IsMatch(line);
        }

        public ExpressionResult ResolveExpression(string expr, AddressingMode addrMode = AddressingMode.None, bool isLogical = false)
        {
            // The role comes from the decoded addressing mode, never from the value:
            // it is the addressing mode that says whether the byte about to be emitted
            // holds a data value, an address, or a branch displacement.
            ExpressionResult result = _evaluator.Evaluate(ExpandProgramCounter(ExpandNames(expr)), _scopeManager, CurrentLocation);

            // The single place an expression that cannot be evaluated is reported, at
            // the line it was written on. The result carries the value 0, so the caller
            // still emits a field of the right size and registers nothing pending.
            if (result.HasError)
                _diagnostics.ReportError(CurrentLocation, result.Error);

            return result.WithRole(RoleFor(addrMode, isLogical));
        }

        /// <summary>
        /// Replaces '*' in operand position with the address of the line, so that
        /// "jmp *" and ".word *+2" read the program counter while "2*3" multiplies.
        /// The expression is rewritten before it is stored for a later pass, so a
        /// pending expression keeps the address of the line it was written on.
        /// </summary>
        private string ExpandProgramCounter(string expr)
        {
            if (string.IsNullOrEmpty(expr) || expr.IndexOf('*') < 0)
                return expr;

            IReadOnlyList<Token> tokens = _tokenizer.Tokenize(expr);
            System.Text.StringBuilder text = new System.Text.StringBuilder(expr);
            string pc = "$" + _lineStartAddress.ToString("X4", CultureInfo.InvariantCulture);
            for (int i = tokens.Count - 1; i >= 0; i--)
            {
                if (tokens[i].Type != TokenType.Multiply)
                    continue;
                bool operandPosition = i == 0 || !EndsOperand(tokens[i - 1].Type);
                if (!operandPosition)
                    continue;
                text.Remove(tokens[i].Index, tokens[i].Length).Insert(tokens[i].Index, pc);
            }
            return text.ToString();
        }

        private static bool EndsOperand(TokenType type)
        {
            switch (type)
            {
                case TokenType.DecimalNumber:
                case TokenType.HexNumber:
                case TokenType.BinaryNumber:
                case TokenType.CharacterConstant:
                case TokenType.Identifier:
                case TokenType.CloseParenthesis:
                case TokenType.True:
                case TokenType.False:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Maps a decoded addressing mode to the role the emitter must play.
        /// The evaluator cannot know this: <c>lda #$05</c> and <c>lda label</c> are the
        /// same expression shape, and only the mode tells them apart.
        /// </summary>
        public static ExpressionRole RoleFor(AddressingMode addrMode, bool isLogical = false)
        {
            if (isLogical)
                return ExpressionRole.None;
            switch (addrMode)
            {
                case AddressingMode.Immediate:
                    return ExpressionRole.Immediate;
                case AddressingMode.Relative:
                    return ExpressionRole.RelativeBranch;
                case AddressingMode.Implicit:
                case AddressingMode.Accumulator:
                case AddressingMode.None:
                    return ExpressionRole.None;
                default:
                    return ExpressionRole.Address;
            }
        }

        private void HandleLabel(Match match)
        {
            string label = DefineName(match.Groups["label"].Value, true);
            string err;
            if (AddSourceSymbol(label, _emitter.CurrentAddress, out err))
            {
                _listingService.PrintLine(LineType.LABEL, _emitter.CurrentAddress);
                _scopeManager.ResolveSymbols(_evaluator, PatchResolvedExpression);
            }
            else
            {
                _diagnostics.ReportError(CurrentLocation, err);
            }
        }

        private void HandleConstant(Match match, bool strict)
        {
            string label = DefineName(match.Groups["label"].Value, false);
            string valueExpr = match.Groups["value"].Value;

            ExpressionResult res = ResolveExpression(valueExpr);
            if (res.IsResolved)
            {
                Value existing;
                if (strict && _scopeManager.TryResolveSymbol(label, out existing)
                    && existing.AsInteger != res.Value.AsInteger)
                {
                    _diagnostics.ReportError(CurrentLocation, string.Format(ErrorCodes.CONSTANT_REDEFINED, label));
                    return;
                }

                string err;
                _scopeManager.AddSymbol(label, res.Value.AsInteger, true, out err);
                _listingService.PrintLine(LineType.CONST, res.Value.ToInt32());
            }
            else
            {
                UnresolvedSymbol unResSymb = new UnresolvedSymbol
                {
                    NbrUndefinedSymb = res.UndefinedSymbols.Count,
                    Expr = res.Expression
                };

                foreach (string symb in res.UndefinedSymbols)
                {
                    _scopeManager.AddDependingSymbol(symb, label);
                }

                _scopeManager.AddUnresolvedSymbol(label, unResSymb);
            }
        }

        private void HandleMemReserve(Match match)
        {
            string label = DefineName(match.Groups["label"].Value, false);
            string valueExpr = match.Groups["value"].Value;

            ExpressionResult res = ResolveExpression(valueExpr);
            if (res.IsResolved)
            {
                ushort memArea = _scopeManager.CurrentScope.MemArea;
                int size = res.Value.ToInt32();

                // A .res reserves without emitting, so it is the one place where a
                // bss region can actually overflow: the region promised free space,
                // and a reservation larger than it runs into whatever follows --
                // silently, because the symbol still resolves and the output file is
                // still byte for byte correct. The label is only recorded when the
                // reservation holds, so a refused one points at nothing rather than
                // at space nobody owns.
                ValidateReservation(memArea, size);

                string err;
                if (AddSourceSymbol(label, memArea, out err))
                {
                    _listingService.PrintLine(LineType.RES, memArea);
                    _scopeManager.CurrentScope.MemArea = (ushort)(memArea + (size & 0xFFFF));
                    _scopeManager.ResolveSymbols(_evaluator, PatchResolvedExpression);
                }
                else
                {
                    _diagnostics.ReportError(CurrentLocation, err);
                }
            }
            else
            {
                _diagnostics.ReportError(CurrentLocation, ErrorCodes.UNDEFINED_SYMBOL);
            }
        }

        private void ValidateReservation(ushort address, int size)
        {
            MemoryMap regions = MemoryMapScope.Current;
            if (regions != null)
                regions.ValidateReservation(address, size, CurrentLocation, _diagnostics);
        }

        private void HandleInstruction(Match match)
        {
            string opcode = match.Groups["opcode"].Value;
            string operands = match.Groups["operands"].Value;
            string label = match.Groups["label"].Value;

            // Check if opcode is a macro call
            if (_macros.ContainsKey(opcode))
            {
                Match macroMatch = MacroCallRegex.Match(string.Format("{0} {1}", opcode, operands));
                HandleMacroCall(macroMatch);
                return;
            }

            ushort opcodeAddress = _emitter.CurrentAddress;

            // The label group in InstructionRegex is greedy, so "lda Mid" parses as
            // label="lda", opcode="Mid": a three letter operand is
            // indistinguishable from a three letter mnemonic. The swap below
            // recovers the right reading, and it has to recover the operand as it
            // was written. Uppercasing it first turned a symbol called "Mid" into
            // "MID", which the linker's ordinal export table then failed to match.
            // Six letter operands are unaffected, because the regex cannot split
            // them and falls back to the unambiguous reading on its own.
            string writtenOpcode = opcode;
            opcode = opcode.ToUpperInvariant();

            if (!string.IsNullOrWhiteSpace(label))
            {
                string tmpLabel = label.ToUpperInvariant();
                if (_cpu.IsInstruction(tmpLabel))
                {
                    operands = writtenOpcode;
                    opcode = tmpLabel;
                }
                else
                {
                    // The error was dropped here, so a label written on an
                    // instruction line could be defined twice with no diagnostic
                    // and the second definition won. The label on its own line has
                    // always reported it; the two spellings are one concept.
                    string err;
                    if (AddSourceSymbol(DefineName(label, true), _emitter.CurrentAddress, out err))
                        _scopeManager.ResolveSymbols(_evaluator, PatchResolvedExpression);
                    else if (!string.IsNullOrEmpty(err))
                        _diagnostics.ReportError(CurrentLocation, err);
                }
            }

            // A word in mnemonic position that is neither an instruction of this CPU nor
            // a macro used to emit $FF; it is an error, and nothing is emitted.
            if (!_cpu.IsInstruction(opcode))
            {
                _diagnostics.ReportError(CurrentLocation, string.Format(ErrorCodes.UNKNOWN_MNEMONIC, writtenOpcode));
                return;
            }

            InstructionInfo info = _cpu.ParseOperand(opcode, operands);
            AddressingMode mode = info.Mode;

            if (!info.HasOpcode)
            {
                // Some forms exist only in zero page (STX zp,Y; LDX zp,Y has an
                // absolute twin, STX does not). The operand reads as absolute, so
                // the zero-page form is taken; a value that does not fit a byte is
                // then a range error like any other one-byte operand.
                AddressingMode zeroPage = AddressingModes.ZeroPageOf(mode);
                byte zpOpcode;
                if (zeroPage != AddressingMode.None && _cpu.TryGetOpcode(opcode, zeroPage, out zpOpcode))
                {
                    info = info.WithModeAndLength(zeroPage, zpOpcode, 2);
                    mode = zeroPage;
                }
                else
                {
                    _diagnostics.ReportError(CurrentLocation, string.Format(ErrorCodes.UNSUPPORTED_ADDRESSING_MODE,
                        opcode, AddressingModes.Describe(mode)));
                    return;
                }
            }

            if (mode == AddressingMode.Implicit || mode == AddressingMode.Accumulator)
            {
                _emitter.EmitByte(info.Opcode);
                _listingService.PrintLine(LineType.INST, 1);
                return;
            }

            if (mode == AddressingMode.ZeroPageRelative)
            {
                EmitBitBranch(opcode, info, opcodeAddress);
                return;
            }

            // Instructions with operands
            ExpressionResult exprRes = ResolveExpression(info.OperandExpression, mode);
            if (exprRes.IsResolved)
            {
                long val = exprRes.Value.AsInteger;

                if (mode == AddressingMode.Relative)
                {
                    byte offset;
                    string relErr;
                    if (_cpu.TryCalculateRelativeOffset(val, opcodeAddress, out offset, out relErr))
                    {
                        _emitter.EmitByte(info.Opcode);
                        _emitter.EmitByte(offset);
                        // Signed: a branch displacement is signed, and 0xFD is -3.
                        RecordRelocation(exprRes, 1, (sbyte)offset);
                        _listingService.PrintLine(LineType.INST, 2);
                    }
                    else
                    {
                        _diagnostics.ReportError(CurrentLocation, relErr);
                    }
                }
                else if (info.Length == 2 && !Value.InByteRange(val))
                {
                    ReportOperandOutOfRange(val, true);
                    _emitter.EmitByte(info.Opcode);
                    _emitter.EmitByte(0);
                    _listingService.PrintLine(LineType.INST, 2);
                }
                else if (info.Length != 2 && !Value.InWordRange(val))
                {
                    ReportOperandOutOfRange(val, false);
                    _emitter.EmitByte(info.Opcode);
                    _emitter.EmitWord(0);
                    _listingService.PrintLine(LineType.INST, 3);
                }
                else
                {
                    // Check for zero-page optimization. This decision has to happen
                    // before the relocation is recorded: it can shrink the operand
                    // from two bytes to one, and the recorded width must follow the
                    // bytes that were actually emitted.
                    AddressingMode optMode;
                    byte optOpc;
                    byte optLen;
                    if (_cpu.TryOptimizeZeroPage(opcode, mode, val, out optMode, out optOpc, out optLen))
                    {
                        _emitter.EmitByte(optOpc);
                        _emitter.EmitByte((byte)(val & 0xFF));
                        RecordRelocation(exprRes, 1, val);
                        _listingService.PrintLine(LineType.INST, optLen);
                    }
                    else if (info.Length == 2)
                    {
                        _emitter.EmitByte(info.Opcode);
                        _emitter.EmitByte((byte)(val & 0xFF));
                        RecordRelocation(exprRes, 1, val);
                        _listingService.PrintLine(LineType.INST, 2);
                    }
                    else
                    {
                        _emitter.EmitByte(info.Opcode);
                        _emitter.EmitWord((ushort)(val & 0xFFFF));
                        RecordRelocation(exprRes, 2, val);
                        _listingService.PrintLine(LineType.INST, 3);
                    }
                }
            }
            else
            {
                // Unresolved operand - emit placeholders and record for second pass
                int bufferOffset = _emitter.Length + 1;
                _emitter.EmitByte(info.Opcode);
                SymbolType symbolType;

                if (info.Length == 2)
                {
                    _emitter.EmitByte(0);
                    symbolType = SymbolType.Byte;
                }
                else
                {
                    _emitter.EmitWord(0);
                    symbolType = SymbolType.Word;
                }

                ushort position = (ushort)(opcodeAddress - _emitter.OriginAddress + 1);
                UnresolvedExpr unresExpr = new UnresolvedExpr
                {
                    Position = position,
                    BufferOffset = bufferOffset,
                    Type = symbolType,
                    AddrMode = mode,
                    NbrUndefinedSymb = exprRes.UndefinedSymbols.Count,
                    Expr = exprRes.Expression,
                    Location = CurrentLocation
                };
                _scopeManager.AddUnresolvedExpression(position, unresExpr);

                // The cross-file JSR case lands here: a placeholder is emitted now
                // and patched in a second pass, but the site is still a relocation and
                // must be recorded with its width, its type and its provenance.
                RecordRelocation(exprRes, symbolType == SymbolType.Byte ? (byte)1 : (byte)2, 0, false);

                foreach (string symb in exprRes.UndefinedSymbols)
                {
                    UnresolvedSymbol unResSymb = new UnresolvedSymbol();
                    unResSymb.ExprList.Add(position);
                    _scopeManager.AddUnresolvedSymbol(symb, unResSymb);
                }

                _listingService.PrintLine(LineType.INST, info.Length);
            }
        }

        /// <summary>
        /// <c>BBRn zp,target</c>: the opcode, the zero-page byte tested, then the branch
        /// displacement, counted from the end of the three-byte instruction. Either
        /// operand may be a forward reference and is then patched on its own.
        /// </summary>
        private void EmitBitBranch(string mnemonic, InstructionInfo info, ushort opcodeAddress)
        {
            if (string.IsNullOrWhiteSpace(info.OperandExpression) || string.IsNullOrWhiteSpace(info.SecondOperandExpression))
            {
                _diagnostics.ReportError(CurrentLocation, string.Format(ErrorCodes.DIRECTIVE_ARGUMENTS, mnemonic, "a zero-page address and a branch target"));
                return;
            }

            _emitter.EmitByte(info.Opcode);

            ExpressionResult zp = ResolveExpression(info.OperandExpression, AddressingMode.ZeroPage);
            if (zp.IsResolved)
            {
                long value = zp.Value.AsInteger;
                if (value < 0 || value > 0xFF)
                {
                    ReportOperandOutOfRange(value, true);
                    _emitter.EmitByte(0);
                }
                else
                {
                    _emitter.EmitByte((byte)value);
                    RecordRelocation(zp, 1, value);
                }
            }
            else
            {
                int bufferOffset = _emitter.Length;
                _emitter.EmitByte(0);
                RegisterPending(zp, (ushort)(opcodeAddress - _emitter.OriginAddress + 1), bufferOffset,
                    SymbolType.Byte, AddressingMode.ZeroPage);
                RecordRelocation(zp, 1, 0, false);
            }

            ExpressionResult target = ResolveExpression(info.SecondOperandExpression, AddressingMode.Relative);
            if (target.IsResolved)
            {
                byte offset;
                string relErr;
                // The displacement counts from opcode+3; TryCalculateRelativeOffset
                // adds 2 to the address it is given.
                if (_cpu.TryCalculateRelativeOffset(target.Value.AsInteger, opcodeAddress + 1, out offset, out relErr))
                {
                    _emitter.EmitByte(offset);
                    RecordRelocation(target, 1, (sbyte)offset);
                }
                else
                {
                    _diagnostics.ReportError(CurrentLocation, relErr);
                    _emitter.EmitByte(0);
                }
            }
            else
            {
                int bufferOffset = _emitter.Length;
                _emitter.EmitByte(0);
                RegisterPending(target, (ushort)(opcodeAddress - _emitter.OriginAddress + 2), bufferOffset,
                    SymbolType.Byte, AddressingMode.ZeroPageRelative);
                RecordRelocation(target, 1, 0, false);
            }

            _listingService.PrintLine(LineType.INST, 3);
        }

        /// <summary>Records a field to patch once the symbols its expression waits for are defined.</summary>
        private void RegisterPending(ExpressionResult res, ushort position, int bufferOffset, SymbolType type, AddressingMode mode)
        {
            UnresolvedExpr pending = new UnresolvedExpr
            {
                Position = position,
                BufferOffset = bufferOffset,
                Type = type,
                AddrMode = mode,
                NbrUndefinedSymb = res.UndefinedSymbols.Count,
                Expr = res.Expression,
                Location = CurrentLocation
            };
            _scopeManager.AddUnresolvedExpression(position, pending);

            foreach (string symbol in res.UndefinedSymbols)
            {
                UnresolvedSymbol unresolved = new UnresolvedSymbol();
                unresolved.ExprList.Add(position);
                _scopeManager.AddUnresolvedSymbol(symbol, unresolved);
            }
        }

        /// <summary>
        /// Records a relocation for a site whose value depends on a symbol. Nothing is
        /// recorded when the expression read no symbol: <c>lda #$05</c> holds a fixed
        /// byte and a relocation there would be a lie.
        ///
        /// The field is the last <paramref name="width"/> bytes just emitted, so the
        /// segment offset is the buffer offset — not the address made relative to the
        /// current OriginAddress, which a later <c>.org</c> would move.
        /// </summary>
        private void RecordRelocation(ExpressionResult exprRes, byte width, long value, bool resolved = true)
        {
            if (exprRes == null || exprRes.IsConstant)
                return;

            RelocationType type = RelocationRecord.TypeFor(exprRes.Role, width, exprRes.Selector);
            if (type == RelocationType.None)
                return;

            int offset = _emitter.Length - width;
            ushort address = (ushort)(_emitter.CurrentAddress - width);

            List<string> symbols = new List<string>();
            foreach (SymbolReference reference in exprRes.UsedSymbols)
            {
                if (!symbols.Contains(reference.Name))
                    symbols.Add(reference.Name);
            }

            RelocationRecord record = new RelocationRecord(
                _emitter.SegmentIndex,
                _emitter.SegmentName,
                offset,
                address,
                width,
                type,
                symbols,
                exprRes.Location,
                exprRes.Expression);

            if (resolved)
                record.MarkResolved(value);
            _emitter.RecordRelocation(record);
        }

        private void ReportOperandOutOfRange(long value, bool byteSized)
        {
            ReportOperandOutOfRange(value, byteSized, CurrentLocation);
        }

        private void ReportOperandOutOfRange(long value, bool byteSized, SourceLocation location)
        {
            string template = byteSized ? ErrorCodes.VALUE_OUT_OF_RANGE_BYTE : ErrorCodes.VALUE_OUT_OF_RANGE_WORD;
            _diagnostics.ReportError(location, string.Format(CultureInfo.InvariantCulture, template, value));
        }

        /// <summary>Where a pending expression was written, or the current line when unknown.</summary>
        private SourceLocation LocationOf(UnresolvedExpr expr)
        {
            return expr != null && expr.HasLocation ? expr.Location : CurrentLocation;
        }

        private void HandleMacroCall(Match match)
        {
            string macroName = match.Groups["label"].Value;
            string argsValue = match.Groups["value"].Value;

            MacroDefinition macroDef;
            if (!_macros.TryGetValue(macroName, out macroDef))
            {
                _diagnostics.ReportError(CurrentLocation, ErrorCodes.MACRO_NOT_EXISTS);
                return;
            }

            _listingService.EndLine();

            BeginExpansion();
            try
            {
                ExpandMacro(macroDef, argsValue);
            }
            finally
            {
                EndExpansion();
            }
        }

        private void ExpandMacro(MacroDefinition macroDef, string argsValue)
        {
            if (!string.IsNullOrEmpty(argsValue))
            {
                // Arguments split like any argument list, so "($10),y" or "a b" is one
                // argument; a parameter is replaced as a whole word, so a parameter
                // named x does not rewrite "tax".
                List<string> paramValues = SourceText.SplitArguments(argsValue);

                // More arguments than parameters: the last parameter takes the rest,
                // so "get ($10),y" passes the whole operand to a one-parameter macro.
                int declared = macroDef.Parameters.Length;
                if (declared > 0 && paramValues.Count > declared)
                {
                    string rest = string.Join(",", paramValues.GetRange(declared - 1, paramValues.Count - declared + 1));
                    paramValues.RemoveRange(declared - 1, paramValues.Count - declared + 1);
                    paramValues.Add(rest);
                }
                foreach (string line in macroDef.Lines)
                {
                    string expandedLine = line;
                    for (int i = 0; i < paramValues.Count && i < macroDef.Parameters.Length; i++)
                    {
                        string parameter = macroDef.Parameters[i];
                        if (string.IsNullOrEmpty(parameter))
                            continue;
                        string value = paramValues[i];
                        expandedLine = Regex.Replace(expandedLine,
                            @"(?<![\w@])" + Regex.Escape(parameter) + @"(?![\w])",
                            m => value, RegexOptions.IgnoreCase);
                    }
                    _listingService.PrintLine(expandedLine);
                    ParseLine(expandedLine, expandedLine);
                }
            }
            else
            {
                if (macroDef.Parameters.Length > 0)
                {
                    _diagnostics.ReportError(CurrentLocation, ErrorCodes.MACRO_CALL_WITHOUT_PARAMS);
                    return;
                }
                foreach (string line in macroDef.Lines)
                {
                    _listingService.PrintLine(line);
                    ParseLine(line, line);
                }
            }
        }

        private void PatchResolvedExpression(UnresolvedExpr expr, Value value)
        {
            // Position is relative to OriginAddress, which a later .org may have moved.
            // BufferOffset is where the field really is in the buffer, so it is what the
            // relocation lookup uses.
            int relocationOffset = expr.BufferOffset > 0 ? expr.BufferOffset : expr.Position;

            if (expr.AddrMode == AddressingMode.Relative || expr.AddrMode == AddressingMode.ZeroPageRelative)
            {
                // Both displacements count from the byte after the field: a branch
                // from opcode+2, a BBRn from opcode+3, and the field is the last byte.
                byte offset;
                string relErr;
                // expr.Position is after the opcode, so instructionAddress = (expr.Position + OriginAddress - 1)
                long instrAddr = expr.Position + _emitter.OriginAddress - 1;
                if (_cpu.TryCalculateRelativeOffset(value.AsInteger, instrAddr, out offset, out relErr))
                {
                    _emitter.PatchByte(expr.Position, offset);
                    _emitter.ResolveRelocation(_emitter.SegmentIndex, relocationOffset, (sbyte)offset);
                }
                else
                {
                    _diagnostics.ReportError(LocationOf(expr), relErr);
                }
            }
            else
            {
                if (expr.Type == SymbolType.Word)
                {
                    if (!Value.InWordRange(value.AsInteger))
                    {
                        ReportOperandOutOfRange(value.AsInteger, false, LocationOf(expr));
                        return;
                    }
                    _emitter.PatchWord(expr.Position, (ushort)(value.AsInteger & 0xFFFF));
                    _emitter.ResolveRelocation(_emitter.SegmentIndex, relocationOffset, value.AsInteger);
                }
                else
                {
                    if (!Value.InByteRange(value.AsInteger))
                    {
                        ReportOperandOutOfRange(value.AsInteger, true, LocationOf(expr));
                        return;
                    }
                    _emitter.PatchByte(expr.Position, (byte)(value.AsInteger & 0xFF));
                    _emitter.ResolveRelocation(_emitter.SegmentIndex, relocationOffset, value.AsInteger);
                }
            }
        }

        private void ExportSymbolFiles(string sourceFile)
        {
            // The whole path with the extension off, so the files land beside the
            // source they describe -- which is where the orchestrator looks for them,
            // and where they have always been written.
            string baseName = SideFiles.BaseNameOf(sourceFile);

            // Local and anonymous labels stay in their unit: another unit that read
            // "__anon_p1_0" from this file would resolve its own "+" to ours.
            Dictionary<string, long> exported = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, long> pair in _scopeManager.GlobalScope.SymbolTable)
            {
                if (!IsGeneratedName(pair.Key))
                    exported[pair.Key] = pair.Value;
            }
            if (exported.Count > 0)
            {
                string symbPath = SideFile(baseName, ".symb");
                File.WriteAllText(symbPath, JsonConvert.SerializeObject(exported));
            }

            if (_scopeManager.GlobalScope.UnsolvedSymbols.Count > 0)
            {
                string unsolvedPath = SideFile(baseName, ".Unsolved");
                File.WriteAllText(unsolvedPath, JsonConvert.SerializeObject(_scopeManager.GlobalScope.UnsolvedSymbols));
            }

            if (_scopeManager.UnsolvedExprList.Count > 0)
            {
                string exprPath = SideFile(baseName, ".UnsolvedExpr");
                File.WriteAllText(exprPath, JsonConvert.SerializeObject(_scopeManager.UnsolvedExprList));
            }
        }

        /// <summary>
        /// Puts a side file where the rest of the build will look for it. The rule is
        /// <see cref="SideFiles"/>'s, so the writer and the reader cannot disagree.
        /// </summary>
        private string SideFile(string baseName, string extension)
        {
            return SideFiles.Locate(baseName + extension, _sideFileDirectory);
        }

        private class SourceFileState
        {
            public string FilePath { get; private set; }
            public int CurrentLineNumber { get; set; }

            private readonly StreamReader _reader;
            private readonly IReadOnlyList<string> _lines;
            private int _lineIndex;

            public SourceFileState(string filePath)
            {
                FilePath = filePath;
                CurrentLineNumber = 0;
                _reader = new StreamReader(filePath);
            }

            public SourceFileState(string name, IReadOnlyList<string> lines)
            {
                FilePath = name;
                CurrentLineNumber = 0;
                _lines = lines ?? new string[0];
                _lineIndex = 0;
            }

            public string ReadLine()
            {
                if (_reader != null)
                    return _reader.ReadLine();

                return _lineIndex < _lines.Count ? _lines[_lineIndex++] : null;
            }

            public void Close()
            {
                if (_reader != null)
                    _reader.Close();
            }
        }
    }
}
