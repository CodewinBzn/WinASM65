using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.Output;
using WinASM65.Projects;
using WinASM65.Segments;
using WinASM65.Targets;
using WinASM65.Tooling.Dap;
using WinASM65.Tooling.Lsp;
using static WinASM65.Tooling.Mcp.McpTool;

namespace WinASM65.Tooling.Mcp
{
    /// <summary>
    /// The assembler as tools: build a project, assemble a fragment, read a
    /// listing, find symbols, look an instruction up, list the systems.
    /// </summary>
    public static class CodeTools
    {
        public static IEnumerable<McpTool> All()
        {
            yield return new McpTool("build_project", "Build a project",
                "Builds a WinASM65 project (its config.json): assembles every unit, combines the image, writes it to "
                + "Output.ObjectFile. Returns success, the image path and size, the target system and every diagnostic as file:line: message.",
                McpEffect.Writes,
                Schema(Arg.Required_("config", "string", "Path of the project's config.json")),
                BuildProject);

            yield return new McpTool("assemble", "Assemble 6502 source",
                "Assembles a fragment of 6502 source text (no project needed) and returns its listing (address, bytes, line) "
                + "and diagnostics. Useful to check syntax or see the bytes and size of some code. Includes with relative paths "
                + "resolve against \"directory\".",
                McpEffect.ReadOnly,
                Schema(Arg.Required_("source", "string", "The source text"),
                    Arg.Optional("cpu", "string", "6502 (default), 6502x, 65c02, r65c02 or w65c02"),
                    Arg.Optional("directory", "string", "Directory for .include/.incbin (default: current directory)")),
                Assemble);

            yield return new McpTool("get_listing", "Listing of a source file",
                "Builds the project and returns the listing of one of its source files (a unit or an included file): one line "
                + "per source line with its address and bytes. Optionally only a range of lines.",
                McpEffect.ReadOnly,
                Schema(Arg.Required_("config", "string", "Path of the project's config.json"),
                    Arg.Required_("file", "string", "The source file (absolute or relative to config.json)"),
                    Arg.Optional("fromLine", "integer", "First line (1-based)"),
                    Arg.Optional("toLine", "integer", "Last line")),
                GetListing);

            yield return new McpTool("find_symbol", "Find symbols",
                "Builds the project and returns its labels and constants whose name matches a pattern (a regular expression, "
                + "case-insensitive), with their values in hex and decimal and the unit defining them.",
                McpEffect.ReadOnly,
                Schema(Arg.Required_("config", "string", "Path of the project's config.json"),
                    Arg.Optional("pattern", "string", "Regular expression on the name (default: all)")),
                FindSymbol);

            yield return new McpTool("describe_instruction", "Describe an instruction",
                "Explains a 6502 mnemonic: what it does, the flags it changes, and every addressing mode with its syntax, "
                + "opcode, length and cycle count, for the given CPU.",
                McpEffect.ReadOnly,
                Schema(Arg.Required_("mnemonic", "string", "The mnemonic, such as LDA or BBR3"),
                    Arg.Optional("cpu", "string", "6502 (default), 6502x, 65c02, r65c02 or w65c02")),
                DescribeInstruction);

            yield return new McpTool("list_systems", "List target systems",
                "Lists the target systems a project can name in Target.System, the CPUs and the output formats.",
                McpEffect.ReadOnly,
                Schema(),
                ListSystems);

            yield return new McpTool("get_config_schema", "config.json reference",
                "Describes the keys of a project's config.json (Target, Input, Output, Emulator) with an example.",
                McpEffect.ReadOnly,
                Schema(),
                _ => McpResult.Ok(ConfigReference));
        }

        // ---------------------------------------------------------------- build

        internal static string ConfigPath(JsonObject arguments)
        {
            string config = Path.GetFullPath(Text(arguments, "config"));
            if (Directory.Exists(config))
                config = Path.Combine(config, "config.json");
            if (!File.Exists(config))
                throw new McpArgumentException("no config.json at " + config);
            return config;
        }

        private static McpResult BuildProject(JsonObject arguments)
        {
            string config = ConfigPath(arguments);
            ProjectBuildResult result = ProjectSession.Open(config).Build(new ProjectBuildOptions { WriteImage = true });
            StringBuilder text = new StringBuilder();
            text.AppendLine(result.Success ? "Build succeeded." : "Build failed.");
            if (result.Success)
            {
                text.AppendLine("Image: " + result.ImagePath + " (" + result.Image.Length + " bytes)");
                if (result.Target != null)
                    text.AppendLine("Target: system " + (result.Target.SystemId ?? "raw") + ", cpu " + result.Target.CpuName
                        + ", format " + result.Target.FormatName);
                foreach (ProjectUnitResult unit in result.Units)
                    text.AppendLine("Unit: " + unit.SourceFile + " -> " + unit.ObjectFile);
            }
            AppendDiagnostics(text, result.Diagnostics, config);
            return result.Success ? McpResult.Ok(text.ToString()) : McpResult.Fail(text.ToString());
        }

        private static void AppendDiagnostics(StringBuilder text, IReadOnlyList<Diagnostic> diagnostics, string config)
        {
            if (diagnostics.Count == 0)
                return;
            text.AppendLine("Diagnostics:");
            foreach (Diagnostic diagnostic in diagnostics)
            {
                string severity = diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning";
                text.AppendLine("  " + severity + ": " + DebugSession.FormatDiagnostic(diagnostic, config));
            }
        }

        // ---------------------------------------------------------------- assemble

        private static McpResult Assemble(JsonObject arguments)
        {
            string source = Text(arguments, "source");
            string cpu = Text(arguments, "cpu", false);
            string directory = Text(arguments, "directory", false) ?? Directory.GetCurrentDirectory();
            ConfigFile configuration = new ConfigFile();
            if (!string.IsNullOrWhiteSpace(cpu))
            {
                CpuFactory.Create(cpu); // refuses an unknown CPU with its list
                configuration.Target = new TargetConf { Cpu = cpu };
            }
            SourceListing listing = ProjectSession.Open(configuration, directory)
                .ListSourceText(source, Path.Combine(directory, "fragment.asm"));

            StringBuilder text = new StringBuilder();
            text.AppendLine(listing.Assembly.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
                ? "Assembly failed." : "Assembled " + listing.Rows.Sum(r => r.Bytes.Count) + " byte(s).");
            AppendListing(text, listing.Rows, null, null);
            AppendDiagnostics(text, listing.Assembly.Diagnostics, "fragment.asm");
            return McpResult.Ok(text.ToString());
        }

        private static void AppendListing(StringBuilder text, IReadOnlyList<ListingRow> rows, int? from, int? to)
        {
            bool shown = false;
            foreach (ListingRow row in rows)
            {
                if (row.LineNumber == 0 && row.Text == null)
                {
                    // A continuation row: the rest of a long .byte line, shown with its line.
                    if (shown && row.Address.HasValue && row.Bytes.Count > 0)
                        text.AppendLine("      $" + row.Address.Value.ToString("X4") + "  " + Hex(row.Bytes));
                    continue;
                }
                shown = !((from.HasValue && row.LineNumber < from.Value) || (to.HasValue && row.LineNumber > to.Value));
                if (!shown)
                    continue;
                string address = row.Address.HasValue ? "$" + row.Address.Value.ToString("X4") : "     ";
                text.AppendLine(row.LineNumber.ToString(CultureInfo.InvariantCulture).PadLeft(5) + " " + address + "  "
                    + Hex(row.Bytes).PadRight(12) + " " + (row.Text ?? string.Empty));
            }
        }

        private static string Hex(IReadOnlyList<byte> bytes)
        {
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }

        // ---------------------------------------------------------------- listing and symbols

        private static McpResult GetListing(JsonObject arguments)
        {
            string config = ConfigPath(arguments);
            string file = Text(arguments, "file");
            if (!Path.IsPathRooted(file))
                file = Path.Combine(Path.GetDirectoryName(config), file);
            file = Path.GetFullPath(file);

            ProjectBuildResult result = ProjectSession.Open(config).Build(new ProjectBuildOptions { CaptureListing = true });
            List<ListingRow> rows = new List<ListingRow>();
            foreach (ProjectUnitResult unit in result.Units)
            {
                bool inFile = false;
                foreach (ListingRow row in unit.Listing)
                {
                    if (row.LineNumber != 0 || row.Text != null)
                        inFile = SamePath(SourceOf(row, unit), file);
                    if (inFile)
                        rows.Add(row);
                }
            }

            StringBuilder text = new StringBuilder();
            if (rows.Count == 0)
            {
                text.AppendLine(result.Success
                    ? file + " is not part of the project's build."
                    : "The build failed before " + file + " could be listed.");
                AppendDiagnostics(text, result.Diagnostics, config);
                return McpResult.Fail(text.ToString());
            }
            text.AppendLine("Listing of " + file + " (line, address, bytes, source):");
            AppendListing(text, rows, Number(arguments, "fromLine", false), Number(arguments, "toLine", false));
            return McpResult.Ok(text.ToString());
        }

        private static string SourceOf(ListingRow row, ProjectUnitResult unit)
        {
            string name = string.IsNullOrEmpty(row.SourceName) ? unit.SourceFile : row.SourceName;
            return Path.IsPathRooted(name) ? name : Path.Combine(Path.GetDirectoryName(unit.SourceFile) ?? string.Empty, name);
        }

        private static bool SamePath(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }

        private static McpResult FindSymbol(JsonObject arguments)
        {
            string config = ConfigPath(arguments);
            string pattern = Text(arguments, "pattern", false) ?? ".*";
            Regex regex;
            try
            {
                regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException ex)
            {
                throw new McpArgumentException("pattern is not a regular expression: " + ex.Message);
            }

            ProjectBuildResult result = ProjectSession.Open(config).Build(new ProjectBuildOptions { CaptureListing = true });
            StringBuilder text = new StringBuilder();
            int count = 0;
            foreach (ProjectUnitResult unit in result.Units)
            {
                foreach (KeyValuePair<string, long> symbol in unit.Symbols.OrderBy(s => s.Value).ThenBy(s => s.Key))
                {
                    if (!regex.IsMatch(symbol.Key))
                        continue;
                    text.AppendLine(symbol.Key + " = $" + symbol.Value.ToString(symbol.Value > 0xFF ? "X4" : "X2")
                        + " (" + symbol.Value.ToString(CultureInfo.InvariantCulture) + ")  in " + Path.GetFileName(unit.SourceFile));
                    if (++count >= 500)
                    {
                        text.AppendLine("… more than 500 matches; narrow the pattern.");
                        return McpResult.Ok(text.ToString());
                    }
                }
            }
            if (count == 0)
            {
                text.AppendLine("No symbol matches " + pattern + ".");
                AppendDiagnostics(text, result.Diagnostics, config);
            }
            return McpResult.Ok(text.ToString());
        }

        // ---------------------------------------------------------------- reference

        private static McpResult DescribeInstruction(JsonObject arguments)
        {
            string mnemonic = Text(arguments, "mnemonic").Trim().ToUpperInvariant();
            ICpuInstructionSet cpu = CpuFactory.Create(Text(arguments, "cpu", false) ?? "6502");
            if (!InstructionDocs.IsMnemonic(mnemonic, cpu))
                return McpResult.Fail(mnemonic + " is not an instruction of this CPU. Its mnemonics: "
                    + string.Join(" ", InstructionDocs.MnemonicsFor(cpu)));
            return McpResult.Ok(HoverCard.Instruction(mnemonic, mnemonic, null, InstructionDocs.ForMnemonic(mnemonic, cpu), null));
        }

        private static McpResult ListSystems(JsonObject arguments)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("Systems (Target.System): " + string.Join(", ", SystemCatalog.Names));
            text.AppendLine("Systems with a full definition for the debugger (memories, boot): "
                + string.Join(", ", WinASM65.Systems.SystemRegistry.DefinedIds) + "; the others debug as a generic 6502.");
            text.AppendLine("CPUs (Target.Cpu): " + string.Join(", ", CpuFactory.Names));
            text.AppendLine("Emulator adapters (Emulator.Name): " + string.Join(", ", WinASM65.Monitor.Emulators.EmulatorAdapters.Names)
                + ", or any name for a custom emulator started with Emulator.Arguments.");
            return McpResult.Ok(text.ToString());
        }

        private const string ConfigReference =
@"A WinASM65 project is a config.json next to its sources. Paths are relative to it.

{
  ""Target"": { ""System"": ""nes"", ""Cpu"": ""6502"", ""Format"": ""bin"", ""LoadAddress"": ""$C000"" },
  ""Input"": [
    { ""FileName"": ""src/header.asm"", ""Dependencies"": [] },
    { ""FileName"": ""src/game.asm"", ""Dependencies"": [] },
    { ""FileName"": ""src/vectors.asm"", ""Dependencies"": [""src/game.asm""] }
  ],
  ""Output"": {
    ""ObjectFile"": ""game.nes"",
    ""Files"": [
      { ""FileName"": ""src/header.o"", ""Size"": ""$10"" },
      { ""FileName"": ""src/game.o"", ""Size"": ""$3FFA"" },
      { ""FileName"": ""src/vectors.o"" },
      { ""FileName"": ""chr/tiles.chr"" }
    ]
  },
  ""Emulator"": { ""Name"": ""mesen2"", ""Path"": ""C:\\Mesen2\\Mesen.exe"" }
}

Target (optional): System (a system id, see list_systems), Cpu, Format (output format), LoadAddress/RunAddress,
  RomSize, DefineHardwareSymbols, Ines {PrgBanks, ChrBanks, Mapper, Mirroring, Battery}, Hardware, Regions.
Input: the units, each assembled to its .o (or OutputFile); Dependencies are units whose .symb resolve this
  unit's undefined names (not transitive).
Output: ObjectFile is the image; Files are concatenated in order, each padded to Size (always hexadecimal) when given.
Emulator (for debugging): Path (required), Name (mesen2, mesence or a custom name), Bridge (a bridge script),
  Arguments (custom command line with {rom}, {script}, {port}), Port.
The full reference is docs/site/segments.html#config; the emulator section is docs/site/bridges.html.";
    }
}
