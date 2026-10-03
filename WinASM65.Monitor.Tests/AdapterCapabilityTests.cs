using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Cpu;
using WinASM65.Monitor.Abstractions;
using WinASM65.Monitor.Protocol;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// What each backend is allowed to claim, one test per backend.
    ///
    /// The flags exist so the host greys out what the attached machine cannot do,
    /// which makes every bit a promise to the user. A promise that is wrong is worse
    /// than no promise: it greys out a working key, or leaves a dead one looking
    /// alive. So these tests are one per measured backend and they are written to
    /// fail loudly — an <c>AreEqual</c> on the whole flag set, not a check that some
    /// bit is set.
    ///
    /// Mesen2 and MesenCE are asserted through the real adapter over a real socket,
    /// because their answer comes from the handshake and the handshake is the only
    /// thing the host has to go on. MAME has no adapter yet — A6 is deferred — so
    /// its row is asserted where it lives, in the table. That asymmetry is
    /// deliberate and temporary: when the MAME adapter lands it reads this same row,
    /// and the test that would then be wrong is the one to move.
    ///
    /// The measurements themselves are not re-derived here. Mesen2's are in
    /// <c>Bridge/bridge_mesen2.lua</c>, MesenCE's in <c>Bridge/bridge.lua</c>, and
    /// MAME's in <c>MameCapabilityTests</c> against a real emulator. This class
    /// checks that the host's answers still match them.
    /// </summary>
    [TestClass]
    public class AdapterCapabilityTests
    {
        // ---------------------------------------------------------------- Mesen2 2.1.1

        [TestMethod]
        public void MesenTwoAnswersEverythingAndIsTheOnlyOneThatDoes()
        {
            using (Attached attached = Attach("Mesen2", "2.1.1"))
            {
                Assert.AreEqual(ExecutionCapability.FullControl, attached.Adapter.Capabilities,
                    "Mesen2 2.1.1 pauses, resumes, resets, steps, breaks on both"
                    + " memory callback kinds, snapshots and reports registers. All eleven"
                    + " bits, and no other measured backend claims all eleven.");

                // Spelled out bit by bit as well, because FullControl is a composite and
                // a composite can be quietly narrowed in the enum without the tests
                // noticing.
                AssertHasEveryBit(attached.Adapter.Capabilities,
                    ExecutionCapability.FullControl, "Mesen2 2.1.1");
            }
        }

        [TestMethod]
        public void MesenTwoReportsItsOwnNameAndVersionRatherThanBeingAssumed()
        {
            using (Attached attached = Attach("Mesen2", "2.1.1"))
            {
                StringAssert.Contains(attached.Adapter.DisplayName, "Mesen2");
                StringAssert.Contains(attached.Adapter.DisplayName, "2.1.1");
            }
        }

        // ---------------------------------------------------------------- MesenCE 2.2.1

        [TestMethod]
        public void MesenCeIsMemoryAndSnapshotsAndNothingElse()
        {
            using (Attached attached = Attach("MesenCE", "2.2.1"))
            {
                Assert.AreEqual(
                    ExecutionCapability.Memory | ExecutionCapability.StateSaveLoad,
                    attached.Adapter.Capabilities,
                    "MesenCE 2.2.1 serves memory and snapshots. emu.pause does not exist;"
                    + " emu.step, emu.resume and emu.breakExecution refuse calls made"
                    + " outside a callback; emu.addMemoryCallback refuses every function,"
                    + " named ones included; emu.getCpuState does not exist; emu.reset"
                    + " changes global state with no callback context.");

                // Named individually, because each of these five is a bit somebody
                // would like to switch on after reading a MesenCE API page.
                AssertIsClear(attached.Adapter.Capabilities, ExecutionCapability.CpuState, "MesenCE 2.2.1");
                AssertIsClear(attached.Adapter.Capabilities, ExecutionCapability.Pause, "MesenCE 2.2.1");
                AssertIsClear(attached.Adapter.Capabilities, ExecutionCapability.Resume, "MesenCE 2.2.1");
                AssertIsClear(attached.Adapter.Capabilities, ExecutionCapability.Reset, "MesenCE 2.2.1");
                AssertIsClear(attached.Adapter.Capabilities, ExecutionCapability.StepInstruction, "MesenCE 2.2.1");
                AssertIsClear(attached.Adapter.Capabilities, ExecutionCapability.BreakpointExecution, "MesenCE 2.2.1");
                AssertIsClear(attached.Adapter.Capabilities, ExecutionCapability.WatchpointRead, "MesenCE 2.2.1");
                AssertIsClear(attached.Adapter.Capabilities, ExecutionCapability.WatchpointWrite, "MesenCE 2.2.1");
            }
        }

        [TestMethod]
        public void MesenCeIsStillSnapshottableWhichIsWhyStateSaveLoadIsSet()
        {
            // The asymmetry that makes the flag set worth having: MesenCE answers no
            // execution control at all and still captures and restores the machine
            // through emu.getState and emu.setState. Reading it as "memory only" would
            // grey out a working key.
            using (Attached attached = Attach("MesenCE", "2.2.1"))
            {
                Assert.IsTrue(ExecutionCapabilities.Has(attached.Adapter.Capabilities, ExecutionCapability.StateSaveLoad));
                Assert.AreEqual(ExecutionCapability.Memory | ExecutionCapability.StateSaveLoad,
                    ExecutionCapabilities.MesenCe_2_2_1);
            }
        }

        // ---------------------------------------------------------------- MAME 0.289

        [TestMethod]
        public void MameClaimsMemoryAndRegistersAndNothingItWouldHaveToBlockFor()
        {
            ExecutionCapability claimed = ExecutionCapabilities.For("MAME", "0.289");

            Assert.AreEqual(ExecutionCapability.Memory | ExecutionCapability.CpuState, claimed,
                "MAME 0.289 holds the cartridge's PRG ROM in its program space, accepts a"
                + " write that comes back, and has readable PC/A/X/Y/SP/P. Its debug:step"
                + " returns without moving the program counter and its debug:bpset blocks"
                + " indefinitely, so every execution verb stays clear — that is what keeps"
                + " MAME non-blocking and in no way a replacement for Mesen2.");

            AssertIsClear(claimed, ExecutionCapability.Pause, "MAME 0.289");
            AssertIsClear(claimed, ExecutionCapability.Resume, "MAME 0.289");
            AssertIsClear(claimed, ExecutionCapability.Reset, "MAME 0.289");
            AssertIsClear(claimed, ExecutionCapability.StepInstruction, "MAME 0.289");
            AssertIsClear(claimed, ExecutionCapability.BreakpointExecution, "MAME 0.289");
            AssertIsClear(claimed, ExecutionCapability.WatchpointRead, "MAME 0.289");
            AssertIsClear(claimed, ExecutionCapability.WatchpointWrite, "MAME 0.289");
            AssertIsClear(claimed, ExecutionCapability.StateSaveLoad, "MAME 0.289");
        }

        // ---------------------------------------------------------------- the test backend

        [TestMethod]
        public void ThePureTestBackendIsGatedAndIsNotGrantedACpu()
        {
            // The precedent this project set with ICpuStateSource, now said to the key
            // bindings too: the fake is a byte array and has no processor. It must not
            // be handed a CPU view by accident, because a machine reporting zero
            // registers is indistinguishable from a real machine at its reset vector.
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                ExecutionCapability claimed = ExecutionCapabilities.Of(machine);

                Assert.IsFalse(ExecutionCapabilities.Has(claimed, ExecutionCapability.CpuState),
                    "a byte array has no registers to report");

                AssertIsClear(claimed, ExecutionCapability.WatchpointRead, "the test backend");
                AssertIsClear(claimed, ExecutionCapability.WatchpointWrite, "the test backend");
            }
        }

        [TestMethod]
        public void TheTestBackendDeclaresOnlyWhatItActuallyImplements()
        {
            // Not "nothing": the fake really does pause, resume, reset, hold a
            // breakpoint and snapshot itself, and a backend that declared none of that
            // would make the shell's whole working path untestable.
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                ExecutionCapability claimed = ExecutionCapabilities.Of(machine);

                AssertHasEveryBit(claimed,
                    ExecutionCapability.MemoryRead | ExecutionCapability.MemoryWrite
                    | ExecutionCapability.Pause | ExecutionCapability.Resume
                    | ExecutionCapability.Reset | ExecutionCapability.StepInstruction
                    | ExecutionCapability.BreakpointExecution | ExecutionCapability.StateSaveLoad,
                    "the test backend");

                Assert.AreEqual(FakeMemoryBackend.DeclaredCapabilities, claimed);
            }
        }

        [TestMethod]
        public void ABackendThatRefusesToReportRegistersSaysSoByName()
        {
            // The declared flag is what the shell gates on; this is the backstop behind
            // it. A refusal that names the machine is something to act on, and one that
            // returned zeros would be a fault the status line could not tell apart from
            // a machine that has not run.
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                MonitorException refusal = Throws(() => ((IExecutionAdapter)machine).ReadCpuState());

                StringAssert.Contains(refusal.Message, "cannot report registers");
                StringAssert.Contains(refusal.Message, machine.DisplayName);
            }
        }

        // ---------------------------------------------------------------- honesty

        [TestMethod]
        public void ABuildNobodyMeasuredDeclaresNothingRatherThanSomethingLikely()
        {
            // The rule that keeps the table honest: no measurement, no claim. MesenCE
            // 2.3.0 is not MesenCE 2.2.1, and inheriting its answers would be the same
            // fiction with a different version number.
            Assert.AreEqual(ExecutionCapability.None, ExecutionCapabilities.For("MesenCE", "2.3.0"));
            Assert.AreEqual(ExecutionCapability.None, ExecutionCapabilities.For("Mesen2", "2.2.0"));
            Assert.AreEqual(ExecutionCapability.None, ExecutionCapabilities.For("MesenCE", null));
            Assert.AreEqual(ExecutionCapability.None, ExecutionCapabilities.For(null, null));

            // And the measured builds are recognised, so a caller can tell "nothing
            // claimed" from "not measured".
            Assert.IsTrue(ExecutionCapabilities.IsMeasured("Mesen2", "2.1.1"));
            Assert.IsFalse(ExecutionCapabilities.IsMeasured("MesenCE", "2.3.0"));
        }

        [TestMethod]
        public void ABackendWithNoAdapterClaimsNothing()
        {
            // The seam is the contract, not the interface: a backend that does not
            // implement IExecutionAdapter has answered no question, so the answer is
            // "nothing" rather than a guess from the members it happens to have.
            using (SilentBackend machine = new SilentBackend())
            {
                Assert.AreEqual(ExecutionCapability.None, ExecutionCapabilities.Of(machine));
            }

            Assert.AreEqual(ExecutionCapability.None, ExecutionCapabilities.Of(null));
        }

        [TestMethod]
        public void AnAdapterThatThrowsFromItsCapabilitiesClaimsNothingRatherThanTakingTheShellDown()
        {
            // A status line, a key table and a session all ask this. None of them may
            // be taken down by a backend whose answer failed to arrive.
            using (ThrowingCapabilityBackend machine = new ThrowingCapabilityBackend())
            {
                Assert.AreEqual(ExecutionCapability.None, ExecutionCapabilities.Of(machine));
            }
        }

        // ---------------------------------------------------------------- refusals

        [TestMethod]
        public void AnActionTheFlagsRefuseIsRefusedByNameAndTheBridgeIsNotAsked()
        {
            // The contract's rule: the host only calls what a bit grants, and it never
            // learns a capability from an exception. So the refusal happens here,
            // without a round trip, and it names the host and the measurement.
            using (Attached attached = Attach("MesenCE", "2.2.1"))
            {
                MonitorException refusal = Throws(() => attached.Adapter.Resume());

                StringAssert.Contains(refusal.Message, "MesenCE 2.2.1");
                StringAssert.Contains(refusal.Message, "resume");
                StringAssert.Contains(refusal.Message, "emu.resume");
            }
        }

        [TestMethod]
        public void EveryClearedBitRefusesAndEverySetBitIsForwarded()
        {
            // One pass over the whole flag set, both directions. A bit that refuses
            // while it is set, or stays silent while it is clear, is a gate that does
            // not gate.
            using (Attached mesence = Attach("MesenCE", "2.2.1"))
            {
                Throws(() => mesence.Adapter.Pause());
                Throws(() => mesence.Adapter.Resume());
                Throws(() => mesence.Adapter.Reset());
                Throws(() => mesence.Adapter.Step());
                Throws(() => mesence.Adapter.SetBreakpoint(0x8000));
                Throws(() => mesence.Adapter.ClearBreakpoints());
                Throws(() => mesence.Adapter.SetWatchpoint(At(0x8000), MemoryAccessKind.Read));
                Throws(() => mesence.Adapter.SetWatchpoint(At(0x8000), MemoryAccessKind.Write));
                Throws(() => mesence.Adapter.ClearWatchpoints());
                Throws(() => mesence.Adapter.ReadCpuState());
                Throws(() => mesence.Adapter.LoadState(new byte[] { 0x00 }));

                // Memory and snapshots are what MesenCE does have, so these must reach
                // the bridge rather than being refused locally.
                mesence.Adapter.Write(At(0x8000), new byte[] { 0xA9, 0x01 });
                CollectionAssert.AreEqual(new byte[] { 0xA9, 0x01 }, mesence.Adapter.Read(At(0x8000), 2));
                Assert.IsTrue(mesence.Adapter.SaveState().Length > 0);
            }

            using (Attached mesen2 = Attach("Mesen2", "2.1.1"))
            {
                mesen2.Adapter.Pause();
                mesen2.Adapter.Resume();
                mesen2.Adapter.Reset();
                mesen2.Adapter.Step();
                mesen2.Adapter.SetBreakpoint(0x8000);
                mesen2.Adapter.ClearBreakpoints();
                mesen2.Adapter.SetWatchpoint(At(0x8000), MemoryAccessKind.Read);
                mesen2.Adapter.SetWatchpoint(At(0x8000), MemoryAccessKind.Write);
                mesen2.Adapter.ClearWatchpoints();

                // Registers are not round-tripped here: the .NET protocol server the
                // tests speak to has no CPU verb, while both real bridges answer one.
                // What matters on this side is that CpuState is set for Mesen2 and clear
                // for MesenCE, so the host asks in one case and refuses in the other —
                // which is what the bit-by-bit assertion above and the refusal above
                // already establish. Reading $C000 back over the socket is the real
                // bridges' business to prove, and Bridge/probe_mesen2.lua does.
            }
        }

        // ---------------------------------------------------------------- regions

        [TestMethod]
        public void TheAdapterExposesTheFlatSpaceItActuallySpeaks()
        {
            using (Attached attached = Attach("Mesen2", "2.1.1"))
            {
                Assert.AreEqual(1, attached.Adapter.Regions.Count);

                MemoryRegion region = attached.Adapter.Regions[0];
                Assert.AreEqual(0x0000, region.StartAddress);
                Assert.AreEqual(0x10000, region.Length);
                Assert.IsFalse(region.IsBanked, "this bridge speaks a flat address space");

                // Region and bank are the contract's; the flat address is this
                // protocol's. An address naming a region or bank this machine does not
                // have is refused rather than folded into the nearest thing that does.
                Throws(() => attached.Adapter.Read(new RegionAddress(1, 0, 0), 1));
                Throws(() => attached.Adapter.Read(new RegionAddress(0, 3, 0), 1));

                // A write bit off makes the region read-only, which is the one
                // per-space fact a flat region can still carry honestly.
                using (Attached mesence = Attach("MesenCE", "2.2.1"))
                {
                    Assert.IsTrue(mesence.Adapter.Regions[0].IsWritable,
                        "MesenCE declares MemoryWrite: emu.write exists and RAM accepts it");
                }
            }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// A real adapter over a real socket to a machine that says it is
        /// <paramref name="name"/> <paramref name="version"/>.
        ///
        /// The handshake is the only thing the host has to go on, so the handshake is
        /// what a test has to control to see any decision at all. Both ends are
        /// returned so the socket is closed on the way out: a test that leaves a
        /// listener behind is a test that changes the port the next one is given.
        /// </summary>
        private static Attached Attach(string name, string version)
        {
            MeasuredMachine machine = new MeasuredMachine(name, version);
            ProtocolServer server = new ProtocolServer(machine, CpuFactory.Create("6502"));
            server.Start();

            try
            {
                return new Attached(BridgeMemoryBackend.Connect(server.Port), server);
            }
            catch (Exception ex)
            {
                server.Dispose();
                throw new AssertFailedException("could not attach a bridge for " + name + ": " + ex.Message, ex);
            }
        }

        private sealed class Attached : IDisposable
        {
            private readonly ProtocolServer _server;

            public Attached(BridgeMemoryBackend bridge, ProtocolServer server)
            {
                Bridge = bridge;
                Adapter = bridge;
                _server = server;
            }

            public BridgeMemoryBackend Bridge { get; }

            /// <summary>
            /// The contract's view of the same object. Typed as the interface because
            /// the adapter implements its members explicitly: <c>Read(int, int)</c> and
            /// <c>Read(RegionAddress, int)</c> are different questions and only the
            /// interface separates them.
            /// </summary>
            public IExecutionAdapter Adapter { get; }

            public void Dispose()
            {
                Bridge.Dispose();
                _server.Dispose();
            }
        }

        /// <summary>
        /// A machine at the other end that answers the handshake it was asked for and
        /// reports registers, so a test can tell the adapter's own refusal from the
        /// host's. Its registers are at <c>$C000</c>, which is neither the reset vector
        /// nor zero, so a refusal cannot be mistaken for a reading.
        /// </summary>
        private sealed class MeasuredMachine : FakeMemoryBackend, ICpuStateSource
        {
            public MeasuredMachine(string name, string version)
                : base(65536, name, version)
            {
            }

            public CpuSnapshot ReadCpuState()
            {
                return new CpuSnapshot(0xC000, 0x01, 0x02, 0x03, 0xFD, 0x05, 42);
            }
        }

        private static RegionAddress At(int address)
        {
            return new RegionAddress(0, 0, address);
        }

        private static MonitorException Throws(Action action)
        {
            try
            {
                action();
            }
            catch (MonitorException ex)
            {
                return ex;
            }

            Assert.Fail("expected a named refusal");
            return null;
        }

        private static void AssertIsClear(ExecutionCapability claimed, ExecutionCapability bit, string who)
        {
            Assert.IsFalse(ExecutionCapabilities.Has(claimed, bit),
                who + " must not claim " + bit);
        }

        private static void AssertHasEveryBit(ExecutionCapability claimed, ExecutionCapability expected, string who)
        {
            foreach (ExecutionCapability bit in new[]
            {
                ExecutionCapability.MemoryRead, ExecutionCapability.MemoryWrite,
                ExecutionCapability.CpuState, ExecutionCapability.Pause, ExecutionCapability.Resume,
                ExecutionCapability.Reset, ExecutionCapability.StepInstruction,
                ExecutionCapability.BreakpointExecution, ExecutionCapability.WatchpointRead,
                ExecutionCapability.WatchpointWrite, ExecutionCapability.StateSaveLoad,
            })
            {
                if ((expected & bit) == bit)
                {
                    Assert.IsTrue(ExecutionCapabilities.Has(claimed, bit),
                        who + " must claim " + bit);
                }
                else
                {
                    AssertIsClear(claimed, bit, who);
                }
            }
        }

        /// <summary>
        /// A backend that is not an adapter at all: memory, and nothing it could
        /// measure itself against the contract.
        /// </summary>
        private sealed class SilentBackend : IMemoryBackend
        {
            public string EmulatorName { get { return "SilentEmu"; } }
            public string EmulatorVersion { get { return "0.0.0"; } }
            public bool IsRunning { get { return false; } }

            public byte[] Read(int address, int length) { return new byte[length]; }
            public void Write(int address, byte[] bytes) { }
            public void Pause() { }
            public void Resume() { }
            public void Step() { }
            public void Reset() { }
            public void AddBreakpoint(int address, string kind) { }
            public void ClearBreakpoints() { }
            public byte[] SaveState() { return new byte[0]; }
            public void LoadState(byte[] state) { }
            public void Dispose() { }
        }

        /// <summary>
        /// An adapter whose capabilities cannot be read. The host asks this on every
        /// redraw, so a throw here would be a redraw that ends the shell.
        /// </summary>
        private sealed class ThrowingCapabilityBackend : FakeMemoryBackend, IExecutionAdapter
        {
            ExecutionCapability IExecutionAdapter.Capabilities
            {
                get { throw new MonitorException("the handshake never completed"); }
            }
        }
    }
}