using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Modules;

namespace WinASM65.Tests
{
    /// <summary>
    /// Archives de modules : index de symboles, references transitives, cycles et
    /// doublons.
    /// </summary>
    [TestClass]
    public class ArchiveTests
    {
        // ------------------------------------------------------------ round trip

        [TestMethod]
        public void AllerRetour_LesMembresReviennentIntacts()
        {
            using (Sandbox sandbox = new Sandbox())
            {
                ModuleArchive written = new ModuleArchive();
                written.Members.Add(Member("ALPHA", new byte[] { 0xA9, 0x01, 0x60 }, 0xC000, "Alpha"));
                written.Members.Add(Member("BETA", new byte[] { 0xEA, 0xEA, 0xEA, 0xEA }, 0xD000, "Beta"));
                written.References.Add("autre.w65a");

                string path = sandbox.File("lib.w65a");
                MustWrite(path, written);
                ModuleArchive read = MustRead(path);

                Assert.AreEqual(2, read.Members.Count, "deux membres");
                Assert.AreEqual("ALPHA", read.Members[0].Name);
                Assert.AreEqual("BETA", read.Members[1].Name);
                CollectionAssert.AreEqual(
                    new byte[] { 0xA9, 0x01, 0x60 },
                    read.Members[0].Module.Segments[0].Data,
                    "les octets du premier membre doivent revenir intacts");
                CollectionAssert.AreEqual(
                    new byte[] { 0xEA, 0xEA, 0xEA, 0xEA },
                    read.Members[1].Module.Segments[0].Data,
                    "les octets du second membre doivent revenir intacts");
                Assert.AreEqual(0xC000, read.Members[0].Module.Segments[0].OriginAddress, "origine");
                Assert.AreEqual(0xD000, read.Members[1].Module.Segments[0].OriginAddress, "origine");
            }
        }

        [TestMethod]
        public void AllerRetour_LesReferencesReviennent()
        {
            using (Sandbox sandbox = new Sandbox())
            {
                ModuleArchive written = new ModuleArchive();
                written.References.Add("base.w65a");
                written.References.Add("..\\partage\\commune.w65a");

                string path = sandbox.File("lib.w65a");
                MustWrite(path, written);
                ModuleArchive read = MustRead(path);
                Assert.AreEqual(2, read.References.Count);
                Assert.AreEqual("base.w65a", read.References[0]);
                Assert.AreEqual("..\\partage\\commune.w65a", read.References[1],
                    "une reference peut contenir des separateurs de chemin");
            }
        }

        [TestMethod]
        public void UneArchiveVideEstValide()
        {
            using (Sandbox sandbox = new Sandbox())
            {
                string path = sandbox.File("vide.w65a");
                MustWrite(path, new ModuleArchive());
                ModuleArchive read = MustRead(path);
                Assert.AreEqual(0, read.Members.Count);
                Assert.AreEqual(0, read.References.Count);
            }
        }

        // --------------------------------------------------------- symbol index

        [TestMethod]
        public void Index_LesExportsDeTousLesMembresSontVisibles()
        {
            using (Sandbox sandbox = new Sandbox())
            {
                ModuleArchive archive = new ModuleArchive();
                archive.Members.Add(Member("M1", new byte[] { 0x60 }, 0xC000, "DrawTile"));
                archive.Members.Add(Member("M2", new byte[] { 0x60 }, 0xC000, "ClearScreen"));

                string path = sandbox.File("lib.w65a");
                MustWrite(path, archive);

                ArchiveResolution resolution;
                OperationResult result = ArchiveFormat.Resolve(path, out resolution);

                Assert.IsTrue(result.Success, Describe(result));
                Assert.AreEqual(2, resolution.Symbols.Count);
                Assert.AreEqual("DrawTile", resolution.Symbols[0].Name);
                Assert.AreEqual("ClearScreen", resolution.Symbols[1].Name);
                Assert.AreEqual("M1", resolution.Symbols[0].MemberName, "l'index dit quel membre exporte");
            }
        }

        [TestMethod]
        public void Index_UnExportEnDoubleEstRefuseEtNommeLesDeuxFournisseurs()
        {
            // Un avertissement ne servirait a rien : rien dans la resolution ne dit
            // lequel des deux modules un importateur voulait, et en choisir un
            // silencieusement relierait l'appel a la mauvaise routine.
            using (Sandbox sandbox = new Sandbox())
            {
                ModuleArchive archive = new ModuleArchive();
                archive.Members.Add(Member("GRAPHICS", new byte[] { 0x60 }, 0xC000, "DrawTile"));
                archive.Members.Add(Member("SPRITES", new byte[] { 0x60 }, 0xC000, "DrawTile"));

                string path = sandbox.File("dup.w65a");
                MustWrite(path, archive);

                ArchiveResolution resolution;
                OperationResult result = ArchiveFormat.Resolve(path, out resolution);

                Assert.IsFalse(result.Success, "un export en double doit echouer");
                string text = Describe(result);
                StringAssert.Contains(text, "DrawTile", "le nom du symbole en double");
                StringAssert.Contains(text, "GRAPHICS", "le premier fournisseur");
                StringAssert.Contains(text, "SPRITES", "le second fournisseur");
            }
        }

        [TestMethod]
        public void Index_UnExportEnDoubleATraversDesArchivesEstRefuse()
        {
            // Le meme nom dans deux archives distinctes est aussi ambigu qu'a
            // l'interieur d'une archive, et doit etre refuse de la meme facon.
            using (Sandbox sandbox = new Sandbox())
            {
                string basePath = sandbox.WriteArchive("base.w65a", new ArchiveMember[]
                {
                    Member("BASE1", new byte[] { 0x60 }, 0xC000, "Shared")
                }, new string[0]);

                string topPath = sandbox.WriteArchive("top.w65a", new ArchiveMember[]
                {
                    Member("TOP1", new byte[] { 0x60 }, 0xC000, "Shared")
                }, new[] { "base.w65a" });

                ArchiveResolution resolution;
                OperationResult result = ArchiveFormat.Resolve(topPath, out resolution);

                Assert.IsFalse(result.Success, "le doublon traverse l'archive referencee");
                StringAssert.Contains(Describe(result), "Shared");
            }
        }

        // ---------------------------------------------------- transitive linking

        [TestMethod]
        public void Transitif_LesReferencesSontChargeesDansLordreDesDependances()
        {
            // top -> milieu -> base. L'ordre compte : base doit etre liee avant
            // milieu, sinon ses imports n'ont encore personne qui les fournisse.
            using (Sandbox sandbox = new Sandbox())
            {
                sandbox.WriteArchive("base.w65a", new ArchiveMember[]
                {
                    Member("BASE1", new byte[] { 0x60 }, 0xC000, "BaseSym")
                }, new string[0]);

                sandbox.WriteArchive("milieu.w65a", new ArchiveMember[]
                {
                    Member("MID1", new byte[] { 0x60 }, 0xC000, "MidSym")
                }, new[] { "base.w65a" });

                string top = sandbox.WriteArchive("top.w65a", new ArchiveMember[]
                {
                    Member("TOP1", new byte[] { 0x60 }, 0xC000, "TopSym")
                }, new[] { "milieu.w65a" });

                ArchiveResolution resolution;
                OperationResult result = ArchiveFormat.Resolve(top, out resolution);

                Assert.IsTrue(result.Success, Describe(result));
                Assert.AreEqual(3, resolution.Archives.Count, "les trois archives");
                Assert.AreEqual("base", Path.GetFileNameWithoutExtension(resolution.Archives[0].Path));
                Assert.AreEqual("milieu", Path.GetFileNameWithoutExtension(resolution.Archives[1].Path));
                Assert.AreEqual("top", Path.GetFileNameWithoutExtension(resolution.Archives[2].Path));
                Assert.AreEqual(3, resolution.Symbols.Count, "les symboles des trois niveaux");
            }
        }

        [TestMethod]
        public void Transitif_UneReferenceEstRelativeA_LArchiveQuiLaNomme()
        {
            using (Sandbox sandbox = new Sandbox())
            {
                string sub = Path.Combine(sandbox.Root, "sous");
                Directory.CreateDirectory(sub);

                sandbox.WriteArchiveIn(sub, "base.w65a", new ArchiveMember[]
                {
                    Member("BASE1", new byte[] { 0x60 }, 0xC000, "BaseSym")
                }, new string[0]);

                string top = sandbox.WriteArchiveIn(sub, "top.w65a", new ArchiveMember[]
                {
                    Member("TOP1", new byte[] { 0x60 }, 0xC000, "TopSym")
                }, new[] { "base.w65a" });

                ArchiveResolution resolution;
                OperationResult result = ArchiveFormat.Resolve(top, out resolution);

                Assert.IsTrue(result.Success,
                    "la reference doit se resoudre par rapport a l'archive, pas au repertoire courant: "
                    + Describe(result));
                Assert.AreEqual(2, resolution.Archives.Count);
            }
        }

        [TestMethod]
        public void Transitif_UneArchiveReferenceeDeuxFoisNEstChargeeQuUneFois()
        {
            // Deux dependants d'une meme base : la base ne doit pas etre liee deux
            // fois, ce qui dupliquerait ses segments et ses exports.
            using (Sandbox sandbox = new Sandbox())
            {
                sandbox.WriteArchive("base.w65a", new ArchiveMember[]
                {
                    Member("BASE1", new byte[] { 0x60 }, 0xC000, "BaseSym")
                }, new string[0]);

                sandbox.WriteArchive("a.w65a", new ArchiveMember[]
                {
                    Member("A1", new byte[] { 0x60 }, 0xC000, "Asym")
                }, new[] { "base.w65a" });

                string top = sandbox.WriteArchive("top.w65a", new ArchiveMember[]
                {
                    Member("TOP1", new byte[] { 0x60 }, 0xC000, "TopSym")
                }, new[] { "base.w65a", "a.w65a" });

                ArchiveResolution resolution;
                OperationResult result = ArchiveFormat.Resolve(top, out resolution);

                Assert.IsTrue(result.Success, Describe(result));
                Assert.AreEqual(3, resolution.Archives.Count, "base, a, top");
                Assert.AreEqual(3, resolution.Symbols.Count,
                    "BaseSym ne doit pas apparaitre deux fois dans l'index");
            }
        }

        // ------------------------------------------------------------- cycles

        [TestMethod]
        public void Cycle_DetecteEtNommeTouteLaChaine()
        {
            // a -> b -> c -> a. Rapporter seulement la paire qui se ferme
            // ("c references a") ne dit pas au lecteur combien d'archives sont
            // dans la boucle, ni laquelle il faut ouvrir en premier ; la chaine
            // entiere est ce qui rend le diagnostic exploitable.
            using (Sandbox sandbox = new Sandbox())
            {
                sandbox.WriteArchive("a.w65a", new ArchiveMember[0], new[] { "b.w65a" });
                sandbox.WriteArchive("b.w65a", new ArchiveMember[0], new[] { "c.w65a" });
                string c = sandbox.WriteArchive("c.w65a", new ArchiveMember[0], new[] { "a.w65a" });

                ArchiveResolution resolution;
                OperationResult result = ArchiveFormat.Resolve(c, out resolution);

                Assert.IsFalse(result.Success, "un cycle doit etre refuse");
                string text = Describe(result);
                StringAssert.Contains(text, "cycle", "le mot cycle");
                StringAssert.Contains(text, "a.w65a");
                StringAssert.Contains(text, "b.w65a");
                StringAssert.Contains(text, "c.w65a");
            }
        }

        [TestMethod]
        public void Cycle_UneArchiveQuiSeReferenceElleMemeEstRefusee()
        {
            using (Sandbox sandbox = new Sandbox())
            {
                string self = sandbox.WriteArchive("seul.w65a", new ArchiveMember[0],
                    new[] { "seul.w65a" });

                ArchiveResolution resolution;
                OperationResult result = ArchiveFormat.Resolve(self, out resolution);

                Assert.IsFalse(result.Success);
                StringAssert.Contains(Describe(result), "seul.w65a");
            }
        }

        // --------------------------------------------------------- robustness

        [TestMethod]
        public void UnMagicIncorrectEstRefuse()
        {
            using (Sandbox sandbox = new Sandbox())
            {
                string path = sandbox.File("faux.w65a");
                File.WriteAllText(path, "ceci n'est pas une archive");

                ModuleArchive read;
                OperationResult result = ArchiveFormat.TryRead(path, out read);

                Assert.IsFalse(result.Success);
                StringAssert.Contains(Describe(result), "magic",
                    "le message doit dire que le magic ne va pas, sinon un .w65 donne l'impression d'etre une archive");
            }
        }

        [TestMethod]
        public void UnModuleDontLesDonneesDepassentLaFinEstRefuseEtNonPlante()
        {
            // Un .w65 lu depuis une archive est relu depuis un tampon, ou ses
            // offsets ne sont protects par rien. Sans borne explicite, Array.Copy
            // levait ArgumentException, que le lecteur ne capturait pas, et
            // l'outillage tombait au lieu de diagnostiquer. Le fichier est
            // corrompu apres coup, comme le ferait un_module_ abime.
            using (Sandbox sandbox = new Sandbox())
            {
                ModuleImage image = new ModuleImage();
                image.AddSegment(new ModuleSegment("RUNAWAY", new byte[] { 0xEA, 0xEA, 0xEA },
                    SegmentKind.Ro, 1, 0));
                byte[] bytes = W65Format.Serialize(image);

                int offset = PatchSegmentFileOffset(bytes, 0, 0xFFFFFF00u);

                ModuleImage read;
                OperationResult result = W65Format.TryRead(bytes, "corrompu.w65", out read, out string moduleName);

                Assert.IsTrue(offset > 0, "le test doit avoir trouve le champ a corrompre");
                Assert.IsFalse(result.Success, "un segment hors fichier doit etre refuse");
                Assert.IsNull(read, "aucun module ne doit etre rendu");
                StringAssert.Contains(Describe(result), "RUNAWAY", "le segment fautif est nomme");
            }
        }

        [TestMethod]
        public void UnMembreDontLeBlobDepasseLaFinEstRefuse()
        {
            using (Sandbox sandbox = new Sandbox())
            {
                ModuleArchive written = new ModuleArchive();
                written.Members.Add(Member("M1", new byte[] { 0xA9, 0x01, 0x60 }, 0xC000, "Sym"));

                string path = sandbox.File("tronque.w65a");
                MustWrite(path, written);

                // Tronque le fichier au milieu du membre.
                byte[] data = File.ReadAllBytes(path);
                using (FileStream stream = File.Create(path))
                    stream.Write(data, 0, data.Length - 6);

                ModuleArchive read;
                OperationResult result = ArchiveFormat.TryRead(path, out read);

                Assert.IsFalse(result.Success, "un membre tronque doit etre refuse, pas lu comme vide");
                StringAssert.Contains(Describe(result), "M1", "le membre fautif est nomme");
            }
        }

        [TestMethod]
        public void UneReferenceVersUnFichierAbsentEstRefusee()
        {
            using (Sandbox sandbox = new Sandbox())
            {
                string top = sandbox.WriteArchive("top.w65a", new ArchiveMember[0],
                    new[] { "absent.w65a" });

                ArchiveResolution resolution;
                OperationResult result = ArchiveFormat.Resolve(top, out resolution);

                Assert.IsFalse(result.Success);
                StringAssert.Contains(Describe(result), "absent.w65a");
            }
        }

        // ------------------------------------------------------------- helpers

        /// <summary>
        /// Writes and insists on it, reporting the diagnostic. A bare
        /// Assert.IsTrue on .Success turns any write failure into "expected true,
        /// got false" with no reason, which is how a throw that Write does not
        /// catch ends up looking like a test problem rather than a code one.
        /// </summary>
        private static void MustWrite(string path, ModuleArchive archive)
        {
            OperationResult result = ArchiveFormat.Write(path, archive);
            Assert.IsTrue(result.Success, "cannot write '" + path + "': " + Describe(result));
        }

        private static ModuleArchive MustRead(string path)
        {
            ModuleArchive read;
            OperationResult result = ArchiveFormat.TryRead(path, out read);
            Assert.IsTrue(result.Success, "cannot read '" + path + "': " + Describe(result));
            Assert.IsNotNull(read, "a successful read must produce an archive");
            return read;
        }

        /// <summary>
        /// Overwrites the file offset of the nth segment in a serialized .w65, and
        /// returns where that field sits, or -1. Walks the segment table the same
        /// way the reader does, so a change to the table layout shows up here as a
        /// failing test rather than as a test that quietly patches nothing.
        /// </summary>
        private static int PatchSegmentFileOffset(byte[] bytes, int segmentIndex, uint value)
        {
            int cursor = 16;                                   // W65Format.HeaderSize
            uint count = (uint)(bytes[cursor] | (bytes[cursor + 1] << 8)
                | (bytes[cursor + 2] << 16) | (bytes[cursor + 3] << 24));
            cursor += 4;

            for (uint i = 0; i < count; i++)
            {
                uint nameLength = (uint)(bytes[cursor] | (bytes[cursor + 1] << 8)
                    | (bytes[cursor + 2] << 16) | (bytes[cursor + 3] << 24));
                cursor += 4 + (int)nameLength;                  // name
                cursor += 4;                                    // size
                cursor += 4;                                    // alignment
                cursor += 2;                                    // kind, bank
                if (i == segmentIndex)
                {
                    bytes[cursor] = (byte)(value & 0xFF);
                    bytes[cursor + 1] = (byte)((value >> 8) & 0xFF);
                    bytes[cursor + 2] = (byte)((value >> 16) & 0xFF);
                    bytes[cursor + 3] = (byte)((value >> 24) & 0xFF);
                    return cursor;
                }
                cursor += 4;                                    // file offset
                cursor += 2;                                    // origin
            }

            return -1;
        }

        private static ArchiveMember Member(string name, byte[] code, ushort origin, params string[] exports)
        {
            ModuleImage image = new ModuleImage();
            ModuleSegment segment = new ModuleSegment("CODE", code, SegmentKind.Ro, 1, 0);
            segment.OriginAddress = origin;
            image.AddSegment(segment);
            for (int i = 0; i < exports.Length; i++)
                image.AddExport(new ModuleExport(exports[i], 0, 0));

            return new ArchiveMember(name, image);
        }

        private static string Describe(OperationResult result)
        {
            if (result == null || result.Diagnostics == null)
                return string.Empty;
            string text = string.Empty;
            for (int i = 0; i < result.Diagnostics.Count; i++)
                text += result.Diagnostics[i].Message + " | ";
            return text;
        }

        /// <summary>A temp directory that cleans itself up.</summary>
        private sealed class Sandbox : IDisposable
        {
            public string Root { get; private set; }

            public Sandbox()
            {
                Root = Path.Combine(Path.GetTempPath(), "WinASM65Archive_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Root);
            }

            public string File(string name)
            {
                return Path.Combine(Root, name);
            }

            public string WriteArchive(string name, ArchiveMember[] members, string[] references)
            {
                return WriteArchiveIn(Root, name, members, references);
            }

            public string WriteArchiveIn(string directory, string name,
                ArchiveMember[] members, string[] references)
            {
                ModuleArchive archive = new ModuleArchive();
                for (int i = 0; i < members.Length; i++)
                    archive.Members.Add(members[i]);
                for (int i = 0; i < references.Length; i++)
                    archive.References.Add(references[i]);

                string path = Path.Combine(directory, name);
                MustWrite(path, archive);
                return path;
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Root, true);
                }
                catch (IOException)
                {
                    // A leftover temp directory is not worth failing a test over.
                }
            }
        }
    }
}
