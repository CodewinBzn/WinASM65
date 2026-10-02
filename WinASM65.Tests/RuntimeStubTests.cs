// WinASM65 - the runtime stub, run rather than compared
//
// The plan is explicit that this code cannot be checked by a golden test: a
// golden proves the generator is still itself, and a stub that is itself and
// wrong moves a machine to the wrong place. So the stub is assembled by the
// repository's own assembler, the block it moves is a real linked image, and
// both are loaded into a real NMOS 6502 with a real stack. What the assertions
// check is what the machine did, not what the bytes were.
//
// It is not a machine: there is no keyboard, no interrupt, no anything else. It
// is the processor, and the processor is the part the stub talks to.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Execution;
using WinASM65.Linking;
using WinASM65.Modules;
using WinASM65.Output;
using WinASM65.TextFormat;

namespace WinASM65.Tests
{
    [TestClass]
    public class RuntimeStubTests
    {
        [TestMethod]
        public void LeStubEstAssembleParLeDepotLuiMeme()
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            RuntimeBlock block = RuntimeBlock.Build(Link(new byte[] { 0x60 }, 0x8000),
                Options(0x2000, 0x4000), diagnostics);

            Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
            Assert.IsTrue(block.StubLength > 0, "le stub a des octets");
            Assert.AreEqual(0x2000, block.StubAddress);
            Assert.AreEqual(0xA2, block.Data[0], "LDX #$00 : le stub prepare sa propre lecture");
        }

        [TestMethod]
        public void LEnteteSeTrouveJusteApresLeCode()
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            RuntimeBlock block = RuntimeBlock.Build(Link(new byte[] { 0x60 }, 0x8000),
                Options(0x2000, 0x4000), diagnostics);
            int header = block.StubLength;

            Assert.AreEqual(0x00, block.Data[header], "l'adresse de destination, petit boutiste");
            Assert.AreEqual(0x40, block.Data[header + 1]);
            Assert.AreEqual(0x00, block.Data[header + 2], "le decalage, petit boutiste");
            Assert.AreEqual(0xC0, block.Data[header + 3], "4000 - 8000, en complement a deux");
            Assert.AreEqual(0x00, block.Data[header + 4], "le point d'entree dans le bloc");
            Assert.AreEqual(0x00, block.Data[header + 5]);
            Assert.AreEqual(0x01, block.Data[header + 6], "la longueur du bloc");
            Assert.AreEqual(0x00, block.Data[header + 7]);
            Assert.AreEqual(0x00, block.Data[header + 8], "aucun site a decaler");
            Assert.AreEqual(0x00, block.Data[header + 9]);
        }

        [TestMethod]
        public void LeStubEcritLeBlocLAdresseDemandee()
        {
            byte[] code = new byte[] { 0xA9, 0x2A, 0x60 };
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            RuntimeBlock block = RuntimeBlock.Build(Link(code, 0x8000), Options(0x2000, 0x4000), diagnostics);
            Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));

            Cpu6502Core cpu = Run(block);

            for (int i = 0; i < code.Length; i++)
                Assert.AreEqual(code[i], cpu[(ushort)(0x4000 + i)], "l'octet " + i + " du bloc");

            Assert.AreEqual(0x2A, cpu.A, "la routine s'est executee");
            Assert.AreEqual(Caller + 3, cpu.PC, "et le stub a rendu la main a son appelant");
        }

        [TestMethod]
        public void LeStubDecaleUneReferenceAbsolue()
        {
            // La routine charge l'adresse absolue $8000, ou le bloc a ete lie.
            // Elle doit charger $4000 une fois le bloc deplace.
            byte[] code = new byte[] { 0xAD, 0x00, 0x80, 0x60 };
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            RuntimeBlock block = RuntimeBlock.Build(Link(code, 0x8000, 1), Options(0x2000, 0x4000), diagnostics);

            Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
            Assert.AreEqual(0x01, block.Data[block.StubLength + 8], "un site a decaler");

            Cpu6502Core cpu = Run(block);

            Assert.AreEqual(0x00, cpu[0x4001], "l'octet bas de la reference a suivi le bloc");
            Assert.AreEqual(0x40, cpu[0x4002], "et l'octet haut aussi");
            Assert.AreEqual(0xAD, cpu[0x4000], "l'instruction elle-meme n'a pas bouge");
        }

        [TestMethod]
        public void LeStubDecalePlusieursReferences()
        {
            byte[] code = new byte[]
            {
                0xAD, 0x00, 0x80,
                0xAD, 0x10, 0x80,
                0xAD, 0x20, 0x80,
                0x60
            };
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            RuntimeBlock block = RuntimeBlock.Build(Link(code, 0x8000, 1, 4, 7), Options(0x2000, 0x4000), diagnostics);

            Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
            Assert.AreEqual(0x03, block.Data[block.StubLength + 8], "trois sites a decaler");

            Cpu6502Core cpu = Run(block);

            Assert.AreEqual(0x40, cpu[0x4002], "premiere reference");
            Assert.AreEqual(0x40, cpu[0x4005], "deuxieme reference");
            Assert.AreEqual(0x40, cpu[0x4008], "troisieme reference");
        }

        [TestMethod]
        public void LeStubMarcheEgalementVersLeBas()
        {
            // Le bloc remonte en memoire et le decalage est negatif. L'origine
            // est $9001 et non $9000 pour que l'octet bas déborde : c'est la
            // retenue de cet octet-la que l'octet haut doit recevoir, et une
            // origine ronde ne la produirait jamais.
            byte[] code = new byte[] { 0xAD, 0x01, 0x90, 0x60 };
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            RuntimeBlock block = RuntimeBlock.Build(Link(code, 0x9001, 1), Options(0x2000, 0x4000), diagnostics);

            Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
            Assert.AreEqual(0xFF, block.Data[block.StubLength + 2], "4000 - 9001, en complement a deux");
            Assert.AreEqual(0xAF, block.Data[block.StubLength + 3]);

            Cpu6502Core cpu = Run(block);

            Assert.AreEqual(0x00, cpu[0x4001], "l'octet bas a suivi la baisse, retenue comprise");
            Assert.AreEqual(0x40, cpu[0x4002], "et l'octet haut avec elle");
        }

        [TestMethod]
        public void LeStubAppelleLaRoutineAuBonEndroit()
        {
            // Le point d'entree n'est pas le debut du bloc : deux octets de
            // donnees d'abord, et la routine doit tout de meme partir de la.
            byte[] code = new byte[] { 0x00, 0x00, 0xA9, 0x42, 0x60 };
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            RuntimeBlockOptions options = Options(0x2000, 0x4000);
            options.EntryOffset = 2;

            RuntimeBlock block = RuntimeBlock.Build(Link(code, 0x8000), options, diagnostics);
            Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
            Assert.AreEqual(0x4002, block.EntryAddress, "l'entree est le bloc decale de son offset");

            Cpu6502Core cpu = Run(block);
            Assert.AreEqual(0x42, cpu.A, "c'est la routine du point d'entree qui a rendu la main");
        }

        [TestMethod]
        public void UneReferenceDUnOctetEstRefusee()
        {
            // A width of one is a value small enough to sit in a byte, and a
            // byte has no room for an address that has moved. The stub cannot
            // shift it, so the block is refused rather than carried to the
            // machine with one address silently left behind.
            //
            // The linker can produce such a record — it is what a source that
            // declared a word site and a byte width links to — and refusing it
            // here is the last thing standing between that and a block that
            // jumps somewhere it should not.
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            ModuleImage module = Module(new byte[] { 0xAD, 0x00, 0x60 }, 0x8000);
            module.AddExport(new ModuleExport("Near", 0, 0x10));
            module.AddRelocation(new RelocationRecord(0, string.Empty, 1, 0x8001, 1,
                RelocationType.Abs16, new List<string> { "Near" },
                new SourceLocation("shared.asm", 1), "Near"));

            LinkedImage image;
            OperationResult linked = new Linker().Link(new List<ModuleImage> { module }, out image);
            Assert.IsTrue(linked.Success, Describe(linked.Diagnostics));
            Assert.AreEqual(1, image.References.Count);
            Assert.AreEqual(1, image.References[0].Width, "le lien a bellement produit un site d'un octet");

            RuntimeBlock block = RuntimeBlock.Build(image, Options(0x2000, 0x4000), diagnostics);

            StringAssert.Contains(Describe(diagnostics), "one byte");
            Assert.AreEqual(0, block.Data.Length, "rien n'est produit quand un site ne peut pas bouger");
        }

        [TestMethod]
        public void UnPointDEntreeHorsDuBlocEstRefuse()
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            RuntimeBlockOptions options = Options(0x2000, 0x4000);
            options.EntryOffset = 10;

            RuntimeBlock.Build(Link(new byte[] { 0x60 }, 0x8000), options, diagnostics);

            StringAssert.Contains(Describe(diagnostics), "not inside it");
        }

        [TestMethod]
        public void UnBlocCompresseEstEcritEntier()
        {
            // Un bloc de 600 octets presque tous nuls : c'est le cas pour lequel
            // le codage existe, et il doit ressortir identique, octet par octet.
            // Son point d'entree est son dernier octet, un RTS, parce que le
            // stub appelle ce qu'il a ecrit, et que des donnees ne s'executent pas.
            byte[] code = new byte[600];
            code[0] = 0x01;
            code[300] = 0x02;
            code[599] = 0x60;

            List<Diagnostic> diagnostics = new List<Diagnostic>();
            RuntimeBlockOptions options = Options(0x2000, 0x4000);
            options.EntryOffset = 599;
            RuntimeBlock block = RuntimeBlock.Build(Link(code, 0x8000), options, diagnostics);
            Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
            Assert.IsTrue(block.CodedLength < 30, "600 octets tiennent en " + block.CodedLength);

            Cpu6502Core cpu = Run(block);

            for (int i = 0; i < code.Length; i++)
                Assert.AreEqual(code[i], cpu[(ushort)(0x4000 + i)], "l'octet " + i);

            Assert.AreEqual(0x00, cpu[0x4000 + 600], "rien n'a ete ecrit apres le bloc");
        }

        [TestMethod]
        public void UnGroupeLitteralNeDepassePas127()
        {
            // Un groupe copie tel quel ne peut pas depasser 127 : sa longueur
            // tient dans un octet dont le zero est reserve a la course. Trois
            // cents octets qui ne se ressemblent pas sont donc trois groupes.
            byte[] code = new byte[300];
            for (int i = 0; i < code.Length; i++)
                code[i] = (byte)(i * 37);
            code[299] = 0x60;

            List<Diagnostic> diagnostics = new List<Diagnostic>();
            RuntimeBlockOptions options = Options(0x2000, 0x4000);
            options.EntryOffset = 299;
            RuntimeBlock block = RuntimeBlock.Build(Link(code, 0x8000), options, diagnostics);
            Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));

            Cpu6502Core cpu = Run(block);
            for (int i = 0; i < code.Length; i++)
                Assert.AreEqual(code[i], cpu[(ushort)(0x4000 + i)], "l'octet " + i);
        }

        [TestMethod]
        public void UnGroupeDeDeuxOctetsEgauxResteUnGroupeCopie()
        {
            // Deux octets egaux ne valent pas un groupe : il en coute trois, et
            // le bloc copierait deux octets pour trois. Le codage ne doit pas
            // grossir ce qu'il recoit, alors il le laisse tel quel.
            byte[] code = new byte[] { 0x41, 0x41, 0x42, 0x60 };
            byte[] coded = RuntimeBlock.Code(code);

            Assert.AreEqual(5, coded.Length, "un octet de longueur et les quatre octets");
            Assert.AreEqual(0x04, coded[0], "le groupe est copie tel quel");
        }

        /// <summary>
        /// Runs the stub the way a program would, and stops where the caller
        /// expects to be when it gets its machine back. The caller is a real
        /// JSR with an RTS behind it, because the contract is not "the stub
        /// returns somewhere" but "the stub returns to whoever called it": a
        /// stub that landed the program one byte off would pass a shortcut that
        /// stopped at a fixed address.
        /// </summary>
        private const ushort Caller = 0x00FE;

        private static Cpu6502Core Run(RuntimeBlock block)
        {
            Cpu6502Core cpu = new Cpu6502Core();
            cpu.Load(block.StubAddress, block.Data);

            cpu[Caller] = 0x20;
            cpu[(ushort)(Caller + 1)] = (byte)(block.StubAddress & 0xFF);
            cpu[(ushort)(Caller + 2)] = (byte)(block.StubAddress >> 8);
            cpu[(ushort)(Caller + 3)] = 0x60;
            cpu.Push16((ushort)(Caller + 4));
            cpu.PC = Caller;
            cpu.Run((ushort)(Caller + 3));
            return cpu;
        }

        /// <summary>
        /// A real linked image, which is what the stub is given: a module with
        /// one segment, an export at its base, and a relocation at the offset
        /// each reference starts. The export matters — a relocation the linker
        /// cannot resolve against writes zero into the operand, and a test that
        /// used one would watch the stub shift a reference that was never there.
        /// </summary>
        private static LinkedImage Link(byte[] data, ushort origin, params int[] referenceOffsets)
        {
            ModuleImage module = Module(data, origin);
            for (int i = 0; i < referenceOffsets.Length; i++)
            {
                int offset = referenceOffsets[i];
                string symbol = "site" + i;
                module.AddExport(new ModuleExport(symbol, 0, 0));
                module.AddRelocation(new RelocationRecord(0, string.Empty, offset,
                    (ushort)(origin + offset), 2, RelocationType.Abs16,
                    new List<string> { symbol }, new SourceLocation("shared.asm", 1), symbol));
            }

            LinkedImage image;
            OperationResult result = new Linker().Link(new List<ModuleImage> { module }, out image);
            if (!result.Success)
                throw new InvalidOperationException(Describe(result.Diagnostics));
            return image;
        }

        private static ModuleImage Module(byte[] data, ushort origin)
        {
            ModuleImage module = new ModuleImage();
            module.ModuleName = "shared";
            ModuleSegment segment = new ModuleSegment("CODE", data, SegmentKind.Ro, 1, 0);
            segment.OriginAddress = origin;
            module.AddSegment(segment);
            return module;
        }

        private static RuntimeBlockOptions Options(ushort stubAddress, ushort blockAddress)
        {
            RuntimeBlockOptions options = new RuntimeBlockOptions();
            options.StubAddress = stubAddress;
            options.BlockAddress = blockAddress;
            return options;
        }

        private static string Describe(IReadOnlyList<Diagnostic> diagnostics)
        {
            string text = string.Empty;
            foreach (Diagnostic diagnostic in diagnostics)
                text += diagnostic.ToString() + " | ";
            return text;
        }
    }
}
