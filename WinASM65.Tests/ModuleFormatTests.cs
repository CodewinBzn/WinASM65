using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Modules;
using WinASM65.Output;

namespace WinASM65.Tests
{
    /// <summary>
    /// Aller-retour .w65 : ecrire puis relire doit rendre la meme image.
    /// <para>
    /// Ces tests verrouillent l'alignement des tables entre l'ecriture et la
    /// lecture. Une asymetrie de largeur sur un seul champ decale le curseur de
    /// deux octets a chaque enregistrement et rend la table suivante illisible,
    /// sans que l'appelant voie jamais d'erreur.
    /// </para>
    /// </summary>
    [TestClass]
    public class ModuleFormatTests
    {
        [TestMethod]
        public void AllerRetour_HeaderEstStable()
        {
            ModuleImage image = BuildImage();
            ModuleImage read = RoundTrip(image);

            Assert.AreEqual(image.Segments.Count, read.Segments.Count);
            Assert.AreEqual(image.Exports.Count, read.Exports.Count);
            Assert.AreEqual(image.Imports.Count, read.Imports.Count);
            Assert.AreEqual(image.Relocations.Count, read.Relocations.Count);
        }

        [TestMethod]
        public void AllerRetour_AdressesDeRelocationSontExactes()
        {
            ModuleImage read = RoundTrip(BuildImage());

            // L'ancien code ecrivait l'adresse sur 4 octets et la relisait sur 2 :
            // la valeur etait fausse et le reste de la table decale. Chaque adresse
            // doit maintenant revenir exacte, y compris les plus hautes.
            CollectionAssert.AreEqual(
                new ushort[] { 0xC000, 0xC010, 0xC020, 0xFFFF },
                new List<ushort> { read.Relocations[0].Address, read.Relocations[1].Address,
                                   read.Relocations[2].Address, read.Relocations[3].Address });
        }

        [TestMethod]
        public void AllerRetour_ChampsDeRelocationSontPreserves()
        {
            ModuleImage read = RoundTrip(BuildImage());

            for (int i = 0; i < 4; i++)
            {
                RelocationRecord original = BuildImage().Relocations[i];
                RelocationRecord back = read.Relocations[i];
                Assert.AreEqual(original.SegmentIndex, back.SegmentIndex, "segment " + i);
                Assert.AreEqual(original.Offset, back.Offset, "offset " + i);
                Assert.AreEqual(original.Width, back.Width, "width " + i);
                Assert.AreEqual(original.Type, back.Type, "type " + i);
                Assert.AreEqual(original.TargetSymbol, back.TargetSymbol, "target " + i);
                Assert.AreEqual(original.SourceFile, back.SourceFile, "file " + i);
                Assert.AreEqual(original.SourceLine, back.SourceLine, "line " + i);
            }
        }

        [TestMethod]
        public void AllerRetour_UneCibleVideRedonneUneListeDeSymbolesVide()
        {
            ModuleImage image = new ModuleImage();
            image.AddSegment(new ModuleSegment("CODE", new byte[] { 0xEA }, SegmentKind.Ro, 1, 0));
            image.AddRelocation(new RelocationRecord(0, string.Empty, 0, 0xC000, 2,
                RelocationType.Abs16, new List<string>(), new Core.SourceLocation("a.asm", 1), string.Empty));

            ModuleImage read = RoundTrip(image);

            Assert.AreEqual(0, read.Relocations[0].Symbols.Count,
                "une cible vide ne doit pas produire une liste contenant une chaine vide");
            Assert.IsNull(read.Relocations[0].TargetSymbol);
        }

        [TestMethod]
        public void AllerRetour_ExportEstSegmentPlusOffset()
        {
            ModuleImage read = RoundTrip(BuildImage());

            ModuleExport export = read.Exports[0];
            Assert.AreEqual("DrawTile", export.Name);
            Assert.AreEqual(0, export.SegmentIndex);
            Assert.AreEqual(4u, export.Offset, "l'export porte un offset, pas une adresse");
        }

        [TestMethod]
        public void AllerRetour_UnSegmentBssOccupeUnePlaceMaisNAcritAucunOctet()
        {
            ModuleImage read = RoundTrip(BuildImage());

            ModuleSegment bss = null;
            foreach (ModuleSegment segment in read.Segments)
                if (segment.Kind == SegmentKind.Bss) bss = segment;

            Assert.IsNotNull(bss, "le segment BSS doit survivre a l'aller-retour");
            // La taille reservee fait partie du contrat : c'est elle que le linker
            // doit reservation. Ce qui ne doit pas se produire, c'est l'ecriture de
            // ces octets dans le fichier.
            Assert.AreEqual(16u, bss.OccupiedSize, "la place reservee reste connue");
            Assert.AreEqual(0u, bss.FileOffset, "un BSS ne porte aucune donnee dans le fichier");
        }

        [TestMethod]
        public void UnFichierSansMagicEstRefuse()
        {
            string path = Path.Combine(Path.GetTempPath(), "w65bad_" + Guid.NewGuid().ToString("N") + ".w65");
            File.WriteAllBytes(path, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 });
            try
            {
                ModuleImage image;
                string name;
                OperationResultHolder result = TryRead(path, out image, out name);
                Assert.IsFalse(result.Success);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private sealed class OperationResultHolder
        {
            public bool Success;
        }

        private static OperationResultHolder TryRead(string path, out ModuleImage image, out string moduleName)
        {
            Core.OperationResult result = W65Format.TryRead(path, out image, out moduleName);
            return new OperationResultHolder { Success = result.Success };
        }

        private static ModuleImage BuildImage()
        {
            ModuleImage image = new ModuleImage();
            image.AddSegment(new ModuleSegment("CODE", new byte[] { 0xA9, 0x01, 0x60, 0xEA, 0xAA, 0xBB },
                SegmentKind.Ro, 1, 0));
            image.AddSegment(new ModuleSegment("VARS", new byte[16], SegmentKind.Bss, 1, 0xFF));
            image.AddExport(new ModuleExport("DrawTile", 0, 4));
            image.AddImport(new ModuleImport("Helper", "External"));
            image.AddRelocation(new RelocationRecord(0, string.Empty, 0, 0xC000, 2, RelocationType.Abs16,
                new List<string> { "DrawTile" }, new Core.SourceLocation("a.asm", 3), "DrawTile"));
            image.AddRelocation(new RelocationRecord(0, string.Empty, 4, 0xC010, 1, RelocationType.Zp8,
                new List<string> { "Start" }, new Core.SourceLocation("a.asm", 4), "Start"));
            image.AddRelocation(new RelocationRecord(0, string.Empty, 8, 0xC020, 2, RelocationType.Data16,
                new List<string> { "Tbl" }, new Core.SourceLocation("a.asm", 5), "Tbl"));
            image.AddRelocation(new RelocationRecord(0, string.Empty, 12, 0xFFFF, 1, RelocationType.Rel8,
                new List<string> { "Loop" }, new Core.SourceLocation("a.asm", 6), "Loop"));
            return image;
        }

        private static ModuleImage RoundTrip(ModuleImage image)
        {
            string directory = Path.Combine(Path.GetTempPath(), "WinASM65W65_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "module.w65");
                Core.OperationResult written = W65Format.Write(path, image);
                Assert.IsTrue(written.Success, Describe(written.Diagnostics));

                ModuleImage read;
                string moduleName;
                Core.OperationResult result = W65Format.TryRead(path, out read, out moduleName);
                Assert.IsTrue(result.Success, "relecture: " + Describe(result.Diagnostics));
                Assert.IsNotNull(read);
                return read;
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string Describe(IReadOnlyList<Core.Diagnostic> diagnostics)
        {
            string text = string.Empty;
            foreach (Core.Diagnostic diagnostic in diagnostics)
                text += diagnostic.ToString() + " | ";
            return text;
        }
    }
}
