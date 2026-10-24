using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace WinASM65.Monitor
{
    /// <summary>The emulator identifiers the bridge launcher understands.</summary>
    public static class EmulatorIds
    {
        public const string MesenCe = "mesence";
        public const string Mesen2 = "mesen2";
    }

    /// <summary>
    /// Starts an emulator with the monitor's Lua bridge loaded.
    ///
    /// Diagnostics go to stderr, never stdout: a front end may be using stdout as
    /// its protocol channel.
    /// </summary>
    public static class BridgeLauncher
    {
        /// <summary>
        /// Starts an emulator headless with the bridge script, in the invocation its
        /// own build was proven with. The two supported emulators disagree on almost
        /// everything: argument order, script naming and whether the script sandbox
        /// has to be opened before a socket can exist. Guessing would produce a
        /// bridge that never listens, which looks exactly like a crash.
        /// </summary>
        // Shared by the shell's launcher, the command-line startup and the debug
        // adapter, so all of them use exactly the same, emulator-specific
        // invocation.  MesenCE and Mesen2 do not accept interchangeable script
        // arguments.
        public static Process LaunchEmulator(string emulatorPath, string romPath, string emulator, int port,
            string bridgeOverride = null)
        {
            if (!File.Exists(emulatorPath))
            {
                Console.Error.WriteLine("Emulator not found: " + emulatorPath);
                return null;
            }
            if (!File.Exists(romPath))
            {
                Console.Error.WriteLine("ROM not found: " + romPath);
                return null;
            }

            string scriptName = emulator == EmulatorIds.Mesen2 ? "bridge_mesen2.lua" : "bridge.lua";
            string bridgeScript = bridgeOverride ?? Path.Combine(AppContext.BaseDirectory, "Bridge", scriptName);
            if (!File.Exists(bridgeScript))
            {
                // Fall back to the source tree when running from a build output that
                // did not copy the script, so the tool works straight from bin.
                bridgeScript = FindBridgeInSourceTree(scriptName);
            }
            if (bridgeScript == null)
            {
                Console.Error.WriteLine(scriptName + " not found next to the executable or in the source tree.");
                return null;
            }

            ProcessStartInfo start = new ProcessStartInfo(emulatorPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            if (emulator == EmulatorIds.Mesen2)
            {
                if (!EnsureMesen2ScriptSettings(emulatorPath))
                    return null;

                // A plain ROM plus Lua script opens Mesen2's normal graphical
                // window.  --testRunner is specifically its headless mode, so it
                // made the monitor claim the emulator was started while showing no
                // emulator at all.  Mesen2 loads the ROM first, then opens and
                // starts the script (AutoStartScriptOnLoad is enabled by default).
                start.ArgumentList.Add(romPath);
                start.ArgumentList.Add(bridgeScript);
            }
            else
            {
                start.ArgumentList.Add("--testrunner");
                start.ArgumentList.Add(romPath);
                start.ArgumentList.Add(bridgeScript);
            }

            // Mesen2 gives a script no way to be told the port, so it is passed the
            // one way the sandbox does offer once the OS library is enabled.
            start.Environment["MACHINE_PORT"] = port.ToString(CultureInfo.InvariantCulture);

            Process process = Process.Start(start);
            if (process == null)
            {
                Console.Error.WriteLine("Could not start " + emulator + ".");
                return null;
            }

            process.OutputDataReceived += (s, e) => { if (e.Data != null) Console.Error.WriteLine("[" + emulator + "] " + e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.Error.WriteLine("[" + emulator + "] " + e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }

        /// <summary>
        /// Mesen2 runs every script with io, require and os disabled, and refuses to
        /// execute a script longer than ScriptTimeout seconds. Without the three
        /// settings below, a bridge cannot open a socket at all and a long pause
        /// kills the script mid-command. The flags are written beside the emulator
        /// and reported, never applied silently: this changes a file outside the
        /// project, and a tool that rewrites its host's configuration without
        /// saying so is the behaviour this project refuses everywhere else.
        /// </summary>
        private static bool EnsureMesen2ScriptSettings(string emulatorPath)
        {
            string settingsPath = Path.Combine(Path.GetDirectoryName(emulatorPath) ?? ".", "settings.json");

            if (File.Exists(settingsPath))
            {
                string problem = DescribeMesen2SettingsProblem(File.ReadAllText(settingsPath));
                if (problem == null) return true;

                Console.Error.WriteLine(
                    "[mesen2] " + settingsPath + " does not grant the bridge what it needs: " + problem + ".");
                Console.Error.WriteLine("[mesen2] Add: \"Debug\": { \"ScriptWindow\": { \"AllowIoOsAccess\": true,"
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
                Console.Error.WriteLine("[mesen2] cannot write " + settingsPath + ": " + ex.Message);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine("[mesen2] cannot write " + settingsPath + ": " + ex.Message);
                return false;
            }

            Console.Error.WriteLine("[mesen2] wrote " + settingsPath
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

        private static string FindBridgeInSourceTree(string scriptName)
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "WinASM65.Monitor.Core", "Bridge", scriptName);
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
