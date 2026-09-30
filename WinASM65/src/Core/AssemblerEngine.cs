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
        void ResolvePendingSymbols();
    }

    public class AssemblerEngine : IAssembler, IAssemblyContext
    {
        private readonly ICpuInstructionSet _cpu;
        private readonly ITokenizer _tokenizer;
        private readonly IExpressionEvaluator _evaluator;
        private readonly IScopeManager _scopeManager;
        private readonly IBinaryEmitter _emitter;
        private readonly IListingService _listingService;
        private readonly IDiagnosticReporter _diagnostics;
        private readonly IDirectiveDispatcher _directiveDispatcher;
        private readonly IDictionary<string, long> _predefinedSymbols;
        private readonly ushort? _defaultOrigin;

        private readonly Dictionary<string, MacroDefinition> _macros = new Dictionary<string, MacroDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly ConditionalState _conditionState = new ConditionalState();
        private readonly RepeatBlockState _repeatState = new RepeatBlockState();
        private readonly Stack<SourceFileState> _fileStack = new Stack<SourceFileState>();

        private SourceFileState _currentFile;
        private bool _stopAssembling;
        private MacroDefinition _currentMacroBeingDefined;

        // Regex patterns for line matching
        private static readonly Regex StartLocalScopeRegex = new Regex(@"^\s*\{\s*", RegexOptions.Compiled);
        private static readonly Regex EndLocalScopeRegex = new Regex(@"^\s*\}\s*", RegexOptions.Compiled);
        private static readonly Regex LabelDeclareRegex = new Regex(@"\s*(?<label>[a-zA-Z_][a-zA-Z_0-9]*):\s*", RegexOptions.Compiled);
        private static readonly Regex DirectiveRegex = new Regex(@"\s*(?<directive>\.[a-zA-Z]+)(\s+(?<value>(.)+))?", RegexOptions.Compiled);
        private static readonly Regex InstructionRegex = new Regex(@"^(\s*(?<label>[a-zA-Z_][a-zA-Z_0-9]*)\s+)?(?<opcode>[a-zA-Z]{3})((\s+(?<operands>(.)+))|$)", RegexOptions.Compiled);
        private static readonly Regex ConstantRegex = new Regex(@"^\s*(?<label>[a-zA-Z_][a-zA-Z_0-9]*)\s*=\s*(?<value>(.)+)$", RegexOptions.Compiled);
        private static readonly Regex MemReserveRegex = new Regex(@"^\s*(?<label>[a-zA-Z_][a-zA-Z_0-9]*)\s+\.(RES|res)\s+(?<value>(.)+)$", RegexOptions.Compiled);
        private static readonly Regex MacroCallRegex = new Regex(@"^(\s*(?<label>[a-zA-Z_][a-zA-Z_0-9]*))(\s+(?<value>(.)+))?", RegexOptions.Compiled);

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
            ushort? defaultOrigin = null)
        {
            _cpu = cpu ?? new Cpu6502();
            _tokenizer = tokenizer ?? new WinASM65.Expressions.Tokenizer();
            _evaluator = evaluator ?? new ExpressionEvaluator(_tokenizer);
            _scopeManager = scopeManager ?? new ScopeManager();
            _emitter = emitter ?? new BinaryEmitter();
            _listingService = listingService ?? new ListingService();
            _diagnostics = diagnostics ?? new DiagnosticReporter();
            _directiveDispatcher = directiveDispatcher ?? CreateDefaultDispatcher();
            _predefinedSymbols = predefinedSymbols;
            _defaultOrigin = defaultOrigin;
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
            dispatcher.Register(new EndDirectiveHandler());
            dispatcher.Register(new ExportDirectiveHandler());
            dispatcher.Register(new ImportDirectiveHandler());
            return dispatcher;
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

            PushSourceFile(sourceFile);
            ProcessFileStack();

            ResolvePendingSymbols();

            _emitter.SaveToFile(outputFile);
            _listingService.Finish(_emitter.ToArray());

            ExportSymbolFiles(sourceFile);

            return new AssemblyResult(!_diagnostics.HasErrors, _emitter.ToArray(), _diagnostics.Diagnostics,
                _emitter.OriginAddress, _emitter.Relocations, BuildModule(sourceFile));
        }

        /// <summary>
        /// Builds the module this unit publishes, or null when it declared no
        /// exports and no imports. Exports are resolved to a segment and an offset
        /// here, never to an absolute address: the address is the linker's to
        /// decide, and baking one in would defeat the whole point of a module.
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

            ModuleSegment segment = new ModuleSegment(
                string.IsNullOrEmpty(image.ModuleName) ? "code" : image.ModuleName,
                _emitter.ToArray(), SegmentKind.Ro, 1, 0)
            {
                OriginAddress = _emitter.OriginAddress
            };
            int segmentIndex = image.AddSegment(segment);

            for (int i = 0; i < state.Exports.Count; i++)
            {
                Value address;
                if (!_scopeManager.TryResolveSymbol(state.Exports[i], out address))
                    continue; // already diagnosed by the directive
                // Offsets are segment-relative. A module is unplaced, so the
                // address the symbol currently holds only tells us where it sits
                // inside this unit's own buffer.
                int offset = (int)((address.AsInteger - segment.OriginAddress) & 0xFFFF);
                image.AddExport(new ModuleExport(state.Exports[i], segmentIndex, (uint)offset));
            }

            for (int i = 0; i < state.Imports.Count; i++)
            {
                image.AddImport(new ModuleImport(state.Imports[i].Symbol, state.Imports[i].ModuleName));
            }

            for (int i = 0; i < _emitter.Relocations.Count; i++)
                image.AddRelocation(_emitter.Relocations[i]);

            return image;
        }

        private void ApplyDefaultOrigin()
        {
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
            }
        }

        public void ResolvePendingSymbols()
        {
            _scopeManager.ResolveSymbols(_evaluator, PatchResolvedExpression);
        }

        private void Reset()
        {
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

        public void StopAssembling()
        {
            _stopAssembling = true;
        }

        private void ProcessFileStack()
        {
            while (_currentFile != null && !_stopAssembling)
            {
                string rawLine;
                while (!_stopAssembling && (rawLine = _currentFile.ReadLine()) != null)
                {
                    string originalLine = rawLine;
                    _listingService.PrintLine(originalLine);

                    string trimmed = rawLine.Trim();
                    _currentFile.CurrentLineNumber++;

                    // Strip comment
                    trimmed = Regex.Replace(trimmed, ";(.)*", "").Trim();
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        _listingService.EndLine();
                        continue;
                    }

                    if (_currentMacroBeingDefined != null &&
                        !trimmed.Equals(".endmacro", StringComparison.OrdinalIgnoreCase) &&
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

        public void ParseLine(string line, string originalLine)
        {
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
                !line.Equals(".endrep", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith(".rep", StringComparison.OrdinalIgnoreCase))
            {
                _repeatState.Lines.Add(line);
                _listingService.EndLine();
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
            else if (MemReserveRegex.IsMatch(line))
            {
                HandleMemReserve(MemReserveRegex.Match(line));
            }
            else if (DirectiveRegex.IsMatch(line))
            {
                Match match = DirectiveRegex.Match(line);
                string directiveName = match.Groups["directive"].Value.ToLowerInvariant();
                string argument = match.Groups["value"].Value;
                _directiveDispatcher.TryDispatch(directiveName, argument, this);
            }
            else if (LabelDeclareRegex.IsMatch(line))
            {
                HandleLabel(LabelDeclareRegex.Match(line));
            }
            else if (ConstantRegex.IsMatch(line))
            {
                HandleConstant(ConstantRegex.Match(line));
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

            _listingService.EndLine();
        }

        private static bool IsConditionalDirective(string line)
        {
            return line.Equals(".endif", StringComparison.OrdinalIgnoreCase) ||
                   line.Equals(".else", StringComparison.OrdinalIgnoreCase) ||
                   line.StartsWith(".if ", StringComparison.OrdinalIgnoreCase) ||
                   line.StartsWith(".ifdef ", StringComparison.OrdinalIgnoreCase) ||
                   line.StartsWith(".ifndef ", StringComparison.OrdinalIgnoreCase);
        }

        public ExpressionResult ResolveExpression(string expr, AddressingMode addrMode = AddressingMode.None, bool isLogical = false)
        {
            // The role comes from the decoded addressing mode, never from the value:
            // it is the addressing mode that says whether the byte about to be emitted
            // holds a data value, an address, or a branch displacement.
            ExpressionResult result = _evaluator.Evaluate(expr, _scopeManager, CurrentLocation);
            return result.WithRole(RoleFor(addrMode, isLogical));
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
            string label = match.Groups["label"].Value;
            string err;
            if (_scopeManager.AddSymbol(label, _emitter.CurrentAddress, false, out err))
            {
                _listingService.PrintLine(LineType.LABEL, _emitter.CurrentAddress);
                _scopeManager.ResolveSymbols(_evaluator, PatchResolvedExpression);
            }
            else
            {
                _diagnostics.ReportError(CurrentLocation, err);
            }
        }

        private void HandleConstant(Match match)
        {
            string label = match.Groups["label"].Value;
            string valueExpr = match.Groups["value"].Value;

            ExpressionResult res = ResolveExpression(valueExpr);
            if (res.IsResolved)
            {
                string err;
                _scopeManager.AddSymbol(label, res.Value.AsInteger, true, out err);
                _listingService.PrintLine(LineType.CONST, res.Value.ToInt32());
            }
            else
            {
                UnresolvedSymbol unResSymb = new UnresolvedSymbol
                {
                    NbrUndefinedSymb = res.UndefinedSymbols.Count,
                    Expr = valueExpr
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
            string label = match.Groups["label"].Value;
            string valueExpr = match.Groups["value"].Value;

            ExpressionResult res = ResolveExpression(valueExpr);
            if (res.IsResolved)
            {
                ushort memArea = _scopeManager.CurrentScope.MemArea;
                string err;
                if (_scopeManager.AddSymbol(label, memArea, false, out err))
                {
                    _listingService.PrintLine(LineType.RES, memArea);
                    _scopeManager.CurrentScope.MemArea += res.Value.ToUInt16();
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
            opcode = opcode.ToUpperInvariant();

            if (!string.IsNullOrWhiteSpace(label))
            {
                string tmpLabel = label.ToUpperInvariant();
                if (_cpu.IsInstruction(tmpLabel))
                {
                    operands = opcode;
                    opcode = tmpLabel;
                }
                else
                {
                    string err;
                    _scopeManager.AddSymbol(label, _emitter.CurrentAddress, false, out err);
                }
            }

            InstructionInfo info = _cpu.ParseOperand(opcode, operands);
            AddressingMode mode = info.Mode;

            if (mode == AddressingMode.Implicit || mode == AddressingMode.Accumulator)
            {
                _emitter.EmitByte(info.Opcode);
                _listingService.PrintLine(LineType.INST, 1);
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
                    Expr = info.OperandExpression
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

            RelocationType type = RelocationRecord.TypeFor(exprRes.Role, width);
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
            string template = byteSized ? ErrorCodes.VALUE_OUT_OF_RANGE_BYTE : ErrorCodes.VALUE_OUT_OF_RANGE_WORD;
            _diagnostics.ReportError(CurrentLocation, string.Format(CultureInfo.InvariantCulture, template, value));
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

            if (!string.IsNullOrEmpty(argsValue))
            {
                string[] paramValues = Regex.Replace(argsValue, @"\s+", "").Split(',');
                foreach (string line in macroDef.Lines)
                {
                    string expandedLine = line;
                    for (int i = 0; i < paramValues.Length && i < macroDef.Parameters.Length; i++)
                    {
                        expandedLine = expandedLine.Replace(macroDef.Parameters[i], paramValues[i]);
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

            if (expr.AddrMode == AddressingMode.Relative)
            {
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
                    _diagnostics.ReportError(CurrentLocation, relErr);
                }
            }
            else
            {
                if (expr.Type == SymbolType.Word)
                {
                    if (!Value.InWordRange(value.AsInteger))
                    {
                        ReportOperandOutOfRange(value.AsInteger, false);
                        return;
                    }
                    _emitter.PatchWord(expr.Position, (ushort)(value.AsInteger & 0xFFFF));
                    _emitter.ResolveRelocation(_emitter.SegmentIndex, relocationOffset, value.AsInteger);
                }
                else
                {
                    if (!Value.InByteRange(value.AsInteger))
                    {
                        ReportOperandOutOfRange(value.AsInteger, true);
                        return;
                    }
                    _emitter.PatchByte(expr.Position, (byte)(value.AsInteger & 0xFF));
                    _emitter.ResolveRelocation(_emitter.SegmentIndex, relocationOffset, value.AsInteger);
                }
            }
        }

        private void ExportSymbolFiles(string sourceFile)
        {
            string baseName = sourceFile.Split('.')[0];

            if (_scopeManager.GlobalScope.SymbolTable.Count > 0)
            {
                string symbPath = string.Format("{0}.symb", baseName);
                File.WriteAllText(symbPath, JsonConvert.SerializeObject(_scopeManager.GlobalScope.SymbolTable));
            }

            if (_scopeManager.GlobalScope.UnsolvedSymbols.Count > 0)
            {
                string unsolvedPath = string.Format("{0}.Unsolved", baseName);
                File.WriteAllText(unsolvedPath, JsonConvert.SerializeObject(_scopeManager.GlobalScope.UnsolvedSymbols));
            }

            if (_scopeManager.UnsolvedExprList.Count > 0)
            {
                string exprPath = string.Format("{0}.UnsolvedExpr", baseName);
                File.WriteAllText(exprPath, JsonConvert.SerializeObject(_scopeManager.UnsolvedExprList));
            }
        }

        private class SourceFileState
        {
            public string FilePath { get; private set; }
            public int CurrentLineNumber { get; set; }
            private readonly StreamReader _reader;

            public SourceFileState(string filePath)
            {
                FilePath = filePath;
                CurrentLineNumber = 0;
                _reader = new StreamReader(filePath);
            }

            public string ReadLine()
            {
                return _reader.ReadLine();
            }

            public void Close()
            {
                _reader.Close();
            }
        }
    }
}
