using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using WinASM65.Monitor.Emulators;

namespace WinASM65.Monitor
{
    /// <summary>The names of the built-in emulator adapters (see <see cref="EmulatorAdapters"/>).</summary>
    public static class EmulatorIds
    {
        public const string MesenCe = EmulatorAdapters.MesenCe;
        public const string Mesen2 = EmulatorAdapters.Mesen2;
    }

    /// <summary>
    /// Starts an emulator with its bridge loaded, through the adapter of that
    /// emulator (<see cref="EmulatorAdapters"/>).
    ///
    /// Diagnostics go to stderr, never stdout: a front end may be using stdout as
    /// its protocol channel.
    /// </summary>
    public static class BridgeLauncher
    {
        /// <summary>
        /// Starts an emulator named by its path and adapter name, as the monitor's
        /// command line and older callers give it.
        /// </summary>
        public static Process LaunchEmulator(string emulatorPath, string romPath, string emulator, int port,
            string bridgeOverride = null)
        {
            EmulatorConfiguration configuration = EmulatorConfiguration.Of(emulatorPath, emulator);
            if (configuration != null && bridgeOverride != null)
                configuration.Bridge = bridgeOverride;
            return Launch(configuration, romPath, port, Console.Error);
        }

        /// <summary>
        /// Starts the emulator a project configured, loading <paramref name="romPath"/>
        /// and the bridge, with the bridge told to listen on <paramref name="port"/>.
        /// Returns null, with the reason in <paramref name="log"/>, when it cannot.
        /// </summary>
        public static Process Launch(EmulatorConfiguration emulator, string romPath, int port, TextWriter log)
        {
            log = log ?? Console.Error;
            if (emulator == null || !File.Exists(emulator.Path))
            {
                log.WriteLine("Emulator not found: " + (emulator != null ? emulator.Path : "(none configured)"));
                return null;
            }
            if (!File.Exists(romPath))
            {
                log.WriteLine("ROM not found: " + romPath);
                return null;
            }
            // The emulator runs in its own folder: a path relative to ours would not be found there.
            romPath = Path.GetFullPath(romPath);

            IEmulatorAdapter adapter = EmulatorAdapters.For(emulator);
            string bridgeScript = BridgeScriptOf(emulator, adapter, log);
            if (bridgeScript == null && adapter.BuiltInBridge != null)
                return null;

            ProcessStartInfo start = new ProcessStartInfo(emulator.Path)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(emulator.Path) ?? Directory.GetCurrentDirectory()
            };

            if (!adapter.Prepare(emulator, log))
                return null;
            adapter.AddArguments(start, emulator, romPath, bridgeScript, port);

            // A script cannot be given arguments by every emulator, so the port also
            // travels the one way every sandbox that allows sockets offers.
            start.Environment["MACHINE_PORT"] = port.ToString(CultureInfo.InvariantCulture);

            Process process = Process.Start(start);
            if (process == null)
            {
                log.WriteLine("Could not start " + adapter.DisplayName + ".");
                return null;
            }

            string prefix = "[" + adapter.Name + "] ";
            process.OutputDataReceived += (s, e) => { if (e.Data != null) log.WriteLine(prefix + e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) log.WriteLine(prefix + e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }

        /// <summary>
        /// The bridge script to load: the one the project names (a path, or the file
        /// name of a built-in bridge), else the adapter's own. Built-in bridges are
        /// looked up next to the executable, then in the source tree.
        /// </summary>
        public static string BridgeScriptOf(EmulatorConfiguration emulator, IEmulatorAdapter adapter, TextWriter log)
        {
            string wanted = !string.IsNullOrWhiteSpace(emulator.Bridge) ? emulator.Bridge : adapter.BuiltInBridge;
            if (wanted == null)
                return null;
            if (Path.IsPathRooted(wanted))
            {
                if (File.Exists(wanted))
                    return wanted;
                (log ?? Console.Error).WriteLine("Bridge script not found: " + wanted);
                return null;
            }

            string beside = Path.Combine(AppContext.BaseDirectory, "Bridge", wanted);
            string found = File.Exists(beside) ? beside : FindBridgeInSourceTree(wanted);
            if (found == null)
                (log ?? Console.Error).WriteLine(wanted + " not found next to the executable or in the source tree.");
            return found;
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
