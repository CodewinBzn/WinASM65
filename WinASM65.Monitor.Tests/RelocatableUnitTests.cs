using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.Monitor.Protocol;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// M6, the reason the monitor exists: assemble once, then put the same routine at
    /// a second address without assembling again.
    ///
    /// Everything here runs against the fake backend. A test that needs an emulator
    /// would prove less, not more: the relocation arithmetic belongs to the linker,
    /// which is already covered, and the only emulator-specific part is the write,
    /// which the fake performs faithfully.
    /// </summary>
    [TestClass]
    public class RelocatableUnitTests : IDisposable
    {
        private readonly string _directory;

        public RelocatableUnitTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "WinASM65MonitorTest_" + Guid.NewGuid().ToString("N"));
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

        /// <summary>
        /// A routine with two address-dependent values in it: an absolute read of a
        /// table, and the high byte of its own address through a relocation. Placing
        /// it elsewhere therefore has two sites to correct, and one of them is the
        /// kind that only exists because of the byte selector.
        ///
        /// Layout at $8000, offsets in the linked image:
        /// 0-2 lda Table, 3-4 sta $10, 5-6 lda #&gt;Routine, 7-8 sta $11, 9 rts, 10 the table byte.
        /// </summary>
        private static string RoutineSource
        {
            get
            {
                return ".org $8000\n" +
                       "Routine: lda Table\n" +
                       "         sta $10\n" +
                       "         lda #>Routine\n" +
                       "         sta $11\n" +
                       "         rts\n" +
                       "Table:   .byte $5A\n" +
                       "         .export Routine\n";
            }
        }

        private string WriteSource(string name, string text)
        {
            string path = Path.Combine(_directory, name);
            File.WriteAllText(path, text);
            return path;
        }

        private static AssemblerOptions Options()
        {
            AssemblerOptions options = new AssemblerOptions();
            options.Cpu = CpuFactory.Create("6502");
            options.ReportUndefinedSymbols = true;
            return options;
        }

        [TestMethod]
        public void UneUniteAssembleeEstGardeeAvecSonOrigineNaturelle()
        {
            RelocatableUnit unit = RelocatableUnit.FromSource(
                WriteSource("routine.asm", RoutineSource), Options());

            Assert.AreEqual("routine", unit.Name);
            Assert.AreEqual(0x8000, unit.NaturalOrigin);
            Assert.AreEqual(11, unit.Length, "lda abs, sta zp, lda imm, sta zp, rts, table");
            CollectionAssert.Contains(new System.Collections.Generic.List<string>(unit.Exports), "Routine");
        }

        [TestMethod]
        public void LaMemeRoutineSePoseALaMemeAdresse()
        {
            // Same source, same address: the placement must reproduce the bytes the
            // assembler produced, or "reloading" is a different operation.
            RelocatableUnit unit = RelocatableUnit.FromSource(
                WriteSource("id.asm", RoutineSource), Options());

            LoadedBlock block = unit.PlaceAt(0x8000);

            Assert.AreEqual(0x8000, block.Address);
            Assert.AreEqual(0xAD, block.Bytes[0], "lda Table");
            Assert.AreEqual(0x80, block.Bytes[2], "la table est a $8000");
            Assert.AreEqual(0x80, block.Bytes[6], "le haut de Routine vaut $80");
        }

        [TestMethod]
        public void LaMemeRoutineSePosePlusHautSansEtreReassemblee()
        {
            RelocatableUnit unit = RelocatableUnit.FromSource(
                WriteSource("haut.asm", RoutineSource), Options());

            LoadedBlock block = unit.PlaceAt(0xF000);

            Assert.AreEqual(0xF000, block.Address);
            Assert.AreEqual(0xF0, block.Bytes[2], "la table est a $F000");
            Assert.AreEqual(0xF0, block.Bytes[6], "le haut de Routine vaut $F0");
            Assert.IsTrue(block.Sites.Count > 0, "un deplacement qui ne corrige rien n'est pas une relocation");
        }

        [TestMethod]
        public void UneAdresseInferieureALOrigineProduitUnDecalageNegatif()
        {
            // Descending is the same operation as ascending: the linker wraps the
            // shift into 16 bits. If it did not, "load the routine lower" would fail
            // while "load it higher" works, for no reason a user could act on.
            RelocatableUnit unit = RelocatableUnit.FromSource(
                WriteSource("bas.asm", RoutineSource), Options());

            LoadedBlock block = unit.PlaceAt(0x4000);

            Assert.AreEqual(0x4000, block.Address);
            Assert.AreEqual(0x40, block.Bytes[2]);
            Assert.AreEqual(0x40, block.Bytes[6]);
        }

        [TestMethod]
        public void DeuxPlacementsSuccessifsNeSePerturbentPas()
        {
            // The units are kept between placements, so a second call must not inherit
            // anything from the first. A shift stored on the unit instead of computed
            // per call would make the second placement land at the first's address.
            RelocatableUnit unit = RelocatableUnit.FromSource(
                WriteSource("deux.asm", RoutineSource), Options());

            LoadedBlock first = unit.PlaceAt(0x8000);
            LoadedBlock second = unit.PlaceAt(0xC000);
            LoadedBlock third = unit.PlaceAt(0x8000);

            Assert.AreEqual(0x80, first.Bytes[2]);
            Assert.AreEqual(0xC0, second.Bytes[2]);
            Assert.AreEqual(0x80, third.Bytes[2], "revenir a la premiere adresse redonne les memes octets");
        }

        [TestMethod]
        public void UneAdresseHorsDeLEspaceDAdressageEstRefuseeNommee()
        {
            RelocatableUnit unit = RelocatableUnit.FromSource(
                WriteSource("hors.asm", RoutineSource), Options());

            try
            {
                unit.PlaceAt(0x10000);
                Assert.Fail("a placement outside the address space must be refused");
            }
            catch (MonitorException ex)
            {
                StringAssert.Contains(ex.Message, "outside the address space");
            }
        }

        [TestMethod]
        public void UneUniteSansExportNeProduitPasDeModuleEtLeDit()
        {
            // Named rather than silently empty: without an export there is nothing for
            // a linker to hold, and the failure the user would otherwise see is "the
            // unit is empty", which sends them looking in the wrong place.
            string path = WriteSource("rien.asm", ".org $8000\nBoucle: jmp Boucle\n");

            try
            {
                RelocatableUnit.FromSource(path, Options());
                Assert.Fail("a unit with no export and no import produces no module");
            }
            catch (MonitorException ex)
            {
                StringAssert.Contains(ex.Message, "no .export");
                StringAssert.Contains(ex.Message, "rien.asm");
            }
        }

        [TestMethod]
        public void UneEtiquetteLocaleNonExporteeSuitLeDeplacement()
        {
            // Depends on the local symbol table added with the .w65 version 1.1: the
            // branch names a label the unit defines itself, so a link without that
            // table fails outright.
            string path = WriteSource("boucle.asm",
                ".org $8000\n"
                + "Start:  nop\n"
                + "Boucle: jmp Boucle\n"
                + "        .export Start\n");

            RelocatableUnit unit = RelocatableUnit.FromSource(path, Options());
            LoadedBlock block = unit.PlaceAt(0x9000);

            Assert.AreEqual(0x4C, block.Bytes[1], "jmp");
            Assert.AreEqual(0x01, block.Bytes[2], "Boucle est l'adresse du jmp lui-meme, soit + 1");
            Assert.AreEqual(0x90, block.Bytes[3], "soit $9001");
        }

        [TestMethod]
        public void UneUniteADeuxOrgSeDeplaceDUnBlocCommeUnTout()
        {
            // A unit is not restricted to a single block: the second .org is a second
            // segment, and both have to arrive at the new base with their own
            // address-dependent sites corrected. If a segment were dropped here, the
            // unit would look like it loaded and simply be missing half its code.
            string path = WriteSource("deux.asm",
                ".org $8000\n"
                + "Depart:  lda BlocA\n"
                + "         jsr Second\n"
                + "         rts\n"
                + "BlocA:   .byte $11\n"
                + "         .export Depart\n"
                + ".org $9000\n"
                + "Second:  lda BlocB\n"
                + "         rts\n"
                + "BlocB:   .byte $22\n"
                + "         .export Second\n");

            RelocatableUnit unit = RelocatableUnit.FromSource(path, Options());

            // Placed at its own addresses, the two blocks keep their origins.
            // lda is AD low high, so the low byte of the target sits at index 1.
            LoadedBlock naturel = unit.PlaceAt(0x8000);
            Assert.AreEqual(0xAD, naturel.Bytes[0]);
            Assert.AreEqual(0x07, naturel.Bytes[1], "BlocA reste a $8007");
            Assert.AreEqual(0x80, naturel.Bytes[2]);
            Assert.AreEqual(0x00, naturel.Bytes[4], "Second reste a $9000");
            Assert.AreEqual(0x90, naturel.Bytes[5]);

            // Placed lower, the whole image shifts by the same amount, and the gap
            // between the blocks is what has to survive the move. The image starts at
            // $3000, so the second block lands at offset $1000.
            //
            // The shift is the same for both blocks: $8000 -> $3000 is -$5000, and
            // $9000 -> $4000 is -$5000 too. That is what "place the unit here" means
            // for a unit with two blocks -- they keep their distance.
            LoadedBlock deplace = unit.PlaceAt(0x3000);
            Assert.AreEqual(0xAD, deplace.Bytes[0]);
            Assert.AreEqual(0x07, deplace.Bytes[1], "BlocA a suivi : $3007, pas $8007");
            Assert.AreEqual(0x30, deplace.Bytes[2]);
            Assert.AreEqual(0x20, deplace.Bytes[3], "jsr");
            Assert.AreEqual(0x00, deplace.Bytes[4], "Second a suivi : $4000, pas $9000");
            Assert.AreEqual(0x40, deplace.Bytes[5]);

            // The second block is really in the loaded image, not merely linked.
            Assert.AreEqual(0xAD, deplace.Bytes[0x1000], "le second bloc commence par un lda");
            Assert.AreEqual(0x04, deplace.Bytes[0x1001], "il vise son propre $4004");
            Assert.AreEqual(0x60, deplace.Bytes[0x1003], "puis son rts");
            Assert.AreEqual(0x22, deplace.Bytes[0x1004], "et son octet de donnee y est");
        }

        [TestMethod]
        public void UneErreurDAssemblageEstRapporteeParSonDiagnostic()
        {
            string path = WriteSource("faux.asm", ".org $8000\n lda #$100\n");

            try
            {
                RelocatableUnit.FromSource(path, Options());
                Assert.Fail("an unknown instruction must fail the assembly");
            }
            catch (MonitorException ex)
            {
                StringAssert.Contains(ex.Message, "assembly failed");
            }
        }

        // ------------------------------------------------------- the protocol side

        [TestMethod]
        public void AssemblePuisLoadEcritLaRoutineALAdresseDemandee()
        {
            FakeMemoryBackend backend = new FakeMemoryBackend();
            ProtocolServer server = new ProtocolServer(backend, CpuFactory.Create("6502"), _directory);
            WriteSource("routine.asm", RoutineSource);

            string assembled = server.Handle("ASSEMBLE routine.asm");
            StringAssert.StartsWith(assembled, "OK");
            StringAssert.Contains(assembled, "routine $8000");

            string loaded = server.Handle("LOAD routine $F000");
            StringAssert.StartsWith(loaded, "OK");
            StringAssert.Contains(loaded, "$F000");

            byte[] memory = backend.Read(0xF000, 11);
            Assert.AreEqual(0xAD, memory[0], "lda Table");
            Assert.AreEqual(0xF0, memory[2], "la table est a $F000");
            Assert.AreEqual(0xF0, memory[6], "le haut de Routine vaut $F0");
        }

        [TestMethod]
        public void ChargerLaMemeUniteDeuxEcritDeuxAdresses()
        {
            FakeMemoryBackend backend = new FakeMemoryBackend();
            ProtocolServer server = new ProtocolServer(backend, CpuFactory.Create("6502"), _directory);
            WriteSource("routine.asm", RoutineSource);
            server.Handle("ASSEMBLE routine.asm");

            StringAssert.StartsWith(server.Handle("LOAD routine $C000"), "OK");
            StringAssert.StartsWith(server.Handle("LOAD routine $F000"), "OK");

            Assert.AreEqual(0xC0, backend.Read(0xC002, 1)[0], "premiere copie a $C000");
            Assert.AreEqual(0xF0, backend.Read(0xF002, 1)[0], "second copie a $F000");
        }

        [TestMethod]
        public void ChargerUneUniteInconnueEstRefuseEtDitDeLAssembler()
        {
            FakeMemoryBackend backend = new FakeMemoryBackend();
            ProtocolServer server = new ProtocolServer(backend, CpuFactory.Create("6502"), _directory);

            string response = server.Handle("LOAD fantome $C000");

            StringAssert.StartsWith(response, "ERR");
            StringAssert.Contains(response, "unknown unit");
            StringAssert.Contains(response, "ASSEMBLE");
        }

        [TestMethod]
        public void UneRelectureQuiDifferaitSeraitRefusee()
        {
            // The fake backend cannot corrupt itself, so this pins the contract the
            // way it is honoured rather than the way it fails: LOAD compares what the
            // machine holds with what the linker produced, and a mismatch is an ERR,
            // never an OK with a caveat.
            CorruptingMemoryBackend backend = new CorruptingMemoryBackend();
            ProtocolServer server = new ProtocolServer(backend, CpuFactory.Create("6502"), _directory);
            WriteSource("routine.asm", RoutineSource);
            server.Handle("ASSEMBLE routine.asm");

            backend.RejectWrites = true;
            string response = server.Handle("LOAD routine $C000");

            StringAssert.StartsWith(response, "ERR");
            StringAssert.Contains(response, "read-back mismatch");
        }

        [TestMethod]
        public void UneUniteTropGrandePourUnChargementEstRefuseeAvantDEcrire()
        {
            // 16 KiB is the cap: a bigger block in one bridge message freezes the
            // emulator, and the user sees a hang, not a refusal.
            CorruptingMemoryBackend backend = new CorruptingMemoryBackend();
            ProtocolServer server = new ProtocolServer(backend, CpuFactory.Create("6502"), _directory);

            System.Text.StringBuilder filler = new System.Text.StringBuilder();
            filler.Append(".org $0000\n");
            for (int i = 0; i < MonitorProtocol.MaxLoadLength + 16; i++)
                filler.Append(".byte $00\n");
            filler.Append("Entree: rts\n        .export Entree\n");
            WriteSource("gros.asm", filler.ToString());

            StringAssert.StartsWith(server.Handle("ASSEMBLE gros.asm"), "OK");
            string response = server.Handle("LOAD gros $8000");

            StringAssert.StartsWith(response, "ERR");
            StringAssert.Contains(response, "too large");
            Assert.AreEqual(0, backend.WriteCount, "rien ne doit etre ecrit apres un refus de taille");
        }

        [TestMethod]
        public void UnitesListeCeQuiEstAssemble()
        {
            FakeMemoryBackend backend = new FakeMemoryBackend();
            ProtocolServer server = new ProtocolServer(backend, CpuFactory.Create("6502"), _directory);
            WriteSource("routine.asm", RoutineSource);

            Assert.AreEqual("OK no unit", server.Handle("UNITS"));
            server.Handle("ASSEMBLE routine.asm");
            StringAssert.Contains(server.Handle("UNITS"), "routine $8000");
        }

        /// <summary>
        /// Wraps the shared fake to make the bridge misbehave. Written here rather
        /// than added to the shared fake so this file stays independent of the other
        /// monitor tests, which are another workstream's.
        /// </summary>
        private sealed class CorruptingMemoryBackend : IMemoryBackend
        {
            private readonly FakeMemoryBackend _inner = new FakeMemoryBackend();

            /// <summary>When set, writes are silently dropped: the machine keeps zeros.</summary>
            public bool RejectWrites { get; set; }

            public int WriteCount { get; private set; }

            public string EmulatorName { get { return _inner.EmulatorName; } }
            public string EmulatorVersion { get { return _inner.EmulatorVersion; } }
            public bool IsRunning { get { return _inner.IsRunning; } }

            public byte[] Read(int address, int length) { return _inner.Read(address, length); }

            public void Write(int address, byte[] bytes)
            {
                WriteCount++;
                if (!RejectWrites)
                    _inner.Write(address, bytes);
            }

            public void Pause() { _inner.Pause(); }
            public void Resume() { _inner.Resume(); }
            public void Step() { _inner.Step(); }
            public void Reset() { _inner.Reset(); }
            public void AddBreakpoint(int address, string kind) { _inner.AddBreakpoint(address, kind); }
            public void ClearBreakpoints() { _inner.ClearBreakpoints(); }
            public byte[] SaveState() { return _inner.SaveState(); }
            public void LoadState(byte[] state) { _inner.LoadState(state); }
            public void Dispose() { _inner.Dispose(); }
        }
    }
}
