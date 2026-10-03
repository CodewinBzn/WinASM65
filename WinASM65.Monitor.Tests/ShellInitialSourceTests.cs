using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Terminal.Gui;
using WinASM65.Cpu;
using WinASM65.Monitor.Shell;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// Opening a source named at startup.
    ///
    /// This exists because the argument was missing: the monitor could be launched on a
    /// known program and still open on nothing, leaving the user to navigate a tree and
    /// press a key to reach the file they had already named on the command line.
    ///
    /// The refusal matters as much as the opening. A name that does not resolve is
    /// reported before the terminal is taken, so the user reads it on stderr and gets an
    /// exit code — not a shell they have to discover the problem in and then quit.
    ///
    /// ShellRunner.Run brackets its own Init and Shutdown, so these tests hand it a
    /// driver and never take the console themselves.
    /// </summary>
    [TestClass]
    public class ShellInitialSourceTests
    {
        private const string Good = "        .org $C000" + "\n" + "        LDA #$5A" + "\n";

        private static string ScratchDirectory()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "InitialSource");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static int RunWith(string directory, string sessionDirectory, string initialSource)
        {
            FakeDriver driver = new FakeDriver();
            driver.SetWindowSize(120, 40);
            driver.Refresh();

            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                ICpuInstructionSet cpu = new Cpu6502();
                MonitorSession session = new MonitorSession(machine, cpu, sessionDirectory);
                return ShellRunner.Run(session, ShellTheme.Default,
                    ListingSourceFactory.Create(cpu, sessionDirectory), directory, null,
                    driver, ShellRunner.OneIteration, initialSource);
            }
        }

        [TestMethod]
        public void ASourceNamedRelativeToTheDirectoryIsOpened()
        {
            string dir = ScratchDirectory();
            string file = Path.Combine(dir, "startup.asm");
            File.WriteAllText(file, Good);

            try
            {
                Assert.AreEqual(0, RunWith(dir, dir, "startup.asm"),
                    "a source that resolves must not stop the run");
            }
            finally
            {
                File.Delete(file);
            }
        }

        [TestMethod]
        public void ASourceNamedByAbsolutePathIsOpened()
        {
            string dir = ScratchDirectory();
            string file = Path.Combine(dir, "absolute.asm");
            File.WriteAllText(file, Good);

            try
            {
                // The session's directory is elsewhere, so only the absolute path can
                // find this file.
                Assert.AreEqual(0, RunWith(dir, AppContext.BaseDirectory, file));
            }
            finally
            {
                File.Delete(file);
            }
        }

        [TestMethod]
        public void ASourceThatDoesNotExistIsRefusedWithItsOwnExitCode()
        {
            string dir = ScratchDirectory();

            Assert.AreEqual(4, RunWith(dir, dir, "no-such-file.asm"),
                "a name that resolves to nothing must be refused, not left to paint an empty pane");
        }

        [TestMethod]
        public void ASourceOutsideTheDirectoryIsNotSilentlyAccepted()
        {
            // The directory is the session's, and a name that escapes it has to be named
            // in full. Otherwise "startup.asm" in the session directory and one in the
            // working directory would be two different files under one name.
            string dir = ScratchDirectory();
            Assert.AreEqual(4, RunWith(dir, dir, @"..\..\..\..\etc\passwd"));
        }

        [TestMethod]
        public void NoSourceNamedMeansNoOpeningAndNoComplaint()
        {
            string dir = ScratchDirectory();

            Assert.AreEqual(0, RunWith(dir, dir, null), "the default launch is unchanged");
            Assert.AreEqual(0, RunWith(dir, dir, "   "), "whitespace is not a source name");
            Assert.AreEqual(0, RunWith(dir, dir, string.Empty));
        }

        [TestMethod]
        public void TheDriverlessOverloadsCannotBeExercisedWithoutAConsole()
        {
            // Asserted rather than merely noted, because the hazard is easy to walk back
            // into. ShellRunner.Run's short forms take no driver, so they fall through
            // to the real console, and they default to UntilQuit, which is no bound at
            // all. Together that means "run until the user presses Ctrl+Q on a
            // terminal" — the executable's path.
            //
            // A first draft of this file called the five-argument overload to check it
            // still worked after a parameter was added, and hung the whole suite for
            // eight minutes. This asserts the shape that made that true, so the next
            // person to add an overload finds out here instead.
            System.Reflection.MethodInfo[] overloads = typeof(ShellRunner).GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            int driverless = 0;
            foreach (System.Reflection.MethodInfo method in overloads)
            {
                if (method.Name != "Run")
                    continue;

                bool takesDriver = false;
                foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
                {
                    if (parameter.ParameterType == typeof(IConsoleDriver))
                        takesDriver = true;
                }

                if (!takesDriver)
                    driverless++;
            }

            Assert.IsTrue(driverless > 0,
                "the driverless overloads disappeared; if they now take a driver, the ones "
                + "above are no longer the only testable path and this test should be rewritten");

            Assert.AreEqual(0, ShellRunner.UntilQuit,
                "UntilQuit is the absence of a bound, and it is what makes a driverless run untestable");
            Assert.AreEqual(1, ShellRunner.OneIteration,
                "OneIteration is the bound a caller with no keyboard can finish on");
        }
    }
}