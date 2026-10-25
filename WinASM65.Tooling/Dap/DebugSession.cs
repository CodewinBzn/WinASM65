using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using WinASM65.Cpu;
using WinASM65.Monitor;
using WinASM65.Monitor.Debugging;
using WinASM65.Monitor.Emulators;
using WinASM65.Projects;
using WinASM65.Systems;

namespace WinASM65.Tooling.Dap
{
    /// <summary>
    /// One debugging session of a 6502 machine, driven by the Debug Adapter Protocol.
    ///
    /// Every request and every event from the machine is handled on one thread, the
    /// session loop (<see cref="Run"/>). The bridge reports stops on its receiver
    /// thread, which must never wait for a reply, so its events are only queued here;
    /// a handler on the loop is then free to read registers or memory.
    /// </summary>
    public sealed class DebugSession : IDisposable
    {
        private const int ThreadId = 1;
        private const int RegistersReference = 1;
        private const int FlagsReference = 2;
        private const int ZeroPageReference = 3;
        private const int AddressSpace = 0x10000;
        private const int MaxChunk = 4096;
        private const byte OpcodeJsr = 0x20;

        private static readonly HashSet<string> MonitorVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "HELP", "?", "PING", "READ", "WRITE", "DISASM", "PAUSE", "RESUME", "STEP", "RESET", "BREAK",
            "STATE", "ASSEMBLE", "BUILD", "ROM", "LOAD", "UNITS", "CPU"
        };

        private readonly IDebugEnvironment _environment;
        private readonly Action<JsonObject> _send;
        private readonly BlockingCollection<Action> _work = new BlockingCollection<Action>();
        private int _seq;

        private IMemoryBackend _backend;
        private IEmulatorProcess _emulator;
        private MonitorSession _monitor;
        private SourceMap _map = SourceMap.Empty;

        // The last successful build, and the machine it runs on: from the project's
        // Target.System, else from the bridge's HELLO, else from the image's header.
        private ProjectBuildResult _build;
        private SystemDefinition _system = new Generic6502System(null);
        private bool _launched;
        private bool _configured;
        private bool _stopOnEntry;

        // The pause of a launch, until its stop arrives. The stop is reported by
        // the bridge on its own thread and may come after configurationDone; it is
        // the adapter's own doing and must not reach VS Code as a stop.
        private bool _launchPausePending;
        private bool _terminated;
        private Timer _watchdog;

        // Breakpoints as VS Code set them, per source file.
        private readonly Dictionary<string, List<UserBreakpoint>> _bySource =
            new Dictionary<string, List<UserBreakpoint>>(StringComparer.OrdinalIgnoreCase);
        private int _nextBreakpointId = 1;

        // What the bridge holds: user breakpoints counted per address, plus the
        // temporary ones a step over or out plants. An address is set on the bridge
        // while either side wants it.
        private readonly Dictionary<int, int> _userAddresses = new Dictionary<int, int>();
        private readonly HashSet<int> _temporary = new HashSet<int>();
        private string _temporaryReason;

        // Set when the session itself made the machine run, so the RESUMED that
        // follows is not reported as a run nobody asked for.
        private bool _resumeExpected;

        private StopInfo _stop;

        public DebugSession(IDebugEnvironment environment, Action<JsonObject> send)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _send = send ?? throw new ArgumentNullException(nameof(send));
        }

        /// <summary>True once the client disconnected; <see cref="Run"/> then returns.</summary>
        public bool Finished { get; private set; }

        /// <summary>Queues a request from the client.</summary>
        public void Dispatch(JsonObject request)
        {
            Post(() => Handle(request));
        }

        public void Post(Action action)
        {
            if (!_work.IsAddingCompleted)
                _work.Add(action);
        }

        /// <summary>The session loop. Returns when the client disconnects.</summary>
        public void Run()
        {
            foreach (Action action in _work.GetConsumingEnumerable())
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Output("internal error: " + ex.Message + "\n", "stderr");
                }
                if (Finished)
                    break;
            }
        }

        /// <summary>Called when the client's input ended without a disconnect.</summary>
        public void InputClosed()
        {
            Post(() => { Shutdown(_launched); Finished = true; });
        }

        public void Dispose()
        {
            _watchdog?.Dispose();
            _work.CompleteAdding();
        }

        // ------------------------------------------------------------- dispatch

        private void Handle(JsonObject request)
        {
            string command = Str(request, "command");
            JsonObject args = request["arguments"] as JsonObject ?? new JsonObject();
            try
            {
                switch (command)
                {
                    case "initialize": Initialize(request); break;
                    case "launch": Launch(request, args); break;
                    case "attach": Attach(request, args); break;
                    case "setBreakpoints": SetBreakpoints(request, args); break;
                    case "setExceptionBreakpoints": Respond(request, new JsonObject { ["breakpoints"] = new JsonArray() }); break;
                    case "configurationDone": ConfigurationDone(request); break;
                    case "threads": Threads(request); break;
                    case "stackTrace": StackTrace(request); break;
                    case "scopes": Scopes(request); break;
                    case "variables": Variables(request, args); break;
                    case "continue": Continue(request); break;
                    case "next": Next(request); break;
                    case "stepIn": StepIn(request); break;
                    case "stepOut": StepOut(request); break;
                    case "pause": Pause(request); break;
                    case "readMemory": ReadMemory(request, args); break;
                    case "writeMemory": WriteMemory(request, args); break;
                    case "evaluate": Evaluate(request, args); break;
                    case "winasm65.symbols": Symbols(request, args); break;
                    case "winasm65.system": DescribeSystem(request); break;
                    case "winasm65.memory.read": ReadSpace(request, args); break;
                    case "winasm65.memory.write": WriteSpace(request, args); break;
                    case "terminate": Terminate(request); break;
                    case "disconnect": Disconnect(request, args); break;
                    default:
                        Fail(request, "unsupported request: " + command);
                        break;
                }
            }
            catch (MonitorException ex)
            {
                Fail(request, ex.Message);
            }
            catch (IOException ex)
            {
                Fail(request, "the bridge connection failed: " + ex.Message);
            }
        }

        // ------------------------------------------------------- initialisation

        private void Initialize(JsonObject request)
        {
            Respond(request, new JsonObject
            {
                ["supportsConfigurationDoneRequest"] = true,
                ["supportsReadMemoryRequest"] = true,
                ["supportsWriteMemoryRequest"] = true,
                ["supportsEvaluateForHovers"] = true,
                ["supportsTerminateRequest"] = true,
                ["supportsSteppingGranularity"] = false,
                ["supportsRestartRequest"] = false,
                ["supportsSetVariable"] = false
            });
        }

        private void Launch(JsonObject request, JsonObject args)
        {
            string config = ConfigPath(args);
            if (config == null)
            {
                Fail(request, "launch needs \"config\": the project's config.json");
                return;
            }

            ProjectBuildResult build = BuildProject(config, true);
            if (build == null || !build.Success)
            {
                Fail(request, "the build failed; see the debug console");
                return;
            }

            // The project's Emulator section, with what launch.json overrides.
            EmulatorConfiguration emulator = _environment.ConfiguredEmulator(config) ?? new EmulatorConfiguration();
            string emulatorPath = Str(args, "emulatorPath");
            if (!string.IsNullOrWhiteSpace(emulatorPath))
                emulator.Path = emulatorPath;
            string emulatorName = Str(args, "emulator");
            if (!string.IsNullOrWhiteSpace(emulatorName))
                emulator.Name = emulatorName.Trim().ToLowerInvariant();
            string bridge = Str(args, "bridge");
            if (!string.IsNullOrWhiteSpace(bridge))
                emulator.Bridge = Path.IsPathRooted(bridge) ? bridge : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(config), bridge));
            if (string.IsNullOrWhiteSpace(emulator.Path))
            {
                Fail(request, "no emulator: set \"emulatorPath\" in launch.json or \"Emulator.Path\" in config.json");
                return;
            }
            IEmulatorAdapter adapter = EmulatorAdapters.For(emulator);

            string rom = Str(args, "rom");
            if (string.IsNullOrWhiteSpace(rom))
                rom = build.ImagePath;
            int port = Int(args, "port", 0);
            if (port <= 0)
                port = _environment.FreePort();
            int timeout = Int(args, "connectTimeoutMs", BridgeConnector.DefaultTimeoutMs);

            Output("starting " + adapter.DisplayName + " (" + Path.GetFileName(emulator.Path) + ") with " + rom
                + " on port " + port + "\n", "console");
            _emulator = _environment.Launch(emulator, rom, port);
            _launched = true;

            IMemoryBackend backend = ConnectWhileAlive(port, timeout);
            if (backend == null)
            {
                _emulator.Kill();
                Fail(request, _emulator.HasExited
                    ? "the emulator exited before its bridge answered." + (adapter.EarlyExitHint != null ? " " + adapter.EarlyExitHint : "")
                    : "no bridge answered on 127.0.0.1:" + port + " within " + timeout / 1000 + "s");
                return;
            }

            if (!Adopt(request, backend, config))
                return;

            _stopOnEntry = Bool(args, "stopOnEntry", false);
            _emulator.Exited += () => Post(() => Terminated("the emulator exited"));

            // Stopped until configurationDone, so breakpoints VS Code is about to
            // send are in place before the program runs a single frame.
            _launchPausePending = true;
            _backend.Pause();
            Respond(request, null);
            SendEvent("initialized", null);
        }

        private void Attach(JsonObject request, JsonObject args)
        {
            int port = Int(args, "port", BridgeConnector.DefaultPort);
            string config = ConfigPath(args);
            if (config != null && BuildProject(config, false) == null)
            {
                Fail(request, "the build failed; see the debug console");
                return;
            }

            IMemoryBackend backend = _environment.Connect(port, Int(args, "connectTimeoutMs", BridgeConnector.DefaultTimeoutMs));
            if (!Adopt(request, backend, config))
                return;

            _stopOnEntry = Bool(args, "stopOnAttach", false);
            Respond(request, null);
            SendEvent("initialized", null);
        }

        /// <summary>Retries the connection while the emulator it waits for is still alive.</summary>
        private IMemoryBackend ConnectWhileAlive(int port, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (_emulator != null && _emulator.HasExited)
                    return null;
                try
                {
                    return _environment.Connect(port, 1000);
                }
                catch (MonitorException) { }
                catch (System.Net.Sockets.SocketException) { }
                catch (IOException) { }
                Thread.Sleep(200);
            }
            return null;
        }

        private bool Adopt(JsonObject request, IMemoryBackend backend, string config)
        {
            IExecutionEvents events = backend as IExecutionEvents;
            if (events == null || !events.ReportsExecutionState || !(backend is ICpuStateSource))
            {
                string name = backend.EmulatorName + " " + backend.EmulatorVersion;
                backend.Dispose();
                Fail(request, name + " cannot be debugged: its bridge does not report when the machine stops"
                    + " (it needs STATUS, PAUSE and the PUSH STOPPED line of the bridge contract, see bridges.html).");
                return false;
            }

            _backend = backend;
            BridgeMemoryBackend bridge = backend as BridgeMemoryBackend;
            _system = SystemRegistry.Resolve(_build != null && _build.Target != null ? _build.Target.SystemId : null,
                bridge != null ? bridge.BridgeSystem : null, _build != null ? _build.Image : null);
            if (_build != null)
                _map = SourceMapBuilder.Build(_build, _system);

            _monitor = new MonitorSession(backend, CpuOf(_build),
                config != null ? Path.GetDirectoryName(config) : Directory.GetCurrentDirectory());
            events.Stopped += stop => Post(() => OnStopped(stop));
            events.Resumed += () => Post(OnResumed);
            _watchdog = new Timer(_ => Post(() =>
            {
                if (_backend != null && !_backend.IsConnected)
                    Terminated("the bridge closed the connection");
            }), null, 1000, 1000);

            Output("connected to " + backend.EmulatorName + " " + backend.EmulatorVersion
                + ", " + _system.DisplayName + "\n", "console");
            return true;
        }

        /// <summary>The processor the project was built for, the plain 6502 when it says nothing.</summary>
        private static ICpuInstructionSet CpuOf(ProjectBuildResult build)
        {
            try
            {
                return CpuFactory.Create(build != null && build.Target != null ? build.Target.CpuName : "6502");
            }
            catch (ArgumentException)
            {
                return new Cpu6502();
            }
        }

        private ProjectBuildResult BuildProject(string config, bool writeImage)
        {
            Output("building " + config + "\n", "console");
            ProjectBuildResult result;
            try
            {
                result = _environment.Build(config, writeImage);
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is MonitorException)
            {
                Output(config + ": " + ex.Message + "\n", "stderr");
                return null;
            }

            foreach (WinASM65.Core.Diagnostic diagnostic in result.Diagnostics)
                Output(FormatDiagnostic(diagnostic, config) + "\n", "stderr");
            if (!result.Success)
                return null;

            _build = result;
            _map = SourceMapBuilder.Build(result);
            Output("built " + result.Units.Count + " unit(s), " + _map.Lines.Count + " mapped line(s)\n", "console");
            return result;
        }

        internal static string FormatDiagnostic(WinASM65.Core.Diagnostic diagnostic, string config)
        {
            string file = string.IsNullOrEmpty(diagnostic.Location.FilePath) ? config : diagnostic.Location.FilePath;
            int line = diagnostic.Location.LineNumber;
            return file + (line > 0 ? ":" + line.ToString(CultureInfo.InvariantCulture) : "") + ": " + diagnostic.Message;
        }

        private void ConfigurationDone(JsonObject request)
        {
            Respond(request, null);
            _configured = true;

            if (!_launched)
            {
                // Attached to a machine that is already doing something: report it
                // as it is, and stop it only when asked.
                if (_stopOnEntry)
                    _backend.Pause();
                else if (((IExecutionEvents)_backend).IsStopped)
                    OnStopped(((IExecutionEvents)_backend).LastStop);
                return;
            }

            // Launched, now that every breakpoint is set: a cartridge runs from its
            // reset vector, a loaded program from where it was loaded. Stop on its
            // first instruction when asked.
            _resumeExpected = true;
            if (_system.Boot == BootKind.ResetVector)
            {
                if (_stopOnEntry)
                    AddTemporary(ReadWord(0xFFFC), "entry");
                _backend.Reset();
                return;
            }

            ushort? entry = _build != null && _build.Target != null
                ? (_build.Target.RunAddress ?? _build.Target.LoadAddress)
                : null;
            if (_stopOnEntry && entry.HasValue)
                AddTemporary(entry.Value, "entry");
            _backend.Resume();
        }

        // ---------------------------------------------------------- breakpoints

        private sealed class UserBreakpoint
        {
            public int Id;
            public int Line;
            public bool Verified;
            public string Message;
            public List<MappedLine> Targets = new List<MappedLine>();
        }

        private void SetBreakpoints(JsonObject request, JsonObject args)
        {
            string path = Str(args["source"] as JsonObject, "path");
            if (path == null)
            {
                Fail(request, "setBreakpoints needs a source path");
                return;
            }
            path = SourceMap.Normalize(path);

            List<UserBreakpoint> previous;
            if (_bySource.TryGetValue(path, out previous))
            {
                foreach (UserBreakpoint old in previous)
                    foreach (MappedLine target in old.Targets)
                        ReleaseUserAddress(target.Address);
            }

            List<UserBreakpoint> current = new List<UserBreakpoint>();
            JsonArray result = new JsonArray();
            foreach (JsonNode node in args["breakpoints"] as JsonArray ?? new JsonArray())
            {
                int line = Int(node as JsonObject, "line", 0);
                UserBreakpoint breakpoint = new UserBreakpoint { Id = _nextBreakpointId++, Line = line };

                int emitting;
                if (_map.TryNearestEmittingLine(path, line, out emitting))
                {
                    breakpoint.Line = emitting;
                    breakpoint.Targets.AddRange(_map.AtLine(path, emitting));
                    breakpoint.Verified = true;
                    foreach (MappedLine target in breakpoint.Targets)
                    {
                        if (!TryHoldUserAddress(target.Address, out string refusal))
                        {
                            breakpoint.Verified = false;
                            breakpoint.Message = refusal;
                        }
                    }
                    if (breakpoint.Verified && breakpoint.Targets.Any(t => _map.AtAddress(t.Address).Count > 1))
                        breakpoint.Message = "this address is shared with another bank; only this bank stops";
                }
                else
                {
                    breakpoint.Message = _map.Lines.Count == 0 ? "the project is not built yet" : "no code at or after this line";
                }

                current.Add(breakpoint);
                JsonObject reported = new JsonObject
                {
                    ["id"] = breakpoint.Id,
                    ["verified"] = breakpoint.Verified,
                    ["line"] = breakpoint.Line
                };
                if (breakpoint.Message != null)
                    reported["message"] = breakpoint.Message;
                result.Add(reported);
            }

            _bySource[path] = current;
            Respond(request, new JsonObject { ["breakpoints"] = result });
        }

        private bool TryHoldUserAddress(int address, out string refusal)
        {
            refusal = null;
            int count;
            _userAddresses.TryGetValue(address, out count);
            if (count == 0 && !_temporary.Contains(address) && _backend != null)
            {
                try
                {
                    _backend.AddBreakpoint(address, BreakpointKind.Exec);
                }
                catch (MonitorException ex)
                {
                    refusal = ex.Message;
                    return false;
                }
            }
            _userAddresses[address] = count + 1;
            return true;
        }

        private void ReleaseUserAddress(int address)
        {
            int count;
            if (!_userAddresses.TryGetValue(address, out count))
                return;
            if (count > 1)
            {
                _userAddresses[address] = count - 1;
                return;
            }
            _userAddresses.Remove(address);
            if (!_temporary.Contains(address))
                RemoveFromBridge(address);
        }

        private void AddTemporary(int address, string reason)
        {
            _temporaryReason = reason;
            if (_temporary.Add(address) && !_userAddresses.ContainsKey(address))
                _backend.AddBreakpoint(address, BreakpointKind.Exec);
        }

        private void ClearTemporary()
        {
            foreach (int address in _temporary)
            {
                if (!_userAddresses.ContainsKey(address))
                    RemoveFromBridge(address);
            }
            _temporary.Clear();
            _temporaryReason = null;
        }

        private void RemoveFromBridge(int address)
        {
            if (_backend == null)
                return;
            IBreakpointRemoval removal = _backend as IBreakpointRemoval;
            if (removal != null)
            {
                removal.RemoveBreakpoint(address, BreakpointKind.Exec);
                return;
            }
            _backend.ClearBreakpoints();
            foreach (int kept in _userAddresses.Keys.Concat(_temporary).Distinct())
                _backend.AddBreakpoint(kept, BreakpointKind.Exec);
        }

        // ------------------------------------------------------------ run state

        private void OnStopped(StopInfo stop)
        {
            if (stop == null || _terminated)
                return;
            _resumeExpected = false;
            _stop = stop;
            if (_launchPausePending && stop.Reason == StopReason.Pause)
            {
                _launchPausePending = false;
                return; // the launch pause, however late it is reported
            }
            if (!_configured)
                return; // nobody is looking yet

            string reason;
            List<int> hits = new List<int>();

            if (_temporary.Count > 0 && stop.Reason == StopReason.Breakpoint && _temporary.Contains(stop.Pc)
                && !HitsUserBreakpoint(stop, hits))
            {
                reason = _temporaryReason ?? "step";
                ClearTemporary();
            }
            else if (stop.Reason == StopReason.Breakpoint)
            {
                if (_userAddresses.ContainsKey(stop.Pc) && !HitsUserBreakpoint(stop, hits) && !_temporary.Contains(stop.Pc))
                {
                    // Another bank at the same address: not the line the user marked.
                    _resumeExpected = true;
                    _backend.Resume();
                    return;
                }
                ClearTemporary();
                reason = "breakpoint";
            }
            else
            {
                ClearTemporary();
                reason = stop.Reason == StopReason.Step ? "step" : "pause";
            }

            JsonObject body = new JsonObject
            {
                ["reason"] = reason,
                ["threadId"] = ThreadId,
                ["allThreadsStopped"] = true
            };
            if (hits.Count > 0)
                body["hitBreakpointIds"] = new JsonArray(hits.Select(id => (JsonNode)id).ToArray());
            SendEvent("stopped", body);
        }

        /// <summary>
        /// Whether a stop at a user breakpoint is in the bank the breakpoint was set
        /// in. Without a PRG offset on either side the address alone decides.
        /// </summary>
        private bool HitsUserBreakpoint(StopInfo stop, List<int> hits)
        {
            foreach (List<UserBreakpoint> list in _bySource.Values)
            {
                foreach (UserBreakpoint breakpoint in list)
                {
                    foreach (MappedLine target in breakpoint.Targets)
                    {
                        if (target.Address != stop.Pc)
                            continue;
                        if (!stop.PrgOffset.HasValue || !target.PrgOffset.HasValue || stop.PrgOffset == target.PrgOffset)
                        {
                            hits.Add(breakpoint.Id);
                            break;
                        }
                    }
                }
            }
            return hits.Count > 0;
        }

        private void OnResumed()
        {
            _stop = null;
            if (_resumeExpected || !_configured)
            {
                _resumeExpected = false;
                return;
            }
            // Someone else let the machine run (the emulator's own window).
            SendEvent("continued", new JsonObject { ["threadId"] = ThreadId, ["allThreadsContinued"] = true });
        }

        private void Continue(JsonObject request)
        {
            ClearTemporary();
            Run(() => _backend.Resume());
            Respond(request, new JsonObject { ["allThreadsContinued"] = true });
        }

        private void Next(JsonObject request)
        {
            int pc = CurrentPc();
            byte opcode = _backend.Read(pc, 1)[0];
            if (opcode == OpcodeJsr)
            {
                // Over the call: run until the instruction after it.
                AddTemporary((pc + 3) & 0xFFFF, "step");
                Run(() => _backend.Resume());
            }
            else
            {
                Run(() => _backend.Step());
            }
            Respond(request, null);
        }

        private void StepIn(JsonObject request)
        {
            Run(() => _backend.Step());
            Respond(request, null);
        }

        private void StepOut(JsonObject request)
        {
            // Best effort: the return address of the innermost JSR is the top of the
            // stack while no PHA has been left on it.
            CpuSnapshot cpu = ((ICpuStateSource)_backend).ReadCpuState();
            int low = _backend.Read(0x100 + ((cpu.Sp + 1) & 0xFF), 1)[0];
            int high = _backend.Read(0x100 + ((cpu.Sp + 2) & 0xFF), 1)[0];
            int target = ((high << 8) | low) + 1;
            AddTemporary(target & 0xFFFF, "step");
            Run(() => _backend.Resume());
            Respond(request, null);
        }

        private void Pause(JsonObject request)
        {
            _backend.Pause();
            Respond(request, null);
        }

        private void Run(Action action)
        {
            _resumeExpected = true;
            _stop = null;
            action();
        }

        private int CurrentPc()
        {
            if (_stop != null)
                return _stop.Pc;
            return ((ICpuStateSource)_backend).ReadCpuState().Pc;
        }

        // --------------------------------------------------------------- inspect

        private void Threads(JsonObject request)
        {
            Respond(request, new JsonObject
            {
                ["threads"] = new JsonArray(new JsonObject { ["id"] = ThreadId, ["name"] = _system.Cpu.Variant })
            });
        }

        private void StackTrace(JsonObject request)
        {
            int pc = CurrentPc();
            bool ambiguous;
            MappedLine line = _map.Resolve(pc, _stop == null ? null : _stop.PrgOffset, out ambiguous);

            int offset;
            string label = _map.NearestLabel(pc, out offset);
            string name = label == null ? "$" + pc.ToString("X4")
                : offset == 0 ? label : label + "+" + offset.ToString(CultureInfo.InvariantCulture);
            if (ambiguous)
                name += " (bank unknown)";

            JsonObject frame = new JsonObject
            {
                ["id"] = 1,
                ["name"] = name,
                ["line"] = line == null ? 0 : line.Line,
                ["column"] = line == null ? 0 : 1,
                ["instructionPointerReference"] = MemoryReference(pc)
            };
            if (line != null)
                frame["source"] = new JsonObject { ["name"] = Path.GetFileName(line.Path), ["path"] = line.Path };
            else
                frame["presentationHint"] = "subtle";

            Respond(request, new JsonObject { ["stackFrames"] = new JsonArray(frame), ["totalFrames"] = 1 });
        }

        private void Scopes(JsonObject request)
        {
            Respond(request, new JsonObject
            {
                ["scopes"] = new JsonArray(
                    new JsonObject { ["name"] = "Registers", ["variablesReference"] = RegistersReference, ["presentationHint"] = "registers", ["expensive"] = false },
                    new JsonObject { ["name"] = "Flags", ["variablesReference"] = FlagsReference, ["expensive"] = false },
                    new JsonObject { ["name"] = "Zero page", ["variablesReference"] = ZeroPageReference, ["expensive"] = true })
            });
        }

        private void Variables(JsonObject request, JsonObject args)
        {
            int reference = Int(args, "variablesReference", 0);
            JsonArray variables = new JsonArray();

            if (reference == RegistersReference || reference == FlagsReference)
            {
                CpuSnapshot cpu = ((ICpuStateSource)_backend).ReadCpuState();
                if (reference == RegistersReference)
                {
                    variables.Add(Variable("PC", "$" + cpu.Pc.ToString("X4"), MemoryReference(cpu.Pc)));
                    variables.Add(Variable("A", Byte(cpu.A), null));
                    variables.Add(Variable("X", Byte(cpu.X), null));
                    variables.Add(Variable("Y", Byte(cpu.Y), null));
                    variables.Add(Variable("SP", Byte(cpu.Sp), MemoryReference(0x100 + cpu.Sp)));
                    variables.Add(Variable("PS", Byte(cpu.Ps) + "  " + FlagString(cpu.Ps), null));
                    variables.Add(Variable("cycles", cpu.CycleCount.ToString(CultureInfo.InvariantCulture), null));
                }
                else
                {
                    string[] names = { "N", "V", "-", "B", "D", "I", "Z", "C" };
                    for (int bit = 7; bit >= 0; bit--)
                    {
                        if (bit == 5)
                            continue;
                        variables.Add(Variable(names[7 - bit], (cpu.Ps & (1 << bit)) != 0 ? "1" : "0", null));
                    }
                }
            }
            else if (reference == ZeroPageReference)
            {
                byte[] page = _backend.Read(0x0000, 0x100);
                for (int row = 0; row < 0x100; row += 16)
                {
                    string value = string.Join(" ", page.Skip(row).Take(16).Select(b => b.ToString("X2")));
                    variables.Add(Variable("$" + row.ToString("X2"), value, MemoryReference(row)));
                }
            }

            Respond(request, new JsonObject { ["variables"] = variables });
        }

        private static JsonObject Variable(string name, string value, string memoryReference)
        {
            JsonObject variable = new JsonObject { ["name"] = name, ["value"] = value, ["variablesReference"] = 0 };
            if (memoryReference != null)
                variable["memoryReference"] = memoryReference;
            return variable;
        }

        internal static string FlagString(int ps)
        {
            const string upper = "NV-BDIZC";
            char[] text = new char[8];
            for (int i = 0; i < 8; i++)
            {
                bool set = (ps & (0x80 >> i)) != 0;
                text[i] = upper[i] == '-' ? '-' : set ? upper[i] : char.ToLowerInvariant(upper[i]);
            }
            return new string(text);
        }

        private void ReadMemory(JsonObject request, JsonObject args)
        {
            int address = ParseMemoryReference(Str(args, "memoryReference")) + Int(args, "offset", 0);
            int count = Int(args, "count", 0);
            int start = Math.Max(0, Math.Min(address, AddressSpace));
            int end = Math.Max(start, Math.Min(address + count, AddressSpace));

            byte[] data = new byte[end - start];
            for (int done = 0; done < data.Length; done += MaxChunk)
            {
                int take = Math.Min(MaxChunk, data.Length - done);
                Buffer.BlockCopy(_backend.Read(start + done, take), 0, data, done, take);
            }

            JsonObject body = new JsonObject
            {
                ["address"] = MemoryReference(start),
                ["data"] = Convert.ToBase64String(data)
            };
            int unreadable = Math.Max(0, count - data.Length);
            if (unreadable > 0)
                body["unreadableBytes"] = unreadable;
            Respond(request, body);
        }

        private void WriteMemory(JsonObject request, JsonObject args)
        {
            int address = ParseMemoryReference(Str(args, "memoryReference")) + Int(args, "offset", 0);
            byte[] data = Convert.FromBase64String(Str(args, "data") ?? "");
            if (address < 0 || address + data.Length > AddressSpace)
            {
                Fail(request, "the write runs past $FFFF");
                return;
            }
            for (int done = 0; done < data.Length; done += MaxChunk)
            {
                int take = Math.Min(MaxChunk, data.Length - done);
                byte[] chunk = new byte[take];
                Buffer.BlockCopy(data, done, chunk, 0, take);
                _backend.Write(address + done, chunk);
            }
            Respond(request, new JsonObject { ["bytesWritten"] = data.Length });
            SendEvent("memory", new JsonObject
            {
                ["memoryReference"] = MemoryReference(address),
                ["offset"] = 0,
                ["count"] = data.Length
            });
        }

        private void Evaluate(JsonObject request, JsonObject args)
        {
            string expression = (Str(args, "expression") ?? string.Empty).Trim();
            string context = Str(args, "context") ?? "repl";

            if (context == "repl")
            {
                string line = expression.StartsWith(">", StringComparison.Ordinal) ? expression.Substring(1).Trim() : expression;
                string verb = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                if (expression.StartsWith(">", StringComparison.Ordinal) || MonitorVerbs.Contains(verb))
                {
                    if (verb.Equals("RESUME", StringComparison.OrdinalIgnoreCase) || verb.Equals("STEP", StringComparison.OrdinalIgnoreCase)
                        || verb.Equals("RESET", StringComparison.OrdinalIgnoreCase))
                        _resumeExpected = false; // let the machine's own events speak
                    string answer = string.Join("\n", _monitor.Execute(line));
                    Respond(request, new JsonObject { ["result"] = answer, ["variablesReference"] = 0 });
                    return;
                }
            }

            string result;
            string memoryReference;
            if (TryEvaluate(expression, out result, out memoryReference))
            {
                JsonObject body = new JsonObject { ["result"] = result, ["variablesReference"] = 0 };
                if (memoryReference != null)
                    body["memoryReference"] = memoryReference;
                Respond(request, body);
                return;
            }

            Fail(request, context == "repl"
                ? "not a symbol, register, address or monitor command (HELP lists the commands)"
                : "not a symbol, register or address");
        }

        /// <summary>
        /// A register, a symbol of the build, or an address written <c>$xxxx</c>.
        /// An address answers with the byte stored there, because that is what a
        /// reader hovering over <c>LDA $10</c> wants to know.
        /// </summary>
        private bool TryEvaluate(string expression, out string result, out string memoryReference)
        {
            result = null;
            memoryReference = null;
            if (string.IsNullOrEmpty(expression) || _backend == null)
                return false;

            string upper = expression.ToUpperInvariant();
            if (upper == "A" || upper == "X" || upper == "Y" || upper == "SP" || upper == "PC" || upper == "PS")
            {
                CpuSnapshot cpu = ((ICpuStateSource)_backend).ReadCpuState();
                int value = upper == "A" ? cpu.A : upper == "X" ? cpu.X : upper == "Y" ? cpu.Y
                    : upper == "SP" ? cpu.Sp : upper == "PS" ? cpu.Ps : cpu.Pc;
                result = upper == "PC" ? "$" + value.ToString("X4") : Byte(value);
                if (upper == "PC")
                    memoryReference = MemoryReference(value);
                return true;
            }

            long symbol;
            if (_map.Symbols.TryGetValue(expression, out symbol))
            {
                result = "$" + symbol.ToString("X4");
                if (symbol >= 0 && symbol < AddressSpace)
                {
                    memoryReference = MemoryReference((int)symbol);
                    result += "  [" + Byte(_backend.Read((int)symbol, 1)[0]) + "]";
                }
                return true;
            }

            int address;
            if (WinASM65.Monitor.Protocol.MonitorProtocol.TryParseAddress(expression, out address)
                && (expression.StartsWith("$", StringComparison.Ordinal) || expression.StartsWith("0x", StringComparison.OrdinalIgnoreCase)))
            {
                result = Byte(_backend.Read(address, 1)[0]);
                memoryReference = MemoryReference(address);
                return true;
            }
            return false;
        }

        /// <summary>
        /// The build's names whose value lies in [from, to]: what the RAM panel shows
        /// over a byte, so $0042 reads as FRAMEDONE.
        /// </summary>
        private void Symbols(JsonObject request, JsonObject args)
        {
            int from = Int(args, "from", 0);
            int to = Int(args, "to", AddressSpace - 1);
            JsonArray symbols = new JsonArray();
            foreach (KeyValuePair<string, long> symbol in _map.Symbols
                .Where(s => s.Value >= from && s.Value <= to)
                .OrderBy(s => s.Value).ThenBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
            {
                symbols.Add(new JsonObject { ["name"] = symbol.Key, ["value"] = symbol.Value });
            }
            Respond(request, new JsonObject { ["symbols"] = symbols });
        }

        /// <summary>
        /// The machine being debugged, for a memory panel or a graphics editor: its
        /// memories (the CPU's and the others the bridge offers), the views to show
        /// first, its colours, and the emulator with what it can do.
        /// </summary>
        private void DescribeSystem(JsonObject request)
        {
            IVideoMemory video = _backend as IVideoMemory;
            IReadOnlyList<string> available = video != null ? video.MemorySpaces : new string[0];

            JsonArray spaces = new JsonArray(new JsonObject
            {
                ["name"] = SystemDefinition.CpuSpace,
                ["length"] = AddressSpace,
                ["writable"] = true,
                ["description"] = "The " + _system.Cpu.Variant + "'s address space",
                ["available"] = _backend != null
            });
            foreach (WinASM65.Monitor.Abstractions.MemorySpace space in _system.MemorySpaces)
            {
                spaces.Add(new JsonObject
                {
                    ["name"] = space.Name,
                    ["length"] = space.Length,
                    ["writable"] = space.IsWritable,
                    ["description"] = space.Description,
                    ["available"] = available.Any(a => string.Equals(a, space.Name, StringComparison.OrdinalIgnoreCase))
                });
            }

            JsonArray views = new JsonArray();
            foreach (MemoryView view in _system.Views)
            {
                views.Add(new JsonObject
                {
                    ["name"] = view.Name,
                    ["space"] = view.Space,
                    ["start"] = view.Start,
                    ["length"] = view.Length,
                    ["writable"] = view.IsWritable
                });
            }

            JsonArray palette = new JsonArray();
            foreach (int colour in _system.MasterPalette)
                palette.Add(colour);

            JsonObject body = new JsonObject
            {
                ["id"] = _system.SystemId,
                ["name"] = _system.DisplayName,
                ["cpu"] = _system.Cpu.Variant,
                ["spaces"] = spaces,
                ["views"] = views,
                ["palette"] = palette
            };
            if (_backend != null)
            {
                WinASM65.Monitor.Abstractions.IExecutionAdapter adapter = _backend as WinASM65.Monitor.Abstractions.IExecutionAdapter;
                body["emulator"] = new JsonObject
                {
                    ["name"] = _backend.EmulatorName,
                    ["version"] = _backend.EmulatorVersion,
                    ["protocol"] = _backend is BridgeMemoryBackend ? ((BridgeMemoryBackend)_backend).ProtocolVersion : 0,
                    ["capabilities"] = adapter != null ? adapter.Capabilities.ToString() : string.Empty
                };
            }
            Respond(request, body);
        }

        /// <summary>Reads a memory by name: the CPU's, or one the system and the bridge offer.</summary>
        private void ReadSpace(JsonObject request, JsonObject args)
        {
            string space = Str(args, "space") ?? SystemDefinition.CpuSpace;
            int address = Int(args, "address", 0);
            int count = Int(args, "count", 0);
            if (count < 0 || count > 0x10000)
            {
                Fail(request, "count must be between 0 and 65536");
                return;
            }

            byte[] data = new byte[count];
            int done = 0;
            while (done < count)
            {
                int take = Math.Min(4096, count - done);
                byte[] chunk = IsCpu(space)
                    ? _backend.Read(address + done, take)
                    : Video().ReadSpace(space, address + done, take);
                Buffer.BlockCopy(chunk, 0, data, done, take);
                done += take;
            }
            Respond(request, new JsonObject
            {
                ["space"] = space,
                ["address"] = address,
                ["data"] = Convert.ToBase64String(data)
            });
        }

        /// <summary>Writes a memory by name; a graphics editor's live preview goes through here.</summary>
        private void WriteSpace(JsonObject request, JsonObject args)
        {
            string space = Str(args, "space") ?? SystemDefinition.CpuSpace;
            int address = Int(args, "address", 0);
            byte[] data;
            try
            {
                data = Convert.FromBase64String(Str(args, "data") ?? string.Empty);
            }
            catch (FormatException)
            {
                Fail(request, "data is not base64");
                return;
            }

            int done = 0;
            while (done < data.Length)
            {
                int take = Math.Min(4096, data.Length - done);
                byte[] chunk = new byte[take];
                Buffer.BlockCopy(data, done, chunk, 0, take);
                if (IsCpu(space))
                    _backend.Write(address + done, chunk);
                else
                    Video().WriteSpace(space, address + done, chunk);
                done += take;
            }
            Respond(request, new JsonObject { ["bytesWritten"] = data.Length });
            if (IsCpu(space))
                SendEvent("memory", new JsonObject { ["memoryReference"] = MemoryReference(address), ["offset"] = 0, ["count"] = data.Length });
        }

        private static bool IsCpu(string space)
        {
            return string.Equals(space, SystemDefinition.CpuSpace, StringComparison.OrdinalIgnoreCase);
        }

        private IVideoMemory Video()
        {
            IVideoMemory video = _backend as IVideoMemory;
            if (video == null || video.MemorySpaces.Count == 0)
                throw new MonitorException(_backend.EmulatorName + " " + _backend.EmulatorVersion
                    + " offers no memory besides the CPU's");
            return video;
        }

        // ------------------------------------------------------------- shutdown

        private void Terminate(JsonObject request)
        {
            Respond(request, null);
            Shutdown(true);
            Terminated(null);
        }

        private void Disconnect(JsonObject request, JsonObject args)
        {
            Shutdown(Bool(args, "terminateDebuggee", _launched));
            Respond(request, null);
            Finished = true;
        }

        private void Shutdown(bool killEmulator)
        {
            _watchdog?.Dispose();
            _watchdog = null;
            if (_backend != null)
            {
                try
                {
                    if (_backend.IsConnected)
                    {
                        _backend.ClearBreakpoints();
                        if (!killEmulator)
                            _backend.Resume();
                    }
                }
                catch (MonitorException) { }
                catch (IOException) { }
                _backend.Dispose();
                _backend = null;
            }
            if (killEmulator && _emulator != null)
                _emulator.Kill();
        }

        private void Terminated(string why)
        {
            if (_terminated)
                return;
            _terminated = true;
            if (why != null)
                Output(why + "\n", "console");
            SendEvent("terminated", null);
        }

        // ------------------------------------------------------------- protocol

        private void Respond(JsonObject request, JsonObject body)
        {
            JsonObject response = new JsonObject
            {
                ["seq"] = Interlocked.Increment(ref _seq),
                ["type"] = "response",
                ["request_seq"] = Int(request, "seq", 0),
                ["success"] = true,
                ["command"] = Str(request, "command")
            };
            if (body != null)
                response["body"] = body;
            _send(response);
        }

        private void Fail(JsonObject request, string message)
        {
            _send(new JsonObject
            {
                ["seq"] = Interlocked.Increment(ref _seq),
                ["type"] = "response",
                ["request_seq"] = Int(request, "seq", 0),
                ["success"] = false,
                ["command"] = Str(request, "command"),
                ["message"] = message,
                ["body"] = new JsonObject
                {
                    ["error"] = new JsonObject { ["id"] = 1, ["format"] = message, ["showUser"] = true }
                }
            });
        }

        private void SendEvent(string name, JsonObject body)
        {
            JsonObject message = new JsonObject
            {
                ["seq"] = Interlocked.Increment(ref _seq),
                ["type"] = "event",
                ["event"] = name
            };
            if (body != null)
                message["body"] = body;
            _send(message);
        }

        private void Output(string text, string category)
        {
            SendEvent("output", new JsonObject { ["category"] = category, ["output"] = text });
        }

        // -------------------------------------------------------------- helpers

        private int ReadWord(int address)
        {
            byte[] bytes = _backend.Read(address, 2);
            return bytes[0] | (bytes[1] << 8);
        }

        private static string ConfigPath(JsonObject args)
        {
            string config = Str(args, "config");
            if (string.IsNullOrWhiteSpace(config))
                return null;
            return Path.GetFullPath(config);
        }

        internal static string MemoryReference(int address)
        {
            return "0x" + address.ToString("X4");
        }

        internal static int ParseMemoryReference(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                throw new MonitorException("no memory reference");
            string text = reference.Trim();
            int value;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                return value;
            if (text.StartsWith("$", StringComparison.Ordinal)
                && int.TryParse(text.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                return value;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return value;
            throw new MonitorException("not a memory reference: " + reference);
        }

        private static string Byte(int value)
        {
            return "$" + (value & 0xFF).ToString("X2");
        }

        private static string Str(JsonObject node, string key)
        {
            if (node == null)
                return null;
            JsonNode value = node[key];
            return value is JsonValue v && v.TryGetValue(out string text) ? text : null;
        }

        private static int Int(JsonObject node, string key, int fallback)
        {
            if (node == null)
                return fallback;
            JsonNode value = node[key];
            if (value is JsonValue v)
            {
                if (v.TryGetValue(out int number))
                    return number;
                if (v.TryGetValue(out long big))
                    return (int)big;
                if (v.TryGetValue(out double real))
                    return (int)real;
            }
            return fallback;
        }

        private static bool Bool(JsonObject node, string key, bool fallback)
        {
            if (node == null)
                return fallback;
            JsonNode value = node[key];
            return value is JsonValue v && v.TryGetValue(out bool flag) ? flag : fallback;
        }
    }
}
