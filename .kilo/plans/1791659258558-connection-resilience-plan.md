# WinASM65 Monitor - Connection Resilience & Emulator Control Plan

## Overview
Add connection recovery, emulator launch/control from monitor, and reliable RAM refresh.

## Current State
- Connection established at startup via `Program.Connect()` with retry loop
- No reconnection once connection drops during operation
- RAM refresh relies solely on `PUSH RAM` events from bridge
- Emulator launch only via CLI args (`--mesen --rom`)
- Emulator control via keybindings (F9/F10/F8/F12)

## Changes Required

### 1. Manual Reconnect Command
**Files**: `Program.cs`, `BridgeMemoryBackend.cs`, `MonitorSession.cs`, `ShellWindow.cs`

- Add `Connect` command to `MonitorSession.Execute()`: `CONNECT [port]`
- Add reconnect method to `BridgeMemoryBackend` that recreates `ProtocolClient`
- Add "Reconnect" menu item to file tree context menu
- Add status bar indicator: `Connected` / `Disconnected - Press R to reconnect`
- Add keybinding `R` (or `Ctrl+R`) to trigger reconnect

### 2. Launch Emulator from Monitor
**Files**: `Program.cs` (extract `LaunchEmulator`), `MonitorSession.cs`, `ShellWindow.cs`

- Extract `LaunchEmulator` logic into reusable `EmulatorLauncher` class
- Add `EMULATOR START <rom> [--emulator mesence|mesen2] [--port <n>]` command
- Add "Launch Emulator" to file tree context menu (for ROM files)
- Add to main menu / toolbar
- Support: ROM path, emulator type, port, optional arguments

### 3. Emulator Control (Already Sufficient)
No code changes needed. Existing:
- F9 = Resume, F10 = Step, F8 = Breakpoint, F12 = Toggle ROM pane
- REPL commands: PAUSE, RESUME, STEP, RESET, BREAK

### 4. Reliable RAM Refresh (Polling Fallback)
**Files**: `BridgeMemoryBackend.cs`, `ShellWindow.cs`, `RamPane.cs`

- Add periodic background `READ` polling when PUSH events stop arriving
- Configurable interval (default 500ms, adjustable)
- Fallback: if no PUSH received for 2x interval, start polling
- Stop polling when PUSH events resume
- Expose polling status in UI ("RAM: Live" / "RAM: Polling")

## Data Flow

```
┌─────────────┐     CONNECT cmd      ┌──────────────────┐
│  Monitor    │ ──────────────────►  │ BridgeMemory     │
│  Session    │                      │ Backend          │
└─────────────┘                      │ - new Protocol   │
       ▲                              │   Client         │
       │ PUSH / Poll                  └────────┬─────────┘
       │                                      │
       ▼                                      ▼
┌─────────────┐                      ┌──────────────────┐
│   Shell     │ ◄──────────────────  │  Lua Bridge      │
│  (UI)       │   RAM updates        │  (emulator)      │
└─────────────┘                      └──────────────────┘
```

## Risk Mitigation
- Reconnect uses same `Connect()` retry logic (15s timeout, 250ms retry)
- Polling uses existing `READ` command with `MaxReadLength` bound
- Emulator launch validates paths before starting process
- All new commands follow existing error pattern (OK/ERR responses)

## Validation
1. Start monitor, connect to bridge
2. Kill bridge process → monitor shows "Disconnected"
3. Press R / run CONNECT → reconnects successfully
4. Launch emulator via UI → bridge starts, monitor connects
5. Run emulator without PUSH support → RAM shows "Polling" and updates
6. All existing tests pass (962 tests)