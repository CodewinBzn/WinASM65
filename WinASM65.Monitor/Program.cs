using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using WinASM65.Cpu;
using WinASM65.Monitor;
using WinASM65.Monitor.Emulators;
using WinASM65.Monitor.Protocol;
using WinASM65.Monitor.Shell;

namespace WinASM65.Monitor.Cli
{
    /// <summary>
    /// Interactive monitor entry point.
    ///
    /// The monitor itself is a library on purpose: everything it does is reachable
    /// from tests without an emulator. This is only the shell around it — it starts
    /// an emulator with its bridge if asked, waits for the bridge, prints what the
    /// session answers, and holds no logic of its own.
    ///
    /// All commands live in <see cref="MonitorSession"/>, so a line typed here and a
    /// line sent over a socket cannot mean different things.
    /// </summary>
    public static class Program
    {
        private const int DefaultPort = BridgeConnector.DefaultPort;

        public static int Main(string[] args)
        {
            // SharpConsoleUI renders Unicode directly. Windows consoles may still use
            // a legacy code page (for example CP850), which turns accented characters
            // and the UI's box-drawing glyphs into mojibake. Configure the process before
            // any console driver or terminal control is created.
            Console.InputEncoding = System.Text.Encoding.UTF8;
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);

            int port = DefaultPort;
            string emulatorPath = null;
            string romPath = null;
            string emulator = null;
            string bridge = null;
            string themePath = null;
              string sourceFile = null;
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
                        case "--emulator-path":
                        case "--mesen":
                            emulatorPath = args[++i];
                            break;
                        case "--bridge":
                            bridge = Path.GetFullPath(args[++i]);
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
                          case "--source":
                              sourceFile = args[++i];
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

                if (emulatorPath != null && romPath == null)
                {
                    Console.Error.WriteLine("--emulator-path requires --rom.");
                    return 2;
                }

                Process mesen = null;
                EmulatorLauncher launcher = null;
                if (emulatorPath != null)
                {
                    // Any emulator: its adapter (mesen2, mesence, or a custom one) is
                    // chosen by name, or by the executable when no name is given.
                    EmulatorConfiguration configured = EmulatorConfiguration.Of(emulatorPath, emulator);
                    configured.Bridge = bridge;
                    launcher = new EmulatorLauncher(configured, romPath, port);
                    mesen = launcher.Start();
                    if (mesen == null)
                        return 3;
                }
                else
                {
                    // The emulator the project's config.json declares, if any, for
                    // the shell's launch action.
                    launcher = EmulatorLauncher.FromProjectConfiguration(Directory.GetCurrentDirectory());
                }

                try
                {
                    // The session takes the backend, so the shell never holds the
                    // protocol itself. MesenCE refuses PAUSE, STEP, BREAK and RESET
                    // with a named reason, and that reason reaches the user intact
                    // because the backend re-raises what the bridge said. Mesen2
                    // answers all of them, because it has the API to do it.
                    using (IMemoryBackend backend = BridgeConnector.TryConnect(port) ?? new DummyBridgeBackend())
                    {
                        // One CPU instance, given to both the session and the listing.
                        // They have to agree: what a listing reports as a mnemonic, how
                        // long each form is and how many cycles it takes all come from
                        // the opcode table, so a listing built against a different CPU
                        // would colour a source by rules the assembler never applied.
                        ICpuInstructionSet cpu = new Cpu6502();

                        MonitorSession session =
                            new MonitorSession(backend, cpu, Directory.GetCurrentDirectory());

                        // The launcher belongs to the session, not just to the TUI.
                        // Set it before selecting the UI so every entry point (and
                        // every menu created during startup) sees the same launch
                        // configuration.  ShellRunner sets it too for callers that
                        // invoke the shell directly.
                        session.Launcher = launcher;

                        if (UseShell(wantShell))
                        {
                            ShellTheme theme = ShellTheme.Load(themePath);
                            return ShellRunner.Run(session, theme,
                                ListingSourceFactory.Create(cpu, Directory.GetCurrentDirectory()),
                                Directory.GetCurrentDirectory(), mesen, null,
                                ShellRunner.UntilQuit, sourceFile, launcher);
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
            Console.WriteLine("  --emulator-path <exe>  launch this emulator with its bridge (--mesen is the same)");
            Console.WriteLine("  --rom <file>     ROM to load (required with --emulator-path)");
            Console.WriteLine("  --emulator <name> adapter: " + string.Join(", ", EmulatorAdapters.Names)
                + ", or a custom name (default: mesen2 for a Mesen.exe)");
            Console.WriteLine("  --bridge <file>  bridge script to load instead of the adapter's own");
            Console.WriteLine("  --theme <file>   palette to load (default: theme.json beside the executable)");
            Console.WriteLine("  --source <file>  source to open in the editor at startup, relative to the working directory");
            Console.WriteLine("  --tui | --repl   force the terminal shell, or the line prompt");
            Console.WriteLine("With no --emulator-path, the monitor attaches to an already running bridge.");
            Console.WriteLine("The shell is chosen automatically: with a real terminal it draws the");
            Console.WriteLine("panes, and with a redirected stdin or stdout it reads lines instead.");
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
