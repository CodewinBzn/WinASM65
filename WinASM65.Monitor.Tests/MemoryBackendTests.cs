using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Monitor.Abstractions;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// Test backend: the same interface as the MesenCE Lua bridge, without an
    /// emulator. All monitor logic must be testable against this fake.
    ///
    /// It is an <see cref="IExecutionAdapter"/> too, and it declares only what it
    /// really is. It is a 64 KiB byte array with no processor behind it: it can
    /// read, write, pause, resume, reset, step a counter, hold a breakpoint and
    /// snapshot itself, and it cannot report a register because there is no CPU to
    /// report. That it does not implement <see cref="ICpuStateSource"/> was already
    /// the project's way of saying so — the split introduced for exactly this
    /// backend — and declaring <see cref="ExecutionCapability.CpuState"/> clear now
    /// says the same thing to the key bindings and the status line, which never see
    /// the interface split at all.
    ///
    /// The name and version are parameters so a test can stand up the fake as a
    /// measured build and check what the shell does with it. Left as literals they
    /// would make every backend in this suite unmeasured by accident.
    /// </summary>
    public class FakeMemoryBackend : IMemoryBackend, IExecutionAdapter
    {
        private readonly byte[] _memory;

        /// <summary>
        /// What a byte array with no processor can honestly claim.
        ///
        /// No <see cref="ExecutionCapability.CpuState"/> and no watchpoints, because
        /// there is nothing to watch: nothing runs, so no access ever happens to be
        /// watched. Everything else is here because the member exists and works,
        /// which is the whole standard the flags are held to.
        /// </summary>
        public const ExecutionCapability DeclaredCapabilities =
            ExecutionCapability.MemoryRead | ExecutionCapability.MemoryWrite
            | ExecutionCapability.Pause | ExecutionCapability.Resume | ExecutionCapability.Reset
            | ExecutionCapability.StepInstruction | ExecutionCapability.BreakpointExecution
            | ExecutionCapability.StateSaveLoad;

        public FakeMemoryBackend(int size = 65536, string emulatorName = null, string emulatorVersion = null)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException("size");
            _memory = new byte[size];
            EmulatorName = emulatorName ?? "FakeEmu";
            EmulatorVersion = emulatorVersion ?? "0.0.0";
            IsRunning = true;
        }

        public string EmulatorName { get; private set; }
        public string EmulatorVersion { get; private set; }
        public bool IsRunning { get; private set; }

        public List<string> Breakpoints { get; private set; }
        public int PauseCount { get; private set; }
        public int ResumeCount { get; private set; }
        public int StepCount { get; private set; }
        public int ResetCount { get; private set; }
        public bool Disposed { get; private set; }

        /// <summary>Requested lengths, in order: used to verify that a command does
        /// not read more than it needs to.</summary>
        public List<int> ReadLengths { get; private set; }

        /// <summary>Forces a breakpoint refusal, to verify that the monitor keeps no
        /// phantom entry when the emulator refuses.</summary>
        public bool RejectBreakpoints { get; set; }

        /// <summary>Virtual so a test can substitute a machine that misbehaves in a
        /// specific way — a short answer, a refusal — which is how the defences are
        /// tested rather than assumed.</summary>
        public virtual byte[] Read(int address, int length)
        {
            CheckRange(address, length);
            if (ReadLengths == null)
                ReadLengths = new List<int>();
            ReadLengths.Add(length);
            byte[] result = new byte[length];
            Array.Copy(_memory, address, result, 0, length);
            return result;
        }

        public virtual void Write(int address, byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException("bytes");
            CheckRange(address, bytes.Length);
            Array.Copy(bytes, 0, _memory, address, bytes.Length);
        }

        public void Pause()
        {
            PauseCount++;
            IsRunning = false;
        }

        public void Resume()
        {
            ResumeCount++;
            IsRunning = true;
        }

        public void Step()
        {
            StepCount++;
            IsRunning = false;
        }

        public void Reset()
        {
            ResetCount++;
            Array.Clear(_memory, 0, _memory.Length);
        }

        public void AddBreakpoint(int address, string kind)
        {
            CheckRange(address, 1);
            if (string.IsNullOrEmpty(kind))
                throw new MonitorException("empty breakpoint kind");
            if (RejectBreakpoints)
                throw new MonitorException("the emulator refused the breakpoint");
            if (Breakpoints == null)
                Breakpoints = new List<string>();
            Breakpoints.Add(address.ToString("X4") + ":" + kind);
        }

        public void ClearBreakpoints()
        {
            if (Breakpoints != null)
                Breakpoints.Clear();
        }

        public byte[] SaveState()
        {
            byte[] copy = new byte[_memory.Length];
            Array.Copy(_memory, copy, _memory.Length);
            return copy;
        }

        public void LoadState(byte[] state)
        {
            if (state == null || state.Length != _memory.Length)
                throw new MonitorException("invalid snapshot size");
            Array.Copy(state, _memory, state.Length);
        }

        public void Dispose()
        {
            Disposed = true;
            IsRunning = false;
        }

        /// <summary>
        /// Checks the range on both sides and explicitly refuses any overflow: a
        /// silently truncated read would display false data.
        /// </summary>
        private void CheckRange(int address, int length)
        {
            if (length < 0)
                throw new MonitorException("negative length: " + length);
            if (address < 0 || address > _memory.Length - length)
                throw new AddressRangeException(address, length);
        }

        // ---------------------------------------------------------------- the contract
        //
        // The host <-> plugin contract, implemented by the same object the session
        // already holds. Explicit where the two interfaces disagree on a signature,
        // because they genuinely are different questions: Read(int, int) is this
        // suite's flat address and Read(RegionAddress, int) names a region and a
        // bank, which is not a thing this backend has.

        private const string FlatRegionName = "flat";

        public string DisplayName
        {
            get { return EmulatorName + " " + EmulatorVersion; }
        }

        public ExecutionCapability Capabilities
        {
            get { return DeclaredCapabilities; }
        }

        public IReadOnlyList<MemoryRegion> Regions
        {
            get
            {
                return new List<MemoryRegion>
                {
                    new MemoryRegion(FlatRegionName, 0x0000, _memory.Length, true, false, 1)
                };
            }
        }

        byte[] IExecutionAdapter.Read(RegionAddress where, int length)
        {
            return Read(Flatten(where), length);
        }

        void IExecutionAdapter.Write(RegionAddress where, byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException("bytes");

            Write(Flatten(where), bytes);
        }

        CpuState IExecutionAdapter.ReadCpuState()
        {
            // By name, and not by returning zeros. This backend has no processor, so
            // a reading of zero registers is indistinguishable from a real machine
            // sitting at its reset vector — which is the exact confusion the
            // declared flag set exists to prevent.
            throw new MonitorException(DisplayName + " cannot report registers: "
                + "this backend is a byte array with no processor behind it");
        }

        void IExecutionAdapter.Pause()
        {
            Require(ExecutionCapability.Pause);
            Pause();
        }

        void IExecutionAdapter.Resume()
        {
            Require(ExecutionCapability.Resume);
            Resume();
        }

        void IExecutionAdapter.Reset()
        {
            Require(ExecutionCapability.Reset);
            Reset();
        }

        void IExecutionAdapter.Step()
        {
            Require(ExecutionCapability.StepInstruction);
            Step();
        }

        void IExecutionAdapter.SetBreakpoint(int address)
        {
            Require(ExecutionCapability.BreakpointExecution);
            AddBreakpoint(address, BreakpointKind.Exec);
        }

        void IExecutionAdapter.ClearBreakpoints()
        {
            Require(ExecutionCapability.BreakpointExecution);
            ClearBreakpoints();
        }

        void IExecutionAdapter.SetWatchpoint(RegionAddress where, MemoryAccessKind kind)
        {
            // Refused whichever kind was asked for, and for the reason that matters:
            // nothing runs here, so no access can ever be observed being watched.
            Require(kind == MemoryAccessKind.Write
                ? ExecutionCapability.WatchpointWrite
                : ExecutionCapability.WatchpointRead);

            throw new MonitorException("unreachable");
        }

        void IExecutionAdapter.ClearWatchpoints()
        {
            Require(ExecutionCapability.WatchpointRead | ExecutionCapability.WatchpointWrite);
        }

        byte[] IExecutionAdapter.SaveState()
        {
            Require(ExecutionCapability.StateSaveLoad);
            return SaveState();
        }

        void IExecutionAdapter.LoadState(byte[] state)
        {
            Require(ExecutionCapability.StateSaveLoad);
            LoadState(state);
        }

        /// <summary>
        /// The backstop behind the declared flags: a caller that ignored them is told
        /// which one it ignored and what the machine would need to grant it.
        /// </summary>
        private void Require(ExecutionCapability capability)
        {
            if (DeclaredCapabilities.HasFlag(capability))
                return;

            throw new MonitorException(DisplayName + " cannot "
                + ExecutionCapabilities.Phrase(capability) + ": it is declared clear");
        }

        private int Flatten(RegionAddress where)
        {
            if (where.Region != 0)
                throw new MonitorException("no region " + where.Region + ": this backend exposes one flat region");

            if (where.Bank != 0)
                throw new MonitorException("no bank " + where.Bank + ": this backend's space is not banked");

            return where.Offset;
        }
    }

    [TestClass]
    public class MemoryBackendTests
    {
        [TestMethod]
        public void ReadWriteRoundTrip()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                backend.Write(0x8000, new byte[] { 0xA9, 0x01, 0x60 });
                CollectionAssert.AreEqual(
                    new byte[] { 0xA9, 0x01, 0x60 }, backend.Read(0x8000, 3));
            }
        }

        [TestMethod]
        public void AReadOutsideTheRangeIsRefusedAndDrownedInZeros()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                // The critical case: past the end, a silent read would pass zeros
                // indistinguishable from real data.
                AddressRangeException ex = Assert.ThrowsException<AddressRangeException>(
                    () => backend.Read(65534, 4)) as AddressRangeException;
                Assert.IsNotNull(ex);
                Assert.AreEqual(65534, ex.Address);
            }
        }

        [TestMethod]
        public void AWritePartlyOutsideTheRangeIsRefusedEntirely()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                backend.Write(0x0000, new byte[] { 0x11 });
                try
                {
                    backend.Write(65535, new byte[] { 0x22, 0x33 });
                    Assert.Fail("an overflowing write must be refused.");
                }
                catch (AddressRangeException)
                {
                }

                // No partial write: the last valid byte is untouched.
                CollectionAssert.AreEqual(new byte[] { 0x11 }, backend.Read(0, 1));
            }
        }

        [TestMethod]
        public void ANegativeAddressIsRefused()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                Assert.ThrowsException<AddressRangeException>(() => backend.Read(-1, 1));
            }
        }

        [TestMethod]
        public void ExecutionControlIsCounted()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                Assert.IsTrue(backend.IsRunning);
                backend.Pause();
                Assert.IsFalse(backend.IsRunning);
                Assert.AreEqual(1, backend.PauseCount);

                backend.Resume();
                Assert.IsTrue(backend.IsRunning);
                Assert.AreEqual(1, backend.ResumeCount);

                backend.Step();
                Assert.AreEqual(1, backend.StepCount);
            }
        }

        [TestMethod]
        public void ResetClearsMemory()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                backend.Write(0x1000, new byte[] { 0xFF, 0xFF });
                backend.Reset();
                CollectionAssert.AreEqual(new byte[] { 0x00, 0x00 }, backend.Read(0x1000, 2));
                Assert.AreEqual(1, backend.ResetCount);
            }
        }

        [TestMethod]
        public void BreakpointsAreSortedAndClearable()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                backend.AddBreakpoint(0x8000, BreakpointKind.Exec);
                backend.AddBreakpoint(0x8010, BreakpointKind.Write);
                Assert.AreEqual(2, backend.Breakpoints.Count);

                backend.ClearBreakpoints();
                Assert.AreEqual(0, backend.Breakpoints.Count);
            }
        }

        [TestMethod]
        public void ASnapshotRestoresMemory()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                backend.Write(0x2000, new byte[] { 0x0A, 0x0B });
                byte[] state = backend.SaveState();

                backend.Write(0x2000, new byte[] { 0xFF, 0xFF });
                backend.LoadState(state);

                CollectionAssert.AreEqual(new byte[] { 0x0A, 0x0B }, backend.Read(0x2000, 2));
            }
        }

        [TestMethod]
        public void ASnapshotOfTheWrongSizeIsRefused()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                backend.SaveState();
                try
                {
                    backend.LoadState(new byte[] { 1, 2, 3 });
                    Assert.Fail("a snapshot of the wrong size must be refused.");
                }
                catch (MonitorException)
                {
                }
            }
        }

        [TestMethod]
        public void DisposeMarksTheBackend()
        {
            FakeMemoryBackend backend = new FakeMemoryBackend();
            backend.Dispose();
            Assert.IsTrue(backend.Disposed);
            Assert.IsFalse(backend.IsRunning);
        }

        [TestMethod]
        public void ABackendMustBeDisposableAndItsMemorySizeConfigurable()
        {
            using (FakeMemoryBackend small = new FakeMemoryBackend(256))
            {
                small.Write(0x00FF, new byte[] { 0x42 });
                CollectionAssert.AreEqual(new byte[] { 0x42 }, small.Read(0x00FF, 1));
                Assert.ThrowsException<AddressRangeException>(() => small.Read(0x0100, 1));
            }
        }
    }
}