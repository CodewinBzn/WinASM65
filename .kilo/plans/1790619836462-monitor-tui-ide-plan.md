# WinASM65 — Mini-Assembler monitor, plugins, AI assistant, documentation portal

Four workstreams on one project, sharing one plugin contract.

- **Part A** — terminal monitor: editor as Mini-Assembler, plugins, remaining adapters.
- **Part B** — web documentation portal on GitHub Pages.
- **Part C** — AI assistant integrated into the environment.
- **Part D** — graphics visualisation per system.

All code, comments, identifiers, documentation and commit messages are in English.

**Order of work: Part A first.** NES is the focus; Parts B, C and D keep their full scope and are
planned here, they are simply not the next thing to build.

---

## Coordination: file ownership

The sessions working on this repository are not on the coordination board, and there is no
channel to reach them. This table is therefore the coordination artefact: it lives in the
repository, which every session reads before writing.

| Workstream | Exclusive files |
|---|---|
| 1. Monitor (Part A) | `WinASM65.Monitor/` except `Bridge/`, `WinASM65.Monitor.Tests/`, `WinASM65/src/Cpu/InstructionDocs.cs`, `WinASM65/src/Output/ListingService.cs`, the source-string assembly entry point |
| 2. AI assistant (Part C) | `WinASM65.Monitor.Ai/`, `WinASM65.Monitor/Assistant/` — new files, no overlap |
| 3. Documentation portal (Part B) | `mkdocs.yml`, `site/docs/`, `.github/workflows/docs.yml`, `WinASM65/src/Cli/Reference.cs` — all new |
| 4. GEOS, already in flight | `WinASM65/src/Targets/Geos*.cs`, `WinASM65.Tests/GeosStubTests.cs`, `docs/geos-d64.md` |

**Do not rewrite** `Bridge/bridge_mesen2.lua` or `Bridge/bridge.lua`: both are measured and
correct. Workstream 4's uncommitted files are another session's work in progress.

The only meaningful overlap is `docs/`. Workstream 1 does not write there without reading
`git status` first.

---

## Current state, verified against the repository

Written last, because it changes the plan. Several tasks this plan would have created already
exist, and one of them invalidates an earlier conclusion.

### Built and committed by other work

| What | Where | Consequence for this plan |
|---|---|---|
| Mesen2 adapter, **full execution control** | `Bridge/bridge_mesen2.lua` (762 lines), `Program.cs:31,152,174,222` | Part A4 shrinks to MAME only |
| `--emulator mesen2`, `-novideo`, script selection | `Program.cs:128,152,179` | The external backend exists |
| Mesen2 script permissions patched into `settings.json` | `Program.cs:222` `EnsureMesen2ScriptSettings` | No manual setup step remains |
| Relocatable units, isolated session | `UnitLibrary.cs`, `RelocatableUnit.cs`, `BreakpointSet.cs`, commit `eb75c89` | The Mini-Assembler already has a place to put code |
| Protocol layer and 101 monitor tests | `Protocol/`, `WinASM65.Monitor.Tests/` | Foundation for the plugin contract |
| Capability difference already modelled | `MonitorSession.cs:299` | Capability gating is the existing pattern |

### In flight, uncommitted, not to be planned over

GEOS on Apple II: `WinASM65/src/Targets/GeosStub.cs`, `GeosRelocation.cs`, `geos-stub.asm`,
`WinASM65.Tests/GeosStubTests.cs`, `docs/geos-d64.md`. Another workstream owns it.

### Correction to an earlier conclusion

**Mesen2 2.1.1 does support the Mini-Assembler workflow.** Measured, not assumed:
`emu.breakExecution`, `emu.resume` and `emu.step` exist and all three refuse to be called from
the script body — "This function cannot be called outside a callback" — and all three work
inside an event callback. `emu.addMemoryCallback` accepts `callbackType.exec`, `.read` and
`.write`, with CPU memory types. `emu.getState()` returns a flat table keyed `cpu.pc`, `cpu.a`,
`cpu.x`, `cpu.y`, `cpu.sp`, `cpu.ps`, `cpu.cycleCount`. Headless, and it boots the cartridge:
`Mesen.exe --testRunner <script> <rom> -novideo -noaudio -noinput -enablestdout`.

The earlier statement "Mesen cannot do this" was measured on **MesenCE 2.2.1** and is correct
there and only there. The bridge encodes three measured constraints that shape its design:

1. `inputPolled` only fires while the machine runs, so a stopped machine must be served from
   inside the callback that stopped it.
2. `emu.step` cannot be called while already broken in the same callback invocation — execution
   advances only after the callback returns — so a step is issued while running and completed
   later.
3. `eventType.codeBreak` is the single place a stopped machine is served.

### Still missing

`WinASM65.Monitor.Abstractions`; any Terminal.Gui dependency; `mkdocs.yml`; `.github`; AI
projects; any execution core inside `WinASM65/src/`.

---

## Findings that shape the work

- **No in-memory assembly.** `IAssembler.Assemble(sourceFile, outputFile)` requires file paths.
  `IListingService` (`WinASM65/src/Output/ListingService.cs:19`) writes a `.lst` to disk.
  `AssemblyResult` carries nothing joining a source line to an address and its bytes.
- **No instruction documentation.** `Cpu6502.cs:37`, `Cpu65C02.cs:68` hold opcode bytes only.
- **No execution core in `src/`.** `Cpu6502` is `ICpuInstructionSet`: static tables, no PC, no
  bus. The only executable 6502 is `WinASM65.Tests/TestCpu6502.cs:20` — `internal`, flat 64K
  RAM, no banking, no cycles, decimal mode unsupported.
- **MesenCE cannot be driven.** All 64 `emu` entries enumerated; none advances the machine, so
  the CPU stays at `cycles=7`, `PC=$8000` while the bridge serves. It remains a memory-only
  host, and its `emu` table also carries a render API — `getPixel`, `getScreenBuffer`,
  `drawString` — whose target is unknown. That question is now moot for usefulness, since the
  frozen machine has nothing to render.
- **Terminal.Gui v2 renders images.** `ImageView` takes a `Color[,]` buffer and picks the best
  available protocol: Kitty, then Sixel, then cell-based rendering, the last covering Alacritty,
  Warp and Apple Terminal. No custom renderer needs to be written.
- **An HTML generator already exists.** `ModuleViewer` emits a single self-contained HTML file
  with hand-written CSS and an `Escape()` helper (`WinASM65/src/Modules/ModuleViewer.cs:373`).
- **33 targets modelled** (`SystemCatalog.cs:78`), `apple2`/`apple2e` included. No 65816.
- **`.kilo/plans/` contains absolute personal paths** and would be published with the repository.

---

## Shared architecture: one plugin contract

`Virtual`, `MAME`, `NES`, `Apple II`, `MesenCE` and every AI provider are plugins behind one
contract assembly. The contract is split by capability, because a plugin supplies what it can
and the host asks what it has — the discipline that already separates Mesen2 from MesenCE.

```
WinASM65.Monitor.Abstractions        host <-> plugin contract, no behaviour

  IExecutionAdapter   memory by region/bank, CPU state, pause, step, breakpoints, watchpoints
  IVideoProvider      frame capture as Color[,] plus width, height, palette
  ISystemProfile      identity, CPU, address space map, tile/nametable layout, assets
  IAssistantProvider  streaming completion, model catalogue, context budget
```

**The contract assembly is the load-bearing constraint.** A shared interface compiled into both
host and plugins yields distinct types and a cast failure at runtime with nothing to catch at
compile time. Loading uses a per-plugin `AssemblyLoadContext` with an explicit resolver
redirecting that assembly to the host's copy. The existing `ICpuStateSource` split on
`CpuSnapshot.cs` is the precedent: a test backend has no processor and must not pretend it does.

Capabilities are declared, not thrown: `IExecutionAdapter.Capabilities` is a flag set and the
host greys out what the attached system cannot do. **That flag set is also the data behind the
published capability table**, the only way that page stays honest.

```
WinASM65.Monitor                 TUI + REPL, references Abstractions
WinASM65.Monitor.Plugins.Virtual virtual target
WinASM65.Monitor.Plugins.Mame    MAME adapter, execution and video
WinASM65.Monitor.Plugins.Nes     NES system profile
WinASM65.Monitor.Plugins.Apple2  Apple II system profile
WinASM65.Monitor.Ai.Ollama       local assistant provider
WinASM65                          assembler, execution core, listing API
```

Mesen2 and MesenCE are migrated into `Plugins` once the contract exists; their Lua bridges are
already correct and must not be rewritten.

---

# Part A — Terminal monitor

## Decisions

Terminal.Gui v2; TrueColor palette with 16-colour fallback; controlled degradation on narrow
terminals; listing by extending WinASM65; editor as the Mini-Assembler with no separate prompt;
Mesen2 as the primary external backend, virtual target as the deterministic one; dynamic DLL
plugins; homebrew `.nes` built by WinASM65 as the reference workload.

**The TUI is built now, not after the REPL.** A2 (listing), the TUI shell and the Mini-Assembler
land before the contract assembly, because the contract only pays off once a second backend
exists, and the value is in the editor. Nothing is removed by that ordering.

**Memory scope: PRG ROM is the editable source of truth; RAM is a first-class, editable panel.**
A NES session has two distinct kinds of memory and the editor must not pretend otherwise. The
ROM pane carries addresses, bytes and source. The RAM pane is a live view of `$0000-$07FF` plus
the zero page and stack, editable in place, with no source text behind it. `STATE SAVE` and
`STATE LOAD` stay available from both, so a machine can be captured after a RAM edit and restored
after a step that ruins it.

## Presentation specification

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

Palette roles, background never painted. Roles are named, never inline hex, so the 16-colour
fallback is a lookup and not a second hand-maintained theme.

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

Rounded borders, 1-cell padding, dim titles, single-line fallback when the font lacks glyphs.
No emoji. Status line: system and version, capability marks, ROM, registers, cycle count,
frozen-machine warning.

| Width | Layout |
|---|---|
| >= 120 | file tree (28) + editor + right pane (34) |
| 100-119 | tree collapsed to a 3-cell drawer |
| 80-99 | right pane becomes a toggled overlay |
| < 80 | editor and listing full width |
| < 60 | clear minimum-width message, not a broken layout |

Code never wraps and never scrolls horizontally. Keys: `F1` help, `F2` tree, `F3` right pane,
`F4` RAM pane, `F5` assemble into live memory, `F7` save state, `F8` breakpoint, `F9` run,
`F10` step, `F12` video, `Ctrl+A` assistant, `Ctrl+Q` quit.

## The two memory panes

They are not two views of one thing and must not be built as one widget.

| | ROM pane | RAM pane |
|---|---|---|
| Address range | PRG, `$8000` upward | `$0000-$07FF` live, zero page and stack grouped |
| Content | address, bytes, cycles, source | address, byte, ASCII |
| Source of truth | the `.asm` file in the editor | the machine; nothing on disk |
| Edits go to | assembler output, then written back | written straight through |
| Refresh | on assemble, on step | on demand and on a timer while running |

The RAM pane must mark the three regions a 6502 programmer reads constantly — zero page `$00-$FF`,
stack `$0100-$01FF`, and free RAM — because an unlabelled hex dump of `$0000-$07FF` hides the one
address that matters most of the time.

Edits in either pane follow the same rule: **write, read back, and report the difference.** A RAM
write that does not come back is a fault, not a success to be reported as one.

## Listing, single-line assembly, instruction data

- In-memory `IListingService` returning `(lineNumber, address, emittedBytes, text)` per line.
- A source-string entry point, so F5 assembles the line under the cursor without the filesystem.
- An instruction emitting N bytes produces N byte cells, which is why a source line number is
  distinct from a display row.
- `WinASM65/src/Cpu/InstructionDocs.cs`: 6502 then 65C02, bytes and length read from the existing
  CPU tables, agreement tests that fail on drift. Highlighting built on those tables rather
  than on a generic engine that would misread `*` and `.`.

## Remaining adapters

Mesen2 is done and needs migration to the contract, not rewriting. What remains:

- **Virtual target.** `TestCpu6502` promoted to `WinASM65/src/Machine/` with per-opcode cycles,
  a memory bus, NMI/IRQ/BRK and exact indexed access. `TestCpu6502` becomes a client of it.
  Execution results must stay identical to the existing suite. This is what gives a deterministic
  backend that does not depend on any external binary.
- **MAME**, for what Mesen2 does not cover — its `apple2` driver being the obvious case.
  Non-blocking by construction: a periodic Lua callback polls the socket with a zero timeout, at
  most one command per tick. A command sets `debugger.execution_state = "stop"` first, halting at
  an instruction boundary, then reads or writes, then replies. A blocking receive inside the
  callback would stall the machine — the MesenCE failure, which must not be repeated. MAME is
  GPLv3, driven as an external process only.
- **MesenCE** stays declared memory-only, and its existing adapter keeps its measured refusals
  rather than gaining simulated capabilities.

## Mini-Assembler

F5 assembles under the cursor, writes the bytes to the live machine, advances the cursor by the
instruction length, and reuses the existing `UnitLibrary` / `RelocatableUnit` / `LOAD` machinery
for placing multi-block units. F8/F9/F10 bound and gated by capability. Errors per line.

## Tasks

**A0 — DONE, measured.** Mesen2 2.1.1 confirmed against `example_bomberman-nes/bomber.nes`
(mapper 0, PRG 16 KiB, CHR 8 KiB, CRC32 `0xB9804046`). Running unassisted: cycles
37908190 → 54138573 → 70130694 across three `CPU` reads. `PAUSE` gives a deterministic machine:
two successive reads returned `PC=$CBFD A=01 X=00 Y=5E SP=FD PS=05 cycles=43358022` identically.
`STEP` is exact from there: +9 cycles to `$C01A`, then +3 to `$C01B`. `BREAK SET exec $C01A` →
`OK`, `DISASM $C01A 4` → `PHA/TXA/PHA/TYA`. 102 tests green, 0 warnings.

Consequence for the code: **a static cycle count is not a broken machine.** It is MesenCE's
symptom (7 cycles, PC stuck at `$8000`) *and* Mesen2's behaviour when deliberately paused. The
frozen-machine warning must key on a pause that was not requested, or it will condemn the only
usable backend.

**A1** Listing API, source-string entry point, `InstructionDocs`, highlighting lexer.

**A2** TUI shell and theme: skeleton, responsive rules, theme file, file tree, editor pane,
listing pane, RAM pane, status line. Add `Terminal.Gui` v2 to `WinASM65.Monitor.csproj`.

**A3** Mini-Assembler actions, and RAM/state actions: F5 assemble under the cursor, RAM cell edit,
`STATE SAVE`/`STATE LOAD` bound in the shell, breakpoint toggle.

**A4** Contract assembly and capability flags. Repoint the monitor, migrate the Mesen2 and
MesenCE adapters into plugin form. Deferred until a second backend exists — the TUI does not
need it. 102 tests stay green.

**A5** Execution core promoted from `TestCpu6502`, then exposed as a plugin.

**A6** MAME adapter, non-blocking. Apple II profile if its driver is confirmed adequate.

---

# Part B — Documentation portal

MkDocs Material with reference pages generated by WinASM65; GitHub Pages via Actions; new tree
`site/docs/` authored for readers while `docs/` stays internal; reference pages generated at
build time, never committed.

**Single source of truth.** `MonitorSession` dispatches on a `switch` with no enumeration. Extract
a registry — verb, arity, usage, summary, required capability — that both the dispatch and the
generator read, so a command cannot be added without appearing in the reference. Same for
`StandardDirectives`. The capability flags from A1 become the published capability table.

Generated pages: instruction set (CPU tables + `InstructionDocs`), directives, target catalogue,
monitor commands, wire protocol, build info. The instruction page depends on A2; until A2 lands
it emits opcodes and lengths from the CPU tables alone, then gains cycles and flags.

**Tasks.** B0 pre-publication audit — the repository becomes public, so `.kilo/` and the probe
scripts' absolute paths, and any credentials in history, must be handled first; this gate comes
before any publishing step. B1 `mkdocs.yml`, Material theme, `site_dir: site/` gitignored,
search plugin, **strict** build so a broken link fails. B2 authored content: index, user manual
for assembler and monitor, concepts (mnemonic versus opcode versus addressing mode, why assembly
takes two passes, why relocation resolution can stay silent, why a debugger GUI is not a
scriptable API, and the MesenCE versus Mesen2 callback constraint, which is the single most
useful thing a user could read), internals (pipeline, module format, monitor architecture,
plugin model). B3 `WinASM65 reference -o <dir>`, idempotent. B4 `.github/workflows/docs.yml`:
build, test, generate, `mkdocs build --strict`, deploy to Pages only on green. B5 maintainer
workflow with `mkdocs serve`.

---

# Part C — AI assistant

Pluggable provider, model chosen at runtime. Ollama local as the default because it is free,
offline and private — code never leaves the machine — with cloud providers available and
explicitly opted into. This tool inspects emulated systems and ROMs; shipping that to a third
party should be a decision, not a side effect.

**Applying generated code is never a blind write.** The user asked for both "write into a file"
and "generate code I save myself". Both are served by one flow: generate, show a diff, apply on
confirmation. A model rewriting a whole file with no intermediate review is the class of failure
this project refuses everywhere else.

- The assistant never writes; it produces a proposed change.
- The diff is computed against current file content, not against what the model believed.
- Application is a discrete keystroke, per hunk or per file.
- Files outside the current project are refused rather than suggested.

**Context assembly matters more than model size.** Writing a 6502 fragment is narrow, and a
small local model handles it. What decides success is what gets sent: the current file region,
the relevant instruction documentation, the attached system's capability flags, the target
profile. Focused context beats a large model given the whole repository.

- Context is assembled by explicit rules, so what the model saw is inspectable.
- The panel shows the context sent and the response received, with a visible token budget.
- Graphics generation — NES palettes, tile patterns, nametable layouts — produces resources the
  target profile can load, previewed before being written like any other generated file.

**Tasks.** C1 `IAssistantProvider`. C2 Ollama provider. C3 diff-based apply with per-hunk
confirmation. C4 context rules per request type. C5 assistant panel with streaming output, model
picker, visible context budget. C6 graphics generation. C7 a cloud provider, opt-in, keys read
from the environment rather than stored in the repository.

---

# Part D — Graphics visualisation

`ImageView` handles display: `Color[,]` in, best available protocol out, cell fallback
everywhere. No custom renderer is written. Video is a capability like any other, so a system that
cannot produce frames says so instead of showing an empty pane.

**Sources, in order of confidence.** Mesen2 runs `-novideo`, which suggests the framebuffer is
still computed even when no window is shown, but whether its Lua reaches it is unverified and is
the first task. MAME exposes a full render system, so frame capture there is a read rather than a
screen scrape. The virtual target has none: it executes the CPU and has no PPU, so video is
absent from its capability set rather than faked. MesenCE is dropped from this part — a frozen
machine has nothing to show.

**Tasks.** **D1** Probe Mesen2: whether its Lua exposes the emulated framebuffer under `-novideo`,
what shape it arrives in, and whether it advances across frames. Record the finding; do not
assume either outcome. **D2** `IVideoProvider` in Abstractions, frames marshalled as `Color[,]`.
**D3** `ImageView` pane, `F12` to toggle, zoom, and frame throttling so a 60 Hz source does not
saturate the render loop. **D4** provider for whichever host D1 confirms, MAME otherwise.
**D5** NES system profile: palette, nametable geometry, tile layout, so frames can be read as a
structured screen rather than a bitmap.

---

## Risks

- **Publishing a public repository** exposes history, paths and design notes. Mitigated by the
  B0 audit gate before anything is deployed.
- **Concurrent workstreams.** GEOS is in flight and shares `WinASM65/src/Targets/` and
  `docs/`. A1 and B2 touch the same directories. Sequence them, do not parallelise them.
- **Plugins: type identity** fails at runtime, not compile time. Dedicated assembly plus a
  loading test.
- **Terminal.Gui v2 is recent** and its API moves. Pin the version; no opportunistic upgrades.
- **Per-opcode cycle counts** are the assembly lock and the most expensive correction.
- **Shared files.** `Program.cs`, `MonitorSession.cs` and both bridges are actively edited. A2
  and A3 touch them; do it early and arbitrated.
- **A TUI that needs the contract assembly would stall on A4.** The shell must bind to
  `MonitorSession` and `IMemoryBackend` as they exist today. If a task starts reaching for a
  contract type that does not exist yet, that is the signal A4 has been pulled forward by
  accident.
- **The Mesen2 callback constraint is subtle.** `emu.step` cannot be called from inside the
  callback that broke execution. A bridge that appears correct and deadlocks is a plausible
  regression; the existing bridge's comments must be preserved verbatim.
- **AI-generated code is untrusted input.** Never executed, never allowed to widen file scope,
  never applied without a diff.
- **Frame capture outruns rendering.** Throttle, and report the real rate.
- **Generated pages committed by accident** would publish a stale reference. `site/` is
  gitignored and regenerated in CI.

## Validation

- 408 tests in `WinASM65.Tests` and 102 in `WinASM65.Monitor.Tests` stay green.
- Byte agreement between `InstructionDocs` and the CPU tables.
- Execution equality: virtual core against `TestCpu6502`, program by program.
- Plugin loading test, run by the suite.
- **Headline test on Mesen2:** on a WinASM65-built ROM, type `LDA #$42`, press F5, then F10, and
  assert the machine reports `A=42` with an advancing cycle count. Identical and deterministic on
  the virtual target.
- **RAM pane against a live machine, never against itself.** Write a value to `$0000-$07FF`, read
  it back, then let the CPU run long enough to overwrite it and confirm the change came from the
  CPU. An all-`5AA5C3C3` round trip proves nothing: read and write share one shadow space on
  MesenCE, and self-consistency is not evidence.
- `STATE SAVE` then `STATE LOAD` restores registers *and* RAM exactly, compared field by field.
- Registry consistency: every command and directive appears in the generated reference, asserted
  by count so a new command without documentation fails the suite.
- `mkdocs build --strict` passes; generator output is idempotent.
- Alignment snapshot: lines of differing mnemonic length start the byte column at the same cell.
- Degradation at 120, 100, 80 and 60 columns: nothing overflows, code never wraps.
- Video: capture a frame, assert dimensions and palette; with no provider the pane states that.
- AI: no proposed change is written without confirmation; the diff is regenerated against
  changed file content, never reused.

## Out of scope

- **65816**, including for the Apple II.
- **PPU emulation in the virtual target.** It executes the CPU; it has no video.
- **Driving MesenCE.** No API exists; it stays a memory viewer.
- **GEOS.** In flight with another workstream.
- **Commercial ROM compatibility.** Homebrew is the reference workload.
- **Versioned documentation and localisation.** English, single version.

## Open questions

1. **Should `.kilo/` be committed at all?** It holds plan files with absolute personal paths.
2. **Does Mesen2 expose its framebuffer under `-novideo`?** It decides how many video sources
   exist in Part D.
3. **RESOLVED — keep the REPL.** It stays the reference implementation: the TUI renders what
   `MonitorSession` already answers, and every action in the shell is testable as a typed line.
   The TUI adds no behaviour the REPL cannot express, which is what keeps it honest.
4. **Apple II depth.** `ISystemProfile` covers RAM, ROM, Language Card and two text pages. PIA,
   keyboard and video text are not emulated — and GEOS work in flight may already answer part of
   this.