using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Cpu;
using WinASM65.Monitor;
using WinASM65.Monitor.Protocol;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// M5 on the .NET side: breakpoint tracking. The Lua bridge is blocked on
    /// MesenCE itself, but everything the monitor does *around* breakpoints is
    /// testable against the fake backend.
    ///
    /// These tests guard two invariants, both questions of truth: what the monitor
    /// displays, is it really set on the machine?
    /// </summary>
    [TestClass]
    public class BreakpointSetTests
    {
        private static BreakpointSet Set(FakeMemoryBackend backend)
        {
            return new BreakpointSet(backend);
        }

        [TestMethod]
        public void PlacedBreakpointIsVisibleAndSetOnTheMachine()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                BreakpointSet set = Set(backend);

                Assert.IsTrue(set.Add(0xC000, BreakpointKind.Exec));
                Assert.AreEqual(1, set.Count);
                Assert.IsTrue(set.Contains(0xC000, BreakpointKind.Exec));

                // On the machine side: the backend received exactly that call.
                Assert.AreEqual(1, backend.Breakpoints.Count);
                Assert.AreEqual("C000:exec", backend.Breakpoints[0]);
            }
        }

        [TestMethod]
        public void DuplicateIsNotRecordedTwice()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                BreakpointSet set = Set(backend);

                Assert.IsTrue(set.Add(0xC000, BreakpointKind.Exec));
                Assert.IsFalse(set.Add(0xC000, BreakpointKind.Exec), "already set");
                Assert.IsFalse(set.Add(0xC000, BreakpointKind.Exec));

                // The critical point: exactly one call to the backend. Two callbacks
                // for the same address, the user sees one, and CLEAR is needed to free
                // the other.
                Assert.AreEqual(1, set.Count);
                Assert.AreEqual(1, backend.Breakpoints.Count);
            }
        }

        [TestMethod]
        public void SameAddressOtherKindIsADistinctBreakpoint()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                BreakpointSet set = Set(backend);

                set.Add(0xC000, BreakpointKind.Exec);
                set.Add(0xC000, BreakpointKind.Write);

                // These are not duplicates: reading and executing at the same address
                // are different conditions, and merging them would lose one.
                Assert.AreEqual(2, set.Count);
                Assert.AreEqual(2, backend.Breakpoints.Count);
            }
        }

        [TestMethod]
        public void BreakpointRefusedByTheEmulatorLeavesNoGhost()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                backend.RejectBreakpoints = true;
                BreakpointSet set = Set(backend);

                try
                {
                    set.Add(0xC000, BreakpointKind.Exec);
                    Assert.Fail("the emulator refusal must propagate.");
                }
                catch (MonitorException)
                {
                }

                // The case that matters: had the monitor recorded before calling the
                // backend, it would display a set breakpoint here. The user would
                // re-arm it believing it exists, and nothing would ever stop.
                Assert.AreEqual(0, set.Count);
                Assert.IsFalse(set.Contains(0xC000, BreakpointKind.Exec));
            }
        }

        [TestMethod]
        public void RemovingRestoresTheOthersAndLeavesTheMachineConsistent()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                BreakpointSet set = Set(backend);
                set.Add(0xC000, BreakpointKind.Exec);
                set.Add(0xC010, BreakpointKind.Exec);
                set.Add(0xC020, BreakpointKind.Write);

                Assert.IsTrue(set.Remove(0xC010, BreakpointKind.Exec));

                Assert.AreEqual(2, set.Count);
                Assert.IsFalse(set.Contains(0xC010, BreakpointKind.Exec));

                // The machine and the list must match exactly: the interface has no
                // single removal, so the rest are set again.
                Assert.AreEqual(2, backend.Breakpoints.Count);
                Assert.IsFalse(backend.Breakpoints.Contains("C010:exec"));
                Assert.IsTrue(backend.Breakpoints.Contains("C000:exec"));
                Assert.IsTrue(backend.Breakpoints.Contains("C020:write"));
            }
        }

        [TestMethod]
        public void RemovingAMissingBreakpointIsRefusedAndChangesNothing()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                BreakpointSet set = Set(backend);
                set.Add(0xC000, BreakpointKind.Exec);

                Assert.IsFalse(set.Remove(0x9999, BreakpointKind.Exec));
                Assert.AreEqual(1, set.Count);
                Assert.AreEqual(1, backend.Breakpoints.Count);
            }
        }

        [TestMethod]
        public void ClearEmptiesTheListAndTheMachine()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                BreakpointSet set = Set(backend);
                set.Add(0xC000, BreakpointKind.Exec);
                set.Add(0xC010, BreakpointKind.Write);

                set.Clear();

                Assert.AreEqual(0, set.Count);
                Assert.AreEqual(0, backend.Breakpoints.Count);
                Assert.AreEqual("0", set.Describe());
            }
        }

        [TestMethod]
        public void UnknownKindIsRefusedBeforeTheEmulator()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                BreakpointSet set = Set(backend);

                MonitorException ex = Assert.ThrowsException<MonitorException>(
                    () => set.Add(0xC000, "fetch")) as MonitorException;
                Assert.IsNotNull(ex);
                Assert.AreEqual(0, set.Count);
                Assert.IsNull(backend.Breakpoints);
            }
        }

        [TestMethod]
        public void DescriptionCanBeReplayedVerbatim()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                BreakpointSet set = Set(backend);
                set.Add(0xC000, BreakpointKind.Exec);
                set.Add(0xE000, BreakpointKind.Write);

                Assert.AreEqual("2 exec $C000 write $E000", set.Describe());
            }
        }

        [TestMethod]
        public void ReadingBackThroughTheProtocolReflectsWhatIsSet()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                ProtocolServer server = new ProtocolServer(backend, new Cpu6502());

                Assert.AreEqual("OK 0", server.Handle("BREAK LIST"));

                server.Handle("BREAK SET exec $C000");
                server.Handle("BREAK SET write $E000");

                Assert.AreEqual("OK 2 exec $C000 write $E000", server.Handle("BREAK LIST"));
                Assert.AreEqual(2, backend.Breakpoints.Count);

                // The duplicate is reported without being treated as an error: it is a
                // reached state, and an ERR would suggest a failure when the requested
                // breakpoint already exists.
                Assert.AreEqual("OK already set", server.Handle("BREAK SET exec $C000"));

                // The duplicate neither grew the list nor was replayed on the emulator.
                Assert.AreEqual("OK 2 exec $C000 write $E000", server.Handle("BREAK LIST"));
                Assert.AreEqual(2, backend.Breakpoints.Count);

                // Targeted removal: machine and list stay in agreement.
                Assert.AreEqual("OK", server.Handle("BREAK REMOVE exec $C000"));
                Assert.AreEqual("OK 1 write $E000", server.Handle("BREAK LIST"));
                Assert.AreEqual(1, backend.Breakpoints.Count);

                // A removal matching nothing is reported, never silent.
                Assert.IsTrue(server.Handle("BREAK REMOVE exec $C000").StartsWith("ERR"));
                Assert.AreEqual("OK", server.Handle("BREAK CLEAR"));
                Assert.AreEqual("OK 0", server.Handle("BREAK LIST"));
                Assert.AreEqual(0, backend.Breakpoints.Count);
            }
        }

        [TestMethod]
        public void TheKindIsCheckedAfterTheAddress()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                ProtocolServer server = new ProtocolServer(backend, new Cpu6502());

                // A user who gets the kind wrong must learn about the kind, not be told
                // they supplied a strange address.
                Assert.IsTrue(server.Handle("BREAK SET foob $C000").StartsWith("ERR unknown kind"));

                // Arity is checked first though: without an address there is nothing to
                // say about the address.
                Assert.IsTrue(server.Handle("BREAK SET foob").StartsWith("ERR BREAK SET expects"));

                Assert.IsTrue(server.Handle("BREAK").StartsWith("ERR"));
                Assert.IsTrue(server.Handle("BREAK LIST $C000").StartsWith("ERR"));
                Assert.IsTrue(server.Handle("BREAK FOO $C000 exec").StartsWith("ERR"));
            }
        }

        [TestMethod]
        public void Only16BitAddressesAreAccepted()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                BreakpointSet set = Set(backend);
                set.Add(0xFFFF, BreakpointKind.Exec);
                Assert.AreEqual(1, set.Count);

                Assert.ThrowsException<AddressRangeException>(
                    () => set.Add(0x10000, BreakpointKind.Exec));
                Assert.AreEqual(1, set.Count);
            }
        }
    }
}