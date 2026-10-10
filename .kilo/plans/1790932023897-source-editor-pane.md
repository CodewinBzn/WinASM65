# WinASM65 — The centre pane becomes an editable source view

All code, comments, identifiers, documentation and commit messages are in English.

## The design, as agreed

The centre pane becomes an **editor**. Each line carries, in this order:

```
line number │ address │ bytes │ cycles │ the source line
```

The last column is whatever the file says — an instruction, a comment, an equate, a blank — and
every line of the file appears. The editor **replaces** the listing pane. There is no second pane
and **no layout change**: `ShellLayout` still gives its leftover width to the centre, and the
fourteen tests in `ShellLayoutTests` are untouched.

Address, bytes and cycles are **recomputed on every keystroke**, in process, from the buffer.

---

## Why this is cheap

Three facts from the repository collapse what looked like the hard parts.

**1. The monitor already assembles text in memory, with nothing cached.**
`AssemblerListingSource` holds `ISourceListingService` (`AssemblerListingSource.cs:55`), and that
interface is implemented by `SourceListingService`, whose own summary is "Lists a source in memory
through the ordinary assembler, so a user interface gets rows without a source file, an object file
or a `.lst` ever being written" (`SourceListingService.cs:9-14`). It exposes `ListSourceText` and
`ListSourceFile` (`SourceListingService.cs:32,47`).

The class already states the rule this feature depends on:

> "Nothing is cached. Every call to `Rows` assembles again, which is what makes F5's refresh honest:
> the listing on screen cannot be one assembly behind the source, because there is no earlier one to
> be behind." — `AssemblerListingSource.cs:35-37`

So the derived columns need no new core code, no disk, and no bridge round trip. They are the same
assembly F5 already performs, run against a string instead of a path.

**2. The seam already exists to be widened.**
`IListingSource` is "the one thing the editor and listing panes ask for, and the entire surface they
know about" (`ListingSource.cs:112-114`), and `AssemblerListingSource` is the only class that knows
the library. Its `ListingRequest(sourceFile, origin, maxRows)` becomes a request that can also name
**text**. `AssemblerListingSource` stays the single translator; no second one grows up.

**3. `maxRows = 0` already means no ceiling.**
Documented at `AssemblerListingSource.cs:223-224`. The editor passes zero, because it needs one row
per buffer line, not a screenful.

---

## The row model

`ListingRow` cannot key the editor: `LineNumber` is **0 for a line that emits no bytes**
(`ListingSource.cs:75`) and `Address` is **-1** for the same lines (`ListingSource.cs:78`). Since the
editor shows *every* line, comments included, it needs its own type that keeps the buffer line
number regardless of whether anything was emitted.

New `EditorRow`:

| Member | Notes |
|---|---|
| `LineNumber` | always the buffer line, 1-based; never 0 |
| `Address` | `-1` when the line emits nothing |
| `Bytes` | never null; empty when the line emits nothing |
| `Cycles` | `null` unless documented for the opcode the row actually emitted |
| `Text` | the buffer line verbatim |
| `Problem` | the assembly diagnostic for this line, or `null` |

`EditorRow` is built by joining the buffer's lines (keyed by position) with the assembled rows
(keyed by the line number they report). This join is the one thing in the plan that rests on an
unverified fact — see the spike below.

The cycle rule carries over unchanged and keeps its existing justifications: read from the opcode
table of the CPU the session is attached to, never invented, never guessed from the source. With no
CPU there are no cycles, and `NoCpuReason` (`AssemblerListingSource.cs:46-50`) already says so.

---

## Layout of the pane

The code column is a real `TextView`; the four columns to its left are a **read-only gutter** painted
alongside it, on a `View` of its own.

The gutter is read-only on purpose. If the address and the bytes were part of the editable text,
they could be typed over, and a wrong address looks exactly as trustworthy as a right one. Kept
separate, they cannot be corrupted by editing, and the cursor's line decides which row is current.

Sync between the two is one thing: the gutter reads the code view's scroll offset and paints the same
rows from the same list. `ListingView` already scrolls by row and already handles
`ScrollLines`/`PageUp`/`PageDown`/`Home`/`End`, so that logic moves rather than being rewritten.

> **Unverified, settled by step 1:** `Terminal.Gui.TextView` exists in the pinned **2.0.0** package
> (`WinASM65.Monitor/WinASM65.Monitor.csproj`) and behaves as an editable multi-line buffer. No
> `TextView`, `TextFormatter`, `WordWrap` or `Multiline` appears anywhere in `WinASM65.Monitor` today,
> so this would be the first use of it here. Could not be confirmed from the command line.

---

## Keys

| Key | Effect | Change |
|---|---|---|
| `F3` | focus the editor | was "listing pane"; the editor is the centre pane now |
| `F5` | build | unchanged |
| `F6` | save the source, then say what was written | new, and the only free function key |
| `F8` | set a breakpoint on the line under the cursor | was reading `_listingPane.CursorAddress`; now reads the editor row's address |
| `F1` | help, now listing `F6` | text change |

`F8` gets more correct: today the cursor can sit on a comment row, whose address is `-1`, and the
listing pane has no answer. An editor row always states its own address.

`F5` does **not** need a dirty-buffer refusal any more. That was in the previous plan because the
listing was built from the file while the buffer held unsaved text — a window in which the screen
disagreed with the file. With the columns derived from the buffer itself, there is no window.

Bound keys today: `F1` help, `F2` file tree, `F3` listing, `F4` RAM, `F5` build, `F7` save state,
`F8` breakpoint, `F9` run, `F10` step, `F12` video *(unbound)*, `Ctrl+A` assistant *(unbound)*,
`Ctrl+Q` quit. **`F6` is free**, which is why it is save rather than a renumbering.

---

## Saving

A source editor that saves with `File.WriteAllText` changes the whole file's line endings and
encoding, turning a one-character edit into a whole-file diff. `example_bomberman-nes/*.NAS` is CRLF.

`SourceDocument.Write` must remember the encoding and the line ending when the document is loaded,
write back with both, and **refuse when the file mixes line endings**, naming the file.

New `WinASM65.Monitor/Shell/SourceDocument.cs` mirrors `IListingSource`'s shape:

| Member | Answer |
|---|---|
| `IReadOnlyList<string> Read(string sourceFile, out string problem)` | the file's lines, or a reason |
| `bool Write(string sourceFile, string text, out string problem)` | what was written, or why not |

It confines paths the way `OpenSource` does, and
`ASourceOutsideTheDirectoryIsNotSilentlyAccepted` must keep passing. The pane never touches
`System.IO`.

`WinASM65/src/TextFormat/BasicSource.cs` is a *parser* (`Read(text, diagnostics)` → auto-numbered
lines) in the core assembly, which stays UI-free. This plan does not touch it.

---

## Refusals

The project's rule is that a failure is a string, never an exception — "a pane that could take the
shell down on a bad path would make the shell worse than the REPL it replaces". Every one of these
lands on the line under the status bar:

- the source cannot be read — named path, and the reason
- the source is outside the directory being edited
- the file cannot be written — permission, lock, or mixed line endings, by name
- the buffer does not assemble — the line number and the diagnostic, and the editor keeps the text
- no CPU, so no cycles — `NoCpuReason`, unchanged

A line that fails to assemble still shows its text, its line number, and its diagnostic. It never
shows bytes, because it emitted none.

---

## Ordered steps

1. **Spike, two questions, one throwaway test, then delete it.**
   - Assemble a string with comments, blanks and equates through `SourceListingService.ListSourceText`
     and print `(lineNumber, address, bytes)` per row. Confirm that emitted rows carry their **true
     buffer line number**, and record whether row count equals buffer line count. The editor's join
     rests on this; if line numbers do not line up, the join has to be rebuilt and step 4 grows.
   - Confirm `Terminal.Gui.TextView` in 2.0.0: construct it, set text, read it back, and observe
     arrows, Home/End, PageUp/PageDown, and how it reports the cursor's line.
2. **`SourceDocument`**, read and write, with path confinement, encoding and line-ending
   preservation. Tested on its own, before any pane exists.
3. **Widen the seam.** `IListingSource` gains a text-based query; `AssemblerListingSource` implements
   it and stays the only class that knows the library. Add `EditorRow` and the join.
4. **`SourceEditorPane`** — the `TextView` plus the gutter — rendered through `FakeDriver` **inside**
   the run loop, as `ShellChromeRenderTests` already does. Read a frame outside the loop and every
   assertion passes on an empty buffer.
5. **Retire the listing pane.** `ListingPane` and `ListingView` go; their column rendering and cursor
   handling move into the gutter. `ShellWindow` owns the editor instead. **No layout change.**
6. **Keys.** `F6` saves, `F8` reads the address from the editor row, `F3` focuses the editor.
7. **Help text** and the `F1` screen listing the keys.
8. **Full suite**, `-warnaserror`, then a manual pass in the shell.

Each step lands green on its own. A step that needs the next one to be visible is the wrong split.

---

## Validation

New:

- the editor shows every line of the file, comments and blanks included, each with its line number
- a line that emits bytes shows the address, the bytes and the documented cycles
- a comment or an equate shows no address and no bytes, and its line is not misnumbered
- an edit on one line re-derives the addresses of the lines after it, without a build
- a line that does not assemble keeps its text and shows its diagnostic, and emits no bytes
- `F6` writes the buffer and reports what it wrote; an unchanged buffer writes nothing and says so
- `F6` preserves CRLF and the original encoding
- `F6` on mixed line endings refuses and says why
- a source outside the edited directory is refused
- `F8` sets a breakpoint on the address of the cursor's line, including when that line emits nothing
- a read or write failure reaches the message line, not out of the run loop

Changed: the `F1` help text.

Unchanged, and must stay green: `ShellLayoutTests` (no layout change), `ShellEditorKeyTests`,
`ShellInitialSourceTests`, `ShellChromeRenderTests`, `RamDumpTests`.

---

## Out of scope

- **Syntax colouring.** The lexer already emits role-tagged runs and the theme has the roles, but
  painting them inside a `TextView` is its own work, and it is not needed in order to edit.
- Multi-file editing, tabs, undo/redo, find and replace, goto line.
- Anything in `Bridge/` — both Lua scripts are measured and correct.
- The video pane and the assistant (`F12`, `Ctrl+A`), still unbound.

---

## Risks

**The join.** Everything in the gutter rests on emitted rows carrying their true buffer line number.
`ListingRow.LineNumber` is documented as 0 for a non-emitting line and `Translate` passes the
library's value through unchanged (`AssemblerListingSource.cs:241`), so the claim is plausible but
unconfirmed. Step 1 settles it before step 3 depends on it, and the fallback — rebuild the mapping
by walking both sequences — is known and local to one class.

**Key routing.** A real `TextView` and a shell that binds function keys globally both have opinions
about arrows, Home/End and PageUp/PageDown. If a key is swallowed the symptom is "the editor does not
scroll", which is the complaint this work starts from — so it is tested per pane.

**Writing.** Losing a source file to a careless save is worse than having no editor at all, which is
why `SourceDocument.Write` preserves encoding and line endings and refuses when it cannot.