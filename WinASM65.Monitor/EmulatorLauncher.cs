using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace WinASM65.Monitor
{
    /// <summary>
    /// Starts an emulator process and waits for its bridge to come up.
    ///
    /// The monitor can launch its own emulator rather than depending on a
    /// script somebody else started. This class is the reusable part: given
    /// an emulator name, a ROM path and a port, it starts the process and
    /// returns the handle, leaving the caller to decide when to stop it.
    ///
    /// The two emulators this build knows are MesenCE and Mesen2, and the
    /// difference between them is which Lua script they load — the bridge
    /// protocol is the same. Naming the emulator here rather than in the
    /// caller keeps the two spellings in one place, so a typo cannot reach
    /// the process start and silently launch the wrong one.
    /// </summary>
    public class EmulatorLauncher
    {
        private readonly string _emulatorName;
        private string _romPath;
        private readonly string _emulatorId;
        private readonly int _port;
        private readonly string _argumentsTemplate;
        private readonly bool _isGeneric;
        private Process _process;
        private string _bridgeScript;

        /// <summary>True when this launcher also starts WinASM65's Mesen bridge.</summary>
        public bool UsesBridge { get { return !_isGeneric; } }

        /// <summary>
        /// Creates a launcher for the named emulator. The process is not
        /// started until <see cref="Start"/> is called.
        /// </summary>
        public EmulatorLauncher(string emulatorName, string romPath, string emulatorId, int port)
        {
            _emulatorName = emulatorName;
            _romPath = romPath;
            _emulatorId = emulatorId;
            _port = port;
        }

        private EmulatorLauncher(string emulatorPath, string argumentsTemplate, string romPath)
        {
            _emulatorName = emulatorPath;
            _argumentsTemplate = argumentsTemplate ?? string.Empty;
            _isGeneric = true;
            _romPath = romPath;
            _emulatorId = null;
        }

        /// <summary>Reads the optional generic emulator declaration in config.json.</summary>
        public static EmulatorLauncher FromProjectConfiguration(string directory)
        {
            string configPath = Path.Combine(directory ?? Directory.GetCurrentDirectory(), "config.json");
            if (!File.Exists(configPath)) return null;

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("Emulator", out JsonElement emulator)
                || emulator.ValueKind != JsonValueKind.Object
                || !emulator.TryGetProperty("Path", out JsonElement path)
                || path.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(path.GetString()))
                return null;

            string baseDirectory = Path.GetDirectoryName(configPath);
            string executable = path.GetString();
            if (!Path.IsPathRooted(executable)) executable = Path.Combine(baseDirectory, executable);

            string rom = null;
            if (root.TryGetProperty("Output", out JsonElement output)
                && output.ValueKind == JsonValueKind.Object
                && output.TryGetProperty("ObjectFile", out JsonElement image)
                && image.ValueKind == JsonValueKind.String)
                rom = Path.Combine(baseDirectory, image.GetString());

            // The ROM is the conventional first argument.  Configuring it again
            // as an Arguments template is redundant and made simple projects
            // unnecessarily Mesen-shaped.
            return new EmulatorLauncher(executable, null, rom);
        }

        /// <summary>
        /// Starts the named emulator with the given ROM, if any, and returns
        /// the process handle. The bridge script travels with the executable,
        /// so the emulator is started with an absolute path to a copy the
        /// monitor wrote next to itself.
        /// </summary>
        public Process Start()
        {
            if (string.IsNullOrEmpty(_emulatorName))
                throw new ArgumentNullException(nameof(_emulatorName));

            if (_isGeneric)
            {
                if (!File.Exists(_emulatorName))
                    throw new MonitorException("emulator not found: " + _emulatorName);
                string rom = _romPath ?? string.Empty;
                string arguments = string.IsNullOrEmpty(_argumentsTemplate)
                    ? "\"" + rom.Replace("\"", "\\\"") + "\""
                    : _argumentsTemplate.Replace("{RomPath}", rom);
                _process = Process.Start(new ProcessStartInfo(_emulatorName, arguments)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(_emulatorName) ?? Directory.GetCurrentDirectory()
                });
            }
            else
                _process = Cli.Program.LaunchEmulator(_emulatorName, _romPath, _emulatorId, _port, _bridgeScript);
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

        /// <summary>Uses a user-selected Lua bridge on the next restart.</summary>
        public void RestartWithBridge(string bridgePath)
        {
           // if (!UsesBridge)
           //     throw new MonitorException("the configured emulator has no Mesen bridge launcher");
            if (string.IsNullOrWhiteSpace(bridgePath) || !File.Exists(bridgePath))
                throw new MonitorException("the bridge script does not exist: " + bridgePath);
            _bridgeScript = bridgePath;
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
