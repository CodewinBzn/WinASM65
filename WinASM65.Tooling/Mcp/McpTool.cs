using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;

namespace WinASM65.Tooling.Mcp
{
    /// <summary>A tool's arguments were wrong; the message tells the model which and how.</summary>
    public sealed class McpArgumentException : Exception
    {
        public McpArgumentException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// What a tool may do, so a client can decide what to ask the user before
    /// running it: <see cref="ReadOnly"/> tools only look, the others change files,
    /// memory or start processes.
    /// </summary>
    public enum McpEffect
    {
        ReadOnly,
        Writes,
        Destructive
    }

    /// <summary>One tool: its name, what it does, its arguments (a JSON Schema) and the code.</summary>
    public sealed class McpTool
    {
        private readonly Func<JsonObject, McpResult> _run;

        public McpTool(string name, string title, string description, McpEffect effect, JsonObject inputSchema,
            Func<JsonObject, McpResult> run)
        {
            Name = name;
            Title = title;
            Description = description;
            Effect = effect;
            InputSchema = inputSchema;
            _run = run;
        }

        public string Name { get; private set; }
        public string Title { get; private set; }
        public string Description { get; private set; }
        public McpEffect Effect { get; private set; }
        public JsonObject InputSchema { get; private set; }

        public McpResult Run(JsonObject arguments)
        {
            return _run(arguments);
        }

        public JsonObject Describe()
        {
            return new JsonObject
            {
                ["name"] = Name,
                ["title"] = Title,
                ["description"] = Description,
                ["inputSchema"] = InputSchema.DeepClone(),
                ["annotations"] = new JsonObject
                {
                    ["title"] = Title,
                    ["readOnlyHint"] = Effect == McpEffect.ReadOnly,
                    ["destructiveHint"] = Effect == McpEffect.Destructive,
                    ["openWorldHint"] = false
                }
            };
        }

        // ------------------------------------------------------------- schemas

        /// <summary>An object schema from (name, type, description, required) entries.</summary>
        public static JsonObject Schema(params Arg[] arguments)
        {
            JsonObject properties = new JsonObject();
            JsonArray required = new JsonArray();
            foreach (Arg argument in arguments)
            {
                JsonObject property = new JsonObject { ["type"] = argument.Type, ["description"] = argument.Description };
                if (argument.Type == "array")
                    property["items"] = new JsonObject { ["type"] = argument.ItemType ?? "string" };
                properties[argument.Name] = property;
                if (argument.Required)
                    required.Add(argument.Name);
            }
            return new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required };
        }

        public sealed class Arg
        {
            public string Name;
            public string Type;
            public string Description;
            public bool Required;
            public string ItemType;

            public static Arg Required_(string name, string type, string description)
            {
                return new Arg { Name = name, Type = type, Description = description, Required = true };
            }

            public static Arg Optional(string name, string type, string description)
            {
                return new Arg { Name = name, Type = type, Description = description };
            }

            public static Arg List(string name, string itemType, string description, bool required)
            {
                return new Arg { Name = name, Type = "array", ItemType = itemType, Description = description, Required = required };
            }
        }

        // ------------------------------------------------------------- argument readers

        public static string Text(JsonObject arguments, string name, bool required = true)
        {
            string value = arguments[name] is JsonValue v && v.TryGetValue(out string s) ? s : null;
            if (required && string.IsNullOrWhiteSpace(value))
                throw new McpArgumentException("missing argument \"" + name + "\"");
            return value;
        }

        /// <summary>An integer given as a number, or as text in decimal, $hex or 0xhex.</summary>
        public static int? Number(JsonObject arguments, string name, bool required = true)
        {
            JsonNode node = arguments[name];
            if (node == null)
            {
                if (required)
                    throw new McpArgumentException("missing argument \"" + name + "\"");
                return null;
            }
            if (node is JsonValue value)
            {
                if (value.TryGetValue(out int i))
                    return i;
                if (value.TryGetValue(out double d) && d == Math.Floor(d))
                    return (int)d;
                if (value.TryGetValue(out string s))
                {
                    s = s.Trim();
                    int parsed;
                    if (s.StartsWith("$", StringComparison.Ordinal)
                        && int.TryParse(s.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed))
                        return parsed;
                    if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed))
                        return parsed;
                    if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                        return parsed;
                }
            }
            throw new McpArgumentException("\"" + name + "\" must be a number (decimal, $hex or 0xhex)");
        }

        public static bool Flag(JsonObject arguments, string name, bool fallback)
        {
            return arguments[name] is JsonValue v && v.TryGetValue(out bool b) ? b : fallback;
        }

        public static IReadOnlyList<int> Numbers(JsonObject arguments, string name)
        {
            List<int> numbers = new List<int>();
            if (arguments[name] is JsonArray array)
            {
                foreach (JsonNode item in array)
                {
                    JsonObject holder = new JsonObject { ["n"] = item?.DeepClone() };
                    numbers.Add(Number(holder, "n").Value);
                }
            }
            return numbers;
        }
    }

    /// <summary>What a tool returns: text, images, and whether it is an error.</summary>
    public sealed class McpResult
    {
        private readonly JsonArray _content = new JsonArray();

        public bool IsError { get; private set; }

        public static McpResult Ok(string text)
        {
            McpResult result = new McpResult();
            result.AddText(text);
            return result;
        }

        public static McpResult Fail(string text)
        {
            McpResult result = Ok(text);
            result.IsError = true;
            return result;
        }

        public McpResult AddText(string text)
        {
            _content.Add(new JsonObject { ["type"] = "text", ["text"] = text ?? string.Empty });
            return this;
        }

        public McpResult AddImage(byte[] png)
        {
            _content.Add(new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(png), ["mimeType"] = "image/png" });
            return this;
        }

        public JsonObject ToJson()
        {
            return new JsonObject { ["content"] = _content.DeepClone(), ["isError"] = IsError };
        }
    }
}
