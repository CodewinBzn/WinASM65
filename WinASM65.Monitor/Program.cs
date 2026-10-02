using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using WinASM65.Cpu;
using WinASM65.Monitor;
using WinASM65.Monitor.Protocol;

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

        public static int Main(string[] args)
        {
            int port = DefaultPort;
            string mesenPath = null;
            string romPath = null;

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

                if (mesenPath != null && romPath == null)
                {
                    Console.Error.WriteLine("--mesen requires --rom.");
                    return 2;
                }

                Process mesen = null;
                if (mesenPath != null)
                {
                    mesen = LaunchMesen(mesenPath, romPath);
                    if (mesen == null)
                        return 3;
                }

                try
                {
                    // The session takes the backend, so the shell never holds the
                    // protocol itself. MesenCE refuses PAUSE, STEP, BREAK and RESET
                    // with a named reason, and that reason reaches the user intact
                    // because the backend re-raises what the bridge said.
                    using (BridgeMemoryBackend backend = Connect(port))
                    {
                        return Run(new MonitorSession(backend, new Cpu6502(), Directory.GetCurrentDirectory()));
                    }
                }
                finally
                {
                    if (mesen != null && !mesen.HasExited)
                    {
                        try { mesen.Kill(); }
                        catch (InvalidOperationException) { }
                    }
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

        private static void PrintUsage()
        {
            Console.WriteLine("WinASM65 monitor");
            Console.WriteLine("  --port <n>    bridge port (default " + DefaultPort + ")");
            Console.WriteLine("  --mesen <exe> launch this MesenCE with the bridge");
            Console.WriteLine("  --rom <file>  ROM to load (required with --mesen)");
            Console.WriteLine("With no --mesen, the monitor attaches to an already running bridge.");
        }

        /// <summary>
        /// Starts MesenCE headless with the bridge script, the same invocation the
        /// bridge was proven with.
        /// </summary>
        private static Process LaunchMesen(string mesenPath, string romPath)
        {
            if (!File.Exists(mesenPath))
            {
                Console.Error.WriteLine("Mesen not found: " + mesenPath);
                return null;
            }
            if (!File.Exists(romPath))
            {
                Console.Error.WriteLine("ROM not found: " + romPath);
                return null;
            }

            string bridgeScript = Path.Combine(AppContext.BaseDirectory, "Bridge", "bridge.lua");
            if (!File.Exists(bridgeScript))
            {
                // Fall back to the source tree when running from a build output that
                // did not copy the script, so the tool works straight from bin.
                bridgeScript = FindBridgeInSourceTree();
            }
            if (bridgeScript == null)
            {
                Console.Error.WriteLine("bridge.lua not found next to the executable or in the source tree.");
                return null;
            }

            ProcessStartInfo start = new ProcessStartInfo(mesenPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("--testrunner");
            start.ArgumentList.Add(romPath);
            start.ArgumentList.Add(bridgeScript);

            Process process = Process.Start(start);
            if (process == null)
            {
                Console.Error.WriteLine("Could not start Mesen.");
                return null;
            }

            process.OutputDataReceived += (s, e) => { if (e.Data != null) Console.Error.WriteLine("[mesen] " + e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.Error.WriteLine("[mesen] " + e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }

        private static string FindBridgeInSourceTree()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "WinASM65.Monitor", "Bridge", "bridge.lua");
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

        private static int Run(MonitorSession session)
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