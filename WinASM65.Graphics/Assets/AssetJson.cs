using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace WinASM65.Graphics.Assets
{
    /// <summary>
    /// Reading and writing the JSON asset files.
    ///
    /// <para>
    /// Writing is meant for files people diff and merge: keys in the order the
    /// classes declare them, two-space indentation, <c>\n</c> line endings and a
    /// final newline whatever the platform, and number arrays laid out as the
    /// picture they describe (a screen's tiles 32 to a line) instead of one number
    /// per line. Small objects whose values are all plain (a metasprite tile) take
    /// one line each.
    /// </para>
    /// </summary>
    public static class AssetJson
    {
        /// <summary>The current version of every asset file format.</summary>
        public const int CurrentVersion = 1;

        private const int CompactLimit = 100;
        private const int DefaultRowLength = 16;

        /// <summary>camelCase names, comments and trailing commas tolerated on read, nulls left out on write.</summary>
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

        private static readonly JsonSerializerOptions ValueOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// The asset a JSON text holds, or null with an error diagnostic when the text
        /// is not JSON or a value has the wrong type.
        /// </summary>
        public static T Parse<T>(string json, string file, List<AssetDiagnostic> diagnostics) where T : class
        {
            try
            {
                T asset = JsonSerializer.Deserialize<T>(json, Options);
                if (asset == null)
                    diagnostics.Add(AssetDiagnostic.Error(file, "$", "the file does not hold a JSON object"));
                return asset;
            }
            catch (JsonException ex)
            {
                diagnostics.Add(AssetDiagnostic.Error(file, ex.Path, "invalid JSON: " + FirstSentence(ex.Message)));
                return null;
            }
        }

        /// <summary>Reads and parses a file; a missing or unreadable file is an error diagnostic.</summary>
        public static T Read<T>(string path, List<AssetDiagnostic> diagnostics) where T : class
        {
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                diagnostics.Add(AssetDiagnostic.Error(path, "$", "cannot read the file: " + ex.Message));
                return null;
            }
            return Parse<T>(text, path, diagnostics);
        }

        /// <summary>The JSON text of an asset, laid out for people (see the class remarks).</summary>
        /// <param name="asset">The asset object.</param>
        /// <param name="rowLength">Numbers per line of a long number array, by property name; null or 0 for the default.</param>
        public static string Serialize(object asset, Func<string, int> rowLength)
        {
            JsonNode node = JsonSerializer.SerializeToNode(asset, asset.GetType(), Options);
            StringBuilder text = new StringBuilder();
            WriteNode(text, node, 0, null, rowLength, true);
            text.Append('\n');
            return text.ToString();
        }

        /// <summary>Writes an asset to a file (UTF-8 without a byte order mark), creating its folder.</summary>
        public static void Write(string path, object asset, Func<string, int> rowLength)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, Serialize(asset, rowLength), new UTF8Encoding(false));
        }

        /// <summary>The asset as a JSON tree, as a protocol response carries it.</summary>
        public static JsonNode ToNode(object asset)
        {
            return JsonSerializer.SerializeToNode(asset, asset.GetType(), Options);
        }

        private static void WriteNode(StringBuilder text, JsonNode node, int indent, string property, Func<string, int> rowLength, bool top)
        {
            if (node is JsonObject obj)
            {
                if (obj.Count == 0)
                {
                    text.Append("{}");
                    return;
                }
                if (!top && IsFlat(obj))
                {
                    string compact = Compact(obj);
                    if (compact.Length + indent <= CompactLimit)
                    {
                        text.Append(compact);
                        return;
                    }
                }
                text.Append("{\n");
                int i = 0;
                foreach (KeyValuePair<string, JsonNode> pair in obj)
                {
                    Indent(text, indent + 2);
                    text.Append(Quote(pair.Key)).Append(": ");
                    WriteNode(text, pair.Value, indent + 2, pair.Key, rowLength, false);
                    if (++i < obj.Count)
                        text.Append(',');
                    text.Append('\n');
                }
                Indent(text, indent);
                text.Append('}');
                return;
            }

            if (node is JsonArray array)
            {
                if (array.Count == 0)
                {
                    text.Append("[]");
                    return;
                }
                if (AllPlain(array))
                {
                    string compact = Compact(array);
                    int row = rowLength == null || property == null ? 0 : rowLength(property);
                    if (row <= 0 && compact.Length + indent + (property ?? "").Length <= CompactLimit)
                    {
                        text.Append(compact);
                        return;
                    }
                    if (row <= 0)
                        row = DefaultRowLength;
                    text.Append("[\n");
                    for (int start = 0; start < array.Count; start += row)
                    {
                        Indent(text, indent + 2);
                        int end = Math.Min(array.Count, start + row);
                        for (int k = start; k < end; k++)
                        {
                            text.Append(Plain(array[k]));
                            if (k < array.Count - 1)
                                text.Append(k < end - 1 ? ", " : ",");
                        }
                        text.Append('\n');
                    }
                    Indent(text, indent);
                    text.Append(']');
                    return;
                }
                text.Append("[\n");
                for (int k = 0; k < array.Count; k++)
                {
                    Indent(text, indent + 2);
                    WriteNode(text, array[k], indent + 2, null, rowLength, false);
                    if (k < array.Count - 1)
                        text.Append(',');
                    text.Append('\n');
                }
                Indent(text, indent);
                text.Append(']');
                return;
            }

            text.Append(Plain(node));
        }

        // An object that fits on one line: its values are plain or arrays of plain values.
        private static bool IsFlat(JsonObject obj)
        {
            foreach (KeyValuePair<string, JsonNode> pair in obj)
            {
                if (pair.Value is JsonObject)
                    return false;
                if (pair.Value is JsonArray array && !AllPlain(array))
                    return false;
            }
            return true;
        }

        private static bool AllPlain(JsonArray array)
        {
            foreach (JsonNode item in array)
            {
                if (item is JsonObject || item is JsonArray)
                    return false;
            }
            return true;
        }

        private static string Compact(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                StringBuilder text = new StringBuilder("{ ");
                int i = 0;
                foreach (KeyValuePair<string, JsonNode> pair in obj)
                {
                    text.Append(Quote(pair.Key)).Append(": ").Append(Compact(pair.Value));
                    if (++i < obj.Count)
                        text.Append(", ");
                }
                return text.Append(" }").ToString();
            }
            if (node is JsonArray array)
            {
                StringBuilder text = new StringBuilder("[");
                for (int k = 0; k < array.Count; k++)
                {
                    if (k > 0)
                        text.Append(", ");
                    text.Append(Compact(array[k]));
                }
                return text.Append(']').ToString();
            }
            return Plain(node);
        }

        private static string Plain(JsonNode node)
        {
            return node == null ? "null" : node.ToJsonString(ValueOptions);
        }

        private static string Quote(string key)
        {
            return JsonSerializer.Serialize(key, ValueOptions);
        }

        private static void Indent(StringBuilder text, int spaces)
        {
            text.Append(' ', spaces);
        }

        private static string FirstSentence(string message)
        {
            int cut = message.IndexOf(" Path:", StringComparison.Ordinal);
            return cut > 0 ? message.Substring(0, cut).Trim() : message.Trim();
        }
    }
}
