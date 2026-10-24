# WinASM65 for VS Code

6502 assembly for [WinASM65](https://github.com/CodewinBzn/WinASM65) projects.

- **Debug on Mesen2.** Press F5 in a folder that has a `config.json`. The project is built, Mesen2 starts with the WinASM65 bridge, and you debug in your `.nas` / `.asm` sources:
  - breakpoints in the gutter
  - continue, step over (steps over `JSR` calls), step into, step out and pause
  - registers, flags and the zero page in the Variables view
  - the 64 KiB address space in the hex memory view
- **Debug console.** Type a symbol, a register or `$0010` to see its value. Monitor commands (`CPU`, `READ $0200 16`, `BREAK LIST`, …) are passed straight to the monitor.
- **Listing column.** Choose **Show Listing** from the file's menu in the Explorer, from the editor's right-click menu, from the line-number margin, or with the button in the editor title bar. Each line then shows the address and bytes it assembled to, in a column to its left, as in a `.lst`. The column follows your edits before you save.
- **RAM panel.** All 2 KiB of RAM ($0000-$07FF) in hex and ASCII. It opens at the first stop and refreshes at every stop:
  - bytes changed since the last stop are highlighted;
  - the zero page and the stack are shaded, and the byte at SP is marked;
  - hovering a byte shows its address, value and any names the build gives it;
  - double-click a byte to change it;
  - tick **Live** to keep it updating while the game runs.
- **Build task.** "WinASM65: build" is offered for each `config.json` in the workspace. Errors go to the Problems panel.
- **Highlighting.** Mnemonics, directives, labels and numbers are coloured, using the same mnemonic and directive tables as the assembler.

## Requirements

- **Mesen2 2.1.1.** Set its path in one of these places:
  - `Emulator.Path` in `config.json`
  - `"emulatorPath"` in `launch.json`
  - the `winasm65.emulatorPath` setting
- **Single instance.** Mesen2 runs as a single instance, so close any open Mesen2 window before you debug.
- **Mesen2 settings.** The first launch writes the `settings.json` entries the bridge needs to open a socket.

## launch.json

```jsonc
{
  "type": "winasm65",
  "request": "launch",
  "name": "Debug on Mesen2",
  "config": "${workspaceFolder}/config.json",
  "stopOnEntry": false
}
```

To use a Mesen2 that is already running the bridge, use `"request": "attach"` with its `"port"`.

## Banks

Two banks assembled at the same CPU address are told apart by their offset in PRG-ROM. A breakpoint stops only in the bank of the line it was set on.
