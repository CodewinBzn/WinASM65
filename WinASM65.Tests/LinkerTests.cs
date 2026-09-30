using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Linking;
using WinASM65.Modules;
using WinASM65.Output;

namespace WinASM65.Tests
{
    /// <summary>
    /// Le linker apparie les imports aux exports par nom, place les segments,
    /// applique les relocations et emet une image plate.
    /// </summary>
    [TestClass]
    public class LinkerTests
    {
        [TestMethod]
        public void UnSegmentEstPlaceALAdresseDeSonOrigine()
        {
            ModuleImage module = NewModule("main");
            module.AddSegment(NewSegment("CODE", new byte[] { 0xEA, 0x60 }, 0xC000));

            LinkedImage image;
            OperationResult result = new Linker().Link(new List<ModuleImage> { module }, out image);

            Assert.IsTrue(result.Success, Describe(result.Diagnostics));
            Assert.AreEqual(0xC000, image.OriginAddress);
            Assert.AreEqual(2, image.Data.Length, "l'image ne couvre que ce qui est place");
        }

        [TestMethod]
        public void UnSegmentVideEstIgnoreSansFaireEchouerLaLien()
        {
            // Un module peut legitimement ne rien produire : une unite incluse qui
            // ne declare rien, ou un bss de taille nulle. Cela ne doit pas faire
            // echouer le lien tant qu'un autre segment existe.
            ModuleImage module = NewModule("main");
            module.AddSegment(NewSegment("EMPTY", new byte[0], 0xC000));
            module.AddSegment(NewSegment("CODE", new byte[] { 0x60 }, 0xC000));

            LinkedImage image;
            OperationResult result = new Linker().Link(new List<ModuleImage> { module }, out image);

            Assert.IsTrue(result.Success, Describe(result.Diagnostics));
            Assert.AreEqual(1, image.Segments.Count, "le segment vide ne produit aucune entree");
        }

        [TestMethod]
        public void UnImportEstResoluContreUnExportDUnAutreModule()
        {
            ModuleImage library = NewModule("lib");
            library.AddSegment(NewSegment("LIB", new byte[] { 0x60 }, 0x8000));
            library.AddExport(new ModuleExport("Helper", 0, 0));

            ModuleImage program = NewModule("main");
            program.AddSegment(NewSegment("CODE", new byte[] { 0xAD, 0, 0 }, 0xC000));
            program.AddImport(new ModuleImport("Helper", "lib"));
            program.AddRelocation(Reloc(0, 1, 0xC001, 2, RelocationType.Abs16, "Helper"));

            LinkedImage image;
            OperationResult result = new Linker().Link(
                new List<ModuleImage> { library, program }, out image);

            Assert.IsTrue(result.Success, Describe(result.Diagnostics));
            // L'image commence au segment le plus bas, donc $8000 : le code de
            // 'main' est a l'offset $C000 - $8000. Helper vaut $8000, l'opcode
            // doit porter cette adresse et pas $0000.
            Assert.AreEqual(0x8000, image.OriginAddress, "l'image commence au segment le plus bas");
            Assert.AreEqual(0x8000, image.Symbols["Helper"]);
            int at = 0xC000 - image.OriginAddress;
            Assert.AreEqual(0xAD, image.Data[at]);
            Assert.AreEqual(0x00, image.Data[at + 1]);
            Assert.AreEqual(0x80, image.Data[at + 2]);
        }

        [TestMethod]
        public void UnImportNonSatisfaitNommeLeSymboleLeModuleEtLEModuleAttendu()
        {
            ModuleImage program = NewModule("main");
            program.AddSegment(NewSegment("CODE", new byte[] { 0xEA }, 0xC000));
            program.AddImport(new ModuleImport("Helper", "lib"));

            LinkedImage image;
            OperationResult result = new Linker().Link(new List<ModuleImage> { program }, out image);

            Assert.IsFalse(result.Success);
            string text = Describe(result.Diagnostics);
            StringAssert.Contains(text, "Helper", "le symbole manquant est nomme");
            StringAssert.Contains(text, "main", "le module importeur est nomme");
            StringAssert.Contains(text, "lib", "le module exporteur attendu est nomme");
        }

        [TestMethod]
        public void DeuxSegmentsQuiSeChevauchentSontRefuses()
        {
            ModuleImage module = NewModule("main");
            module.AddSegment(NewSegment("A", new byte[] { 0xEA, 0xEA, 0xEA, 0xEA }, 0xC000));
            module.AddSegment(NewSegment("B", new byte[] { 0x60, 0x60 }, 0xC002));

            LinkedImage image;
            OperationResult result = new Linker().Link(new List<ModuleImage> { module }, out image);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(Describe(result.Diagnostics), "overlap");
        }

        [TestMethod]
        public void UnSymboleExporteEnDoubleEstSignale()
        {
            ModuleImage a = NewModule("a");
            a.AddSegment(NewSegment("CODE", new byte[] { 0xEA }, 0xC000));
            a.AddExport(new ModuleExport("Thing", 0, 0));

            ModuleImage b = NewModule("b");
            b.AddSegment(NewSegment("CODE", new byte[] { 0xEA }, 0xD000));
            b.AddExport(new ModuleExport("Thing", 0, 0));

            LinkedImage image;
            OperationResult result = new Linker().Link(new List<ModuleImage> { a, b }, out image);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(Describe(result.Diagnostics), "Thing");
        }

        [TestMethod]
        public void UneBrancheRelativeEstCalculeeDepuisLImage()
        {
            // bne de $C000 vers $C010 : 14 octets d'ecart depuis la fin du champ.
            ModuleImage module = NewModule("main");
            ModuleSegment code = NewSegment("CODE", new byte[0x20], 0xC000);
            module.AddSegment(code);
            module.AddRelocation(Reloc(0, 0, 0xC000, 1, RelocationType.Rel8, "Target"));

            // L'export doit pointer $C010 pour que la branche soit valable.
            module.AddExport(new ModuleExport("Target", 0, 0x10));

            LinkedImage image;
            OperationResult result = new Linker().Link(new List<ModuleImage> { module }, out image);

            Assert.IsTrue(result.Success, Describe(result.Diagnostics));
            byte encoded = image.Data[0];
            int delta = encoded >= 0x80 ? encoded - 256 : encoded;
            Assert.AreEqual(0x10 - 1, delta, "la branche va de la fin du champ a la cible");
        }

        [TestMethod]
        public void UneBrancheRelativeTropLoinEstRefusee()
        {
            ModuleImage module = NewModule("main");
            module.AddSegment(NewSegment("CODE", new byte[0x400], 0xC000));
            module.AddRelocation(Reloc(0, 0, 0xC000, 1, RelocationType.Rel8, "Far"));
            module.AddExport(new ModuleExport("Far", 0, 0x200));

            LinkedImage image;
            OperationResult result = new Linker().Link(new List<ModuleImage> { module }, out image);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(Describe(result.Diagnostics), "too big");
        }

        [TestMethod]
        public void UnSegmentBssReserveSansEcrireDOctets()
        {
            ModuleImage module = NewModule("main");
            module.AddSegment(NewSegment("CODE", new byte[] { 0xEA }, 0xC000));
            ModuleSegment vars = NewSegment("VARS", new byte[16], 0x0300);
            vars.Kind = SegmentKind.Bss;
            module.AddSegment(vars);

            LinkedImage image;
            OperationResult result = new Linker().Link(new List<ModuleImage> { module }, out image);

            Assert.IsTrue(result.Success, Describe(result.Diagnostics));
            // Le BSS est le segment le plus bas, donc il ouvre l'image : il occupe
            // les 16 premiers octets, a zero, sans qu'aucun octet n'ait ete ecrit
            // depuis le segment. Le trou jusqu'au code a $C000 fait partie de
            // l'image, ce qui est inherent a un bss a $0300 et du code a $C000.
            Assert.AreEqual(0x0300, image.OriginAddress, "l'image commence au plus bas segment place");
            Assert.AreEqual(0xC001 - 0x0300, image.Data.Length, "l'image va du VARS au code");
            for (int i = 0; i < 16; i++)
                Assert.AreEqual(0, image.Data[i], "octet " + i + " du BSS");
            Assert.AreEqual(0xEA, image.Data[0xC000 - 0x0300], "le code est bien a $C000");
        }

        [TestMethod]
        public void UnSegmentEstAligneSurSaDemande()
        {
            ModuleImage module = NewModule("main");
            ModuleSegment code = NewSegment("CODE", new byte[] { 0xEA, 0xEA }, 0xC001);
            code.Alignment = 4;
            module.AddSegment(code);

            LinkedImage image;
            OperationResult result = new Linker().Link(new List<ModuleImage> { module }, out image);

            Assert.IsTrue(result.Success, Describe(result.Diagnostics));
            Assert.AreEqual(0xC004, image.Segments[0].Address, "$C001 aligne sur 4 donne $C004");
        }

        [TestMethod]
        public void UnDecalageDAdressePlaceLEProgrammeEntierAUnAutreEndroit()
        {
            ModuleImage library = NewModule("lib");
            library.AddSegment(NewSegment("LIB", new byte[] { 0x60 }, 0x8000));
            library.AddExport(new ModuleExport("Helper", 0, 0));

            ModuleImage program = NewModule("main");
            program.AddSegment(NewSegment("CODE", new byte[] { 0xAD, 0, 0 }, 0xC000));
            program.AddImport(new ModuleImport("Helper", "lib"));
            program.AddRelocation(Reloc(0, 1, 0xC001, 2, RelocationType.Abs16, "Helper"));

            LinkerOptions options = new LinkerOptions();
            options.AddressShift = 0x1000;

            LinkedImage image;
            OperationResult result = new Linker().Link(
                new List<ModuleImage> { library, program }, options, out image);

            Assert.IsTrue(result.Success, Describe(result.Diagnostics));
            Assert.AreEqual(0xD000, image.Segments[1].Address, "CODE decale de $1000");
            Assert.AreEqual(0x9000, image.Symbols["Helper"], "Helper suit le decalage");
            int at = 0xD000 - image.OriginAddress + 1;
            Assert.AreEqual(0x00, image.Data[at]);
            Assert.AreEqual(0x90, image.Data[at + 1], "la reference pointe le segment decale");
        }

        [TestMethod]
        public void UneValeurTropGrandePourUnOctetEstRefusee()
        {
            ModuleImage module = NewModule("main");
            module.AddSegment(NewSegment("CODE", new byte[] { 0xA9, 0 }, 0xC000));
            module.AddRelocation(Reloc(0, 1, 0xC001, 1, RelocationType.Zp8, "Far"));
            module.AddExport(new ModuleExport("Far", 0, 0x300));

            LinkedImage image;
            OperationResult result = new Linker().Link(new List<ModuleImage> { module }, out image);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(Describe(result.Diagnostics), "one octet");
        }

        [TestMethod]
        public void LierSansModuleEstRefuse()
        {
            LinkedImage image;
            OperationResult result = new Linker().Link(new List<ModuleImage>(), out image);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(Describe(result.Diagnostics), "Nothing to link");
        }

        private static ModuleImage NewModule(string name)
        {
            ModuleImage module = new ModuleImage();
            module.ModuleName = name;
            return module;
        }

        private static ModuleSegment NewSegment(string name, byte[] data, ushort origin)
        {
            ModuleSegment segment = new ModuleSegment(name, data, SegmentKind.Ro, 1, 0);
            segment.OriginAddress = origin;
            return segment;
        }

        private static RelocationRecord Reloc(int segment, int offset, ushort address, byte width,
            RelocationType type, string symbol)
        {
            return new RelocationRecord(segment, string.Empty, offset, address, width, type,
                new List<string> { symbol }, new SourceLocation("t.asm", 1), symbol);
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
