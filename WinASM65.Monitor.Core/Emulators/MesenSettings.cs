using System;
using System.IO;
using System.Text.Json;

namespace WinASM65.Monitor.Emulators
{
    /// <summary>The settings Mesen2 needs before a bridge script can open a socket.</summary>
    public static class MesenSettings
    {
        /// <summary>
        /// Mesen2 runs every script with io, require and os disabled, and refuses to
        /// execute a script longer than ScriptTimeout seconds. Without the three
        /// settings below, a bridge cannot open a socket at all and a long pause
        /// kills the script mid-command. The flags are written beside the emulator
        /// and reported, never applied silently: this changes a file outside the
        /// project, and a tool that rewrites its host's configuration without
        /// saying so is the behaviour this project refuses everywhere else.
        /// </summary>
        public static bool Ensure(string emulatorPath, TextWriter log)
        {
            string settingsPath = Path.Combine(Path.GetDirectoryName(emulatorPath) ?? ".", "settings.json");

            if (File.Exists(settingsPath))
            {
                string problem = DescribeMesen2SettingsProblem(File.ReadAllText(settingsPath));
                if (problem == null) return true;

                log.WriteLine(
                    "[mesen2] " + settingsPath + " does not grant the bridge what it needs: " + problem + ".");
                log.WriteLine("[mesen2] Add: \"Debug\": { \"ScriptWindow\": { \"AllowIoOsAccess\": true,"
                    + " \"AllowNetworkAccess\": true, \"ScriptTimeout\": 60 } }");
                return false;
            }

            try
            {
                File.WriteAllText(settingsPath,
                    "{\"Debug\":{\"ScriptWindow\":{\"AllowIoOsAccess\":true,\"AllowNetworkAccess\":true,\"ScriptTimeout\":60}}}");
            }
            catch (IOException ex)
            {
                log.WriteLine("[mesen2] cannot write " + settingsPath + ": " + ex.Message);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                log.WriteLine("[mesen2] cannot write " + settingsPath + ": " + ex.Message);
                return false;
            }

            log.WriteLine("[mesen2] wrote " + settingsPath
                + " to let the bridge use sockets and to survive a pause.");
            return true;
        }

        /// <summary>
        /// Names the first setting that would stop the bridge, or returns null when
        /// the file already grants everything the bridge needs.
        ///
        /// The value matters, not merely the key. A file that spells
        /// "AllowNetworkAccess" but sets it to false satisfies a substring test and
        /// still leaves the bridge unable to open a socket, which reaches the user
        /// as an emulator that starts and then silently ignores the script: the same
        /// appearance as a crash, and much harder to diagnose.
        /// </summary>
        private static string DescribeMesen2SettingsProblem(string json)
        {
            JsonElement root;
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                root = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return "the file is not valid JSON";
            }

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("Debug", out JsonElement debug)
                || debug.ValueKind != JsonValueKind.Object
                || !debug.TryGetProperty("ScriptWindow", out JsonElement window)
                || window.ValueKind != JsonValueKind.Object)
            {
                return "it has no Debug.ScriptWindow section";
            }

            foreach (string key in new[] { "AllowIoOsAccess", "AllowNetworkAccess" })
            {
                if (!window.TryGetProperty(key, out JsonElement value) || value.ValueKind != JsonValueKind.True)
                    return key + " is missing or not true";
            }

            if (!window.TryGetProperty("ScriptTimeout", out JsonElement timeout)
                || !timeout.TryGetInt32(out int seconds)
                || seconds <= 0)
            {
                return "ScriptTimeout is missing or not a positive number of seconds";
            }

            return null;
        }
    }
}
