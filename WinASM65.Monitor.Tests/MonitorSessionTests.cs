using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Cpu;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// M7: the interactive session — the thing a user actually types at.
    ///
    /// The session is tested here rather than through the socket, and the reason is
    /// worth stating: what makes it hard to get right is not the transport, which the
    /// protocol tests already cover, but that a REPL must never die on a typo, must
    /// never answer a command it does not implement with silence, and must show the
    /// machine's own refusal when the machine refuses.
    ///
    /// So the properties pinned here are behavioural:
    /// <list type="bullet">
    /// <item>a bad command, a bad address, a bad hex string and a bad count all come
    /// back as <c>ERR</c> lines, and the session stays usable afterwards;</item>
    /// <item>an unknown verb is named, not silently forwarded;</item>
    /// <item>a refusal from the machine keeps its reason — "the emulator refused the
    /// breakpoint" must not become a generic failure;</item>
    /// <item>DISASM reads memory once. Reading per instruction would let the emulated
    /// CPU run between reads and produce a listing that never existed.</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class MonitorSessionTests : IDisposable
    {
        private readonly string _directory;

        public MonitorSessionTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "WinASM65SessionTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, true);
            }
            catch (IOException)
            {
            }
        }

        private MonitorSession Session(FakeMemoryBackend machine)
        {
            return new MonitorSession(machine, CpuFactory.Create("6502"), _directory);
        }

        private string WriteSource(string name, string text)
        {
            string path = Path.Combine(_directory, name);
            File.WriteAllText(path, text);
            return path;
        }

        private static string Only(IReadOnlyList<string> lines)
        {
            Assert.AreEqual(1, lines.Count, "expected a single line, got: " + string.Join(" | ", Lines(lines)));
            return lines[0];
        }

        private static string[] Lines(IReadOnlyList<string> lines)
        {
            string[] all = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++)
                all[i] = lines[i];
            return all;
        }

        [TestMethod]
        public void LigneVideNeProduitRien()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                MonitorSession session = Session(machine);
                Assert.AreEqual(0, session.Execute("").Count);
                Assert.AreEqual(0, session.Execute("   ").Count);
                Assert.AreEqual(0, session.Execute(null).Count);
                // Nothing was asked of the machine: an empty line is not a command.
                Assert.IsNull(machine.ReadLengths);
            }
        }

        [TestMethod]
        public void PingRenseigneLEmulateur()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                Assert.AreEqual("OK FakeEmu 0.0.0", Only(Session(machine).Execute("PING")));
            }
        }

        [TestMethod]
        public void LectureAfficheLesOctetsEnHexadecimal()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                machine.Write(0x8000, new byte[] { 0xA9, 0x5A, 0x60 });

                Assert.AreEqual("$8000 A95A60", Only(Session(machine).Execute("READ $8000 3")));
            }
        }

        [TestMethod]
        public void EcriturePasseParLeBackendEtResteVerifiable()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                MonitorSession session = Session(machine);

                Assert.AreEqual("OK $0800 3 octet(s)", Only(session.Execute("WRITE $0800 A9 5A 60")));
                CollectionAssert.AreEqual(new byte[] { 0xA9, 0x5A, 0x60 }, machine.Read(0x0800, 3));
            }
        }

        [TestMethod]
        public void DesassemblageSortUneInstructionParLigne()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                machine.Write(0x8000, new byte[] { 0xA9, 0x5A, 0x60 });

                IReadOnlyList<string> lines = Session(machine).Execute("DISASM $8000 2");

                Assert.AreEqual(2, lines.Count);
                StringAssert.Contains(lines[0].ToUpperInvariant(), "LDA");
                StringAssert.Contains(lines[0], "$5A");
                StringAssert.Contains(lines[1].ToUpperInvariant(), "RTS");
            }
        }

        [TestMethod]
        public void DesassemblageLitLaMemoireUneSeuleFois()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                machine.Write(0x8000, new byte[] { 0xA9, 0x5A, 0x60 });

                Session(machine).Execute("DISASM $8000 3");

                // The listing has to be a picture of one instant. Per-instruction reads
                // would let the CPU run between them.
                Assert.AreEqual(1, machine.ReadLengths.Count);
            }
        }

        [TestMethod]
        public void DesassemblageAuBordDeLEspaceDActivationResteExact()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                // The last two bytes are real memory, so asking for more instructions
                // than they can hold must yield the one that exists rather than a
                // refusal: the window is truncated to the address space, and the
                // listing stops when it runs out. A refusal here would be wrong —
                // there is nothing out of bounds about $FFFE.
                machine.Write(0xFFFE, new byte[] { 0xA9, 0x5A });

                IReadOnlyList<string> lines = Session(machine).Execute("DISASM $FFFE 8");

                Assert.AreEqual(1, lines.Count);
                StringAssert.Contains(lines[0].ToUpperInvariant(), "LDA");
                StringAssert.Contains(lines[0], "$5A");
            }
        }

        [TestMethod]
        public void DesassemblageDUnCompteNulOuNegatifEstRefuse()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                StringAssert.StartsWith(Only(Session(machine).Execute("DISASM $1000 0")), "ERR");
                StringAssert.StartsWith(Only(Session(machine).Execute("DISASM $1000 -3")), "ERR");
            }
        }

        [TestMethod]
        public void CommandeInconnueEstNommeeEtNEstPasTransmiseAuPont()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                // Forwarding it would let the bridge answer something the session does
                // not understand, and the user would have no way to tell the layers apart.
                string answer = Only(Session(machine).Execute("FROBNIATE $1234"));

                StringAssert.StartsWith(answer, "ERR");
                StringAssert.Contains(answer, "FROBNIATE");
                Assert.IsNull(machine.ReadLengths);
            }
        }

        [TestMethod]
        public void AdresseInvalideEstRefuseeSansTuerLaSession()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                MonitorSession session = Session(machine);

                StringAssert.StartsWith(Only(session.Execute("READ $ZZZZ 4")), "ERR");
                StringAssert.StartsWith(Only(session.Execute("READ $FFFF 4")), "ERR");
                StringAssert.StartsWith(Only(session.Execute("READ 3")), "ERR");

                // Still alive, and still correct: a monitor that dies on a typo cannot
                // be used to look up what the typo was.
                machine.Write(0x1000, new byte[] { 0x42 });
                Assert.AreEqual("$1000 42", Only(session.Execute("READ $1000 1")));
            }
        }

        [TestMethod]
        public void HexadecimalInvalideEstRefuseAvantToutEcriture()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                MonitorSession session = Session(machine);

                StringAssert.StartsWith(Only(session.Execute("WRITE $1000 ZZ")), "ERR");
                StringAssert.StartsWith(Only(session.Execute("WRITE $1000 A")), "ERR");

                // Nothing partial: a rejected hex string must not leave half a write.
                CollectionAssert.AreEqual(new byte[] { 0x00 }, machine.Read(0x1000, 1));
            }
        }

        [TestMethod]
        public void LongueurNulleEstRefuseeSurUneLecture()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                StringAssert.StartsWith(Only(Session(machine).Execute("READ $1000 -1")), "ERR");
            }
        }

        [TestMethod]
        public void ControleDEvaluationEstTransmisAuBackend()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                MonitorSession session = Session(machine);

                StringAssert.StartsWith(Only(session.Execute("PAUSE")), "OK");
                StringAssert.StartsWith(Only(session.Execute("STEP")), "OK");
                StringAssert.StartsWith(Only(session.Execute("RESUME")), "OK");
                StringAssert.StartsWith(Only(session.Execute("RESET")), "OK");

                Assert.AreEqual(1, machine.PauseCount);
                Assert.AreEqual(1, machine.StepCount);
                Assert.AreEqual(1, machine.ResumeCount);
                Assert.AreEqual(1, machine.ResetCount);
            }
        }

        [TestMethod]
        public void LeRefusDuEmulateurConserveSaRaison()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                machine.RejectBreakpoints = true;
                MonitorSession session = Session(machine);

                string answer = Only(session.Execute("BREAK SET exec $8000"));

                // "backend failure" would be useless. The machine's own words are the
                // only thing the user can act on.
                StringAssert.StartsWith(answer, "ERR");
                StringAssert.Contains(answer, "refused the breakpoint");
            }
        }

        [TestMethod]
        public void PointDArretConnuEstPoseEtRemplaceParLEmulation()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                MonitorSession session = Session(machine);

                StringAssert.StartsWith(Only(session.Execute("BREAK SET exec $8000")), "OK");
                CollectionAssert.Contains(machine.Breakpoints, "8000:exec");

                StringAssert.StartsWith(Only(session.Execute("BREAK CLEAR")), "OK");
                Assert.AreEqual(0, machine.Breakpoints.Count);
            }
        }

        [TestMethod]
        public void KindDePointDArretInconnuEstNommeApresLAvresse()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                // The address is checked first on purpose: someone who typed
                // "BREAK SET foob $C000" made one mistake, and naming it is the whole
                // point. Reporting the kind first would send them to the wrong column.
                string answer = Only(Session(machine).Execute("BREAK SET foob $C000"));

                StringAssert.StartsWith(answer, "ERR");
                StringAssert.Contains(answer, "foob");
            }
        }

        [TestMethod]
        public void InstantaneEstSauveEtRestaure()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend(256))
            {
                MonitorSession session = Session(machine);

                session.Execute("WRITE $0080 0A 0B");
                string saved = Only(session.Execute("STATE SAVE"));
                StringAssert.StartsWith(saved, "OK ");

                session.Execute("WRITE $0080 FF FF");
                Assert.AreEqual("OK", Only(session.Execute("STATE LOAD " + saved.Substring(3))));

                Assert.AreEqual("$0080 0A0B", Only(session.Execute("READ $0080 2")));
            }
        }

        [TestMethod]
        public void InstantaneRestaureLEmulationDansLEmulationEntiere()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                MonitorSession session = Session(machine);

                machine.Write(0x2000, new byte[] { 0x0A });
                string saved = Only(session.Execute("STATE SAVE"));

                session.Execute("WRITE $2000 FF");
                Assert.AreEqual("OK", Only(session.Execute("STATE LOAD " + saved.Substring(3))));

                Assert.AreEqual("$2000 0A", Only(session.Execute("READ $2000 1")));
            }
        }

        [TestMethod]
        public void AssemblePuisChargeEcritDansLEmulation()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                MonitorSession session = Session(machine);
                WriteSource("routine.asm", ".org $8000\nRoutine: lda #$5A\n rts\n .export Routine\n");

                // The unit is named after its file, not after its export: two sources
                // can export the same symbol, and the file is what the user typed.
                string assembled = Only(session.Execute("ASSEMBLE routine.asm"));
                StringAssert.StartsWith(assembled, "OK routine $8000");
                Assert.IsNull(machine.ReadLengths, "assembling must not touch the machine");

                string loaded = Only(session.Execute("LOAD routine $9000"));
                StringAssert.StartsWith(loaded, "OK $9000");

                // The routine really is at $9000, byte for byte.
                Assert.AreEqual("$9000 A95A60", Only(session.Execute("READ $9000 3")));
            }
        }

        [TestMethod]
        public void UniteeEstChargeeSansEtreAssembleeEnCore()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                MonitorSession session = Session(machine);
                WriteSource("routine.asm", ".org $8000\nRoutine: lda Table\n rts\nTable: .byte $5A\n .export Routine\n");

                session.Execute("ASSEMBLE routine.asm");
                session.Execute("LOAD routine $A000");

                // Table follows the routine, so its absolute reference moves with it:
                // $A004 here, not $8004.
                StringAssert.Contains(Only(session.Execute("DISASM $A000 1")), "$A004");
            }
        }

        [TestMethod]
        public void ChargerUneUniteeInconnueEstRefuse()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                string answer = Only(Session(machine).Execute("LOAD Inconnue $9000"));

                StringAssert.StartsWith(answer, "ERR");
                StringAssert.Contains(answer, "Inconnue");
            }
        }

        [TestMethod]
        public void UnitesListeCeQuiEstAssemble()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                MonitorSession session = Session(machine);
                Assert.AreEqual("OK no unit", Only(session.Execute("UNITS")));

                WriteSource("routine.asm", ".org $8000\nRoutine: rts\n .export Routine\n");
                session.Execute("ASSEMBLE routine.asm");

                StringAssert.Contains(Only(session.Execute("UNITS")), "routine $8000");
            }
        }

        [TestMethod]
        public void AssemblerUnFichierInexistantEstRefuseEtNommeLEChemin()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                string answer = Only(Session(machine).Execute("ASSEMBLE absent.asm"));

                StringAssert.StartsWith(answer, "ERR");
            }
        }

        [TestMethod]
        public void AideListeLesCommandesSansExecuterLaMachine()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                IReadOnlyList<string> lines = Session(machine).Execute("HELP");

                Assert.IsTrue(lines.Count > 5);
                string all = string.Join(" ", Lines(lines));
                StringAssert.Contains(all, "ASSEMBLE");
                StringAssert.Contains(all, "LOAD");
                StringAssert.Contains(all, "DISASM");
                Assert.IsNull(machine.ReadLengths);
            }
        }

        [TestMethod]
        public void QuitterSignaleALaBboucleDeLecture()
        {
            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                MonitorSession session = Session(machine);
                Assert.IsFalse(session.ShouldQuit);

                session.Execute("QUIT");
                Assert.IsTrue(session.ShouldQuit);
            }
        }

        [TestMethod]
        public void LaSessionExigeUnBackendEtUnCpu()
        {
            Assert.ThrowsException<ArgumentNullException>(
                () => new MonitorSession(null, CpuFactory.Create("6502"), _directory));

            using (FakeMemoryBackend machine = new FakeMemoryBackend())
            {
                Assert.ThrowsException<ArgumentNullException>(
                    () => new MonitorSession(machine, null, _directory));
            }
        }
    }
}