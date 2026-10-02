using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Cpu;
using WinASM65.Monitor.Shell;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// The status line's wording, and what happens when the backend cannot answer.
    ///
    /// A backend that cannot report its CPU is the ordinary case on MesenCE, and a
    /// status line that said nothing there would leave the user reading a blank pane
    /// and concluding the bridge was dead. So the refusal is asserted as content,
    /// not as an absence.
    /// </summary>
    [TestClass]
    public class ShellStatusTests
    {
        private static CpuSnapshot Running()
        {
            return new CpuSnapshot(0xCBFD, 0x01, 0x00, 0x5E, 0xFD, 0x05, 43358022);
        }

        [TestMethod]
        public void TheLineNamesTheHostAndWhetherTheMachineIsRunning()
        {
            IReadOnlyList<StatusSegment> segments = ShellStatus.Compose(
                "mesen2", "2.1.1", true, Running(), false, 0, null);

            string line = ShellStatus.Fit(segments, 200);

            StringAssert.Contains(line, "mesen2 2.1.1");
            StringAssert.Contains(line, "STOPPED");
            StringAssert.Contains(line, "$CBFD");
            StringAssert.Contains(line, "BP=0");
        }

        [TestMethod]
        public void ARunningMachineSaysSo()
        {
            IReadOnlyList<StatusSegment> segments = ShellStatus.Compose(
                "mesen2", "2.1.1", true, Running(), true, 0, null);

            StringAssert.Contains(ShellStatus.Fit(segments, 200), "RUNNING");
        }

        [TestMethod]
        public void TheBreakpointCountIsAlwaysPresentEvenAtZero()
        {
            // A count the user cannot see is a count they cannot trust; "BP=0" and
            // no BP at all are different claims.
            IReadOnlyList<StatusSegment> segments = ShellStatus.Compose(
                "mesen2", "2.1.1", true, Running(), false, 2, null);

            StringAssert.Contains(ShellStatus.Fit(segments, 200), "BP=2");
        }

        [TestMethod]
        public void TheCycleCountIsShownInDecimalBecauseThatIsWhatItIs()
        {
            IReadOnlyList<StatusSegment> segments = ShellStatus.Compose(
                "mesen2", "2.1.1", true, Running(), false, 0, null);

            // 43358022 read as hex would be a plausible-looking wrong number.
            StringAssert.Contains(ShellStatus.Fit(segments, 200), "cyc=43358022");
        }

        [TestMethod]
        public void AMachineThatHasNotRunIsCalledOut()
        {
            IReadOnlyList<StatusSegment> segments = ShellStatus.Compose(
                "mesence", "2.2.1", true, new CpuSnapshot(0x8000, 0, 0, 0, 0, 0, 7), false, 0, null);

            StringAssert.Contains(ShellStatus.Fit(segments, 200), "reset vector");
        }

        [TestMethod]
        public void ADeliberatelyPausedMachineIsNotCalledFrozen()
        {
            // Mesen2 is paused most of the time on purpose. Condemning that would
            // condemn the only backend that can be driven.
            IReadOnlyList<StatusSegment> segments = ShellStatus.Compose(
                "mesen2", "2.1.1", true, new CpuSnapshot(0xCBFD, 1, 0, 0x5E, 0xFD, 5, 43358022), false, 0, null);

            Assert.IsFalse(ShellStatus.Fit(segments, 200).Contains("reset vector"));
        }

        [TestMethod]
        public void ABackendWithNoCpuViewSaysSoInsteadOfGoingBlank()
        {
            IReadOnlyList<StatusSegment> segments = ShellStatus.Compose(
                "mesence", "2.2.1", false, null, true, 0, null);

            StringAssert.Contains(ShellStatus.Fit(segments, 200), "cannot report registers");
        }

        [TestMethod]
        public void ANullBackendStillProducesAReadableLine()
        {
            string line = ShellStatus.Fit(ShellStatus.Compose(null, null, false, null, false, 0, null), 200);

            StringAssert.Contains(line, "WinASM65 monitor");
            StringAssert.Contains(line, "?");
        }

        [TestMethod]
        public void ANarrowLineDropsDetailButNeverTheHostOrTheRunState()
        {
            IReadOnlyList<StatusSegment> segments = ShellStatus.Compose(
                "mesen2", "2.1.1", true, Running(), false, 3, "mapper 0, PRG 16K, CHR 8K");

            string narrow = ShellStatus.Fit(segments, 40);

            Assert.IsTrue(narrow.Length <= 40, "the line must fit: '" + narrow + "'");
            StringAssert.Contains(narrow, "mesen2");
            StringAssert.Contains(narrow, "STOPPED");
        }

        [TestMethod]
        public void ATruncatedLineNeverShowsAHalfWrittenNumber()
        {
            IReadOnlyList<StatusSegment> segments = ShellStatus.Compose(
                "mesen2", "2.1.1", true, Running(), false, 3, "mapper 0, PRG 16K, CHR 8K");

            foreach (int width in new[] { 20, 30, 45, 60, 90, 200 })
            {
                string fitted = ShellStatus.Fit(segments, width);

                foreach (string segment in new[] { "mesen2 2.1.1", "STOPPED", "$CBFD" })
                {
                    int index = fitted.IndexOf(segment, StringComparison.Ordinal);
                    if (index < 0)
                        continue;

                    // Whatever follows a kept segment must begin after it, not inside it.
                    int end = index + segment.Length;
                    if (end < fitted.Length)
                        Assert.IsTrue(fitted[end] == ' ',
                            "a segment was cut in half at width " + width + ": '" + fitted + "'");
                }
            }
        }

        [TestMethod]
        public void ZeroWidthProducesNothingRatherThanThrowing()
        {
            IReadOnlyList<StatusSegment> segments = ShellStatus.Compose(
                "mesen2", "2.1.1", true, Running(), false, 0, null);

            Assert.AreEqual(string.Empty, ShellStatus.Fit(segments, 0));
            Assert.AreEqual(string.Empty, ShellStatus.Fit(null, 80));
        }

        [TestMethod]
        public void ReadingAFakeBackendProducesTheSameLineAsComposingIt()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                IReadOnlyList<StatusSegment> read = ShellStatus.Read(machine, 0);

                Assert.IsFalse(string.IsNullOrEmpty(ShellStatus.Fit(read, 200)));
                StringAssert.Contains(ShellStatus.Fit(read, 200), "FakeEmu");
            }
        }

        [TestMethod]
        public void ABackendThatRefusesToReportItsCpuStillLeavesAReadableLine()
        {
            using (RefusingCpuBackend machine = new RefusingCpuBackend())
            {
                machine.Pause();

                string line = ShellStatus.Fit(ShellStatus.Read(machine, 0), 200);

                StringAssert.Contains(line, "FakeEmu");
                StringAssert.Contains(line, "STOPPED");
                StringAssert.Contains(line, "cannot report registers");
            }
        }

        [TestMethod]
        public void ABackendThatThrowsWhenAskedForRegistersDoesNotStopTheLine()
        {
            using (ThrowingCpuBackend machine = new ThrowingCpuBackend())
            {
                string line = ShellStatus.Fit(ShellStatus.Read(machine, 0), 200);

                StringAssert.Contains(line, "cannot report registers");
            }
        }

        /// <summary>
        /// A backend that can see memory but not the processor — the MesenCE shape,
        /// and the reason <c>ICpuStateSource</c> is a separate interface.
        /// </summary>
        private sealed class RefusingCpuBackend : FakeMemoryBackend, ICpuStateSource
        {
            public RefusingCpuBackend() : base(2048) { }

            public CpuSnapshot ReadCpuState()
            {
                throw new MonitorException("this host cannot observe the CPU");
            }
        }

        private sealed class ThrowingCpuBackend : FakeMemoryBackend, ICpuStateSource
        {
            public ThrowingCpuBackend() : base(2048) { }

            public CpuSnapshot ReadCpuState()
            {
                throw new IOException("the socket went away mid-read");
            }
        }
    }
}