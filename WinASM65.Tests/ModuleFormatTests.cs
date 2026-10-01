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
        public void AllerRetour_LesOctetsDuSegmentReviennentIntacts()
        {
            // Les tests precedents ne regardaient que les champs des tables. Or les
            // offsets de fichier des segments sont ecrits relatifs au payload et lus
            // comme absolus : la donnee revenait decalee, et rien ne s'en apercevait
            // parce que le premier octet lu etait le 'W' du magic.
            ModuleImage read = RoundTrip(BuildImage());

            ModuleSegment code = null;
            foreach (ModuleSegment segment in read.Segments)
                if (segment.Kind == SegmentKind.Ro) code = segment;

            Assert.IsNotNull(code);
            CollectionAssert.AreEqual(
                new byte[] { 0xA9, 0x01, 0x60, 0xEA, 0xAA, 0xBB },
                code.Data,
                "les octets du segment doivent revenir a l'identique, a l'offset announce");
            Assert.IsTrue(code.FileOffset > 0, "un segment porteur de donnees a un offset reel");
        }

        [TestMethod]
        public void AllerRetour_LesOctetsDeDeuxSegmentsRestentDistincts()
        {
            // Deux segments non contigus : un decalage d'un octet par segment
            // deborderait l'un sur l'autre au lieu de rester invisible.
            ModuleImage image = new ModuleImage();
            image.AddSegment(new ModuleSegment("FIRST", new byte[] { 0x11, 0x12, 0x13 },
                SegmentKind.Ro, 1, 0));
            image.AddSegment(new ModuleSegment("SECOND", new byte[] { 0x21, 0x22, 0x23 },
                SegmentKind.Ro, 1, 0));
            ModuleSegment vars = new ModuleSegment("VARS", new byte[4], SegmentKind.Bss, 1, 0xFF);
            image.AddSegment(vars);

            ModuleImage read = RoundTrip(image);

            CollectionAssert.AreEqual(new byte[] { 0x11, 0x12, 0x13 }, read.Segments[0].Data, "FIRST");
            CollectionAssert.AreEqual(new byte[] { 0x21, 0x22, 0x23 }, read.Segments[1].Data, "SECOND");
        }

        [TestMethod]
        public void AllerRetour_LOrigineDuSegmentEstConservee()
        {
            // Le linker se sert de l'origine comme adresse de repli. Perdue a
            // l'ecriture, elle faisait retomber chaque segment sur une adresse
            // par defaut et le code partait n'importe ou.
            ModuleImage read = RoundTrip(BuildImage());

            Assert.AreEqual(0xC000, read.Segments[0].OriginAddress, "CODE garde son origine");
        }

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
            ModuleSegment code = new ModuleSegment("CODE", new byte[] { 0xA9, 0x01, 0x60, 0xEA, 0xAA, 0xBB },
                SegmentKind.Ro, 1, 0);
            code.OriginAddress = 0xC000;
            image.AddSegment(code);
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
            image.AddSymbol(new ModuleSymbol("Boucle", 0, 3));
            image.AddSymbol(new ModuleSymbol("Fixe", 1, 9));
            return image;
        }

        [TestMethod]
        public void AllerRetour_LesEtiquettesLocalesReviennentIntacles()
        {
            ModuleImage read = RoundTrip(BuildImage());

            Assert.AreEqual(2, read.Symbols.Count, "les deux etiquettes locales reviennent");
            Assert.AreEqual("Boucle", read.Symbols[0].Name);
            Assert.AreEqual(0, read.Symbols[0].SegmentIndex);
            Assert.AreEqual(3u, read.Symbols[0].Offset);
            Assert.AreEqual("Fixe", read.Symbols[1].Name, "une etiquette dans un segment bss aussi");
            Assert.AreEqual(1, read.Symbols[1].SegmentIndex);
            Assert.AreEqual(9u, read.Symbols[1].Offset);
        }

        [TestMethod]
        public void UneRedefinitionLocaleNEcritPasUneSecondeEntree()
        {
            // Deux entreites sous le meme nom feraient que la premiere gagne au lien,
            // alors que la derniere definition est celle que le source dit.
            ModuleImage image = new ModuleImage();
            image.AddSegment(new ModuleSegment("CODE", new byte[8], SegmentKind.Ro, 1, 0));
            image.AddSymbol(new ModuleSymbol("Passe", 0, 1));
            image.AddSymbol(new ModuleSymbol("Passe", 0, 5));

            ModuleImage read = RoundTrip(image);

            Assert.AreEqual(1, read.Symbols.Count);
            Assert.AreEqual(5u, read.Symbols[0].Offset, "c'est la derniere definition qui tient");
        }

        [TestMethod]
        public void UnModuleVersion10SeLitSansTableDeSymboles()
        {
            // La compatibilite ascendante est le pari de la version 1.1 : un fichier
            // ecrit avant la table doit encore se lire, et sa table doit etre vide
            // plutot que remplie de bruit lu dans les donnees du segment.
            byte[] older = SerializeAs10("CODE", new byte[] { 0xA9, 0x01, 0x60 }, 0xC000);

            ModuleImage read;
            string moduleName;
            Core.OperationResult result = W65Format.TryRead(older, "ancien.w65", out read, out moduleName);

            Assert.IsTrue(result.Success, Describe(result.Diagnostics));
            Assert.AreEqual(0, read.Symbols.Count, "un fichier 1.0 n'a pas d'etiquettes locales");
            CollectionAssert.AreEqual(new byte[] { 0xA9, 0x01, 0x60 }, read.Segments[0].Data,
                "les donnees du segment sont intactes");
            Assert.AreEqual(1, read.Exports.Count, "les autres tables se lisent toujours");
            Assert.AreEqual("Start", read.Exports[0].Name);
        }

        /// <summary>
        /// Writes a version 1.0 module: magic, 1.0, the four tables a 1.0 file had,
        /// then the payload. Hand-rolled because the only honest way to test the
        /// reader's version gate is to hand it a file the current writer cannot
        /// produce any more, and cutting a 1.1 file down would leave the payload
        /// offsets in the segment table pointing four bytes too far.
        /// <para>
        /// One RO segment, one export, no imports and no relocations: enough to prove
        /// the reader stops at the relocations instead of walking into the payload.
        /// </para>
        /// </summary>
        private static byte[] SerializeAs10(string segmentName, byte[] data, ushort origin)
        {
            MemoryStream tables = new MemoryStream();
            WriteU32(tables, 1);
            WriteString(tables, segmentName);
            WriteU32(tables, (uint)data.Length);
            WriteU32(tables, 1);
            tables.WriteByte((byte)SegmentKind.Ro);
            tables.WriteByte(0);
            WriteU32(tables, 0);                    // file offset, patched below
            WriteU16(tables, origin);

            WriteU32(tables, 1);                    // one export
            WriteString(tables, "Start");
            WriteU32(tables, 0);
            WriteU32(tables, 0);

            WriteU32(tables, 0);                    // no import
            WriteU32(tables, 0);                    // no relocation
            WriteU32(tables, 0);                    // and, being 1.0, no symbol table

            byte[] tableBytes = tables.ToArray();
            uint payloadBase = (uint)(16 + tableBytes.Length);
            // The segment table entry for the one segment starts after the count.
            WriteU32(tableBytes, 4 + 4 + segmentName.Length + 4 + 4 + 2, payloadBase);

            MemoryStream file = new MemoryStream();
            file.Write(new byte[] { (byte)'W', (byte)'6', (byte)'5', 0x00 }, 0, 4);
            WriteU16(file, 1);
            WriteU16(file, 0);
            WriteU32(file, (uint)tableBytes.Length);
            WriteU32(file, payloadBase);
            file.Write(tableBytes, 0, tableBytes.Length);
            file.Write(data, 0, data.Length);
            return file.ToArray();
        }

        private static void WriteString(Stream stream, string value)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            WriteU32(stream, (uint)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteU16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
        }

        private static void WriteU32(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
            stream.WriteByte((byte)((value >> 16) & 0xFF));
            stream.WriteByte((byte)((value >> 24) & 0xFF));
        }

        private static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
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
