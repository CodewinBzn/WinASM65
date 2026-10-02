using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// Test backend: the same interface as the MesenCE Lua bridge, without an
    /// emulator. All monitor logic must be testable against this fake.
    /// </summary>
    public class FakeMemoryBackend : IMemoryBackend
    {
        private readonly byte[] _memory;

        public FakeMemoryBackend(int size = 65536)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException("size");
            _memory = new byte[size];
            EmulatorName = "FakeEmu";
            EmulatorVersion = "0.0.0";
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
    }

    [TestClass]
    public class MemoryBackendTests
    {
        [TestMethod]
        public void LectureEcritureAllerRetour()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                backend.Write(0x8000, new byte[] { 0xA9, 0x01, 0x60 });
                CollectionAssert.AreEqual(
                    new byte[] { 0xA9, 0x01, 0x60 }, backend.Read(0x8000, 3));
            }
        }

        [TestMethod]
        public void LectureHorsPlageEstRefuseeEtNoyeeDansDesZeros()
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
        public void EcriturePartiellementHorsPlageEstRefuseeEntierement()
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
        public void AdresseNegativeeEstRefusee()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                Assert.ThrowsException<AddressRangeException>(() => backend.Read(-1, 1));
            }
        }

        [TestMethod]
        public void ControleDExecutionEstCompte()
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
        public void ResetVideLaMemoire()
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
        public void PointsDArretSontTriesEtEffacables()
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
        public void InstantaneRestaureLaMemoire()
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
        public void InstantaneDeMauvaiseTailleEstRefuse()
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
        public void DisposeMarqueLeBackend()
        {
            FakeMemoryBackend backend = new FakeMemoryBackend();
            backend.Dispose();
            Assert.IsTrue(backend.Disposed);
            Assert.IsFalse(backend.IsRunning);
        }

        [TestMethod]
        public void UnBackendDoitEtreLiberableEtTailleDeMemoireEstParametrable()
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