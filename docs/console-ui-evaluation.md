# Console UI evaluation: SharpConsoleUI vs Terminal.Gui

Decision: **stay on Terminal.Gui**. The three problems that motivated this
evaluation — RAM refresh blocking the UI, source-editor performance on
6154-line files, and push-based RAM updates — are solved at the protocol
and pane level, and none of the solutions depend on the UI toolkit. A
migration would rewrite ~3,000 lines of working, tested UI for no
remaining functional gain, against a constraint that pins the layout
layer to Terminal.Gui's view model.

## What the comparison page says

SharpConsoleUI (nickprotop/ConsoleEx, v2.4.77, MIT, .NET 8/9/10) is a
windowed console UI system with its own rendering engine, compositor,
drivers, layout engine, colour system and markup parser — it is *not*
built on Spectre.Console or Terminal.Gui. Its differentiators:

- **Per-window buffers + compositor.** Each window renders into its own
  `CharacterBuffer`; a compositor merges them with per-cell RGBA alpha
  blending, occlusion culling (rectangle subtraction) and
  `PreBufferPaint`/`PostBufferPaint` hooks. Terminal.Gui paints all
  views into one shared buffer with a back-to-front painter's algorithm.
- **Independent async window threads**, with UI-thread marshaling
  (`EnqueueOnUIThread`) — each panel can update on its own schedule.
- **MultilineEditControl** with TextMate grammar highlighting (~64
  languages), coloured from the application's own `ITheme` via
  `SyntaxPalette`.
- **TableControl** with in-cell edit, compound filter grammar, and an
  `ITableDataSource` pull seam for virtualization (the seam is provided;
  a turnkey virtualizing renderer is not — XenoAtom ships the turnkey
  one).
- **GridControl**: a WinUI-style 2D grid (Star/Auto/Fixed tracks,
  per-track min/max, row/col spans, gaps, interactive splitters).
- **Window management**: overlapping, drag, resize, minimize, maximize,
  taskbar (Alt+1-9), modal stack with input blocking, window templates.
- **DOM layout pipeline** (Measure → Arrange → Paint). Sticky dock is
  Top/Bottom at the window root only; there is **no flex allocator** and
  no Left/Right dock.
- **HeadlessConsoleDriver** for testing.
- Solo-maintained: 1 contributor, ~232 stars, ~16 months old.

Terminal.Gui v2 (GA, ~11k stars, ~130 contributors) is the most mature
.NET TUI library with the widest control set (HexView, ColorPicker,
Wizard, TreeView, GraphView…), Pos/Dim arithmetic layout, cell-level
dirty tracking, and input injection for tests. One caveat the page
states plainly: **Terminal.Gui v2.4.x targets .NET 10** — the version
installed here (2.0.0) targets .NET 8 and builds clean, but upgrading
Terminal.Gui would force a .NET 10 move.

## Can SharpConsoleUI replace Terminal.Gui under the ShellLayout constraint?

`ShellLayout.cs` is pure C# — no Terminal.Gui types. It computes a
decision (Width, Height, Mode, TreeWidth, RightPaneWidth) from a
terminal size, and `ShellWindow.ApplyLayout` consumes it by setting
`View.Frame`/`Pos`/`Dim` on Terminal.Gui views. The constraint "leave
`ShellLayout.cs` untouched" is therefore satisfiable in principle: a
migrated `ApplyLayout` could read the same record.

The cost is the layer around it. SharpConsoleUI positions controls
through its DOM pipeline — `GridControl` tracks, `ColumnContainer`,
`ScrollablePanel`, sticky Top/Bottom dock at the window root — and
documents no "set absolute bounds" API for arbitrary controls. The
shell's layout modes do not map onto that model:

- **Full** (tree 28 + editor + right 34) maps to a fixed-track grid.
- **CollapsedTree** (3-column drawer) and **Overlay** (right pane as a
  toggled overlay) do not; they need a custom absolute-positioning
  container — a layout engine written inside someone else's framework.

A full migration rewrites: `ShellWindow` (Toplevel), `ShellRunner`
(Application.Init/Run/Shutdown, Shortcut, Key), `StatusLine`,
`FileTreePane`, `RamPane`, `ListingPane`/`ListingView`/`ListingRowView`,
`SourceEditorPane`/`EditorTextView`/`GutterView`, `ShellColorScheme`/
`ShellTheme` (ColorScheme/Attribute), and the TUI test suite
(`SourceEditorPaneTests`, `ShellChromeRenderTests`, …). Estimate:
4–6 weeks for one developer, with high regression risk against the
existing suite.

## Is there a minimum viable or hybrid migration?

**No, on both counts.**

- *Pane by pane:* not possible. Both toolkits must own the console —
  Terminal.Gui's `Application.Init` takes over the terminal, and
  SharpConsoleUI has its own `System`/driver and render loop. There is
  no interop seam to host one toolkit's views inside the other, so a
  pane-by-pane swap requires a full cutover of the event loop first.
- *Hybrid (Terminal.Gui editor + SharpConsoleUI RAM):* same obstacle —
  two console owners in one process. The only theoretical hybrid is
  hosting the Terminal.Gui app inside SharpConsoleUI's PTY
  `TerminalControl`, which adds a terminal-emulation hop to every
  keystroke: the opposite of what a performance migration is for.

## Does SharpConsoleUI solve any current problem?

The three motivating problems, and their actual state:

1. **RAM refresh blocked the UI** (400 ms poll on the UI thread).
   Solved at the protocol level, toolkit-independently: the bridge
   installs a write watchpoint (`bridge_mesen2.lua`
   `install_ram_watchpoint`), batches writes into a dirty buffer, and
   drains one `PUSH RAM $addr:byte …` line per tick
   (`drain_ram_push`). The client's receiver thread sorts replies from
   pushes (`ProtocolClient.PushReceived`), `BridgeMemoryBackend` raises
   `RamChanged`, and `RamPane.ApplyChanges` redraws only the rows that
   changed. No timer, no polling, no UI-thread read.
2. **6154-line files froze the editor on keystroke.** Solved:
   `SourceEditorPane.LexBufferIncrementally` re-lexes only the changed
   line (the 6502 lexer is line-isolated), and full assembly runs
   debounced on a background thread (`RecomputeRowsAsync`), so a
   keystroke is O(changed line), not O(file).
3. **Push-based RAM updates.** Implemented end to end, as above.

SharpConsoleUI's differentiators — overlapping windows, compositor
effects, video playback, per-window async threads — are irrelevant to a
single-screen debugger pane layout. Its `MultilineEditControl` would
replace the editor, but the editor is already fast, and TextMate's
bundled grammars do not include this project's 6502 assembly lexer,
whose `ListingToken` roles are exactly what the gutter (address, bytes,
cycles per line) and the breakpoint/cursor logic consume.

## Risks

- **Ecosystem:** SharpConsoleUI is solo-maintained (1 contributor, 232
  stars, 16 months). Terminal.Gui v2 is GA with ~11k stars and ~130
  contributors. For a tool whose UI *is* the product surface, maturity
  is the safer default.
- **Regression:** a migration risks working, tested behaviour (966
  tests) for no remaining functional gain.
- **The honest trigger for revisiting:** Terminal.Gui v2.4.x targets
  .NET 10. If the project moves to .NET 10, re-evaluate SharpConsoleUI
  (net8/9/10) at that point — the cost of the layout rewrite would be
  paid alongside a framework move anyway.

## Decision

**Stay on Terminal.Gui.** Complete and verify the push-based RAM
updates — the actual migration driver — rather than the toolkit swap.
The evaluation remains on record for the .NET 10 move.

## What was implemented in this pass

The push-based RAM architecture was already present in the working
tree (bridge watchpoint → `PUSH RAM` → receiver thread → `RamChanged`
→ incremental pane update; RAM window widened to $0000-$3FFF). This
pass fixes what blocked green:

1. `ProtocolClient.Connect` — `ConnectAsync`/`Wait` wraps a refused
   connection in an `AggregateException`, where the APM pair it
   replaced threw the `SocketException` itself. Unwrapped now, so
   `Program.Connect`'s retry produces the named refusal "No bridge
   answered on 127.0.0.1:<port> within 15s." instead of "Unexpected
   error: One or more errors occurred." This was the flake behind
   `MonitorProgramTests.WithoutABridgeTheProgramFailsNamingTheOneItExpected`.
2. `RamEditingTests.WriteBytesExceedsWindowEndReturnsError` — off by
   one: a 2-byte write at $03FFE ends at $3FFF, the inclusive window
   end, which is a legal write. The test now starts at $3FFF so the
   range ends at $4000 and exceeds the window.
3. Stale comments from the window widening ($0000-$07FF → $0000-$3FFF,
   128 rows → 1024 rows) in `RamDump.cs` and `RamEditing.cs`.

## Verification

- `dotnet build -warnaserror`: 0 warnings, 0 errors.
- `dotnet test` on the solution: **966/966 pass** (20 Abstractions +
  557 core + 389 monitor), 0 failures.
- The app launches: the monitor tests spawn the real executable, and a
  manual run against a dead port prints the named refusal and exits 1.
