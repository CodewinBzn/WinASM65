using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Cpu;
using WinASM65.Monitor.Protocol;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// The monitor as a user meets it: the executable, launched, fed lines on its
    /// standard input, answered on its standard output.
    ///
    /// The rest of the suite tests the library. This one tests the thing that only
    /// exists once the program is started — the argument parsing, the handshake, the
    /// prompt, the fact that a command typed at the prompt reaches a machine and its
    /// answer comes back. Every one of those is a place where the library can be
    /// perfect and the tool still not work.
    ///
    /// It launches the real binary rather than calling Main, because calling Main
    /// would replace the process's input, which is the thing under test.
    /// </summary>
    [TestClass]
    public class MonitorProgramTests
    {
        private static string Executable
        {
            get
            {
                // The test project references the monitor project, so the executable
                // is copied next to the test assembly. Falling back to the source
                // layout keeps the test working if that copy is ever removed.
                string beside = Path.Combine(AppContext.BaseDirectory, "WinASM65.Monitor.exe");
                if (File.Exists(beside))
                    return beside;

                DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "WinASM65.Monitor", "bin",
                        "Debug", "net8.0", "WinASM65.Monitor.exe");
                    if (File.Exists(candidate))
                        return candidate;
                    dir = dir.Parent;
                }
                Assert.Fail("WinASM65.Monitor.exe not found next to the tests or in the source tree.");
                return null;
            }
        }

        /// <summary>
        /// Runs the executable, writes the lines, and returns everything it printed.
        ///
        /// stdin is closed right after writing, so the program reaches end of input
        /// and exits on its own. A monitor that only exits on QUIT would hang here
        /// rather than pass silently, which is the point: the timeout is the test.
        /// </summary>
        private static string Run(FakeMemoryBackend machine, ProtocolServer server, params string[] lines)
        {
            ProcessStartInfo start = new ProcessStartInfo(Executable)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--port");
            start.ArgumentList.Add(server.Port.ToString());

            StringBuilder input = new StringBuilder();
            foreach (string line in lines)
                input.AppendLine(line);

            using (Process process = Process.Start(start))
            {
                process.StandardInput.Write(input.ToString());
                process.StandardInput.Close();

                string output = process.StandardOutput.ReadToEnd();
                string errors = process.StandardError.ReadToEnd();

                if (!process.WaitForExit(30000))
                {
                    try { process.Kill(); }
                    catch (InvalidOperationException) { }
                    Assert.Fail("the monitor did not exit within 30s.\nstdout:\n" + output + "\nstderr:\n" + errors);
                }

                Assert.AreEqual(0, process.ExitCode,
                    "the monitor exited with " + process.ExitCode + "\nstderr:\n" + errors);
                return output;
            }
        }

        private static ProtocolServer Serve(FakeMemoryBackend machine)
        {
            ProtocolServer server = new ProtocolServer(machine, CpuFactory.Create("6502"));
            server.Start();
            return server;
        }

        [TestMethod]
        public void LeProgrammeAnnonceLHotePuisTraiteLesCommandes()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            using (ProtocolServer server = Serve(machine))
            {
                machine.Write(0x8000, new byte[] { 0xA9, 0x5A, 0x60 });

                string output = Run(machine, server,
                    "READ $8000 3",
                    "READ $8000 3",
                    "QUIT");

                // The banner names the host, so "the tests pass" without naming it
                // would prove nothing about which machine answered.
                StringAssert.Contains(output, "FakeEmu");
                StringAssert.Contains(output, "$8000 A95A60");
                StringAssert.Contains(output, "mon> ");
            }
        }

        [TestMethod]
        public void UneFauteDeFrappeEstRepondueEtLeProgrammeResteUtilisable()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            using (ProtocolServer server = Serve(machine))
            {
                string output = Run(machine, server,
                    "READ $ZZZZ 2",
                    "WRITE $1000 A9 5A 60",
                    "READ $1000 3",
                    "QUIT");

                StringAssert.Contains(output, "ERR");
                CollectionAssert.AreEqual(new byte[] { 0xA9, 0x5A, 0x60 }, machine.Read(0x1000, 3));
            }
        }

        [TestMethod]
        public void LaideEstAccessibleSansMachine()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            using (ProtocolServer server = Serve(machine))
            {
                string output = Run(machine, server, "HELP", "QUIT");

                StringAssert.Contains(output, "ASSEMBLE");
                StringAssert.Contains(output, "LOAD");
                StringAssert.Contains(output, "UNITS");
            }
        }

        [TestMethod]
        public void UneAssembleePuisUnChargementAtteignentLaMachine()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            using (ProtocolServer server = Serve(machine))
            {
                string source = Path.Combine(Path.GetTempPath(),
                    "WinASM65ProgramTest_" + Guid.NewGuid().ToString("N") + ".asm");
                File.WriteAllText(source,
                    ".org $8000\nRoutine: lda #$5A\n rts\n .export Routine\n");
                try
                {
                    // The unit is named after the file, not after the .export inside it.
                string unit = Path.GetFileNameWithoutExtension(source);

                string output = Run(machine, server,
                        "ASSEMBLE " + source,
                        "LOAD " + unit + " $9000",
                        "READ $9000 3",
                        "QUIT");

                    // The whole point of the tool, exercised through the executable:
                    // assemble once, place it, and read back what the machine holds.
                    Assert.AreEqual(0xA9, machine.Read(0x9000, 1)[0]);
                    Assert.AreEqual(0x60, machine.Read(0x9002, 1)[0]);
                    StringAssert.Contains(output, "$9000 A95A60");
                }
                finally
                {
                    File.Delete(source);
                }
            }
        }

        [TestMethod]
        public void SansPontLeProgrammeEchoueEnDisantLequelIlAttendait()
        {
            // A port just released by a server this test opened, so it is
            // certainly free. Port 1 would do the same job, but it is reserved on
            // Windows, where the refusal arrives as a socket error and never
            // becomes the message the user reads -- the test would then pass for
            // the wrong reason, or fail for one.
            int port;
            using (ProtocolServer released = Serve(new FakeMemoryBackend()))
                port = released.Port;

            ProcessStartInfo start = new ProcessStartInfo(Executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--port");
            start.ArgumentList.Add(port.ToString());

            using (Process process = Process.Start(start))
            {
                string errors = process.StandardError.ReadToEnd();
                Assert.IsTrue(process.WaitForExit(60000), "the monitor waited forever for a bridge that never came");

                Assert.AreNotEqual(0, process.ExitCode);
                StringAssert.Contains(errors, "127.0.0.1:" + port);
            }
        }

        [TestMethod]
        public void UneOptionInconnueEstRefuseeAvecLUsage()
        {
            ProcessStartInfo start = new ProcessStartInfo(Executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--inexistant");

            using (Process process = Process.Start(start))
            {
                string output = process.StandardOutput.ReadToEnd();
                string errors = process.StandardError.ReadToEnd();
                Assert.IsTrue(process.WaitForExit(30000));

                Assert.AreEqual(2, process.ExitCode);
                StringAssert.Contains(errors, "--inexistant");
                StringAssert.Contains(output, "--port");
            }
        }

        [TestMethod]
        public void MesenSansRomEstRefuseAvantDeLancerQuoiQueCeSoit()
        {
            // Launching the emulator and only then complaining about a missing ROM
            // would leave a MesenCE process running with no way to close it.
            ProcessStartInfo start = new ProcessStartInfo(Executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--mesen");
            start.ArgumentList.Add(@"C:\chemin\qui\nexiste\pas\Mesen.exe");

            using (Process process = Process.Start(start))
            {
                string errors = process.StandardError.ReadToEnd();
                Assert.IsTrue(process.WaitForExit(30000));

                Assert.AreEqual(2, process.ExitCode);
                StringAssert.Contains(errors, "--rom");
            }
        }
    }
}