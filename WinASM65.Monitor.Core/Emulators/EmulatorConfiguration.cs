using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace WinASM65.Monitor.Emulators
{
    /// <summary>
    /// The emulator a project runs on, as its config.json declares it:
    /// <code>
    /// "Emulator": {
    ///     "Name": "mesen2",
    ///     "Path": "C:\\Emulators\\Mesen2\\Mesen.exe",
    ///     "Bridge": "bridges/my_bridge.lua",
    ///     "Arguments": "{rom} {script}",
    ///     "Port": 45678
    /// }
    /// </code>
    /// Only <c>Path</c> is required. <c>Name</c> picks an adapter (<c>mesen2</c>,
    /// <c>mesence</c>, or anything else for a custom emulator); <c>Bridge</c> replaces
    /// the adapter's own bridge script; <c>Arguments</c> is the command line of a
    /// custom emulator, with <c>{rom}</c>, <c>{script}</c> and <c>{port}</c>.
    /// Relative paths are relative to config.json. Key names are case-insensitive.
    ///
    /// Read in one place, so the monitor and the debug adapter cannot disagree on
    /// what a project asked for.
    /// </summary>
    public sealed class EmulatorConfiguration
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Bridge { get; set; }
        public string Arguments { get; set; }
        public int? Port { get; set; }

        /// <summary>The <c>Emulator</c> section of a config.json, or null when it has none or no Path.</summary>
        public static EmulatorConfiguration Read(string configurationPath)
        {
            if (string.IsNullOrEmpty(configurationPath) || !File.Exists(configurationPath))
                return null;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(configurationPath),
                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                {
                    JsonElement emulator;
                    if (document.RootElement.ValueKind != JsonValueKind.Object
                        || !TryGet(document.RootElement, "Emulator", out emulator)
                        || emulator.ValueKind != JsonValueKind.Object)
                        return null;
                    return Parse(emulator, System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(configurationPath)));
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Reads an <c>Emulator</c> object; paths are made absolute against <paramref name="baseDirectory"/>.</summary>
        public static EmulatorConfiguration Parse(JsonElement emulator, string baseDirectory)
        {
            string path = Text(emulator, "Path");
            if (string.IsNullOrWhiteSpace(path))
                return null;

            EmulatorConfiguration configuration = new EmulatorConfiguration
            {
                Name = Normalize(Text(emulator, "Name")),
                Path = Absolute(path, baseDirectory),
                Arguments = Text(emulator, "Arguments")
            };

            string bridge = Text(emulator, "Bridge");
            if (!string.IsNullOrWhiteSpace(bridge))
            {
                // A built-in bridge is named by its file name; anything with a
                // directory or that exists beside config.json is a path.
                string candidate = Absolute(bridge, baseDirectory);
                configuration.Bridge = File.Exists(candidate) || bridge.IndexOfAny(new[] { '/', '\\' }) >= 0
                    ? candidate
                    : bridge;
            }

            JsonElement port;
            int value;
            if (TryGet(emulator, "Port", out port)
                && ((port.ValueKind == JsonValueKind.Number && port.TryGetInt32(out value))
                    || (port.ValueKind == JsonValueKind.String
                        && int.TryParse(port.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)))
                && value > 0 && value < 65536)
                configuration.Port = value;

            return configuration;
        }

        /// <summary>An emulator given on a command line or in launch.json: a path and maybe a name.</summary>
        public static EmulatorConfiguration Of(string path, string name)
        {
            return string.IsNullOrWhiteSpace(path)
                ? null
                : new EmulatorConfiguration { Path = path, Name = Normalize(name) };
        }

        private static string Normalize(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim().ToLowerInvariant();
        }

        private static string Absolute(string path, string baseDirectory)
        {
            path = path.Trim();
            return System.IO.Path.IsPathRooted(path) || string.IsNullOrEmpty(baseDirectory)
                ? path
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, path));
        }

        private static string Text(JsonElement element, string name)
        {
            JsonElement value;
            return TryGet(element, name, out value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        private static bool TryGet(JsonElement element, string name, out JsonElement value)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
            value = default(JsonElement);
            return false;
        }
    }
}
