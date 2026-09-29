# Make WinASM65 Generic — Target System Integration

## Goal

Wire the existing but disconnected target-system infrastructure into the CLI and assembly pipeline so the assembler can produce executables for all known 6502 systems defined in `SystemCatalog` (nes, c64, vic20, apple2, atari8, bbc, etc.).

## Background

The codebase already contains all the building blocks, but they are never invoked:

| Component | Location | Status |
|---|---|---|
| `SystemCatalog` | `src/Targets/SystemCatalog.cs` | 17+ systems defined, never called from CLI |
| `TargetResolver` | `src/Targets/TargetResolver.cs` | Resolves `ResolvedTarget`, **discards** `CpuFactory.Create()` result |
| `ExecutablePublisher` | `src/Targets/ExecutablePublisher.cs` | 6 formats registered, `Publish()` never called |
| `IExecutableFormat` impls | `src/Targets/*.cs` | bin, ines, prg, xex, a2bin, rom — all complete |
| `CpuFactory` | `src/Cpu/CpuFactory.cs` | Creates Cpu6502/Cpu65C02 — works, not used by CLI |
| `AssemblerOptions` | `src/Core/AssemblerFactory.cs` | Has `Cpu` + `PredefinedSymbols` fields — never populated by CLI |
| `TargetConf` | `src/Segments/SegmentModels.cs` | Config `Target` section — parsed but ignored |

## Decisions

1. **Target selection**: CLI `-t <system>` flag (with `-t list` to enumerate) **and** config `Target.System` field. CLI overrides config. No target → default `raw` (CPU=6502, format=bin, no hardware symbols).
2. **Single-file publishing**: When a target is specified, after assembly the raw bytes (`AssemblyResult.OutputBytes`) are handed to `ExecutablePublisher.Publish()` to produce the formatted executable.
3. **Multi-segment publishing**: When a target is specified, after `BinaryCombiner.Concatenate` produces combined bytes, `ExecutablePublisher.Publish()` produces the final executable.
4. **Locatable code**: The actual assembly origin address (`.org` directive result, from `AssemblyResult.OriginAddress`) must be threaded to the publisher so executable formats like PRG, XEX, and A2BIN encode the correct load address. If `.org` is not specified, the target's `LoadAddress` is used as the default origin so code assembles at the correct address.
5. **Portable formats**: Add O65, Intel HEX, and Motorola S-record as new `IExecutableFormat` implementations — all are simple wrappers around assembled bytes with format-specific headers/metadata. Full relocatable O65 with relocation records is a phase-2 effort.

## Files Changed

| File | Change |
|---|---|
| `src/CommandLineApplication.cs` | CLI flags `-t`/`-target`/`-cpu`/`-format`; target resolution; target-aware assembler creation; `ExecutablePublisher` injection; locatable origin threading |
| `src/Program.cs` | Pass `ExecutablePublisher` to constructor |
| `src/Targets/TargetResolver.cs` | Remove discarded `CpuFactory.Create()` call; add `cliFormat` parameter to `Resolve` |
| `src/Targets/ResolvedTarget.cs` | Add `OriginAddress` property, update `Clone()` |
| `src/Targets/CommodorePrgFormat.cs` | Use `OriginAddress ?? LoadAddress` for PRG load address |
| `src/Targets/Apple2BinaryFormat.cs` | Use `OriginAddress ?? LoadAddress` for A2BIN load address |
| `src/Targets/AtariXexFormat.cs` | Use `OriginAddress ?? LoadAddress` for XEX start address |
| `src/Core/AssemblerOptions.cs` | Add `DefaultOrigin` property |
| `src/Core/AssemblerFactory.cs` | Pass `DefaultOrigin` to `AssemblerEngine` |
| `src/Core/AssemblerEngine.cs` | Apply `DefaultOrigin` after `Reset()` |
| `src/Targets/O65Format.cs` | **New file** — O65 object format writer |
| `src/Targets/IntelHexFormat.cs` | **New file** — Intel HEX format writer |
| `src/Targets/MotorolaSrecFormat.cs` | **New file** — Motorola S-record format writer |
| `src/Targets/ExecutablePublisher.cs` | Register 3 new formats |
| `WinASM65.Tests/TargetTests.cs` | **New file** — unit tests |
| `WinASM65.Tests/WinASM65.Tests.csproj` | Add `TargetTests.cs` to compile includes |

Files **not changed**: `RawBinaryFormat`, `InesFormat`, `PaddedRomFormat`, `MultiSegmentOrchestrator.cs`, `CpuFactory.cs`, `README.md`.

## Implementation Steps

### Step 1 — `CommandLineApplication.cs` (CLI wiring + target orchestration)

**New imports**: `using WinASM65.Targets;`, `using WinASM65.Cpu;`

**New field**: `private readonly IExecutablePublisher _executablePublisher;`

**Constructor**: Add 5th parameter `IExecutablePublisher executablePublisher` with null-validation.

**Run method — new local variables** (alongside existing `sourceFile`, `objectFile`, `config`, `enableListing`):
```csharp
string cliSystem = null;
string cliCpu = null;
string cliFormat = null;
```

**Run method — arg parsing** (add inside the `for` loop):
```csharp
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

**Run method — after arg parsing, before assembly**:
- If `cliSystem == "list"`: `_console.WriteLine(SystemCatalog.Describe()); return 0;`
- Resolve target: `ResolvedTarget target = TargetResolver.Resolve(config?.Target, cliSystem, cliCpu, cliFormat);`
  - `cliFormat` is a new parameter — if set, overrides `target.FormatName`. If null, format comes from catalog preset or config.
  - Wrap in try/catch for `ArgumentException` (unknown system/CPU) → `ReportConfigurationError(ex.Message); return 1;`
- Create CPU: `ICpuInstructionSet cpu = CpuFactory.Create(target.CpuName);` (in try/catch)
- Build predefined symbols: `IDictionary<string, long> symbols = target.DefineHardwareSymbols ? target.HardwareSymbols : null;`

**Single-file path** (when `sourceFile` is set) — update with target-aware assembler + locatable origin:
1. `AssemblerOptions options = new AssemblerOptions { EnableListing = enableListing, Cpu = cpu, PredefinedSymbols = symbols, DefaultOrigin = target.LoadAddress };`

   `DefaultOrigin` ensures code without `.org` assembles at the target's expected load address.
2. `AssemblyResult assembly = _assemblerFactory.Create(options).Assemble(sourceFile, objectFile);`
3. If `!assembly.Success` → display diagnostics, return 1.
4. If `target.FormatName != "bin"`:
   - `target.OriginAddress = assembly.OriginAddress;`
   - `OperationResult pub = _executablePublisher.Publish(objectFile, assembly.OutputBytes, target);`
   - If `!pub.Success` → display diagnostics, return 1.
5. If `target.FormatName == "bin"` → behavior unchanged (raw `.o` already saved by `Assemble`).

**Multi-segment path** (when `config.Input` is set) — update with target-aware factory:
1. `Func<IAssembler> asmFactory = () => _assemblerFactory.Create(new AssemblerOptions { Cpu = cpu, PredefinedSymbols = symbols, EnableListing = enableListing, DefaultOrigin = target.LoadAddress });`
2. `MultiSegmentResult segments = new MultiSegmentOrchestrator(asmFactory).AssembleSegments(config.Input);`
3. If `!segments.Success` → display diagnostics, return 1.

**Combine/publish path** (when `config.Output` is set):
- If `target.FormatName != "bin"`:
  1. `byte[] payload; OperationResult concat = _binaryCombiner.Concatenate(config.Output, out payload);`
  2. If `!concat.Success` → display diagnostics, return 1.
  3. `OperationResult pub = _executablePublisher.Publish(config.Output.ObjectFile, payload, target);`
  4. If `!pub.Success` → display diagnostics, return 1.
- If `target.FormatName == "bin"`: `_binaryCombiner.Combine(config.Output)` (existing behavior, unchanged).

**Update `DisplayHelp`**:
```
WinASM65 {version}
Usage: WinASM65 [-f source] [-o object] [-t system] [-cpu cpu] [-format fmt] [-l] [-c config] [-h|-help]
  -t <system>   Target system (nes, c64, c128, vic20, apple2, apple2e, atari8, atari800, atari2600, bbc, bbcmicro, electron, oric, x16, lynx). Use 'list' to enumerate all.
  -cpu <cpu>    CPU override (6502 or 65c02). Defaults to target system CPU.
  -format <fmt> Output format override (bin, nes, ines, prg, xex, a2bin, rom, o65, ihex, srec). Defaults to target system format.
```

### Step 2 — `Program.cs` (composition root)

Add `new ExecutablePublisher()` as 5th argument to `CommandLineApplication` constructor.

### Step 3 — `TargetResolver.cs` (remove misleading call + add cliFormat parameter)

1. Remove line 46: `CpuFactory.Create(target.CpuName);` — the return value is discarded. The CLI (`CommandLineApplication`) creates the CPU from `target.CpuName` instead, with proper error handling.

2. Add `string cliFormat = null` parameter to `Resolve(TargetConf config, string cliSystem, string cliCpu, string cliFormat = null)`. After all other overrides (including config `Format`), if `cliFormat` is not null/whitespace, set `target.FormatName = cliFormat`. This makes CLI format override take highest precedence. Use `= null` default so existing callers (e.g., tests) don't need the 4th argument.

   **Note**: `TargetConf` and `ConfigFile.Target` already exist with all necessary properties (`System`, `Cpu`, `Format`, `LoadAddress`, `RunAddress`, `RomSize`, `DefineHardwareSymbols`, `Ines`). `TargetResolver.Resolve` already applies config overrides after catalog preset. Newtonsoft.Json deserializes the `Target` section automatically. No code changes needed to config classes — they're wired in via CLI (Step 1) and `TargetResolver` (Step 3).

### Step 4 — `ResolvedTarget.cs` (add origin address property)

Add `public ushort? OriginAddress { get; set; }` property (for the actual assembled origin, set by CLI after assembly). Update `Clone()` to copy it.

### Step 5 — `AssemblerOptions` + `AssemblerFactory.cs` (default origin support)

Add `public ushort? DefaultOrigin { get; set; }` to `AssemblerOptions`. Update `AssemblerFactory.Create(AssemblerOptions)` to pass `options.DefaultOrigin` to `AssemblerEngine` constructor.

### Step 6 — `AssemblerEngine.cs` (apply default origin)

- Add `ushort? defaultOrigin = null` constructor parameter, stored as `_defaultOrigin`.
- In `Assemble()`, after `Reset()` and before `ApplyPredefinedSymbols()`:
  ```csharp
  if (_defaultOrigin.HasValue)
  {
      _emitter.CurrentAddress = _defaultOrigin.Value;
      _emitter.OriginAddress = _defaultOrigin.Value;
  }
  ```
- If no `.org` directive is encountered, the emitter stays at the default origin. If `.org` is used, it overrides (existing behavior via `OrgDirectiveHandler`).

### Step 7 — Format implementations (3 files)

**`CommodorePrgFormat.cs`** (prg): Change load address to `target.OriginAddress ?? target.LoadAddress ?? (ushort)0x0801`.

**`Apple2BinaryFormat.cs`** (a2bin): Change load address to `target.OriginAddress ?? target.LoadAddress ?? (ushort)0x0800`.

**`AtariXexFormat.cs`** (xex): Change start address to `target.OriginAddress ?? target.LoadAddress ?? (ushort)0x0600`. Run address still uses `target.RunAddress`.

### Step 8 — Portable formats: `O65Format.cs` (new file)

Implement `IExecutableFormat` with `Name = "o65"`. Header structure:
- Bytes 0-2: magic `"o65"` (0x6F, 0x36, 0x35)
- Byte 3: version `0x00`
- Byte 4: address size `0x01` (16-bit, big-endian)
- Byte 5: OS/065 model `0x00`
- Byte 6: segment count `0x01`
- Bytes 7-8: segment 1 (text) load address — `target.OriginAddress ?? target.LoadAddress ?? 0`, big-endian
- Bytes 9-10: segment 1 size — `payload.Length`, big-endian
- Bytes 11+: segment data (payload bytes)
- No optional header, no relocations, no symbol table

### Step 9 — Portable formats: `IntelHexFormat.cs` (new file)

Implement `IExecutableFormat` with `Name = "ihex"`. Produces Intel HEX 32-bit records:
- Start record (type 0x03) with CS=0, IP=run address (`target.RunAddress ?? load`)
- Data records (type 0x00) — 16 bytes max per record, load address starts at `target.OriginAddress ?? target.LoadAddress`
- End of file record (type 0x01)

### Step 10 — Portable formats: `MotorolaSrecFormat.cs` (new file)

Implement `IExecutableFormat` with `Name = "srec"`. Produces Motorola S-record (S19) format:
- S0 header record (optional, can be empty with 16-bit ADDR in S0)
- S1 data records — 16 bytes max per record, load address = `target.OriginAddress ?? target.LoadAddress`
- S9 termination record with start address = `target.RunAddress ?? load`

### Step 11 — `ExecutablePublisher.cs` (register new formats)

Add to constructor:
```csharp
Register(new O65Format());
Register(new IntelHexFormat());
Register(new MotorolaSrecFormat());
```

## Locatable Code — Design Rationale

### Problem

The format implementations use `target.LoadAddress` (a static value from `SystemCatalog` or config) as the executable's load address. But this is the **expected** load address, not the **actual** assembly origin from `.org`. If the user writes `.org $A000`, the PRG header should contain `$A000`, not the catalog's default `$0801`.

Additionally, if no `.org` is specified, the assembler starts at address 0, which is wrong for systems that expect code at specific addresses (e.g., C64 at `$0801`).

### Solution

1. **Thread actual origin to publisher**: `target.OriginAddress` is set from `assembly.OriginAddress` after assembly. Formats prefer it over `LoadAddress`.
2. **Default origin from target**: `AssemblerOptions.DefaultOrigin` (set to `target.LoadAddress`) makes the assembler start at the correct address when no `.org` is specified.

### Example: `.org $0800` on C64

```
CLI: -t c64 -f src.asm -o game.prg
  src: .org $0800 / lda #$00 / rts
  → target.LoadAddress = 0x0801, DefaultOrigin = 0x0801
  → Assembler starts at 0x0801, .org sets origin to 0x0800
  → AssemblyResult.OriginAddress = 0x0800
  → target.OriginAddress = 0x0800
  → PRG load = 0x0800 (from OriginAddress, not LoadAddress)
  → PRG = [00, 08, A9, 00, 60]
```

### Example: No `.org` on C64

```
CLI: -t c64 -f src.asm -o game.prg
  src: lda #$00 / rts (no .org)
  → target.LoadAddress = 0x0801, DefaultOrigin = 0x0801
  → Assembler starts at 0x0801, stays (no .org)
  → AssemblyResult.OriginAddress = 0x0801
  → target.OriginAddress = 0x0801
  → PRG load = 0x0801 (OriginAddress == LoadAddress)
  → PRG = [01, 08, A9, 00, 60]
```

## Data Flow

```
CLI: -t nes -f src.asm -o game.nes
  → TargetResolver.Resolve(config?.Target, "nes", null, null)
      → SystemCatalog.TryGet("nes") → preset { CpuName="6502", FormatName="ines", LoadAddress=0x8000, HardwareSymbols={PPUCTRL:0x2000,...} }
      → Returns ResolvedTarget
  → CpuFactory.Create("6502") → Cpu6502 instance
  → AssemblerFactory.Create(AssemblerOptions {
      Cpu=Cpu6502, PredefinedSymbols=NES symbols, EnableListing, DefaultOrigin=0x8000 })
      → AssemblerEngine starts at 0x8000
  → AssemblerEngine.Assemble(src.asm, game.nes)
      → Hardware symbols pre-loaded, .org $8000 (matches default), code assembled
      → AssemblyResult { OriginAddress=0x8000, OutputBytes=[...], Success=true }
  → target.OriginAddress = 0x8000
  → ExecutablePublisher.Publish(game.nes, OutputBytes, target)
      → InesFormat.Write(game.nes, payload, target) → 16-byte iNES header + 16KB PRG + 8KB CHR
```

## Risks & Edge Cases

1. **iNES single-file without CHR data**: Default `InesPrgBanks=1, InesChrBanks=1` pads CHR with zeros. Acceptable for first implementation.
2. **Output path collision**: `Assemble` writes raw `.o` to `outputFile`; `Publish` overwrites the same path with formatted output. Intentional — `.o` is intermediate. Symbol files (`.symb`, `.lst`) are derived from source name, not output name, so no collision.
3. **Unknown system/CPU**: `TargetResolver.Resolve` and `CpuFactory.Create` both throw `ArgumentException`. CLI catches and reports cleanly.
4. **Backward compatibility**: No `-t` flag and no config `Target` → `ResolvedTarget` defaults to raw (CPU=6502, format=bin, DefaultOrigin=null). All existing behavior preserved.
5. **Config deserialization**: `ConfigFile.Target` (`TargetConf`) with public properties deserializes automatically via Newtonsoft.Json. Existing configs without `Target` section work unchanged.
6. **`-t list` without other args**: Should print catalog and exit immediately (exit code 0).
7. **Multi-segment origin**: Combined payload may have segments at different origins. The publisher falls back to `target.LoadAddress` (which is correct for system-specific load addresses configured in `SystemCatalog` or config).
8. **DefaultOrigin=0 when no target**: `target.LoadAddress` is null for the default `raw` target, so `DefaultOrigin` is null, and the emitter starts at 0 (existing behavior). ✓
9. **Format selection**: `-format` CLI flag overrides catalog/config format. Detailed iNES options (mapper, mirroring, PRG/CHR banks) remain config-only (`Target.Ines`). Portable formats (o65, ihex, srec) can be selected via `-format` without config.

## Validation Plan

1. **Build**: `dotnet build` compiles all projects.
2. **Existing tests**: All 4 tests in `WinASM65.Tests` pass unchanged.
3. **New unit tests** — create `WinASM65.Tests/TargetTests.cs` and add `<Compile Include="TargetTests.cs" />` to `WinASM65.Tests.csproj` (line 23 uses explicit file includes). Tests:
   - `SystemCatalog.TryGet("nes")` → `CpuName="6502"`, `FormatName="ines"`, `LoadAddress=0x8000`
   - `SystemCatalog.TryGet("apple2e")` → `CpuName="65c02"`
   - `SystemCatalog.TryGet("raw")` → `CpuName="6502"`, `FormatName="bin"`, `DefineHardwareSymbols=false`
   - `CpuFactory.Create("65c02")` returns `Cpu65C02` instance
   - `CpuFactory.Create("6502")` returns `Cpu6502` instance
   - `ExecutablePublisher.Publish` with `format="prg"` + `OriginAddress=0x0800` → PRG header contains `$00 $08` (little-endian 0x0800)
   - `ExecutablePublisher.Publish` with `format="prg"` + no `OriginAddress` but `LoadAddress=0x0801` → PRG header contains `$01 $08` (falls back to LoadAddress)
   - `ExecutablePublisher.Publish` with unknown format → failure diagnostic
   - `ResolvedTarget.Clone()` copies `OriginAddress`
   - **Portable format tests**:
     - O65 format: header starts with `0x6F 0x36 0x35` (magic "o65"), segment load address matches `OriginAddress`
     - Intel HEX format: starts with `:04` (type 04 record) or data record `:10` with correct address, ends with `:00000001FF`
     - Motorola S-record: starts with `S0`, has `S1` data records, ends with `S9`
   - **DefaultOrigin test**: Assemble without `.org` with `DefaultOrigin=0x0801` → `AssemblyResult.OriginAddress` equals `0x0801`
   - **Origin override test**: Assemble with `.org $A000` and `DefaultOrigin=0x0801` → `AssemblyResult.OriginAddress` equals `0xA000`

4. **New integration tests** (`AssemblyIntegrationTests.cs`):
   - `TargetResolver.Resolve(null, "c64", null)` → correct target with C64 symbols
   - `TargetResolver.Resolve(null, "unknown_sys", null)` → throws `ArgumentException`
   - `TargetResolver.Resolve` with config `System` and CLI `-t` override → CLI wins
   - Assemble `.org $0800` + lda/rts with target C64 → `.prg` output starts with `00 08` (origin address, not load address)
   - Assemble without `.org` with target C64 → output starts with `01 08` (load address via DefaultOrigin)
5. **Portable format integration test**:
   - Assemble simple source with `-t raw -format o65 -o out.o65` → file starts with "o65" magic bytes
   - Assemble with `-format ihex -o out.ihex` → valid Intel HEX output
   - Assemble with `-format srec -o out.srec` → valid S-record output
6. **CLI smoke test**: `WinASM65 -t list` shows all systems; `WinASM65 -f src.asm -o game.prg -t c64` produces valid C64 PRG with correct load address.
