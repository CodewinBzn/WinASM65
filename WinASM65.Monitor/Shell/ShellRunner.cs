using System;
using System.Diagnostics;
using System.IO;
using Terminal.Gui;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// Owns the Terminal.Gui lifecycle and the emulator process.
    ///
    /// Both halves are here for the same reason: each has exactly one correct order
    /// and getting it wrong leaks something. <c>Application.Init</c> takes over the
    /// console and <c>Shutdown</c> gives it back, so an <c>Init</c> without a
    /// <c>Shutdown</c> leaves the user's terminal in raw mode with no cursor. An
    /// emulator started without a kill leaves a Mesen process holding a Lua script
    /// open against a ROM nobody is looking at.
    ///
    /// So the rule is one shape, applied once: whatever happens between the two
    /// <c>try</c> blocks, both are released. Nothing here holds logic a test cannot
    /// reach — the emulator is handed in already started, and this class only ever
    /// stops it.
    ///
    /// The run loop is bounded by a parameter rather than by the caller's own hooks.
    /// <c>Application.Init</c> resets Terminal.Gui's static event table, so a handler
    /// a caller subscribes before this call is silently dropped and the loop then
    /// waits for a keystroke that will never come. That is not a test-only problem:
    /// it means the class that owns the console could not be stopped by anyone but
    /// its owner, which is exactly the class whose lifecycle has to be provable.
    /// </summary>
    public static class ShellRunner
    {
        /// <summary>
        /// The bound for a run that lasts until the user quits: no iteration limit,
        /// and the only one a real terminal can honour.
        /// </summary>
        public const int UntilQuit = 0;

        /// <summary>
        /// A run of a single pass: draw the shell once and end. The bound a headless
        /// caller can give honestly, since it needs no keystroke and no waiting.
        /// </summary>
        public const int OneIteration = 1;

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
        /// </summary>
        public static int Run(MonitorSession session, ShellTheme theme, IListingSource listing,
            string directory, Process emulator)
        {
            return Run(session, theme, listing, directory, emulator, null);
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
            return Run(session, theme, listing, directory, emulator, driver, UntilQuit);
        }

        /// <summary>
        /// A run that lasts at most <paramref name="maxIterations"/> passes of the
        /// run loop, then ends on its own.
        ///
        /// The bound is how a caller without a keyboard gets a run it can finish. It
        /// is honoured by the runner itself, after <c>Init</c>, so it works for every
        /// caller rather than only for the ones that guess correctly about when
        /// Terminal.Gui clears its hooks. <see cref="UntilQuit"/> means no bound, and
        /// is what the executable passes.
        /// </summary>
        public static int Run(MonitorSession session, ShellTheme theme, IListingSource listing,
            string directory, Process emulator, IConsoleDriver driver, int maxIterations)
        {
            Exception failure = null;
            bool initialised = false;

            try
            {
                // The guard is inside the released region, not before it. A run
                // that is refused for having no session has already been handed a
                // process to look after, and refusing is exactly as good a moment to
                // stop it as any other. The same holds for the bound: it is checked
                // here, before Init, so a nonsense one is refused without the console
                // ever being taken.
                if (session == null)
                    throw new ArgumentNullException("session");
                if (maxIterations < 0)
                    throw new ArgumentOutOfRangeException("maxIterations", maxIterations,
                        "use " + UntilQuit + " for a run that lasts until the user quits");

                Application.Init(driver);
                initialised = true;

                ShellWindow window = new ShellWindow(session, theme, listing, directory);
                window.PostMessage("Ctrl+Q quits, F1 lists the keys.");

                // The driver reports the real terminal size, which is the only
                // honest input to the breakpoint rule. The handlers are closures
                // over this one window rather than static fields: a static would
                // outlive the run and relayout a window nobody is looking at.
                EventHandler<SizeChangedEventArgs> onResized = delegate (object sender, SizeChangedEventArgs args)
                {
                    if (args != null && args.Size.HasValue)
                        window.ApplyLayout(ShellLayout.ForSize(args.Size.Value.Width, args.Size.Value.Height));
                };

                EventHandler onReady = delegate (object sender, EventArgs args)
                {
                    int width = window.Frame.Width;
                    int height = window.Frame.Height;

                    // A driver that reports no size, which happens under redirection,
                    // must not produce a zero-width layout and a refusal the user
                    // cannot read.
                    if (width <= 0)
                        width = ShellLayout.WideMinimum;
                    if (height <= 0)
                        height = 24;

                    window.ApplyLayout(ShellLayout.ForSize(width, height));
                };

                Application.SizeChanging += onResized;
                window.Ready += onReady;

                // Installed here, after Init, because Init is what clears it.
                // Handing the caller a bound instead of a hook is the whole point:
                // a hook subscribed before the call is dropped by Init, and the loop
                // then sits on a keypress from a terminal that does not exist.
                IterationBound bound = IterationBound.Install(maxIterations);

                try
                {
                    Application.Run(window);
                }
                finally
                {
                    // Removed in the same place it was installed, so a run that threw
                    // mid-loop cannot leave a hook behind for the next one.
                    bound.Remove();

                    Application.SizeChanging -= onResized;
                    window.Ready -= onReady;
                }

                return 0;
            }
            catch (Exception ex)
            {
                failure = ex;
                return 1;
            }
            finally
            {
                // Shutdown first, so the console is restored even when the run
                // failed; then the emulator, so the machine is stopped even when
                // the terminal could not be restored.
                if (initialised)
                {
                    try { Application.Shutdown(); }
                    catch (Exception) { /* the console is already gone; nothing to restore */ }
                }

                StopEmulator(emulator);

                if (failure != null)
                    Report(failure);
            }
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

        /// <summary>
        /// How long the run loop is allowed to go on, wired to the library's own
        /// stop hooks and to nothing of its own.
        ///
        /// One iteration is <see cref="Application.EndAfterFirstIteration"/>, which is
        /// what the library provides for it. Any larger count raises
        /// <see cref="Application.Iteration"/> — subscribed after <c>Init</c>, since
        /// that is the call that empties the table — and asks for the ordinary stop
        /// once the count is reached. <see cref="UntilQuit"/> subscribes to nothing:
        /// an unbounded run is ended by a keystroke, which is the only end a real
        /// terminal can produce.
        /// </summary>
        private sealed class IterationBound
        {
            private readonly int _iterations;
            private int _seen;

            private IterationBound(int iterations)
            {
                _iterations = iterations;
            }

            /// <summary>Arms the bound. Must be called after <c>Application.Init</c>.</summary>
            public static IterationBound Install(int iterations)
            {
                IterationBound bound = new IterationBound(iterations);

                if (iterations == 1)
                {
                    Application.EndAfterFirstIteration = true;
                }
                else if (iterations > 1)
                {
                    Application.Iteration += bound.OnIteration;
                }

                return bound;
            }

            /// <summary>Disarms the bound and clears the static state it used.</summary>
            public void Remove()
            {
                if (_iterations > 1)
                    Application.Iteration -= OnIteration;

                Application.EndAfterFirstIteration = false;
            }

            private void OnIteration(object sender, IterationEventArgs args)
            {
                if (++_seen >= _iterations)
                    Application.RequestStop();
            }
        }
    }
}