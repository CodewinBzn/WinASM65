using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Cpu;
using WinASM65.Monitor.Protocol;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// The backend that joins the bridge to <see cref="IMemoryBackend"/>, exercised
    /// over a real socket against a real protocol server.
    ///
    /// No emulator here, and that is the point rather than a limitation: the machine
    /// at the other end is a fake, but everything this test covers is what the bridge
    /// actually is — a line of text, and the two ends agreeing on it. A test with a
    /// real emulator would additionally prove that MesenCE answers, which it does,
    /// and which changes nothing about whether the bytes written are the bytes the
    /// linker produced.
    ///
    /// The property that matters most is the last one: a refusal must arrive with its
    /// reason intact. "PAUSE unavailable on this host" is something the user can act
    /// on; "backend failure" is not.
    /// </summary>
    [TestClass]
    public class BridgeMemoryBackendTests
    {
        private static ProtocolServer Serve(FakeMemoryBackend machine)
        {
            ProtocolServer server = new ProtocolServer(machine, CpuFactory.Create("6502"));
            server.Start();
            return server;
        }

        [TestMethod]
        public void LaPoigneeDEtablissementRenseigneLENomEtLaVersionDuHote()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            using (ProtocolServer server = Serve(machine))
            using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(server.Port))
            {
                // Not hard-coded on either side: the backend reports what the host
                // said, so a version mismatch is visible before any command is sent.
                Assert.AreEqual("FakeEmu", bridge.EmulatorName);
                Assert.AreEqual("0.0.0", bridge.EmulatorVersion);
            }
        }

        [TestMethod]
        public void LectureEcritureAllerRetourATraversDuPont()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            using (ProtocolServer server = Serve(machine))
            using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(server.Port))
            {
                bridge.Write(0x8000, new byte[] { 0xA9, 0x01, 0x60 });
                CollectionAssert.AreEqual(new byte[] { 0xA9, 0x01, 0x60 }, bridge.Read(0x8000, 3));
            }
        }

        [TestMethod]
        public void UneRelectureParLePontVoitCeQuUneAutreVoit()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            using (ProtocolServer server = Serve(machine))
            using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(server.Port))
            {
                machine.Write(0x10, new byte[] { 0x5A });
                Assert.AreEqual(0x5A, bridge.Read(0x10, 1)[0],
                    "le pont ne doit pas avoir sa propre copie de la memoire");
            }
        }

        [TestMethod]
        public void LeRefusDuHoteArriveAvecSaRaison()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            using (ProtocolServer server = Serve(machine))
            using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(server.Port))
            {
                machine.RejectBreakpoints = true;

                try
                {
                    bridge.AddBreakpoint(0x8000, BreakpointKind.Exec);
                    Assert.Fail("a refusal must surface as an exception");
                }
                catch (MonitorException ex)
                {
                    StringAssert.Contains(ex.Message, "refused the breakpoint");
                    StringAssert.Contains(ex.Message, "BREAK SET", "le refus doit nommer la commande");
                }
            }
        }

        [TestMethod]
        public void UnePlageHorsEspaceEstRefuseeAvantLeTransport()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            using (ProtocolServer server = Serve(machine))
            using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(server.Port))
            {
                // Bounded on this side as well: the backend must not depend on every
                // caller having gone through the protocol to be checked.
                Assert.ThrowsException<AddressRangeException>(() => bridge.Read(0xFFFF, 2));
                Assert.ThrowsException<AddressRangeException>(
                    () => bridge.Write(0xFFFF, new byte[] { 0x11, 0x22 }));
            }
        }

        [TestMethod]
        public void UneRequeteAuDessusDeLaBorneDuProtocoleEstRefuseeAvantLeTransport()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            using (ProtocolServer server = Serve(machine))
            using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(server.Port))
            {
                byte[] tooMuch = new byte[MonitorProtocol.MaxReadLength + 1];
                try
                {
                    bridge.Read(0x8000, tooMuch.Length);
                    Assert.Fail("a read over the protocol bound must be refused");
                }
                catch (MonitorException ex)
                {
                    StringAssert.Contains(ex.Message, "exceeds the protocol bound");
                }
            }
        }

        [TestMethod]
        public void UnInstantaneVaEtRevient()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            using (ProtocolServer server = Serve(machine))
            using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(server.Port))
            {
                // Only on a machine whose snapshot is bounded by the protocol: the
                // fake's snapshot is its whole memory, which the STATE command caps
                // at 128 KiB. It fits, so the round trip can be checked end to end.
                machine.Write(0x2000, new byte[] { 0xDE, 0xAD });
                byte[] snapshot = bridge.SaveState();
                machine.Reset();
                Assert.AreEqual(0x00, bridge.Read(0x2000, 1)[0], "RESET a efface");

                bridge.LoadState(snapshot);
                Assert.AreEqual(0xDE, bridge.Read(0x2000, 1)[0], "l'instantane restaure");
            }
        }

        [TestMethod]
        public void UneGrandeLectureEstDecoupeeEnBlocsBornes()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            using (ProtocolServer server = Serve(machine))
            using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(server.Port))
            {
                // 1024 bytes over a 256-byte chunk bound: four requests, not one.
                // The assembled answer must be identical, because what the caller
                // sees is a byte range and not a conversation.
                byte[] expected = new byte[1024];
                for (int i = 0; i < expected.Length; i++)
                    expected[i] = (byte)(i + 1);
                machine.Write(0x8000, expected);

                byte[] read = bridge.Read(0x8000, expected.Length);

                Assert.AreEqual(4, machine.ReadLengths.Count);
                foreach (int length in machine.ReadLengths)
                    Assert.IsTrue(length <= 256, "un bloc de " + length + " octets depasse la borne du pont");
                CollectionAssert.AreEqual(expected, read);
            }
        }

        [TestMethod]
        public void UneGrandeEcritureEstDecoupeeEnBlocsBornes()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            using (ProtocolServer server = Serve(machine))
            using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(server.Port))
            {
                byte[] payload = new byte[600];
                for (int i = 0; i < payload.Length; i++)
                    payload[i] = (byte)(i ^ 0x5A);

                bridge.Write(0x8000, payload);

                CollectionAssert.AreEqual(payload, machine.Read(0x8000, payload.Length));
            }
        }

        [TestMethod]
        public void UneReponseTropCourteEstRefuseeEtNonCompleteeParDesZeros()
        {
            // A bridge that answers less than it was asked would shift every
            // following byte. The refusal names it instead of showing zeros that are
            // indistinguishable from real data.
            using (ShortAnswerBackend machine = new ShortAnswerBackend())
            {
                ProtocolServer server = new ProtocolServer(machine, CpuFactory.Create("6502"));
                server.Start();
                using (server)
                using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(server.Port))
                {
                    Assert.ThrowsException<MonitorException>(() => bridge.Read(0x8000, 4));
                }
            }
        }

        /// <summary>
        /// A machine that answers a 4-byte read with 2 bytes, the way a bridge with
        /// an off-by-one would.
        /// </summary>
        private sealed class ShortAnswerBackend : FakeMemoryBackend
        {
            public override byte[] Read(int address, int length)
            {
                byte[] full = base.Read(address, length);
                byte[] truncated = new byte[length - 2];
                Array.Copy(full, truncated, truncated.Length);
                return truncated;
            }
        }
    }
}
