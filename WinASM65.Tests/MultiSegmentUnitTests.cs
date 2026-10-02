using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Linking;
using WinASM65.Modules;
using WinASM65.Output;

namespace WinASM65.Tests
{
    /// <summary>
    /// Une unite declaree par plusieurs <c>.org</c>.
    /// <para>
    /// <c>BuildModule</c> ne produisait qu'un segment, donc une etiquette posee apres
    /// un second <c>.org</c> n'avait pas d'offset a donner : elle etait laissee hors
    /// de la table, et une relocation qui la nommait echouait en la citant. Le
    /// defaut etait bruyant, ce qui est deja mieux que silencieux, mais bruyant ne
    /// veut pas dire correct : une unite a deux blocs est perfectly legitimate.
    /// </para>
    /// <para>
    /// Ces tests fixent la forme du découpage : un segment par <c>.org</c>, dans
    /// l'ordre du source, et chaque etiquette dans le segment qui la contient.
    /// </para>
    /// </summary>
    [TestClass]
    public class MultiSegmentUnitTests
    {
        private static ModuleImage Assemble(string source)
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = temp.File("unit.asm");
                File.WriteAllText(path, source);

                AssemblyResult result = new AssemblerEngine().Assemble(path, temp.File("unit.o"));
                Assert.IsTrue(result.Success,
                    result.Diagnostics.Count > 0 ? result.Diagnostics[0].ToString() : "echec");
                Assert.IsNotNull(result.Module, "un module qui declare un export est produit");
                return result.Module;
            }
        }

        private static ModuleSymbol Find(IReadOnlyList<ModuleSymbol> symbols, string name)
        {
            for (int i = 0; i < symbols.Count; i++)
            {
                if (symbols[i].Name == name)
                    return symbols[i];
            }
            return null;
        }

        [TestMethod]
        public void DeuxOrgProduisentDeuxSegmentsDansLOrdreDuSource()
        {
            ModuleImage module = Assemble(
                ".org $8000\n" +
                "Premier: nop\n" +
                "        .export Premier\n" +
                ".org $9000\n" +
                "Second: nop\n" +
                "        .export Second\n");

            Assert.AreEqual(2, module.Segments.Count,
                "chaque .org ouvre un segment : c'est ce qui donne a la seconde etiquette un offset");
            Assert.AreEqual(0x8000, module.Segments[0].OriginAddress);
            Assert.AreEqual(0x9000, module.Segments[1].OriginAddress,
                "l'ordre du source est l'ordre des segments, pas l'ordre des adresses : "
                + "un .org peut revenir en arriere");
        }

        [TestMethod]
        public void ChaqueEtiquetteEstDansLeSegmentQuiLaContient()
        {
            ModuleImage module = Assemble(
                ".org $8000\n" +
                "Premier: nop\n" +
                "Interne: nop\n" +
                "  .export Premier\n" +
                ".org $9000\n" +
                "Second: nop\n" +
                "        .export Second\n");

            // Premier and Second are exported, so they live in the export table and
            // not in the local one. Interne is the interesting name: it is a label the
            // source never exported, and it sits in the first block.
            ModuleSymbol interne = Find(module.Symbols, "Interne");
            Assert.IsNotNull(interne, "Interne doit etre enregistre");
            Assert.AreEqual(0, interne.SegmentIndex, "il est avant le second .org");

            // The exports carry the segment they belong to, and they differ.
            Assert.AreEqual(0, module.Exports[0].SegmentIndex);
            Assert.AreEqual(1, module.Exports[1].SegmentIndex,
                "le second export est dans le second bloc, pas dans le premier");
        }

        [TestMethod]
        public void UneEtiquetteLoraleDUnSegmentPosterieurEstResolueAuLien()
        {
            // The property that matters, and the one the single-segment module could
            // not deliver: a local label defined after a second .org is resolvable,
            // because the module now says which segment it sits in and at what
            // offset. Before, the linker reported "no linked module exports it and
            // the module does not define it" -- for a name that was in the file.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = temp.File("two.asm");
                File.WriteAllText(path,
                    ".org $8000\n" +
                    "Routine:\n" +
                    "        jmp Locale\n" +
                    "        rts\n" +
                    ".org $9000\n" +
                    "Locale: nop\n" +
                    "        .export Routine\n");

                AssemblyResult result = new AssemblerEngine().Assemble(path, temp.File("two.o"));
                Assert.IsTrue(result.Success,
                    result.Diagnostics.Count > 0 ? result.Diagnostics[0].ToString() : "echec");

                Assert.IsNotNull(Find(result.Module.Symbols, "Locale"),
                    "Locale est dans le source, donc dans le module");

                LinkedImage image;
                OperationResult linked = new Linker().Link(
                    new List<ModuleImage> { result.Module }, out image);
                Assert.IsTrue(linked.Success,
                    linked.Diagnostics.Count > 0 ? linked.Diagnostics[0].ToString()
                        : "le lien devrait resoudre Locale");
            }
        }

        [TestMethod]
        public void UneUniteAUnSeulOrgANChangePas()
        {
            // The common case must stay byte for byte what it was, in shape as well
            // as in content: one segment, the same exports, the same local labels.
            ModuleImage module = Assemble(
                ".org $8000\n" +
                "Start:  jmp Boucle\n" +
                "Boucle: jmp Boucle\n" +
                "        .export Start\n");

            Assert.AreEqual(1, module.Segments.Count);
            Assert.AreEqual(1, module.Exports.Count);
            Assert.IsNotNull(Find(module.Symbols, "Boucle"));
            Assert.AreEqual(3u, Find(module.Symbols, "Boucle").Offset);
        }

        [TestMethod]
        public void UnOrgQuiNePoseAucunOctetNePerdPasLeSegmentPrecedent()
        {
            // A .org with nothing after it opens a segment of length 0. Dropping it
            // would renumber the segments, so an export or a local label pointing at
            // segment 1 would land in segment 0 -- a wrong offset rather than a
            // missing one, which is harder to notice.
            ModuleImage module = Assemble(
                ".org $8000\n" +
                "Code:   nop\n" +
                "        .export Code\n" +
                ".org $9000\n");

            Assert.AreEqual(2, module.Segments.Count, "le .org final ouvre un segment, meme vide");
            Assert.AreEqual(0, module.Exports[0].SegmentIndex,
                "l'export ne bouge pas : c'est le premier bloc qui le contient");
            Assert.AreEqual(0x8000, module.Segments[0].OriginAddress);
            Assert.AreEqual(0x9000, module.Segments[1].OriginAddress);
            Assert.AreEqual(0, module.Segments[1].Data.Length, "aucun octet apres le dernier .org");
        }

        [TestMethod]
        public void LesOctetsDeChaqueSegmentSontCeuxDuBlocCorrespondant()
        {
            // The buffer is flat: a second .org changes the address but the bytes keep
            // arriving in the same list. Slicing per segment is what keeps each
            // segment carrying its own bytes rather than a prefix of everything.
            ModuleImage module = Assemble(
                ".org $8000\n" +
                "A: nop\n" +
                "  .export A\n" +
                ".org $9000\n" +
                "B: nop\n" +
                "  nop\n" +
                "  .export B\n");

            CollectionAssert.AreEqual(new byte[] { 0xEA }, module.Segments[0].Data,
                "un octet");
            CollectionAssert.AreEqual(new byte[] { 0xEA, 0xEA }, module.Segments[1].Data,
                "deux octets : le second segment ne doit pas heriter du premier");
        }

        [TestMethod]
        public void TroisOrgSuccessifsRestentDansLOrdre()
        {
            ModuleImage module = Assemble(
                ".org $1000\nA: nop\n .export A\n" +
                ".org $2000\nB: nop\n .export B\n" +
                ".org $3000\nC: nop\n .export C\n");

            Assert.AreEqual(3, module.Segments.Count);
            Assert.AreEqual(0x1000, module.Segments[0].OriginAddress);
            Assert.AreEqual(0x2000, module.Segments[1].OriginAddress);
            Assert.AreEqual(0x3000, module.Segments[2].OriginAddress);
            Assert.AreEqual(0, module.Exports[0].SegmentIndex);
            Assert.AreEqual(1, module.Exports[1].SegmentIndex);
            Assert.AreEqual(2, module.Exports[2].SegmentIndex);
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }

            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "WinASM65MultiSeg_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string File(string name)
            {
                return System.IO.Path.Combine(Path, name);
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Path, true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}