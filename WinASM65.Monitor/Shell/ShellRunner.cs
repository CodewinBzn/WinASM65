using System;
using System.Diagnostics;
using System.IO;
using SharpConsoleUI;
using SharpConsoleUI.Drivers;
using WinASM65.Monitor.Abstractions;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// Runs the shell to completion against whatever driver the terminal offers.
    /// Returns the process exit code.
    ///
    /// <paramref name="emulator"/> may be null, which is the ordinary case when
    /// the monitor attached to a bridge somebody else started.
    ///
    /// <paramref name="listing"/> is the seam the pane draws from, and null gets the
    /// factory's own: the listing for the default CPU, which is all a caller with no
    /// CPU of its own to offer can honestly be given. The program's own path passes
    /// the CPU the session was built with, so the two cannot disagree about what a
    /// mnemonic is.
    ///
    /// <paramref name="sourceFile"/> is the optional source file to open in the editor
    /// at startup. When null, no file is opened initially.
    /// </summary>
    public static class ShellRunner
    {
        /// <summary>One pass of the shell's run loop.</summary>
        public const int UntilQuit = -1;

        /// <summary>A run of a single pass: draw the shell once and end.</summary>
        public const int OneIteration = 1;

        /// <summary>
        /// Runs the shell to completion against whatever driver the terminal offers.
        /// Returns the process exit code.
        /// </summary>
        public static int Run(MonitorSession session, ShellTheme theme, IListingSource listing,
            string directory, Process emulator)
        {
            return Run(session, theme, listing, directory, emulator, null, UntilQuit, null);
        }

        /// <summary>
        /// The same run, on a driver the caller chooses.
        ///
        /// The overload exists so the lifecycle can be exercised without a terminal:
        /// initialisation, the run loop and teardown are the parts that break, and
        /// none of them needs a real console to be worth testing.
        /// </summary>
        public static int Run(MonitorSession session, ShellTheme theme, IListingSource listing,
            string directory, Process emulator, IConsoleDriver driver)
        {
            return Run(session, theme, listing, directory, emulator, driver, UntilQuit, null);
        }

        /// <summary>
        /// A run that lasts at most <paramref name="maxIterations"/> passes of the
        /// run loop, then ends on its own.
        /// </summary>
        public static int Run(MonitorSession session, ShellTheme theme, IListingSource listing,
            string directory, Process emulator, IConsoleDriver driver, int maxIterations)
        {
            return Run(session, theme, listing, directory, emulator, driver, maxIterations, null);
        }

        /// <summary>
        /// The run, with an optional source file to open at startup.
        /// </summary>
        public static int Run(MonitorSession session, ShellTheme theme, IListingSource listing,
            string directory, Process emulator, IConsoleDriver driver, int maxIterations,
            string sourceFile)
        {
            int exitCode = 0;
            Exception failure = null;

            try
            {
                if (session == null)
                    throw new ArgumentNullException(nameof(session));
                if (maxIterations < 0 && maxIterations != UntilQuit)
                    throw new ArgumentOutOfRangeException(nameof(maxIterations), maxIterations,
                        "use " + UntilQuit + " for a run that lasts until the user quits");

                IConsoleDriver chosen = driver ?? new NetConsoleDriver();
                ConsoleWindowSystem ws = new ConsoleWindowSystem(chosen);

                ShellWindow window = new ShellWindow(session, theme, listing, directory, ws);
                if (!string.IsNullOrEmpty(sourceFile))
                {
                    int code = window.TryOpenSource(sourceFile);
                    if (code != 0)
                        return code;
                }
                window.PostMessage("Ctrl+Q quits, F1 lists the keys.");

                int initWidth = 120;
                int initHeight = 34;
                try
                {
                    if (Console.WindowWidth > 0)
                        initWidth = Console.WindowWidth;
                    if (Console.WindowHeight > 0)
                        initHeight = Console.WindowHeight;
                }
                catch
                {
                    // Headless or redirected console
                }
                window.OnResized(initWidth, initHeight);

                ws.WindowResized += (s, e) =>
                    window.OnResized((int)e.Width, (int)e.Height);

                window.RegisterWindows(ws);

                if (maxIterations == UntilQuit)
                {
                    return ws.Run();
                }

                // A bounded run: drive frames until the bound is reached, then
                // exit with the code the caller asked for. This is the path a
                // headless test takes, and it must not leave the framework in a
                // state the next run inherits.
                using (HostedSession hosted = ws.BeginHosted())
                {
                    int iterations = 0;
                    while (hosted.Tick() && iterations < maxIterations)
                        iterations++;

                    return 0;
                }
            }
            catch (Exception ex)
            {
                failure = ex;
                exitCode = 1;
            }
            finally
            {
                // Stop the emulator, so the machine is stopped even when the
                // terminal could not be restored.
                StopEmulator(emulator);

                if (failure != null)
                    Report(failure);
            }

            return exitCode;
        }

        /// <summary>
        /// Stops the emulator this process started, and waits for it to actually
        /// be gone.
        ///
        /// The wait is the point. <see cref="Process.Kill"/> returns once the kill
        /// is delivered, not once the process has left; a monitor that exits in that
        /// window hands the emulator to init, which reparents it and leaves a
        /// headless Mesen running the bridge against a ROM with no owner. The Lua
        /// script lives inside the emulator process, so a stopped emulator is a
        /// stopped script with nothing left to clean up.
        /// </summary>
        public static void StopEmulator(Process emulator)
        {
            if (emulator == null)
                return;

            try
            {
                if (!emulator.HasExited)
                {
                    emulator.Kill();
                    emulator.WaitForExit(5000);
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

        private static void Report(Exception failure)
        {
            // The console may be in raw mode if Shutdown itself failed, so this is
            // written to stderr and nothing else.
            try
            {
                Console.Error.WriteLine("The shell stopped: " + failure.Message);
            }
            catch (IOException)
            {
            }
        }
    }
}