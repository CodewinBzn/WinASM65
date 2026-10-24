using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using WinASM65.Monitor;
using WinASM65.Projects;

namespace WinASM65.Tooling.Dap
{
    /// <summary>An emulator the adapter started, so it can stop it again.</summary>
    public interface IEmulatorProcess
    {
        bool HasExited { get; }
        event Action Exited;
        void Kill();
    }

    /// <summary>
    /// Everything the debug session does outside its own memory: building, starting
    /// the emulator, connecting. Separate so the session can be tested against a
    /// fake machine without an emulator or a disk.
    /// </summary>
    public interface IDebugEnvironment
    {
        ProjectBuildResult Build(string configurationPath, bool writeImage);

        /// <summary>The emulator named by the project's <c>Emulator.Path</c>, or null.</summary>
        string ConfiguredEmulator(string configurationPath);

        int FreePort();

        /// <summary>Starts the emulator with the bridge; throws with a reason on failure.</summary>
        IEmulatorProcess Launch(string emulatorPath, string romPath, string emulatorId, int port);

        IMemoryBackend Connect(int port, int timeoutMs);
    }

    /// <summary>The real environment: the assembler, Mesen and a socket.</summary>
    public sealed class DebugEnvironment : IDebugEnvironment
    {
        public ProjectBuildResult Build(string configurationPath, bool writeImage)
        {
            return ProjectSession.Open(configurationPath)
                .Build(new ProjectBuildOptions { CaptureListing = true, WriteImage = writeImage });
        }

        public string ConfiguredEmulator(string configurationPath)
        {
            if (string.IsNullOrEmpty(configurationPath) || !File.Exists(configurationPath))
                return null;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(configurationPath)))
                {
                    JsonElement emulator, path;
                    if (document.RootElement.TryGetProperty("Emulator", out emulator)
                        && emulator.ValueKind == JsonValueKind.Object
                        && emulator.TryGetProperty("Path", out path)
                        && path.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(path.GetString()))
                    {
                        string executable = path.GetString();
                        return Path.IsPathRooted(executable)
                            ? executable
                            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configurationPath)), executable));
                    }
                }
            }
            catch (JsonException)
            {
            }
            return null;
        }

        public int FreePort()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public IEmulatorProcess Launch(string emulatorPath, string romPath, string emulatorId, int port)
        {
            Process process = BridgeLauncher.LaunchEmulator(emulatorPath, romPath, emulatorId, port);
            if (process == null)
                throw new MonitorException("the emulator could not be started (see the debug console for the reason)");
            return new EmulatorProcess(process);
        }

        public IMemoryBackend Connect(int port, int timeoutMs)
        {
            // No RAM polling: the adapter reads memory when VS Code asks for it.
            return BridgeConnector.Connect(port, timeoutMs, poll: false);
        }

        private sealed class EmulatorProcess : IEmulatorProcess
        {
            private readonly Process _process;

            public EmulatorProcess(Process process)
            {
                _process = process;
                _process.EnableRaisingEvents = true;
                _process.Exited += (s, e) => Exited?.Invoke();
            }

            public bool HasExited
            {
                get
                {
                    try { return _process.HasExited; }
                    catch (InvalidOperationException) { return true; }
                }
            }

            public event Action Exited;

            public void Kill()
            {
                try
                {
                    if (!_process.HasExited)
                        _process.Kill(true);
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
    }
}
