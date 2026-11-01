using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using WinASM65.Monitor.Emulators;

namespace WinASM65.Monitor
{
    /// <summary>
    /// Starts an emulator process with its bridge, and stops it again.
    ///
    /// The monitor can launch its own emulator rather than depending on one
    /// somebody else started. Which emulator, which bridge and which command line
    /// come from an <see cref="EmulatorConfiguration"/> (config.json's
    /// <c>Emulator</c>, or the monitor's command line) and its adapter
    /// (<see cref="EmulatorAdapters"/>); this class only owns the process.
    /// </summary>
    public class EmulatorLauncher
    {
        private readonly EmulatorConfiguration _emulator;
        private readonly int _port;
        private string _romPath;
        private Process _process;

        /// <summary>True when the emulator is started with a bridge the monitor can connect to.</summary>
        public bool UsesBridge
        {
            get { return !string.IsNullOrEmpty(_emulator.Bridge) || EmulatorAdapters.For(_emulator).BuiltInBridge != null; }
        }

        /// <summary>The emulator this launcher starts.</summary>
        public EmulatorConfiguration Emulator
        {
            get { return _emulator; }
        }

        /// <summary>
        /// Creates a launcher for an emulator given by its path and adapter name. The
        /// process is not started until <see cref="Start"/> is called.
        /// </summary>
        public EmulatorLauncher(string emulatorPath, string romPath, string emulatorId, int port)
            : this(EmulatorConfiguration.Of(emulatorPath, emulatorId), romPath, port)
        {
        }

        public EmulatorLauncher(EmulatorConfiguration emulator, string romPath, int port)
        {
            if (emulator == null)
                throw new ArgumentNullException("emulator");
            _emulator = emulator;
            _romPath = romPath;
            _port = port;
        }

        /// <summary>
        /// The emulator config.json in <paramref name="directory"/> declares, loading
        /// the image its <c>Output.ObjectFile</c> names; null when it declares none.
        /// </summary>
        public static EmulatorLauncher FromProjectConfiguration(string directory)
        {
            string configPath = Path.Combine(directory ?? Directory.GetCurrentDirectory(), "config.json");
            EmulatorConfiguration emulator = EmulatorConfiguration.Read(configPath);
            if (emulator == null)
                return null;

            string rom = null;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath)))
                {
                    JsonElement output, image;
                    if (document.RootElement.TryGetProperty("Output", out output)
                        && output.ValueKind == JsonValueKind.Object
                        && output.TryGetProperty("ObjectFile", out image)
                        && image.ValueKind == JsonValueKind.String)
                        rom = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath)), image.GetString());
                }
            }
            catch (JsonException)
            {
            }
            return new EmulatorLauncher(emulator, rom, emulator.Port ?? BridgeConnector.DefaultPort);
        }

        /// <summary>Starts the emulator on the current image and returns the process, or null.</summary>
        public Process Start()
        {
            if (!File.Exists(_emulator.Path))
                throw new MonitorException("emulator not found: " + _emulator.Path);
            _process = BridgeLauncher.Launch(_emulator, _romPath, _port, Console.Error);
            return _process;
        }

        /// <summary>
        /// Launches the emulator, or restarts it if it is already running.
        /// Used by the <c>EMULATOR START</c> command.
        /// </summary>
        public void LaunchEmulator()
        {
            if (_process != null && !_process.HasExited)
                return; // Already running

            Start();
        }

        /// <summary>Restarts the owned emulator on a newly built ROM.</summary>
        public void Restart(string romPath)
        {
            if (string.IsNullOrWhiteSpace(romPath) || !File.Exists(romPath))
                throw new MonitorException("the ROM image does not exist: " + romPath);

            Stop();
            _romPath = romPath;
            if (Start() == null)
                throw new MonitorException("could not restart the emulator");
        }

        /// <summary>Uses a user-selected bridge script, from the next start on.</summary>
        public void RestartWithBridge(string bridgePath)
        {
            if (string.IsNullOrWhiteSpace(bridgePath) || !File.Exists(bridgePath))
                throw new MonitorException("the bridge script does not exist: " + bridgePath);
            _emulator.Bridge = Path.GetFullPath(bridgePath);
            Stop();
            if (Start() == null)
                throw new MonitorException("could not restart the emulator with the selected bridge");
        }

        /// <summary>
        /// Stops the emulator this launcher started, if any. Called by the shell
        /// when the monitor exits, so the emulator does not survive its owner.
        /// </summary>
        public void Stop()
        {
            if (_process == null)
                return;

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(5000);
                }
            }
            catch (InvalidOperationException)
            {
                // Already gone, or never started: nothing to stop.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The OS refused the kill. Reported by the caller's diagnostics, not
                // silently swallowed: a surviving emulator is worth naming.
            }
            catch (NotSupportedException)
            {
            }
        }

    }
}
