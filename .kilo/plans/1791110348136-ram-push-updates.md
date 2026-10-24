# True Push-Based RAM Updates via Bridge Socket

## Goal
Eliminate monitor-side pooling entirely. The bridge sends RAM changes to the
monitor in real time through the existing TCP socket. The monitor only applies
what it receives — no timer, no `READ` commands for RAM.

## RAM Window: $0000-$3FFF
The user confirmed the RAM pane should cover $0000-$3FFF (16 384 bytes).
Current implementation covers $0000-$07FF (2 KB).

- `RamDump.WindowEnd` changes from `0x07FF` to `0x3FFF`.
- Row count grows from 128 to 1024.
- Region marking stays the same: ZeroPage $0000-$00FF, Stack $0100-$01FF,
  Free $0200-$3FFF.
- `BytesPerRow` stays at 16.

## Architecture Change
The current `ProtocolClient.Send()` is strictly synchronous: it writes a command
and blocks waiting for exactly one response line. Unsolicited data from the
bridge would corrupt this. To accept push data we add a **dedicated receiver
thread** that continuously reads from the socket and dispatches lines:

```
Receiver thread:  ── reads line ──▶
                     ├─ starts with OK/ERR  → response queue → unblocks Send()
                     └─ starts with PUSH    → RamChanged event → UI update
```

`Send()` writes its command, then dequeues from the response queue, skipping any
push notifications that arrive before the expected response.

## Bridge Changes (`bridge_mesen2.lua`)

1. Add a module-scope dirty buffer:
   ```lua
   local ramDirty = {}   -- [address] = byte
   local ramDirtyCount = 0
   ```
2. Install a write watchpoint on the full NES RAM range ($0000-$FFFF) using
   `emu.addMemoryCallback`. On every write while running, store the new byte in
   `ramDirty` and increment `ramDirtyCount`. Do NOT return the value (let the
   write proceed normally — this is observation, not interception).
3. In `on_tick` (fires every ~16 ms while running), after draining commands:
   - If `ramDirtyCount > 0`, format a push line:
     ```
     PUSH RAM $addr:byte $addr:byte ...
     ```
     Cap payload to stay within `MaxReadLength` (4096 bytes).
   - `client:send(line)` — this is an unsolicited send, not a response.
   - Clear `ramDirty` and reset `ramDirtyCount`.
4. Never send `PUSH` while in `serve_stopped` (blocking loop for stopped machine).

## Protocol Changes

1. Add `MaxPushPayload = 4096` to `MonitorProtocol`.
2. Add parsing:
   ```csharp
   bool TryParsePushRam(string payload,
       out List<(ushort Address, byte Value)> changes);
   ```

## `ProtocolClient` Changes

1. Add fields:
   - `ConcurrentQueue<string> _responses`
   - `CancellationTokenSource _receiverCts`
   - `Task _receiverTask`
   - `event Action<string> PushReceived`
2. `Connect()`:
   - Start `_receiverTask` that loops `ReadLine()` on the `NetworkStream`.
   - Each line goes to `_responses` queue; if it starts with `PUSH`, also
     raise `PushReceived`.
3. `Send(string)`:
   - `WriteLine(command)`
   - `Dequeue lines until one starts with OK or ERR`
   - Any skipped `PUSH` lines are dispatched via `PushReceived` before
     returning the response.
4. `Disconnect()`: cancel receiver task, clear queues.

## `IMemoryBackend` Changes

Add:
```csharp
event Action<IReadOnlyList<(ushort Address, byte Value)>> RamChanged;
```

## `BridgeMemoryBackend` Changes

1. Subscribe to `_client.PushReceived`.
2. When a `PUSH RAM` line arrives, parse it with `MonitorProtocol.TryParsePushRam`.
3. Raise `RamChanged` with the parsed changes.

## `RamPane` Changes (`ShellPanes.cs`)

1. Add `ApplyChanges(IReadOnlyList<(ushort, byte)>)`:
   - For each (addr, byte), update the cached `RamDumpRow` bytes.
   - If the row's formatted text changes, mark it dirty.
   - Call `SetNeedsDraw()` (no `_list.Source` reassignment — avoids scroll reset).
2. Subscribe to `RamChanged` and call `ApplyChanges` via `Application.Invoke`.

## `ShellRunner` Changes

1. Remove the `Application.AddTimeout` RAM timer entirely.
2. `RefreshRam()` becomes a one-time full read (called on layout/resume/stop),
   not a recurring timer.

## `ShellWindow` Changes

1. Remove all RAM refresh throttling logic from `RefreshRam()`.
2. `RefreshPanes()` still calls `_ram.Refresh(...)` once for the initial state
   after assembly/stop events.

## Files Touched
| File | Change |
|------|--------|
| `bridge_mesen2.lua` | Dirty buffer, write watchpoint, `PUSH RAM` on `on_tick` |
| `MonitorProtocol.cs` | `TryParsePushRam`, `MaxPushPayload` |
| `ProtocolClient.cs` | Receiver thread, response queue, `PushReceived` event |
| `IMemoryBackend.cs` | `RamChanged` event |
| `BridgeMemoryBackend.cs` | Wire `PushReceived` → `RamChanged` |
| `ShellPanes.cs` | `ApplyChanges` on `RamPane` |
| `ShellRunner.cs` | Remove RAM timer |
| `ShellWindow.cs` | Simplify `RefreshRam`, remove throttling |

## Risks
- **Write-callback overhead**: watching $0000-$FFFF fires on every CPU write.
  Mitigation: Lua table insert is O(1); only active while client is connected.
- **TCP socket saturation**: rapid writes could outrun TCP buffering. Mitigation:
  cap payload per push, batch per tick (16 ms window), Lua `client:send` is
  non-blocking (`settimeout(0)`).
- **Missed push during command**: if a `PUSH` arrives while `Send()` is writing
  a command, the receiver thread buffers it. `Send()` skips it before reading
  its own response. No data loss.
- **Backpressure**: if the .NET side is too slow, the receiver queue grows.
  Mitigation: bounded `ConcurrentQueue` — drop oldest push on overflow and do a
  full `Refresh` fallback.

## Validation
1. Build clean (`-warnaserror`).
2. All 963 tests pass.
3. Manual: run emulator, verify RAM pane updates within 50-100 ms of a write
   (e.g., `STA $0200` in source, assemble, run, watch RAM $0200).
