using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Monitor.Shell;
using WinASM65.Monitor.Protocol;
using WinASM65.Monitor.Tests;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// Tests for in-place RAM editing through the memory backend.
    /// </summary>
    [TestClass]
    public class RamEditingTests
    {
        [TestMethod]
        public void WriteByteAtZeroPageSucceedsAndVerifies()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                string result = RamEditing.WriteByte(backend, 0x0042, 0x5A);

                Assert.AreEqual("OK $0042", result);
                Assert.AreEqual(0x5A, backend.Read(0x0042, 1)[0]);
            }
        }

        [TestMethod]
        public void WriteByteAtStackSucceedsAndVerifies()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                string result = RamEditing.WriteByte(backend, 0x01FF, 0xAA);

                Assert.AreEqual("OK $01FF", result);
                Assert.AreEqual(0xAA, backend.Read(0x01FF, 1)[0]);
            }
        }

        [TestMethod]
        public void WriteByteAtFreeRamSucceedsAndVerifies()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                string result = RamEditing.WriteByte(backend, 0x0500, 0x33);

                Assert.AreEqual("OK $0500", result);
                Assert.AreEqual(0x33, backend.Read(0x0500, 1)[0]);
            }
        }

        [TestMethod]
        public void WriteByteAtWindowStartSucceeds()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                string result = RamEditing.WriteByte(backend, RamEditing.EditWindowStart, 0x11);

                Assert.AreEqual("OK $0000", result);
            }
        }

        [TestMethod]
        public void WriteByteAtWindowEndSucceeds()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                string result = RamEditing.WriteByte(backend, RamEditing.EditWindowEnd, 0x22);

                Assert.AreEqual("OK $07FF", result);
            }
        }

        [TestMethod]
        public void WriteByteBelowWindowReturnsError()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                string result = RamEditing.WriteByte(backend, -1, 0x00);

                StringAssert.StartsWith(result, MonitorProtocol.ErrPrefix);
                StringAssert.Contains(result, "outside the editable window");
            }
        }

        [TestMethod]
        public void WriteByteAboveWindowReturnsError()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                string result = RamEditing.WriteByte(backend, 0x0800, 0x00);

                StringAssert.StartsWith(result, MonitorProtocol.ErrPrefix);
                StringAssert.Contains(result, "outside the editable window");
                StringAssert.Contains(result, "$07FF");
            }
        }

        [TestMethod]
        public void WriteByteWithNullBackendReturnsError()
        {
            string result = RamEditing.WriteByte(null, 0x0042, 0x5A);

            StringAssert.StartsWith(result, MonitorProtocol.ErrPrefix);
            StringAssert.Contains(result, "backend is null");
        }

        [TestMethod]
        public void WriteByteBackendRefusalIsSurfacedAsError()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend(256))
            {
                // 256-byte backend only has $0000-$00FF, so $0200 is out of range for it
                string result = RamEditing.WriteByte(backend, 0x0200, 0x5A);

                StringAssert.StartsWith(result, MonitorProtocol.ErrPrefix);
            }
        }

        [TestMethod]
        public void WriteByteBackendAcceptsButValueNotObservedReturnsError()
        {
            using (StuckBackend backend = new StuckBackend())
            {
                // StuckBackend.Write succeeds but Read returns 0
                string result = RamEditing.WriteByte(backend, 0x0042, 0x5A);

                StringAssert.StartsWith(result, MonitorProtocol.ErrPrefix);
                StringAssert.Contains(result, "value not observed");
                StringAssert.Contains(result, "5A");
                StringAssert.Contains(result, "00");
            }
        }

        [TestMethod]
        public void WriteBytesValidRangeSucceedsAndVerifies()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                byte[] data = new byte[] { 0x11, 0x22, 0x33, 0x44 };
                string result = RamEditing.WriteBytes(backend, 0x0300, data);

                Assert.AreEqual("OK $0300 4 byte(s)", result);
                CollectionAssert.AreEqual(data, backend.Read(0x0300, 4));
            }
        }

        [TestMethod]
        public void WriteBytesAtWindowBoundarySucceeds()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                byte[] data = new byte[0x800]; // Full 2 KiB window
                string result = RamEditing.WriteBytes(backend, RamEditing.EditWindowStart, data);

                Assert.AreEqual("OK $0000 2048 byte(s)", result);
            }
        }

        [TestMethod]
        public void WriteBytesEmptyArrayReturnsError()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                string result = RamEditing.WriteBytes(backend, 0x0042, new byte[0]);

                StringAssert.StartsWith(result, MonitorProtocol.ErrPrefix);
                StringAssert.Contains(result, "no bytes to write");
            }
        }

        [TestMethod]
        public void WriteBytesNullArrayReturnsError()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                string result = RamEditing.WriteBytes(backend, 0x0042, null);

                StringAssert.StartsWith(result, MonitorProtocol.ErrPrefix);
                StringAssert.Contains(result, "no bytes to write");
            }
        }

        [TestMethod]
        public void WriteBytesExceedsWindowStartReturnsError()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                byte[] data = new byte[] { 0x11, 0x22 };
                string result = RamEditing.WriteBytes(backend, -1, data);

                StringAssert.StartsWith(result, MonitorProtocol.ErrPrefix);
                StringAssert.Contains(result, "exceeds the editable window");
            }
        }

        [TestMethod]
        public void WriteBytesExceedsWindowEndReturnsError()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                byte[] data = new byte[] { 0x11, 0x22 };
                string result = RamEditing.WriteBytes(backend, 0x07FF, data); // 0x07FF + 1 = 0x0800, exceeds

                StringAssert.StartsWith(result, MonitorProtocol.ErrPrefix);
                StringAssert.Contains(result, "exceeds the editable window");
            }
        }

        [TestMethod]
        public void WriteBytesBackendRefusalIsSurfacedAsError()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend(256))
            {
                byte[] data = new byte[] { 0x11, 0x22 };
                string result = RamEditing.WriteBytes(backend, 0x0100, data); // 0x0100 + 1 = 0x0101, exceeds 256-byte backend

                StringAssert.StartsWith(result, MonitorProtocol.ErrPrefix);
            }
        }

        [TestMethod]
        public void WriteBytesBackendAcceptsButValueNotObservedReturnsError()
        {
            using (StuckBackend backend = new StuckBackend())
            {
                byte[] data = new byte[] { 0xAA, 0xBB };
                string result = RamEditing.WriteBytes(backend, 0x0042, data);

                StringAssert.StartsWith(result, MonitorProtocol.ErrPrefix);
                StringAssert.Contains(result, "value not observed");
            }
        }

        [TestMethod]
        public void WriteBytesReadBackLengthMismatchReturnsError()
        {
            using (ShortReadBackend backend = new ShortReadBackend())
            {
                byte[] data = new byte[] { 0x11, 0x22, 0x33 };
                string result = RamEditing.WriteBytes(backend, 0x0042, data);

                StringAssert.StartsWith(result, MonitorProtocol.ErrPrefix);
                StringAssert.Contains(result, "read-back length mismatch");
            }
        }

        /// <summary>
        /// A backend that accepts writes but always reads back zero.
        /// </summary>
        private sealed class StuckBackend : FakeMemoryBackend
        {
            public override void Write(int address, byte[] bytes)
            {
                // Accept the write but don't store it
            }

            public override byte[] Read(int address, int length)
            {
                // Always return zeros
                return new byte[length];
            }
        }

        /// <summary>
        /// A backend that returns fewer bytes than requested on read.
        /// </summary>
        private sealed class ShortReadBackend : FakeMemoryBackend
        {
            public override byte[] Read(int address, int length)
            {
                // Return one byte less than requested
                byte[] full = base.Read(address, length);
                byte[] shortRead = new byte[Math.Max(0, length - 1)];
                Array.Copy(full, shortRead, shortRead.Length);
                return shortRead;
            }
        }
    }
}