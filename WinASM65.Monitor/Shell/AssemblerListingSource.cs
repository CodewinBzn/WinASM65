using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.Output;
using WinASM65.TextFormat;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// The listing seam wired to the library.
    ///
    /// It is the only class in the shell that knows <c>SourceListingService</c>,
    /// <c>InstructionDocs</c> and the highlighting lexer exist. Everything above it —
    /// the pane, the window, the keys — speaks the seam's own types, so the three
    /// library types can move again without a second translation appearing.
    ///
    /// Three things it deliberately refuses to do:
    ///
    /// 1. Invent. A cycle count that the documentation does not carry stays null and
    ///    the cycle column is blank. There is no cycle table anywhere in this project
    ///    yet, so a number written here would be a second source of truth with
    ///    nothing to check it against.
    /// 2. Guess. Whether it can list at all is read from the CPU it was handed, and
    ///    a row's cycles come from the documented form whose opcode is the row's own
    ///    first byte, matched against the mnemonic the lexer found in the source. A
    ///    data directive that happens to start with an opcode byte therefore gets no
    ///    cycle count, because it is not an instruction.
    /// 3. Fabricate. An assembly error, a missing file, an unreadable directory: each
    ///    produces the reason and no rows at all. The alternative — a pane full of
    ///    rows that were never assembled — looks exactly like a working monitor.
    ///
    /// Nothing is cached. Every call to <see cref="Rows"/> assembles again, which is
    /// what makes F5's refresh honest: the listing on screen cannot be one assembly
    /// behind the source, because there is no earlier one to be behind.
    /// </summary>
    public sealed class AssemblerListingSource : IListingSource
    {
        /// <summary>
        /// Why no listing is possible without a CPU. Names what is missing and where
        /// it would have come from, so the pane's text is a statement about this build
        /// rather than about the user's source.
        /// </summary>
        public const string NoCpuReason =
            "listing unavailable: this build has no CPU to list for. What a listing"
            + " reports — which words are mnemonics, how long each form is, and how many"
            + " cycles it takes — is read from the opcode table of the CPU the session is"
            + " attached to, and there is none here.";

        private static readonly ListingRow[] NoRows = new ListingRow[0];

        private readonly ICpuInstructionSet _cpu;
        private readonly ISourceListingService _service;
        private readonly AssemblyLexer _lexer;
        private readonly Dictionary<byte, InstructionDocumentation> _byOpcode;
        private readonly string _directory;

        private string _problem;

        /// <summary>
        /// Lists for <paramref name="cpu"/>, resolving relative source paths against
        /// the process's current directory.
        /// </summary>
        public AssemblerListingSource(ICpuInstructionSet cpu)
            : this(cpu, null, null)
        {
        }

        /// <summary>
        /// Lists for <paramref name="cpu"/>, resolving relative source paths against
        /// <paramref name="directory"/> — the same directory the session was given, so
        /// the listing of a file the tree offers cannot depend on where the process
        /// happens to have been started.
        /// </summary>
        public AssemblerListingSource(ICpuInstructionSet cpu, string directory)
            : this(cpu, directory, null)
        {
        }

        /// <summary>
        /// As above, with the listing service supplied. The seam takes the interface,
        /// not the concrete class, so a test can hand over a source that fails on
        /// purpose instead of arranging a file that does.
        /// </summary>
        public AssemblerListingSource(ICpuInstructionSet cpu, string directory, ISourceListingService service)
        {
            _cpu = cpu;
            _directory = string.IsNullOrEmpty(directory) ? Directory.GetCurrentDirectory() : directory;
            _service = service ?? new SourceListingService();
            _lexer = new AssemblyLexer(cpu);
            _byOpcode = OpcodesFor(cpu);
        }

        /// <summary>
        /// Whether it can list at all, which is decided by the CPU and by nothing else.
        /// False is a statement about this object, not a guess about the source.
        /// </summary>
        public bool IsAvailable
        {
            get { return _cpu != null; }
        }

        /// <summary>
        /// Why there are no rows right now: the missing CPU, or the reason the last
        /// listing failed. Empty when the last request produced rows, because then
        /// there is nothing to explain.
        /// </summary>
        public string UnavailableReason
        {
            get { return _cpu == null ? NoCpuReason : (_problem ?? string.Empty); }
        }

        /// <summary>
        /// The reason the last <see cref="Rows"/> call produced no rows, or null when it
        /// produced some. The same failure a pane would show, kept as a value so a
        /// caller can report it in a message line without re-parsing the pane's text.
        /// </summary>
        public string LastProblem
        {
            get { return _problem; }
        }

        /// <summary>
        /// The rows for one source, or none, and the reason in
        /// <see cref="LastProblem"/>.
        ///
        /// Never throws. A pane must not be able to take the shell down, and the only
        /// failures here — a path that does not exist, a directory the process cannot
        /// read, a source that does not assemble — are all ordinary answers.
        /// </summary>
        public IReadOnlyList<ListingRow> Rows(ListingRequest request)
        {
            _problem = null;

            if (!IsAvailable)
            {
                _problem = NoCpuReason;
                return NoRows;
            }

            if (request == null || string.IsNullOrEmpty(request.SourceFile))
            {
                _problem = "no source file was named, so there is nothing to list.";
                return NoRows;
            }

            string path = Resolve(request.SourceFile);

            SourceListing listing;
            try
            {
                listing = _service.ListSourceFile(path);
            }
            catch (IOException ex)
            {
                _problem = "cannot read " + path + ": " + ex.Message;
                return NoRows;
            }
            catch (UnauthorizedAccessException ex)
            {
                _problem = "cannot read " + path + ": " + ex.Message;
                return NoRows;
            }

            if (listing == null)
            {
                _problem = "the listing service answered nothing for " + path + ".";
                return NoRows;
            }

            if (!listing.Success)
            {
                _problem = Describe(listing, path);
                return NoRows;
            }

            return Translate(listing, request.MaxRows);
        }

        /// <summary>
        /// Copies the library's rows into the shell's, in display order and under the
        /// ceiling the request asked for.
        /// </summary>
        private List<ListingRow> Translate(SourceListing listing, int maxRows)
        {
            List<ListingRow> rows = new List<ListingRow>();

            foreach (Output.ListingRow row in listing.Rows)
            {
                // The ceiling is honoured here rather than by asking the assembler to
                // stop: the assembler has no such idea, and truncating after the fact
                // is what keeps a long file from costing more than one screen of
                // rows. A non-positive ceiling means no ceiling, which is the only
                // reading under which a caller that does not care is not punished.
                if (maxRows > 0 && rows.Count >= maxRows)
                    break;

                rows.Add(Translate(row));
            }

            return rows;
        }

        private ListingRow Translate(Output.ListingRow row)
        {
            List<ListingToken> tokens = Lex(row.Text);
            string mnemonic = MnemonicOf(tokens);

            ListingRow translated = new ListingRow
            {
                LineNumber = row.LineNumber,

                // The shell's absent address is -1 and the library's is null. Both say
                // "this line emits nothing at no address"; a comment line must never be
                // shown as $0000, which is a real address.
                Address = row.Address.HasValue ? row.Address.Value : -1,
                Bytes = new List<byte>(row.Bytes),
                Cycles = CyclesOf(row, mnemonic),
                Source = row.Text ?? string.Empty,
            };

            translated.SetTokens(tokens);
            return translated;
        }

        /// <summary>
        /// The documented cycle count for a row, or null.
        ///
        /// Read through the opcode the row actually emitted rather than through a
        /// parsing of the source, so the number cannot describe an instruction the
        /// assembler did not produce. Three checks, all of which have to pass:
        ///
        /// <list type="bullet">
        /// <item>the row is an instruction that emitted a byte,</item>
        /// <item>that byte is a documented opcode whose documented length is exactly
        /// how many bytes the row carries, and</item>
        /// <item>the mnemonic the lexer read in the source is the documented one.</item>
        /// </list>
        ///
        /// The length check is what keeps a data directive out: <c>.byte $A9</c> emits
        /// one byte that is a documented <c>LDA</c> immediate opcode, but that form is
        /// two bytes long, so the row is data and gets nothing.
        ///
        /// <see cref="InstructionDocumentation.Cycles"/> is null for every form today —
        /// the project has no cycle table — and that null is passed straight through.
        /// The column is empty because the truth is empty.
        /// </summary>
        private int? CyclesOf(Output.ListingRow row, string mnemonic)
        {
            if (row.Kind != ListingRowKind.Instruction || row.Bytes.Count == 0)
                return null;

            if (mnemonic == null)
                return null;

            InstructionDocumentation documentation;
            if (!_byOpcode.TryGetValue(row.Bytes[0], out documentation))
                return null;

            if (documentation.Length != row.Bytes.Count)
                return null;

            if (!string.Equals(documentation.Mnemonic, mnemonic, StringComparison.OrdinalIgnoreCase))
                return null;

            return documentation.Cycles;
        }

        /// <summary>
        /// The role-tagged runs of one source line.
        ///
        /// A continuation row has bytes and no source of its own; it gets no tokens,
        /// which is why the pane draws it as a row of bytes and nothing else rather
        /// than repeating the line above it.
        /// </summary>
        private List<ListingToken> Lex(string text)
        {
            List<ListingToken> tokens = new List<ListingToken>();
            if (string.IsNullOrEmpty(text))
                return tokens;

            foreach (SourceToken token in _lexer.TokenizeLine(text))
            {
                ListingToken run = new ListingToken(token.Text, RoleFor(token.Kind));
                run.Start = token.Start;
                tokens.Add(run);
            }

            return tokens;
        }

        /// <summary>
        /// Which palette role a lexer token is painted with.
        ///
        /// The lexer's kinds are a classification, the palette's are a set of colours,
        /// and neither has a reason to know the other. This table is the whole
        /// translation, and it names existing <see cref="ThemeRole"/> constants rather
        /// than introducing a second vocabulary the palette would then have to carry.
        ///
        /// Three kinds share roles with their neighbours, because the palette has no
        /// role of their own to share:
        ///
        /// <list type="bullet">
        /// <item><c>Keyword</c> is a directive or an expression keyword such as
        /// <c>TRUE</c>; both are written by the assembler, so both are directives.</item>
        /// <item><c>Operator</c> is punctuation and arithmetic, which is operand text —
        /// the same role as the values it joins.</item>
        /// <item><c>String</c> is a quoted value, which is likewise an operand. It is
        /// deliberately not a comment: <c>.byte "AB"</c> emits two bytes, and colouring
        /// them as commentary would say they were not there.</item>
        /// </list>
        ///
        /// <c>Plain</c> is whatever the assembler dispatches nothing for, and is
        /// ordinary body text.
        /// </summary>
        public static string RoleFor(SourceTokenKind kind)
        {
            switch (kind)
            {
                case SourceTokenKind.Mnemonic:
                    return ThemeRole.Mnemonic;
                case SourceTokenKind.Keyword:
                    return ThemeRole.Directive;
                case SourceTokenKind.Number:
                    return ThemeRole.Number;
                case SourceTokenKind.Label:
                    return ThemeRole.Label;
                case SourceTokenKind.Comment:
                    return ThemeRole.Comment;
                case SourceTokenKind.Operator:
                case SourceTokenKind.String:
                    return ThemeRole.Operand;
                default:
                    return ThemeRole.Default;
            }
        }

        /// <summary>
        /// The first mnemonic in a row's tokens, or null when the row names none — a
        /// directive, a label, a comment. Used only to check a documented opcode
        /// against the source it came from.
        /// </summary>
        private static string MnemonicOf(IList<ListingToken> tokens)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Role == ThemeRole.Mnemonic)
                    return tokens[i].Text;
            }

            return null;
        }

        /// <summary>
        /// Every documented form, keyed by the opcode that encodes it. One pass over
        /// the CPU's own table, which the library already caches per CPU instance, so
        /// this costs nothing after the first call.
        /// </summary>
        private static Dictionary<byte, InstructionDocumentation> OpcodesFor(ICpuInstructionSet cpu)
        {
            Dictionary<byte, InstructionDocumentation> byOpcode =
                new Dictionary<byte, InstructionDocumentation>();

            if (cpu == null)
                return byOpcode;

            foreach (InstructionDocumentation documentation in InstructionDocs.ForCpu(cpu))
            {
                // First form wins. Two forms sharing an opcode would mean the CPU's own
                // table is ambiguous, and taking the first is no worse than refusing
                // to show the row at all.
                if (!byOpcode.ContainsKey(documentation.Opcode))
                    byOpcode.Add(documentation.Opcode, documentation);
            }

            return byOpcode;
        }

        /// <summary>
        /// The assembler's own complaint, as one sentence.
        ///
        /// The diagnostics are named rather than summarised, because "assembly failed"
        /// tells a user nothing they did not already know and "line 7: Syntax Error"
        /// is the whole answer. The file is shown by name: the shell knows the path it
        /// asked about, and a diagnostic that repeats the absolute path of every line
        /// of a 400-line failure is noise.
        /// </summary>
        private static string Describe(SourceListing listing, string path)
        {
            string name = Path.GetFileName(path);

            if (listing.Assembly == null || listing.Assembly.Diagnostics.Count == 0)
                return "the assembly of " + name + " failed without naming a reason.";

            List<string> problems = new List<string>();
            foreach (Diagnostic diagnostic in listing.Assembly.Diagnostics)
            {
                if (diagnostic == null || diagnostic.Severity != DiagnosticSeverity.Error)
                    continue;

                int line = diagnostic.Location.LineNumber;
                problems.Add(line > 0
                    ? name + ":" + line.ToString(CultureInfo.InvariantCulture) + ": " + diagnostic.Message
                    : name + ": " + diagnostic.Message);
            }

            if (problems.Count == 0)
                return "the assembly of " + name + " failed without naming a reason.";

            return string.Join(Environment.NewLine, problems.ToArray());
        }

        private string Resolve(string sourceFile)
        {
            return Path.IsPathRooted(sourceFile) ? sourceFile : Path.Combine(_directory, sourceFile);
        }
    }
}
