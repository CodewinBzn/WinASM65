using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using WinASM65.Tooling.Dap;
using static WinASM65.Tooling.Mcp.McpTool;

namespace WinASM65.Tooling.Mcp
{
    /// <summary>
    /// The debugger as tools. They drive the same <see cref="DebugSession"/> VS Code
    /// talks to, in process, with Debug Adapter Protocol messages: launching,
    /// breakpoints, stops, registers and memory work, and fail, exactly as they do
    /// in the editor. One session at a time.
    /// </summary>
    public sealed class DebugTools : IDisposable
    {
        private const int RequestTimeoutMs = 120000;

        private readonly Func<IDebugEnvironment> _environment;
        private readonly object _lock = new object();
        private DebugSession _session;
        private Thread _loop;
        private BlockingCollection<JsonObject> _messages;
        private readonly List<JsonObject> _backlog = new List<JsonObject>();
        private readonly StringBuilder _console = new StringBuilder();
        private int _seq;

        public DebugTools() : this(() => new DebugEnvironment())
        {
        }

        /// <summary>For tests: the session runs against the given environment (a fake machine).</summary>
        public DebugTools(Func<IDebugEnvironment> environment)
        {
            _environment = environment;
        }

        public IEnumerable<McpTool> All()
        {
            yield return new McpTool("debug_launch", "Start debugging",
                "Builds the project, starts its emulator with the WinASM65 bridge (config.json's Emulator section, or "
                + "emulatorPath/emulator/bridge here), sets the breakpoints given, and runs the program. Returns the console "
                + "output and, if it stopped (stopOnEntry or a breakpoint hit within waitSeconds), the state. Ends any previous session.",
                McpEffect.Writes,
                Schema(Arg.Required_("config", "string", "Path of the project's config.json"),
                    Arg.Optional("emulatorPath", "string", "Emulator executable (overrides config.json)"),
                    Arg.Optional("emulator", "string", "Emulator adapter name: mesen2, mesence or a custom name"),
                    Arg.Optional("bridge", "string", "Bridge script (overrides the adapter's)"),
                    Arg.Optional("stopOnEntry", "boolean", "Stop on the program's first instruction"),
                    Arg.List("breakpoints", "string", "Breakpoints as \"file:line\" (file relative to config.json)", false),
                    Arg.Optional("waitSeconds", "number", "How long to wait for a stop after starting (default 3)")),
                Launch);

            yield return new McpTool("debug_attach", "Attach to a running emulator",
                "Connects to an emulator already running a WinASM65 bridge on a port. config is optional and gives source breakpoints and symbols.",
                McpEffect.Writes,
                Schema(Arg.Optional("port", "integer", "Bridge port (default 45678)"),
                    Arg.Optional("config", "string", "The project's config.json")),
                Attach);

            yield return new McpTool("set_breakpoints", "Set breakpoints in a file",
                "Replaces the breakpoints of one source file with the given lines. Returns where each one landed (a line with "
                + "no code moves to the next instruction) or why it could not be set.",
                McpEffect.Writes,
                Schema(Arg.Required_("file", "string", "Source file (absolute, or relative to the project's config.json)"),
                    Arg.List("lines", "integer", "Line numbers (1-based); empty clears", true)),
                SetBreakpoints);

            yield return new McpTool("continue", "Continue",
                "Resumes the program and waits up to waitSeconds for it to stop again (a breakpoint). Returns the state if it stopped.",
                McpEffect.Writes,
                Schema(Arg.Optional("waitSeconds", "number", "How long to wait for a stop (default 5, 0 to return at once)")),
                Continue);

            yield return new McpTool("step", "Step",
                "Executes one step and returns the new state: kind \"over\" (next line, over subroutine calls), \"into\" "
                + "(one instruction) or \"out\" (until the current subroutine returns).",
                McpEffect.Writes,
                Schema(Arg.Optional("kind", "string", "over (default), into or out")),
                Step);

            yield return new McpTool("pause", "Pause", "Stops the running program and returns the state.",
                McpEffect.Writes, Schema(), Pause);

            yield return new McpTool("get_state", "Program state",
                "Where the program is stopped and why: source file and line with its text, the routine (nearest label), the "
                + "registers and flags, and the debug console output since the last call.",
                McpEffect.ReadOnly, Schema(), _ => McpResult.Ok(State(true)));

            yield return new McpTool("read_memory", "Read memory",
                "Reads bytes as a hex dump. space is CPU (default: the processor's address space) or a memory of the machine "
                + "such as PPU, PAL, OAM or CHR on the NES (see describe_machine).",
                McpEffect.ReadOnly,
                Schema(Arg.Required_("address", "string", "Start address ($hex, 0xhex or decimal)"),
                    Arg.Required_("length", "integer", "Number of bytes (at most 65536)"),
                    Arg.Optional("space", "string", "CPU (default), or a memory space of the machine")),
                ReadMemory);

            yield return new McpTool("write_memory", "Write memory",
                "Writes bytes (hex, such as \"A9 01\" or \"A901\") at an address of the CPU's memory or of another memory space.",
                McpEffect.Destructive,
                Schema(Arg.Required_("address", "string", "Start address ($hex, 0xhex or decimal)"),
                    Arg.Required_("bytes", "string", "The bytes in hexadecimal"),
                    Arg.Optional("space", "string", "CPU (default), or a memory space of the machine")),
                WriteMemory);

            yield return new McpTool("evaluate", "Evaluate",
                "Evaluates a register (A, X, Y, SP, PC, P), a symbol of the build, or $address (its byte), and returns its value. "
                + "A line starting with '>' is a monitor command (see the monitor's HELP).",
                McpEffect.ReadOnly,
                Schema(Arg.Required_("expression", "string", "The expression")),
                Evaluate);

            yield return new McpTool("describe_machine", "Describe the machine",
                "The machine being debugged: its system, CPU, memory spaces (and which the emulator offers), the memory views "
                + "worth looking at, and the emulator with its capabilities.",
                McpEffect.ReadOnly, Schema(), DescribeMachine);

            yield return new McpTool("debug_stop", "Stop debugging",
                "Ends the session; an emulator the session started is closed.",
                McpEffect.Writes, Schema(), _ => { Stop(); return McpResult.Ok("Debug session ended."); });
        }

        // ---------------------------------------------------------------- session

        private void Start()
        {
            Stop();
            _messages = new BlockingCollection<JsonObject>();
            BlockingCollection<JsonObject> messages = _messages;
            _session = new DebugSession(_environment(), message => messages.Add(message));
            DebugSession session = _session;
            _loop = new Thread(() => session.Run()) { IsBackground = true, Name = "mcp-debug" };
            _loop.Start();
            _backlog.Clear();
            _console.Clear();
        }

        private void Stop()
        {
            if (_session == null)
                return;
            try
            {
                Request("disconnect", new JsonObject { ["terminateDebuggee"] = true }, 10000);
            }
            catch (InvalidOperationException)
            {
            }
            _session.InputClosed();
            _loop.Join(5000);
            _session.Dispose();
            _session = null;
        }

        private DebugSession Session()
        {
            if (_session == null)
                throw new InvalidOperationException("no debug session: call debug_launch or debug_attach first");
            return _session;
        }

        /// <summary>Sends a request and waits for its response; events met meanwhile are kept.</summary>
        private JsonObject Request(string command, JsonObject arguments, int timeoutMs = RequestTimeoutMs)
        {
            DebugSession session = Session();
            int seq = Interlocked.Increment(ref _seq);
            session.Dispatch(new JsonObject
            {
                ["seq"] = seq,
                ["type"] = "request",
                ["command"] = command,
                ["arguments"] = arguments ?? new JsonObject()
            });
            JsonObject response = Take(m => (string)m["type"] == "response" && (int?)m["request_seq"] == seq, timeoutMs);
            if (response == null)
                throw new InvalidOperationException(command + " got no answer");
            if (!(bool)response["success"])
                throw new InvalidOperationException(command + ": " + (string)response["message"]);
            return response["body"] as JsonObject ?? new JsonObject();
        }

        /// <summary>The first message matching, from the backlog or as it arrives; null after the timeout.</summary>
        private JsonObject Take(Func<JsonObject, bool> match, int timeoutMs)
        {
            lock (_lock)
            {
                JsonObject kept = _backlog.FirstOrDefault(match);
                if (kept != null)
                {
                    _backlog.Remove(kept);
                    return kept;
                }
            }

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            JsonObject message;
            while (true)
            {
                int left = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
                if (!_messages.TryTake(out message, left))
                    return null;
                Observe(message);
                if (match(message))
                    return message;
                lock (_lock)
                    _backlog.Add(message);
            }
        }

        private void Observe(JsonObject message)
        {
            if ((string)message["type"] == "event" && (string)message["event"] == "output")
                _console.Append((string)message["body"]["output"]);
        }

        /// <summary>Waits for the program to stop; true when it did.</summary>
        private bool WaitStopped(double seconds)
        {
            if (seconds <= 0)
                return false;
            JsonObject stopped = Take(m => (string)m["type"] == "event"
                && ((string)m["event"] == "stopped" || (string)m["event"] == "terminated"), (int)(seconds * 1000));
            return stopped != null && (string)stopped["event"] == "stopped";
        }

        // ---------------------------------------------------------------- tools

        private McpResult Launch(JsonObject arguments)
        {
            string config = CodeTools.ConfigPath(arguments);
            Start();
            Request("initialize", new JsonObject { ["adapterID"] = "winasm65", ["clientID"] = "mcp" });

            JsonObject launch = new JsonObject
            {
                ["config"] = config,
                ["stopOnEntry"] = Flag(arguments, "stopOnEntry", false)
            };
            foreach (string key in new[] { "emulatorPath", "emulator", "bridge" })
            {
                string value = Text(arguments, key, false);
                if (!string.IsNullOrWhiteSpace(value))
                    launch[key] = value;
            }
            try
            {
                Request("launch", launch);
            }
            catch (InvalidOperationException ex)
            {
                string console = DrainConsole();
                Stop();
                return McpResult.Fail(ex.Message + (console.Length > 0 ? "\n\nConsole:\n" + console : string.Empty));
            }
            Take(m => (string)m["type"] == "event" && (string)m["event"] == "initialized", 10000);

            StringBuilder report = new StringBuilder();
            if (arguments["breakpoints"] is JsonArray breakpoints)
            {
                foreach (IGrouping<string, KeyValuePair<string, int>> file in breakpoints
                    .Select(b => ParseBreakpoint((string)b, config))
                    .GroupBy(b => b.Key, StringComparer.OrdinalIgnoreCase))
                    report.Append(SetBreakpointsIn(file.Key, file.Select(b => b.Value).ToList()));
            }

            Request("configurationDone", null);
            double wait = Seconds(arguments, "waitSeconds", 3);
            bool stopped = WaitStopped(wait);
            report.Insert(0, "Debugging " + config + ".\n");
            report.AppendLine(stopped ? State(true) : "Running (no stop within " + wait + " s)." + ConsoleText());
            return McpResult.Ok(report.ToString());
        }

        private static double Seconds(JsonObject arguments, string name, double fallback)
        {
            if (arguments[name] is JsonValue value)
            {
                double seconds;
                int whole;
                string text;
                if (value.TryGetValue(out seconds))
                    return Math.Max(0, Math.Min(seconds, 600));
                if (value.TryGetValue(out whole))
                    return Math.Max(0, Math.Min(whole, 600));
                if (value.TryGetValue(out text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                    return Math.Max(0, Math.Min(seconds, 600));
            }
            return fallback;
        }

        private static KeyValuePair<string, int> ParseBreakpoint(string text, string config)
        {
            int colon = text == null ? -1 : text.LastIndexOf(':');
            int line;
            if (colon <= 0 || !int.TryParse(text.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out line))
                throw new McpArgumentException("a breakpoint is \"file:line\", not \"" + text + "\"");
            string file = text.Substring(0, colon);
            if (!Path.IsPathRooted(file))
                file = Path.Combine(Path.GetDirectoryName(config), file);
            return new KeyValuePair<string, int>(Path.GetFullPath(file), line);
        }

        private McpResult Attach(JsonObject arguments)
        {
            Start();
            Request("initialize", new JsonObject { ["adapterID"] = "winasm65", ["clientID"] = "mcp" });
            JsonObject attach = new JsonObject { ["port"] = Number(arguments, "port", false) ?? 45678 };
            string config = Text(arguments, "config", false);
            if (!string.IsNullOrWhiteSpace(config))
                attach["config"] = CodeTools.ConfigPath(arguments);
            Request("attach", attach);
            Take(m => (string)m["type"] == "event" && (string)m["event"] == "initialized", 10000);
            Request("configurationDone", null);
            return McpResult.Ok("Attached." + ConsoleText());
        }

        private McpResult SetBreakpoints(JsonObject arguments)
        {
            string file = Text(arguments, "file");
            Session();
            if (!Path.IsPathRooted(file))
                throw new McpArgumentException("give the file's absolute path");
            return McpResult.Ok(SetBreakpointsIn(Path.GetFullPath(file), Numbers(arguments, "lines")));
        }

        private string SetBreakpointsIn(string file, IReadOnlyList<int> lines)
        {
            JsonObject body = Request("setBreakpoints", new JsonObject
            {
                ["source"] = new JsonObject { ["path"] = file },
                ["breakpoints"] = new JsonArray(lines.Select(l => (JsonNode)new JsonObject { ["line"] = l }).ToArray())
            });
            StringBuilder text = new StringBuilder();
            JsonArray set = (JsonArray)body["breakpoints"];
            for (int i = 0; i < set.Count; i++)
            {
                bool verified = (bool)set[i]["verified"];
                text.AppendLine("Breakpoint " + Path.GetFileName(file) + ":" + lines[i] + " -> "
                    + (verified ? "set at line " + (int)set[i]["line"] : "not set: " + (string)set[i]["message"]));
            }
            if (set.Count == 0)
                text.AppendLine("Breakpoints of " + Path.GetFileName(file) + " cleared.");
            return text.ToString();
        }

        private McpResult Continue(JsonObject arguments)
        {
            Request("continue", new JsonObject { ["threadId"] = 1 });
            double wait = Seconds(arguments, "waitSeconds", 5);
            return McpResult.Ok(WaitStopped(wait) ? State(true) : "Running (no stop within " + wait + " s)." + ConsoleText());
        }

        private McpResult Step(JsonObject arguments)
        {
            string kind = (Text(arguments, "kind", false) ?? "over").ToLowerInvariant();
            string command = kind == "over" ? "next" : kind == "into" ? "stepIn" : kind == "out" ? "stepOut" : null;
            if (command == null)
                throw new McpArgumentException("kind is over, into or out");
            Request(command, new JsonObject { ["threadId"] = 1 });
            return McpResult.Ok(WaitStopped(30) ? State(true) : "The step did not stop within 30 s." + ConsoleText());
        }

        private McpResult Pause(JsonObject arguments)
        {
            Request("pause", new JsonObject { ["threadId"] = 1 });
            return McpResult.Ok(WaitStopped(10) ? State(true) : "The program did not stop." + ConsoleText());
        }

        private string State(bool withConsole)
        {
            StringBuilder text = new StringBuilder();
            JsonObject trace = Request("stackTrace", new JsonObject { ["threadId"] = 1 });
            JsonObject frame = ((JsonArray)trace["stackFrames"]).FirstOrDefault() as JsonObject;
            if (frame != null)
            {
                string path = (string)frame["source"]?["path"];
                int line = (int?)frame["line"] ?? 0;
                text.AppendLine("Stopped in " + (string)frame["name"]
                    + (path != null ? " at " + path + ":" + line : " (no source line)")
                    + (frame["instructionPointerReference"] != null ? ", PC " + (string)frame["instructionPointerReference"] : string.Empty));
                string source = SourceLine(path, line);
                if (source != null)
                    text.AppendLine("  " + line + ": " + source.Trim());
            }

            JsonArray scopes = (JsonArray)Request("scopes", new JsonObject { ["frameId"] = 1 })["scopes"];
            foreach (JsonNode scope in scopes)
            {
                if ((bool?)scope["expensive"] == true)
                    continue;
                JsonArray variables = (JsonArray)Request("variables", new JsonObject
                {
                    ["variablesReference"] = (int)scope["variablesReference"]
                })["variables"];
                text.AppendLine((string)scope["name"] + ": " + string.Join(", ",
                    variables.Select(v => (string)v["name"] + "=" + (string)v["value"])));
            }
            if (withConsole)
                text.Append(ConsoleText());
            return text.ToString();
        }

        private static string SourceLine(string path, int line)
        {
            try
            {
                if (path == null || line <= 0 || !File.Exists(path))
                    return null;
                return File.ReadLines(path).Skip(line - 1).FirstOrDefault();
            }
            catch (IOException)
            {
                return null;
            }
        }

        private string ConsoleText()
        {
            string console = DrainConsole();
            return console.Length == 0 ? string.Empty : "\nConsole:\n" + console;
        }

        private string DrainConsole()
        {
            // Output events still queued are part of what happened.
            JsonObject message;
            while (_messages != null && _messages.TryTake(out message))
            {
                Observe(message);
                lock (_lock)
                    _backlog.Add(message);
            }
            string text = _console.ToString();
            _console.Clear();
            return text;
        }

        private McpResult ReadMemory(JsonObject arguments)
        {
            int address = Number(arguments, "address").Value;
            int length = Number(arguments, "length").Value;
            string space = Text(arguments, "space", false) ?? "CPU";
            if (length <= 0 || length > 0x10000)
                throw new McpArgumentException("length is 1 to 65536");
            JsonObject body = Request("winasm65.memory.read", new JsonObject { ["space"] = space, ["address"] = address, ["count"] = length });
            byte[] data = Convert.FromBase64String((string)body["data"]);
            StringBuilder text = new StringBuilder();
            text.AppendLine(space.ToUpperInvariant() + " $" + address.ToString("X4") + "-$" + (address + length - 1).ToString("X4") + ":");
            for (int row = 0; row < data.Length; row += 16)
            {
                int count = Math.Min(16, data.Length - row);
                text.Append('$').Append((address + row).ToString("X4")).Append("  ");
                for (int i = 0; i < count; i++)
                    text.Append(data[row + i].ToString("X2")).Append(i == 7 ? "  " : " ");
                text.AppendLine();
            }
            return McpResult.Ok(text.ToString());
        }

        private McpResult WriteMemory(JsonObject arguments)
        {
            int address = Number(arguments, "address").Value;
            string space = Text(arguments, "space", false) ?? "CPU";
            string hex = new string(Text(arguments, "bytes").Where(c => !char.IsWhiteSpace(c) && c != ',' && c != '$').ToArray());
            if (hex.Length == 0 || hex.Length % 2 != 0)
                throw new McpArgumentException("bytes must be pairs of hex digits");
            byte[] data = Convert.FromHexString(hex);
            Request("winasm65.memory.write", new JsonObject
            {
                ["space"] = space,
                ["address"] = address,
                ["data"] = Convert.ToBase64String(data)
            });
            return McpResult.Ok("Wrote " + data.Length + " byte(s) at " + space.ToUpperInvariant() + " $" + address.ToString("X4") + ".");
        }

        private McpResult Evaluate(JsonObject arguments)
        {
            JsonObject body = Request("evaluate", new JsonObject
            {
                ["expression"] = Text(arguments, "expression"),
                ["context"] = Text(arguments, "expression").TrimStart().StartsWith(">", StringComparison.Ordinal) ? "repl" : "watch"
            });
            return McpResult.Ok((string)body["result"]);
        }

        private McpResult DescribeMachine(JsonObject arguments)
        {
            JsonObject system = Request("winasm65.system", null);
            StringBuilder text = new StringBuilder();
            text.AppendLine((string)system["name"] + " (" + (string)system["id"] + "), CPU " + (string)system["cpu"]);
            if (system["emulator"] is JsonObject emulator)
                text.AppendLine("Emulator: " + (string)emulator["name"] + " " + (string)emulator["version"]
                    + ", bridge contract " + (int)emulator["protocol"] + ", can: " + (string)emulator["capabilities"]);
            text.AppendLine("Memory spaces:");
            foreach (JsonNode space in (JsonArray)system["spaces"])
                text.AppendLine("  " + (string)space["name"] + " (" + (int)space["length"] + " bytes" + ((bool)space["writable"] ? "" : ", read-only")
                    + ((bool)space["available"] ? "" : ", not offered by this emulator") + "): " + (string)space["description"]);
            text.AppendLine("Views:");
            foreach (JsonNode view in (JsonArray)system["views"])
                text.AppendLine("  " + (string)view["name"] + ": " + (string)view["space"] + " $" + ((int)view["start"]).ToString("X4")
                    + "-$" + ((int)view["start"] + (int)view["length"] - 1).ToString("X4"));
            return McpResult.Ok(text.ToString());
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
