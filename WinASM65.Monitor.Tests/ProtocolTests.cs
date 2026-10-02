using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Cpu;
using WinASM65.Monitor.Protocol;

namespace WinASM65.Monitor.Tests
{
    [TestClass]
    public class ProtocolTests
    {
        [TestMethod]
        public void AdresseAccepteLesEcrituresHabituelles()
        {
            int address;
            Assert.IsTrue(MonitorProtocol.TryParseAddress("$8000", out address));
            Assert.AreEqual(0x8000, address);

            Assert.IsTrue(MonitorProtocol.TryParseAddress("0x8000", out address));
            Assert.AreEqual(0x8000, address);

            Assert.IsTrue(MonitorProtocol.TryParseAddress("8000", out address));
            Assert.AreEqual(0x8000, address);

            // Decimal is reachable through its explicit prefix, which is unambiguous.
            Assert.IsTrue(MonitorProtocol.TryParseAddress("d32768", out address));
            Assert.AreEqual(0x8000, address);

            Assert.IsTrue(MonitorProtocol.TryParseAddress("$FF", out address));
            Assert.AreEqual(0xFF, address);
        }

        [TestMethod]
        public void PrefixeHexEstToujoursAutoritaire()
        {
            // Regression: "$8000" and "0x8000" must mean 32768. An earlier version read
            // them as decimal and aimed at address $1F40, silently at the wrong memory.
            int address;
            Assert.IsTrue(MonitorProtocol.TryParseAddress("$8000", out address));
            Assert.AreEqual(0x8000, address, "the $ prefix forces hex.");

            Assert.IsTrue(MonitorProtocol.TryParseAddress("0x8000", out address));
            Assert.AreEqual(0x8000, address, "the 0x prefix forces hex.");

            // Upper case: that is how 6502 listings are written.
            Assert.IsTrue(MonitorProtocol.TryParseAddress("$FFF0", out address));
            Assert.AreEqual(0xFFF0, address, "IndexOfAny is case sensitive.");
        }

        [TestMethod]
        public void AdresseHorsPlage16BitsEstRefusee()
        {
            // A 6502 address is 16 bits: an explicit refusal beats a silently
            // truncated address.
            int address;
            Assert.IsFalse(MonitorProtocol.TryParseAddress("$10000", out address));
            Assert.IsFalse(MonitorProtocol.TryParseAddress("d65536", out address));
        }

        [TestMethod]
        public void AdresseInvalideEstRefusee()
        {
            int address;
            Assert.IsFalse(MonitorProtocol.TryParseAddress("", out address));
            Assert.IsFalse(MonitorProtocol.TryParseAddress("$", out address));
            Assert.IsFalse(MonitorProtocol.TryParseAddress("zz", out address));
            Assert.IsFalse(MonitorProtocol.TryParseAddress(null, out address));
        }

        [TestMethod]
        public void HexAccepteEtRefuseCorRECTement()
        {
            byte[] bytes;
            Assert.IsTrue(MonitorProtocol.TryParseHex("A90160", out bytes));
            CollectionAssert.AreEqual(new byte[] { 0xA9, 0x01, 0x60 }, bytes);

            Assert.IsTrue(MonitorProtocol.TryParseHex("a9 01 60", out bytes), "spaces are ignored.");
            CollectionAssert.AreEqual(new byte[] { 0xA9, 0x01, 0x60 }, bytes);

            Assert.IsFalse(MonitorProtocol.TryParseHex("A90", out bytes), "odd digit count.");
            Assert.IsFalse(MonitorProtocol.TryParseHex("AZ", out bytes), "non-hex character.");
            Assert.IsFalse(MonitorProtocol.TryParseHex("", out bytes));
        }

        [TestMethod]
        public void HexEstMajusculeEtStable()
        {
            CollectionAssert.AreEqual(new byte[] { 0xA9, 0x01, 0x60 },
                HexOf(MonitorProtocol.ToHex(new byte[] { 0xA9, 0x01, 0x60 })));
            Assert.AreEqual("A90160", MonitorProtocol.ToHex(new byte[] { 0xA9, 0x01, 0x60 }));
            Assert.AreEqual(string.Empty, MonitorProtocol.ToHex(new byte[0]));
        }

        [TestMethod]
        public void PingRepondLeNomEtLaVersion()
        {
            FakeMemoryBackend backend = new FakeMemoryBackend();
            ProtocolServer server = NewServer(backend);
            try
            {
                string response = server.Handle("PING");
                Assert.IsTrue(response.StartsWith("OK FakeEmu "), response);
            }
            finally
            {
                server.Dispose();
                backend.Dispose();
            }
        }

        [TestMethod]
        public void LectureEcritureParLeProtocole()
        {
            FakeMemoryBackend backend = new FakeMemoryBackend();
            ProtocolServer server = NewServer(backend);
            try
            {
                Assert.AreEqual("OK", server.Handle("WRITE $8000 A90160"));
                Assert.AreEqual("OK A90160", server.Handle("READ $8000 3"));
                Assert.AreEqual("OK A9", server.Handle("READ $8000 1"));
            }
            finally
            {
                server.Dispose();
                backend.Dispose();
            }
        }

        [TestMethod]
        public void DesassemblageViaLeProtocole()
        {
            FakeMemoryBackend backend = new FakeMemoryBackend();
            ProtocolServer server = NewServer(backend);
            try
            {
                server.Handle("WRITE $8000 A90160A510");
                string response = server.Handle("DISASM $8000 3");

                Assert.IsTrue(response.StartsWith("OK 3 "), response);
                Assert.IsTrue(response.Contains("LDA #$01"), response);
                Assert.IsTrue(response.Contains("LDA $10"), response);
                Assert.IsTrue(response.Contains("$8000:"), response);
            }
            finally
            {
                server.Dispose();
                backend.Dispose();
            }
        }

        [TestMethod]
        public void OpcodeInconnuEstSignaleSansEtreDevine()
        {
            FakeMemoryBackend backend = new FakeMemoryBackend();
            ProtocolServer server = NewServer(backend);
            try
            {
                server.Handle("WRITE $8000 02");
                string response = server.Handle("DISASM $8000 1");
                Assert.IsTrue(response.Contains(".byte $02"), response);
            }
            finally
            {
                server.Dispose();
                backend.Dispose();
            }
        }

        [TestMethod]
        public void LectureHorsPlageEstRefuseeEtNommee()
        {
            FakeMemoryBackend backend = new FakeMemoryBackend(256);
            ProtocolServer server = NewServer(backend);
            try
            {
                string response = server.Handle("READ $FFF0 16");
                Assert.IsTrue(response.StartsWith("ERR "), response);
                Assert.IsTrue(response.Contains("outside the memory space"), response);
            }
            finally
            {
                server.Dispose();
                backend.Dispose();
            }
        }

        [TestMethod]
        public void BornesSontAppliqueesAvantLeBackend()
        {
            // The point: an unbounded request would freeze the Lua bridge. The cap
            // must therefore be refused before any backend call, otherwise it is
            // already too late.
            FakeMemoryBackend backend = new FakeMemoryBackend();
            ProtocolServer server = NewServer(backend);
            try
            {
                Assert.IsTrue(server.Handle("READ $8000 999999").StartsWith("ERR "));
                Assert.IsTrue(server.Handle("READ $8000 " + MonitorProtocol.MaxReadLength).StartsWith("OK"));
                Assert.IsTrue(server.Handle("DISASM $8000 100000").StartsWith("ERR "));
                Assert.IsTrue(server.Handle("DISASM $8000 " + MonitorProtocol.MaxDisasmCount).StartsWith("OK"));
                Assert.IsTrue(server.Handle("DISASM $8000 0").StartsWith("ERR "));
                Assert.IsTrue(server.Handle("READ $8000 -1").StartsWith("ERR "));
            }
            finally
            {
                server.Dispose();
                backend.Dispose();
            }
        }

        [TestMethod]
        public void PointsDArretPassentParLeProtocole()
        {
            FakeMemoryBackend backend = new FakeMemoryBackend();
            ProtocolServer server = NewServer(backend);
            try
            {
                Assert.AreEqual("OK", server.Handle("BREAK SET exec $8000"));
                Assert.AreEqual("OK", server.Handle("BREAK SET write $9000"));
                Assert.AreEqual(2, backend.Breakpoints.Count);

                Assert.AreEqual("OK", server.Handle("BREAK CLEAR"));
                Assert.AreEqual(0, backend.Breakpoints.Count);

                Assert.IsTrue(server.Handle("BREAK SET bogus $8000").StartsWith("ERR "));
                Assert.IsTrue(server.Handle("BREAK").StartsWith("ERR "));
            }
            finally
            {
                server.Dispose();
                backend.Dispose();
            }
        }

        [TestMethod]
        public void ControleDExecutionEtInstantanePassentParLeProtocole()
        {
            FakeMemoryBackend backend = new FakeMemoryBackend(256);
            ProtocolServer server = NewServer(backend);
            try
            {
                Assert.AreEqual("OK", server.Handle("PAUSE"));
                Assert.AreEqual(1, backend.PauseCount);
                Assert.AreEqual("OK", server.Handle("RESUME"));
                Assert.AreEqual(1, backend.ResumeCount);
                Assert.AreEqual("OK", server.Handle("STEP"));
                Assert.AreEqual(1, backend.StepCount);

                server.Handle("WRITE $0010 AABB");
                string saved = server.Handle("STATE SAVE");
                Assert.IsTrue(saved.StartsWith("OK "), saved);

                server.Handle("WRITE $0010 0000");
                Assert.AreEqual("OK", server.Handle("STATE LOAD " + saved.Substring(3)));
                Assert.AreEqual("OK AABB", server.Handle("READ $0010 2"));

                // Snapshot of the wrong size: refused and named.
                Assert.IsTrue(server.Handle("STATE LOAD 0102").StartsWith("ERR "));
            }
            finally
            {
                server.Dispose();
                backend.Dispose();
            }
        }

        [TestMethod]
        public void CommandeInconnueEstRefuseeEtNommee()
        {
            FakeMemoryBackend backend = new FakeMemoryBackend();
            ProtocolServer server = NewServer(backend);
            try
            {
                string response = server.Handle("FRED");
                Assert.IsTrue(response.StartsWith("ERR unknown command: FRED"), response);
                Assert.AreEqual("ERR empty command", server.Handle("   "));
            }
            finally
            {
                server.Dispose();
                backend.Dispose();
            }
        }

        [TestMethod]
        public void ParametresMalformesSontRefusesNomme()
        {
            FakeMemoryBackend backend = new FakeMemoryBackend();
            ProtocolServer server = NewServer(backend);
            try
            {
                Assert.IsTrue(server.Handle("READ $8000").StartsWith("ERR READ expects"));
                Assert.IsTrue(server.Handle("READ zz 4").StartsWith("ERR invalid address"));
                Assert.IsTrue(server.Handle("READ $8000 xyz").StartsWith("ERR invalid length"));
                Assert.IsTrue(server.Handle("WRITE $8000 A90").StartsWith("ERR invalid hex"));
                Assert.IsTrue(server.Handle("READ $10000 1").StartsWith("ERR invalid address"));
            }
            finally
            {
                server.Dispose();
                backend.Dispose();
            }
        }

        [TestMethod]
        public void AllerRetourSurSocketReel()
        {
            // The end to end test: since the Lua bridge is a synchronous script, what
            // matters is that the server answers line by line without ever blocking or
            // interleaving two responses.
            FakeMemoryBackend backend = new FakeMemoryBackend();
            ProtocolServer server = NewServer(backend);
            try
            {
                int port = server.Start();
                Assert.IsTrue(port > 0, "the server must obtain a port.");

                using (ProtocolClient client = ProtocolClient.Connect(port))
                {
                    Assert.IsTrue(client.IsOk(client.Send("PING")), "PING must answer.");
                    Assert.IsTrue(client.IsOk(client.Send("WRITE $8000 A90160")));
                    Assert.AreEqual("A90160", ProtocolClient.PayloadOf(client.Send("READ $8000 3")));

                    string disasm = ProtocolClient.PayloadOf(client.Send("DISASM $8000 2"));
                    Assert.IsTrue(disasm.Contains("LDA #$01"), disasm);

                    Assert.IsTrue(client.IsOk(client.Send("BREAK SET exec $8000")));
                    Assert.IsTrue(client.IsOk(client.Send("PAUSE")));
                    Assert.AreEqual(1, backend.PauseCount);

                    string refused = client.Send("READ $8000 999999");
                    Assert.IsFalse(client.IsOk(refused));
                    Assert.IsNotNull(ProtocolClient.ErrorOf(refused));
                }
            }
            finally
            {
                server.Dispose();
                backend.Dispose();
            }
        }

        [TestMethod]
        public void ConnexionSurPortInexistantEstRefuseeEtNommee()
        {
            // The monitor must say "no bridge" clearly rather than crash on a mute
            // socket exception.
            try
            {
                using (ProtocolClient client = ProtocolClient.Connect(1, 300))
                {
                    Assert.Fail("the connection must fail.");
                }
            }
            catch (MonitorException)
            {
            }
        }

        [TestMethod]
        public void DesassemblageNeLitQueCeQuIlFaut()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                backend.Write(0x8000, new byte[] { 0xA9, 0x01, 0x60 });
                ProtocolServer server = NewServer(backend);

                server.Handle("DISASM $8000 3");

                // The longest instruction is 3 bytes, so 3 instructions fit in 9 bytes.
                // A 4096-byte read per command would waste 4096 bytes of socket round
                // trip while the emulator is frozen.
                Assert.AreEqual(1, backend.ReadLengths.Count);
                Assert.AreEqual(9, backend.ReadLengths[0]);
            }
        }

        [TestMethod]
        public void DesassemblagePresDeLaFinDeLEspaceEstTronquePasRefuse()
        {
            using (FakeMemoryBackend backend = new FakeMemoryBackend())
            {
                // $FFFE + 3 bytes overflows: disassembling the end of the space must
                // stay possible, otherwise the last instructions of a ROM are unreadable.
                backend.Write(0xFFFE, new byte[] { 0x60, 0x60 });
                ProtocolServer server = NewServer(backend);

                string response = server.Handle("DISASM $FFFE 5");

                Assert.IsTrue(response.StartsWith("OK "), response);
                Assert.AreEqual(2, backend.ReadLengths[0]);
            }
        }

        private static ProtocolServer NewServer(FakeMemoryBackend backend)
        {
            return new ProtocolServer(backend, new Cpu6502());
        }

        private static byte[] HexOf(string text)
        {
            byte[] bytes;
            Assert.IsTrue(MonitorProtocol.TryParseHex(text, out bytes));
            return bytes;
        }
    }
}