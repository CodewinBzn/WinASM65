# WinASM65 — Mini-Assembler monitor and documentation portal

Two workstreams on one project, sharing the instruction dataset.

- **Part A** — terminal monitor: editor as Mini-Assembler, virtual target, MAME adapter.
- **Part B** — web documentation portal: MkDocs Material, published on GitHub Pages.

All code, comments, identifiers, documentation and commit messages are in English.

---

## Findings that shape both parts

Measured or read in the code, not assumed.

- **No in-memory assembly.** `IAssembler.Assemble(sourceFile, outputFile)` requires file
  paths. `IListingService` (`WinASM65/src/Output/ListingService.cs:19`) writes a `.lst` to
  disk. `AssemblyResult` carries nothing joining a source line to an address and its bytes.
- **No instruction documentation.** `Cpu6502.cs:37`, `Cpu65C02.cs:68` hold opcode bytes only.
- **No execution core in `src/`.** `Cpu6502` is `ICpuInstructionSet`: static tables, no PC, no
  bus. The only executable 6502 is `WinASM65.Tests/TestCpu6502.cs:20` — `internal`, flat 64K
  RAM, no banking, no cycles.
- **An HTML generator already exists.** `ModuleViewer` emits a single self-contained HTML file
  with hand-written CSS and an `Escape()` helper (`WinASM65/src/Modules/ModuleViewer.cs:373`),
  reached through the `view` subcommand (`CommandLineApplication.cs:78`). No Markdown engine,
  no templates, no site tree.
- **Mesen cannot be driven.** Measured on MesenCE 2.2.1: all 64 `emu` entries enumerated, none
  advances the machine, CPU frozen at `cycles=7`, `PC=$8000` while the bridge serves. The
  second build at `C:\Projects\NES_PROJECTS\Mesen\Mesen.exe` is the same 0.9.x lineage —
  suspected, not measured. Mesen ships a debugger GUI; a GUI debugger is not a scriptable API.
- **MAME can, verified** in the 0.289 Lua reference: `debugger.execution_state = "stop"/"run"`,
  `debug:step(n)`, `debug:go()`, `debug:bpset(addr)`, `debug:wpset(space,"rw",addr,len)`,
  `symbols:read_memory/write_memory` with explicit `disable_se`, `symbols:add`.
- **33 targets modelled** (`SystemCatalog.cs:78`), `apple2`/`apple2e` included. No 65816.
- **No `.github` directory**: no CI, no publishing infrastructure.
- **`.kilo/plans/` contains absolute personal paths** and would be published with the repository.

---

# Part A — Terminal monitor

## Decisions

| Topic | Decision |
|---|---|
| Interface | TUI in the terminal, Terminal.Gui v2 (MIT) |
| Presentation | TrueColor palette with 16-colour fallback |
| Narrow terminals | Controlled degradation, no horizontal code scrolling |
| Listing | Extend WinASM65 with a public listing API |
| Instruction docs | Internal dataset, complete, 6502 + 65C02 |
| Entry point | The editor is the Mini-Assembler; no separate prompt |
| Backends | MAME adapter (primary external) + virtual target |
| Plugins | Dynamic DLL loading from a folder |
| Reference workload | Homebrew `.nes` built by WinASM65 |

## Architecture

```
WinASM65.Monitor.Abstractions        NEW  host <-> plugin contract
WinASM65.Monitor                     TUI + REPL
WinASM65.Monitor.Plugins.Mame        MAME adapter
WinASM65.Monitor.Plugins.Virtual     virtual target
WinASM65.Monitor.Plugins.Mesen       Mesen adapter, memory only
WinASM65                             assembler, execution core, listing API
```

**The contract assembly is the load-bearing constraint.** A shared interface compiled into
both host and plugins yields distinct types and a cast failure at runtime with nothing to
catch at compile time. `WinASM65.Monitor.Abstractions` holds `IMemoryBackend`,
`ICpuStateSource`, `IRomInfoSource`, `MonitorException`, `AddressRangeException`,
`AddressSpace`, `BreakpointKind`, `CpuSnapshot`. Loading uses a per-plugin
`AssemblyLoadContext` with an explicit resolver redirecting that assembly to the host's copy.

### Capability model

An adapter declares what it can do instead of throwing at call time.

| Adapter | read/write | pause | step | breakpoints | watchpoints |
|---|---|---|---|---|---|
| Virtual target | yes | yes | yes | yes | yes |
| MAME | yes | yes | yes | yes | yes |
| Mesen | yes | no | no | no | no |

The TUI greys out what the attached adapter cannot do, rather than failing after the user has
typed a command. **The capability list itself becomes the data behind the published command
reference**, which is the only way that table stays honest.

### Address spaces, not a flat 64K

MAME addresses memory by space tag; several systems switch banks in `$D000-$DFFF`.
`Read`/`Write` take a region or bank selector. The current flat NES path stays one region.

### MAME adapter: non-blocking, pause-then-act

This is where the Mesen bug must not be repeated. A blocking receive inside a periodic Lua
callback stalls the machine.

- A periodic callback polls the socket with a zero timeout, at most one command per tick.
- Handling a command sets `debugger.execution_state = "stop"` first, halting at an instruction
  boundary, then reads or writes, then replies. Pausing is a debugger operation, so there is
  no race with the emulated CPU.
- `MAME <rom> -debug` is required; the adapter names it when absent.
- MAME is GPLv3, driven as an external process only: no linking, no copied code.

### Listing and single-line assembly

- In-memory `IListingService` returning `(lineNumber, address, emittedBytes, text)` per line.
- A source-string entry point, so F5 assembles the line under the cursor without the filesystem.
- An instruction emitting N bytes produces N byte cells, which is why a source line number is
  distinct from a display row.

### Presentation specification

Centre pane, fixed columns:

| Column | Width | Content | Style |
|---|---|---|---|
| Gutter | digits + 1 | right-aligned line number, breakpoint marks | dim |
| Address | 8 | `$XXXX` + 2 spaces | dim |
| Bytes | 11 | up to 3 groups of `XX `, `+N` continuation beyond | high contrast |
| Cycles | 7 | right-aligned, blank when undocumented | dim |
| Source | rest | tokenised | full palette |

Alignment is exact by construction: the listing model computes display width per cell, so the
byte column never shifts when a mnemonic is long.

Palette roles (TrueColor, with 16-colour fallback), background never painted:

| Role | TrueColor | Fallback |
|---|---|---|
| default text | `#C8D0D8` | default |
| gutter, chrome | `#5A6672` | bright black |
| mnemonic | `#7FB2E5` | blue |
| operand / address | `#E8B86D` | yellow |
| directive | `#C58AF9` | magenta |
| label | `#8FD67A` | green |
| number | `#F0C674` | yellow |
| comment | `#6B7785` | bright black |
| error | `#F26D6D` | red |
| breakpoint | `#E06C75` | red |
| warning | `#E5C07B` | yellow |

Roles are named, never inline hex. Rounded borders, 1-cell padding, dim titles, single-line
fallback when the font lacks glyphs. No emoji. Status line: adapter and version, capability
marks, ROM, registers, cycle count, frozen-machine warning.

Responsive rules, code never wraps and never scrolls horizontally:

| Width | Layout |
|---|---|
| >= 120 | tree (28) + editor + right pane (34) |
| 100-119 | tree collapsed to a 3-cell drawer |
| 80-99 | right pane becomes a toggled overlay |
| < 80 | editor and listing full width |
| < 60 | clear minimum-width message, not a broken layout |

Keys: `F1` help, `F2` tree, `F3` right pane, `F5` assemble into live memory, `F8` breakpoint,
`F9` run, `F10` step, `Ctrl+Q` quit.

## Tasks

**A0 Prerequisites.** Install MAME, confirm its `nes` driver boots a WinASM65 ROM; if not, say
so now. Run `probe_api.lua` against the second Mesen build to turn suspicion into fact.

**A1 Contract.** Create `WinASM65.Monitor.Abstractions`, move shared types in, repoint
`WinASM65.Monitor`. All 101 monitor tests stay green.

**A2 Listing and docs.** In-memory `IListingService` plus the source-string entry point, with
tests for line/address/byte correlation. Then `WinASM65/src/Cpu/InstructionDocs.cs`: 6502 then
65C02, bytes and length read from the existing tables, agreement tests that fail on drift.
Then the highlighting lexer on those tables.

**A3 Execution core.** Promote `TestCpu6502` to `src/Machine/` with per-opcode cycles and a
memory bus. Execution results must stay identical to the existing suite.

**A4 Adapters.** Region/bank model and capability flags. Virtual target first, then MAME
non-blocking, then Mesen declared memory-only. Plugin loading test with a purpose-built dll.

**A5 Shell and theme.** Terminal.Gui skeleton with the responsive rules. Theme file with named
roles and fallback table. File tree with filtering search. Editor pane with exact alignment.
Right pane: CPU, hex view, instruction sheet.

**A6 Mini-Assembler.** F5 assembles under the cursor, writes to the live machine, advances by
instruction length. F8/F9/F10 bound and gated by capability. Errors reported per line, never as
a global banner.

---

# Part B — Documentation portal

## Decisions

| Topic | Decision |
|---|---|
| Producer | MkDocs Material, with reference pages generated by WinASM65 |
| Hosting | GitHub Pages via Actions, repository becomes public |
| Content | New tree `site/docs/`, authored for readers; `docs/` stays internal |
| Reference pages | Generated at build time, never written by hand, never committed |

## Single source of truth

A hand-maintained command table is a lie waiting to happen. Two registries must be extracted
so the REPL dispatch and the doc generator read the same data:

- **Monitor commands.** `MonitorSession` dispatches on a `switch` over verbs, with no
  enumeration. Extract a registry: verb, arity, usage, summary, required capability. The
  dispatch consumes it; the reference page renders it. A monitor command added without
  documentation is then impossible, because the generator reads the same table.
- **Assembler directives.** Same treatment on `StandardDirectives`: name, arity, summary.

Generated reference pages:

| Page | Source of truth |
|---|---|
| Instruction set | `Cpu6502`, `Cpu65C02`, `InstructionDocs` |
| Directives | `StandardDirectives` registry |
| Target catalogue | `SystemCatalog` — 33 targets, CPU, format, load address |
| Monitor commands | `MonitorSession` command registry |
| Wire protocol | `MonitorProtocol` constants |
| Build info | version, commit hash, generated timestamp |

**Dependency:** the instruction reference page needs `InstructionDocs` (task A2). Until A2
lands, the generator can emit opcodes and lengths from the CPU tables alone, and the cycles and
flags columns appear once A2 completes. Sequence accordingly rather than blocking.

## Tasks

**B0 Pre-publication audit.** Making the repository public publishes everything in it.
`.kilo/plans/` contains absolute personal paths (`C:\Users\ghani\...`,
`C:\Projects\NES_PROJECTS\...`), and so do the probe scripts and parts of `docs/`. Decide
whether `.kilo/` is committed at all, and scrub personal paths from anything reachable. Also
confirm no credentials, keys or tokens are in history. **This gate comes before any
publishing step.**

**B1 Site skeleton.** `mkdocs.yml` with the Material theme, `site_dir: site/` added to
`.gitignore`, `docs_dir: site/docs`. Enable the search plugin, admonitions, tables and code
highlighting. Build in **strict** mode so a broken internal link fails the build rather than
reaching readers.

**B2 Content.** Author in `site/docs/`, in English, for readers rather than for implementers:
- `index.md` — what the tool is, install, quick start.
- `user-manual/` — assembler: source, directives, modules, output formats, linker. Monitor:
  launching, keys, the TUI, the Mini-Assembler workflow, adapters.
- `concepts/` — mnemonic versus opcode versus addressing mode; why assembly takes two passes;
  relocation and why resolution can be silent; ROM and RAM; banking; why the emulator's
  debugger GUI is not a scriptable API.
- `internals/` — assembly pipeline, module format, monitor architecture, adapter and plugin
  model, the Mesen freeze and what MAME offers instead, and why it is documented rather than
  hidden.
- `reference/` is generated, not authored.

**B3 Generator.** `WinASM65 reference -o <dir>` emitting Markdown pages from the sources above.
Output is gitignored and regenerated on every build, so it cannot be committed stale. Must be
idempotent.

**B4 Publishing.** `.github/workflows/docs.yml`: on push to `main`, set up .NET, restore, build,
run both test suites, run the generator, then `mkdocs build --strict`, then deploy to Pages
(`pages: write`, `id-token: write`). Pages deploys only on green — a broken site never
publishes.

**B5 Maintainer workflow.** Document locally: `mkdocs serve` for preview, and the regenerate
then rebuild loop. Contributors need Python 3; CI installs it.

## Validation

Part B, in addition to Part A's:

- `mkdocs build --strict` passes: no broken internal links, no missing pages.
- Registry consistency: every monitor command in the registry appears on the generated page,
  and every directive too. A test asserts both counts, so a new command without a doc entry
  fails the suite rather than shipping.
- Generator idempotence: two consecutive runs produce byte-identical output.
- Reference accuracy: instruction bytes and lengths on the page equal the CPU tables.
- Pages deployment from a green build on a test push, verified at the published URL.
- Pre-publication audit passes: no personal paths, no secrets, `.kilo/` handled as decided.

---

## Risks

- **Publishing a public repository** exposes history, paths and design notes. Mitigated by the
  B0 audit gate before anything is deployed.
- **Plugins: type identity** fails at runtime, not compile time. Dedicated assembly plus a
  loading test.
- **Terminal.Gui v2 is recent** and its API moves. Pin the version; no opportunistic upgrades.
- **MAME's NES driver is incomplete.** A0 runs before any investment; homebrew with a simple
  mapper is the supported case.
- **Per-opcode cycle counts** are the assembly lock and the most expensive correction.
- **Shared files.** `Program.cs`, `MonitorSession.cs`, `Bridge/bridge.lua` are touched by
  another session. A1 touches them little; do it early and arbitrated.
- **Live machine vs. source of truth.** Writes go into the machine, not the `.nes` file. The UI
  must never imply the ROM on disk changed.
- **Two content trees.** `docs/` and `site/docs/` could drift. `docs/` is explicitly internal
  and not published; only `site/docs/` is maintained for readers.
- **Generated pages committed by accident** would publish a stale reference. `site/` is
  gitignored and regenerated in CI.

## Out of scope

- **65816**, including for the Apple II.
- **NES PPU/APU/timing.** The virtual target executes the CPU; no video rendering.
- **Driving Mesen.** No API exists; it stays a memory viewer.
- **Commercial ROM compatibility.** Homebrew is the reference workload.
- **Apple II adapter.** MAME's `apple2` driver is the natural second target, not designed here.
- **Versioned documentation** and **localisation**. English only, single version.

## Open questions

1. **Should `.kilo/` be committed at all?** It holds plan files with absolute personal paths.
   Excluding it keeps the public repository clean; including it preserves the design history.
2. **Keep the REPL?** The plan keeps it: same session, different shell, and it remains the only
   interface testable without a terminal.
3. **Listing column position.** The layout puts bytes immediately before the source in one
   pane. Confirm that rather than two separate panes.