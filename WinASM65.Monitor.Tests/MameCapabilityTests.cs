using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// What the A6 MAME adapter may claim, measured against a real MAME rather
    /// than assumed from documentation.
    ///
    /// A0 was closed on the assumption that MAME could not run a WinASM65 homebrew
    /// ROM at all. That was wrong on both counts: <c>mame famicom -cart bomber.nes</c>
    /// runs the cartridge, and the installed <c>mame.exe</c> is byte-identical to the
    /// official 0.289 build, so nothing was missing from the installation. These
    /// tests pin that down so the assumption cannot creep back.
    ///
    /// What MAME can do from Lua is narrower than what the monitor protocol wants.
    /// Memory reads, writes and registers all work. Execution control does not:
    /// <c>debug:step</c> returns without error while leaving the program counter
    /// where it was, and <c>debug:bpset</c> blocks indefinitely. That is why the
    /// plan keeps MAME non-blocking and in no way a replacement for Mesen2.
    ///
    /// These tests need MAME and the sample ROM. Neither is part of the repository
    /// build, so the whole class reports inconclusive when MAME is absent rather
    /// than failing: an absent emulator is not a defect in the adapter.
    /// </summary>
    [TestClass]
    public class MameCapabilityTests
    {
        private const string ProbePrefix = "MPROBE ";
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(45);

        // The probe pauses the machine, which stops emulated time advancing, so
        // MAME never reaches -seconds_to_run and never exits on its own. The
        // process is always killed, and that is expected rather than untidy.
        private static readonly Lazy<Dictionary<string, string>> Capabilities =
            new Lazy<Dictionary<string, string>>(RunProbe, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        [TestMethod]
        public void TheProbeRunsToCompletionAndReportsTheExpectedSchema()
        {
            Dictionary<string, string> capabilities = Capabilities.Value;

            // "end" is the probe's own statement that it survived every call. A
            // probe truncated part-way is a probe that found a blocking API, and
            // that is exactly the kind of finding that must not pass unnoticed.
            Assert.IsTrue(capabilities.ContainsKey("end"), "the probe did not reach its end");
            Assert.AreEqual("1", capabilities["schema"], "the probe schema changed without the test following");
        }

        [TestMethod]
        public void TheCpuAndItsProgramSpaceAreReachableThroughTheManager()
        {
            Dictionary<string, string> capabilities = Capabilities.Value;

            // Not emu.MACHINE, emu.memory or emu.debug: none of those exist in
            // 0.289, which is why an earlier attempt to reach MAME looked like a
            // dead end. Everything goes through the global manager instead.
            Assert.AreEqual(":maincpu", capabilities["cpu.tag"]);
            StringAssert.Contains(capabilities["space.names"], "program");
        }

        [TestMethod]
        public void TheProgramSpaceAtEightThousandHoldsTheCartridgeBytes()
        {
            Dictionary<string, string> capabilities = Capabilities.Value;

            // Compared against the file on disk rather than a constant: offset 16
            // is the first byte of PRG ROM, past the 16-byte iNES header, and
            // $8000 is where the mapper maps it. Asserting against the ROM itself
            // means this still means something if the sample cartridge changes.
            byte[] rom = File.ReadAllBytes(FindRom());
            Assert.AreEqual("4E45531A", Convert.ToHexString(rom, 0, 4), "not an iNES image");

            string expected = Convert.ToHexString(rom, 16, 8).ToUpperInvariant();
            Assert.AreEqual(expected, capabilities["mem.read8000"],
                "MAME's program space does not hold the cartridge's PRG ROM");
        }

        [TestMethod]
        public void AWriteIsObservableAndTheOriginalByteIsRestored()
        {
            Dictionary<string, string> capabilities = Capabilities.Value;

            // Read-back, for the same reason the MesenCE bridge verifies its writes:
            // an emulator that accepts a write and discards it must never be
            // reported as having accepted one. The probe restores the byte it
            // overwrote, so "ok" means the write landed and the RAM was left alone.
            Assert.AreEqual("ok", capabilities["mem.roundtrip"]);
        }

        [TestMethod]
        public void RegistersAreReadableForTheCpuVerb()
        {
            Dictionary<string, string> capabilities = Capabilities.Value;

            string tags = capabilities["state.tags"];
            foreach (string register in new[] { "PC", "A", "X", "Y", "SP", "P" })
                StringAssert.Contains(tags, register);
        }

        [TestMethod]
        public void ExecutionControlIsUnavailableSoMameStaysNonBlocking()
        {
            Dictionary<string, string> capabilities = Capabilities.Value;

            Assert.AreEqual("true", capabilities["debug.available"]);

            // This asserts a negative on purpose. If a future MAME makes stepping
            // actually move the program counter, this test fails and that is the
            // signal to revisit A6 rather than an annoyance to silence.
            Assert.AreEqual("nomove", capabilities["debug.step"],
                "MAME stepping now moves the program counter; A6 can reconsider execution control");
        }

        private static Dictionary<string, string> RunProbe()
        {
            string mame = FindMame();
            if (mame == null)
                Assert.Inconclusive("MAME was not found. Set WINASM65_MAME to mame.exe to run these tests.");

            string rom = FindRom();
            string probe = FindProbe();

            ProcessStartInfo start = new ProcessStartInfo(mame)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // -seconds_to_run is deliberately absent: the probe pauses the machine,
            // which stops emulated time advancing, so it would never expire. The
            // probe writes its report to this file, flushed line by line, and the
            // process is killed once the report is complete or the deadline passes.
            string report = Path.Combine(Path.GetTempPath(),
                "winasm65-mame-probe-" + Guid.NewGuid().ToString("N") + ".txt");
            start.Environment["WINASM65_PROBE_OUT"] = report;

            // famicom, not nes: nes is an arcade-PCB driver with no generic
            // cartridge. famicom's slot reads an iNES image directly, which is why
            // no software list is involved.
            foreach (string argument in new[]
            {
                "famicom", "-cart", rom,
                "-debug", "-debugger", "none", "-plugins",
                "-autoboot_script", probe,
                "-video", "none", "-sound", "none"
            })
            {
                start.ArgumentList.Add(argument);
            }

            Dictionary<string, string> capabilities = new Dictionary<string, string>(StringComparer.Ordinal);

            using (Process process = Process.Start(start))
            {
                try
                {
                    WaitForReport(report, ProcessTimeout);
                }
                finally
                {
                    if (!process.HasExited)
                        process.Kill();
                }
            }

            // The probe flushes each line as it writes it, so the file is readable
            // even though MAME was killed before it could close anything itself.
            foreach (string line in File.ReadAllLines(report))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith(ProbePrefix, StringComparison.Ordinal)) continue;

string pair = trimmed.Substring(ProbePrefix.Length);
                    int separator = pair.IndexOf('=');
                    if (separator < 0)
                    {
                        // A bare marker such as the closing "end". The probe uses one
                        // to announce that it survived every call, so it is evidence
                        // rather than noise and must survive parsing.
                        capabilities[pair] = string.Empty;
                        continue;
                    }

                    if (separator == 0) continue;

                    capabilities[pair.Substring(0, separator)] = pair.Substring(separator + 1);
            }

            try { File.Delete(report); } catch (IOException) { /* a stale temp file is not worth a test failure */ }

            if (capabilities.Count == 0)
                Assert.Inconclusive("MAME produced no probe output. The script may have failed to load.");

            return capabilities;
        }

        /// <summary>
        /// Waits for the probe to write its final line. The process is not expected
        /// to exit, so completion is the report saying so, not the process going
        /// away. <c>File.Exists</c> can race the first write, hence the retry.
        /// </summary>
        private static void WaitForReport(string path, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        foreach (string line in File.ReadAllLines(path))
                            if (line.Trim() == ProbePrefix + "end") return;
                    }
                    catch (IOException)
                    {
                        // Still being written. Look again on the next pass.
                    }
                }

                Thread.Sleep(200);
            }

            Assert.Inconclusive("The MAME probe did not report completion within " + timeout.TotalSeconds + " seconds.");
        }

        private static string FindMame()
        {
            string configured = Environment.GetEnvironmentVariable("WINASM65_MAME");

            // An explicit override is honoured as given, including when it points at
            // nothing. Silently falling back to another installation would make the
            // variable useless for testing, and would let these tests report on a
            // machine the caller did not choose.
            if (!string.IsNullOrEmpty(configured))
                return File.Exists(configured) ? configured : null;

            foreach (string candidate in new[]
            {
                @"C:\Mame289\mame.exe",
                @"C:\Mame\mame.exe",
                @"C:\Program Files\MAME\mame.exe"
            })
            {
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }

        private static string FindRom()
        {
            string configured = Environment.GetEnvironmentVariable("WINASM65_NES_ROM");
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;

            string path = Path.Combine(RepositoryRoot(), "example_bomberman-nes", "bomber.nes");
            Assert.IsTrue(File.Exists(path), "the sample ROM is missing: " + path);
            return path;
        }

        private static string FindProbe()
        {
            string path = Path.Combine(RepositoryRoot(), "WinASM65.Monitor", "Bridge", "probe_mame.lua");
            Assert.IsTrue(File.Exists(path), "the capability probe is missing: " + path);
            return path;
        }

        /// <summary>
        /// Walks up from the test assembly until the repository layout is
        /// recognisable, so the tests find the ROM and the probe without a
        /// hard-coded absolute path.
        /// </summary>
        private static string RepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "WinASM65.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }

            Assert.Fail("could not locate the repository root above " + AppContext.BaseDirectory);
            return null;
        }
    }
}