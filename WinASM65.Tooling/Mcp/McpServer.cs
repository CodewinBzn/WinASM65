using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WinASM65.Tooling.Mcp
{
    /// <summary>
    /// A Model Context Protocol server: the coding, debugging and graphics tools of
    /// WinASM65, offered to AI assistants (Claude Code, Claude Desktop, Copilot,
    /// Cursor…) over stdio.
    ///
    /// The transport is MCP's: one JSON-RPC 2.0 message per line, no Content-Length
    /// header. The server answers initialize, ping, tools/list and tools/call; what
    /// a tool may do (build, launch an emulator, write memory or files) is declared
    /// in its annotations, and asking the user before it runs is the client's job.
    /// </summary>
    public sealed class McpServer
    {
        /// <summary>The protocol versions this server speaks, newest first.</summary>
        public static readonly string[] ProtocolVersions = { "2025-06-18", "2025-03-26", "2024-11-05" };

        private readonly IReadOnlyList<McpTool> _tools;
        private readonly Action<JsonObject> _send;
        private readonly Dictionary<string, McpTool> _byName = new Dictionary<string, McpTool>(StringComparer.Ordinal);

        public McpServer(IReadOnlyList<McpTool> tools, Action<JsonObject> send)
        {
            _tools = tools;
            _send = send;
            foreach (McpTool tool in tools)
                _byName[tool.Name] = tool;
        }

        /// <summary>Reads messages until the input ends; one at a time, in order.</summary>
        public void Run(TextReader input)
        {
            string line;
            while ((line = input.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                JsonObject message;
                try
                {
                    message = JsonNode.Parse(line) as JsonObject;
                }
                catch (JsonException ex)
                {
                    _send(Error(null, -32700, "parse error: " + ex.Message));
                    continue;
                }
                if (message != null)
                    Handle(message);
            }
        }

        /// <summary>Handles one message: answers a request, ignores a notification.</summary>
        public void Handle(JsonObject message)
        {
            JsonNode id = message["id"]?.DeepClone();
            string method = (string)message["method"];
            JsonObject parameters = message["params"] as JsonObject ?? new JsonObject();
            if (method == null || id == null)
                return; // a notification (initialized, cancelled) or a response: nothing to answer

            try
            {
                switch (method)
                {
                    case "initialize":
                        _send(Result(id, Initialize(parameters)));
                        break;
                    case "ping":
                        _send(Result(id, new JsonObject()));
                        break;
                    case "tools/list":
                        _send(Result(id, ListTools()));
                        break;
                    case "tools/call":
                        _send(Result(id, Call(parameters)));
                        break;
                    default:
                        _send(Error(id, -32601, "method not found: " + method));
                        break;
                }
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                _send(Error(id, -32603, ex.Message));
            }
        }

        private static JsonObject Initialize(JsonObject parameters)
        {
            string requested = (string)parameters["protocolVersion"];
            string version = Array.IndexOf(ProtocolVersions, requested) >= 0 ? requested : ProtocolVersions[0];
            return new JsonObject
            {
                ["protocolVersion"] = version,
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                ["serverInfo"] = new JsonObject { ["name"] = "winasm65", ["version"] = typeof(McpServer).Assembly.GetName().Version.ToString() },
                ["instructions"] =
                    "WinASM65 is a 6502 assembler, linker and debugger. A project is a config.json (see get_config_schema). "
                    + "Build with build_project, read errors as file:line, look at listings and symbols; debug with debug_launch "
                    + "(starts the emulator named in config.json's Emulator section), set_breakpoints, continue, step, get_state, "
                    + "read_memory/write_memory. Graphics assets (.chr, .pal.json, .msprite.json, .screen.json, .map.json) are "
                    + "listed, rendered to images, edited and exported by the graphics tools."
            };
        }

        private JsonObject ListTools()
        {
            JsonArray tools = new JsonArray();
            foreach (McpTool tool in _tools)
                tools.Add(tool.Describe());
            return new JsonObject { ["tools"] = tools };
        }

        private JsonObject Call(JsonObject parameters)
        {
            string name = (string)parameters["name"];
            McpTool tool;
            if (name == null || !_byName.TryGetValue(name, out tool))
                return McpResult.Fail("unknown tool: " + name).ToJson();

            JsonObject arguments = parameters["arguments"] as JsonObject ?? new JsonObject();
            try
            {
                return tool.Run(arguments).ToJson();
            }
            catch (Exception ex) when (ex is McpArgumentException || ex is IOException || ex is ArgumentException
                || ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is WinASM65.Monitor.MonitorException
                || ex is FormatException || ex is JsonException)
            {
                // A tool that fails tells the model why, as a result it can act on,
                // not as a protocol error it cannot see.
                return McpResult.Fail(ex.Message).ToJson();
            }
        }

        private static JsonObject Result(JsonNode id, JsonNode result)
        {
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
        }

        private static JsonObject Error(JsonNode id, int code, string message)
        {
            return new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
            };
        }
    }

    /// <summary>Writes MCP messages to a stream, one per line, from any thread.</summary>
    public sealed class McpLineWriter
    {
        private readonly Stream _output;
        private readonly object _lock = new object();

        public McpLineWriter(Stream output)
        {
            _output = output;
        }

        public void Write(JsonObject message)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message.ToJsonString() + "\n");
            lock (_lock)
            {
                _output.Write(bytes, 0, bytes.Length);
                _output.Flush();
            }
        }
    }
}
