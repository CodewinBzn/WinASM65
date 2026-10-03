using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using WinASM65.Cpu;
using WinASM65.Monitor;
using WinASM65.Monitor.Protocol;
using WinASM65.Monitor.Shell;

namespace WinASM65.Monitor.Cli
{
    /// <summary>
    /// Interactive monitor entry point.
    ///
    /// The monitor itself is a library on purpose: everything it does is reachable
    /// from tests without an emulator. This is only the shell around it — it starts
    /// MesenCE if asked, waits for the bridge, prints what the session answers, and
    /// holds no logic of its own.
    ///
    /// All commands live in <see cref="MonitorSession"/>, so a line typed here and a
    /// line sent over a socket cannot mean different things.
    /// </summary>
    public static class Program
    {
        private const int DefaultPort = 45678;
        private const int ConnectTimeoutMs = 15000;
        private const int ConnectRetryMs = 250;

        private const string MesenCe = "mesence";
        private const string Mesen2 = "mesen2";

        public static int Main(string[] args)
        {
            int port = DefaultPort;
            string mesenPath = null;
            string romPath = null;
            string emulator = MesenCe;
            string themePath = null;
            bool? wantShell = null;

            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--port":
                            port = int.Parse(args[++i]);
                            break;
                        case "--mesen":
                            mesenPath = args[++i];
                            break;
                        case "--rom":
                            romPath = args[++i];
                            break;
                        case "--emulator":
                            emulator = args[++i].ToLowerInvariant();
                            break;
                        case "--theme":
                            themePath = args[++i];
                            break;
                        case "--tui":
                            wantShell = true;
                            break;
                        case "--repl":
                            wantShell = false;
                            break;
                        case "--help":
                        case "-h":
                            PrintUsage();
                            return 0;
                        default:
                            Console.Error.WriteLine("Unknown option: " + args[i]);
                            PrintUsage();
                            return 2;
                    }
                }

                if (emulator != MesenCe && emulator != Mesen2)
                {
                    Console.Error.WriteLine("Unknown emulator: " + emulator + " (expected " + MesenCe + " or " + Mesen2 + ")");
                    return 2;
                }

                if (mesenPath != null && romPath == null)
                {
                    Console.Error.WriteLine("--mesen requires --rom.");
                    return 2;
                }

                Process mesen = null;
                if (mesenPath != null)
                {
                    mesen = LaunchEmulator(mesenPath, romPath, emulator, port);
                    if (mesen == null)
                        return 3;
                }

                try
                {
                    // The session takes the backend, so the shell never holds the
                    // protocol itself. MesenCE refuses PAUSE, STEP, BREAK and RESET
                    // with a named reason, and that reason reaches the user intact
                    // because the backend re-raises what the bridge said. Mesen2
                    // answers all of them, because it has the API to do it.
                    using (BridgeMemoryBackend backend = Connect(port))
                    {
                        // One CPU instance, given to both the session and the listing.
                        // They have to agree: what a listing reports as a mnemonic, how
                        // long each form is and how many cycles it takes all come from
                        // the opcode table, so a listing built against a different CPU
                        // would colour a source by rules the assembler never applied.
                        ICpuInstructionSet cpu = new Cpu6502();

                        MonitorSession session =
                            new MonitorSession(backend, cpu, Directory.GetCurrentDirectory());

                        if (UseShell(wantShell))
                        {
                            ShellTheme theme = ShellTheme.Load(themePath);
                            return ShellRunner.Run(session, theme,
                                ListingSourceFactory.Create(cpu, Directory.GetCurrentDirectory()),
                                Directory.GetCurrentDirectory(), mesen);
                        }

                        return RunRepl(session);
                    }
                }
                finally
                {
                    // Runs on every path out of the shell as well as the REPL, so a
                    // crash inside Terminal.Gui cannot leave Mesen running.
                    ShellRunner.StopEmulator(mesen);
                }
            }
            catch (MonitorException ex)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Unexpected error: " + ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// Whether to draw the TUI rather than read lines.
        ///
        /// The TUI owns the console: it puts the terminal into an alternate screen
        /// and draws over it. On a redirected stdin or stdout there is no terminal
        /// to own, and a shell started there produces escape sequences in a file
        /// and never sees the keys that would close it. So redirection selects the
        /// REPL, which is exactly the case it was built for.
        ///
        /// <c>--tui</c> and <c>--repl</c> override the guess, for the user who
        /// knows better.
        /// </summary>
        internal static bool UseShell(bool? requested)
        {
            if (requested.HasValue)
                return requested.Value;

            return !Console.IsInputRedirected && !Console.IsOutputRedirected;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("WinASM65 monitor");
            Console.WriteLine("  --port <n>       bridge port (default " + DefaultPort + ")");
            Console.WriteLine("  --mesen <exe>    launch this emulator with the bridge");
            Console.WriteLine("  --rom <file>     ROM to load (required with --mesen)");
            Console.WriteLine("  --emulator <id>  " + MesenCe + " (default, memory only) or " + Mesen2 + " (full control)");
            Console.WriteLine("  --theme <file>   palette to load (default: theme.json beside the executable)");
            Console.WriteLine("  --tui | --repl   force the terminal shell, or the line prompt");
            Console.WriteLine("With no --mesen, the monitor attaches to an already running bridge.");
            Console.WriteLine("The shell is chosen automatically: with a real terminal it draws the");
            Console.WriteLine("panes, and with a redirected stdin or stdout it reads lines instead.");
        }

        /// <summary>
        /// Starts an emulator headless with the bridge script, in the invocation its
        /// own build was proven with. The two supported emulators disagree on almost
        /// everything: argument order, script naming and whether the script sandbox
        /// has to be opened before a socket can exist. Guessing would produce a
        /// bridge that never listens, which looks exactly like a crash.
        /// </summary>
        private static Process LaunchEmulator(string emulatorPath, string romPath, string emulator, int port)
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

            string scriptName = emulator == Mesen2 ? "bridge_mesen2.lua" : "bridge.lua";
            string bridgeScript = Path.Combine(AppContext.BaseDirectory, "Bridge", scriptName);
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

            if (emulator == Mesen2)
            {
                if (!EnsureMesen2ScriptSettings(emulatorPath))
                    return null;

                start.ArgumentList.Add("--testRunner");
                start.ArgumentList.Add(bridgeScript);
                start.ArgumentList.Add(romPath);
                start.ArgumentList.Add("-novideo");
                start.ArgumentList.Add("-noaudio");
                start.ArgumentList.Add("-noinput");
                start.ArgumentList.Add("-enablestdout");
                start.ArgumentList.Add("-donotsavesettings");
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
                string candidate = Path.Combine(dir.FullName, "WinASM65.Monitor", "Bridge", scriptName);
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        /// <summary>
        /// Retries the connection: the bridge needs a moment after the ROM loads
        /// before it listens, and failing on the first refused attempt would make
        /// launching Mesen from here unusable.
        /// </summary>
        private static BridgeMemoryBackend Connect(int port)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(ConnectTimeoutMs);
            while (true)
            {
                try
                {
                    return new BridgeMemoryBackend(ProtocolClient.Connect(port));
                }
                catch (MonitorException)
                {
                    if (DateTime.UtcNow >= deadline)
                        throw new MonitorException(
                            "No bridge answered on 127.0.0.1:" + port + " within "
                            + (ConnectTimeoutMs / 1000) + "s.");
                }
                catch (SocketException)
                {
                    if (DateTime.UtcNow >= deadline)
                        throw new MonitorException(
                            "No bridge answered on 127.0.0.1:" + port + " within "
                            + (ConnectTimeoutMs / 1000) + "s.");
                }
                catch (IOException)
                {
                    if (DateTime.UtcNow >= deadline)
                        throw new MonitorException(
                            "The bridge on 127.0.0.1:" + port + " closed the connection during startup.");
                }

                Thread.Sleep(ConnectRetryMs);
            }
        }

        private static int RunRepl(MonitorSession session)
        {
            Console.WriteLine("WinASM65 monitor - " + session.Backend.EmulatorName
                + " " + session.Backend.EmulatorVersion);
            Console.WriteLine("Type 'help' for commands, 'quit' to leave.");

            while (!session.ShouldQuit)
            {
                Console.Write("mon> ");
                Console.Out.Flush();

                string line = Console.ReadLine();
                if (line == null)
                    return 0;

                foreach (string answer in session.Execute(line))
                    Console.WriteLine(answer);
            }

            return 0;
        }
    }
}