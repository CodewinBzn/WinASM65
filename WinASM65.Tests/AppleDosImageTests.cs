// WinASM65 - the Apple II DOS 3.2 and DOS 3.3 containers
//
// The checks start from the format rather than from the writer: the volume
// table is read back field by field, the catalog entry is followed to the track
// and sector list, and the list is followed to the sectors, which are then
// compared with the file that went in. Nothing here runs DOS, so a mistake that
// DOS would reject has to be caught by reading the volume the way DOS reads it.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Targets;

namespace WinASM65.Tests
{
    [TestClass]
    public class AppleDosImageTests
    {
        [TestMethod]
        public void LesDeuxVolumesNOntPasLaMemeTaille()
        {
            Assert.AreEqual(143360, Size(AppleDosVersion.Dos33),
                "35 * 16 * 256 octets, ce que formate DOS 3.3");
            Assert.AreEqual(116480, Size(AppleDosVersion.Dos32),
                "35 * 13 * 256 octets, ce que formate DOS 3.2");
        }

        [TestMethod]
        public void LaTableDesSecteursDitLaGeometrieDuVolume()
        {
            byte[] dos33 = Build(AppleDosVersion.Dos33, 16);
            int vtoc = At(dos33, AppleDosVolume.VtocTrack, AppleDosVolume.VtocSector);

            Assert.AreEqual(AppleDosVolume.Marker33, dos33[vtoc], "$00 vaut 4 sur un volume 3.3");
            Assert.AreEqual(3, dos33[vtoc + 0x03], "$03 dit la version du DOS qui a formate");
            Assert.AreEqual(17, dos33[vtoc + 0x01], "le catalogue est sur la piste 17");
            Assert.AreEqual(15, dos33[vtoc + 0x02], "au dernier secteur d'une piste de seize");
            Assert.AreEqual(AppleDosVolume.TsPairs, dos33[vtoc + 0x27], "$27 dit 122 paires");
            Assert.AreEqual(35, dos33[vtoc + 0x34], "$34 dit le nombre de pistes");
            Assert.AreEqual(16, dos33[vtoc + 0x35], "$35 dit le nombre de secteurs");
            Assert.AreEqual(0x00, dos33[vtoc + 0x36], "$36-$37 valent 256");
            Assert.AreEqual(0x01, dos33[vtoc + 0x37]);
            Assert.AreEqual(1, dos33[vtoc + 0x31], "$31 dit que l'allocation monte");

            byte[] dos32 = Build(AppleDosVersion.Dos32, 16);
            int vtoc32 = At(new AppleDosVolumeOptions(AppleDosVersion.Dos32),
                AppleDosVolume.VtocTrack, AppleDosVolume.VtocSector);

            Assert.AreEqual(AppleDosVolume.Marker32, dos32[vtoc32], "$00 vaut 2 sur un volume 3.2");
            Assert.AreEqual(2, dos32[vtoc32 + 0x03], "et $03 vaut 2");
            Assert.AreEqual(12, dos32[vtoc32 + 0x02],
                "une piste de treize secteurs n'a pas de secteur 15");
            Assert.AreEqual(13, dos32[vtoc32 + 0x35], "$35 vaut 13");
        }

        [TestMethod]
        public void LaCarteDesSecteursLibresEstUnBitParSecteur()
        {
            AppleDosVolumeOptions options = new AppleDosVolumeOptions(AppleDosVersion.Dos33);
            byte[] image = AppleDosVolume.Build(new byte[512], options);
            int vtoc = At(options, AppleDosVolume.VtocTrack, AppleDosVolume.VtocSector);
            int map = AppleDosVolume.FreeBitmapAt;

            // Track 0 cannot hold a file, so it is full from the start.
            Assert.AreEqual(0x00, image[vtoc + map], "piste 0");
            Assert.AreEqual(0x00, image[vtoc + map + 1]);

            // Track 2 is untouched: sixteen free sectors, then the two bytes of
            // sectors this volume does not have.
            Assert.AreEqual(0xFF, image[vtoc + map + 8], "piste 2");
            Assert.AreEqual(0xFF, image[vtoc + map + 9]);
            Assert.AreEqual(0x00, image[vtoc + map + 10]);

            // Track 18, the one the reference describes, is free as well.
            Assert.AreEqual(0xFF, image[vtoc + map + 18 * 4], "piste 18");

            // Track 17 holds the catalog and the list, so its last two sectors
            // are taken and the thirteen before them are free.
            Assert.AreEqual(0xFF, image[vtoc + map + 17 * 4], "piste 17");
            Assert.AreEqual(0x3F, image[vtoc + map + 17 * 4 + 1], "les deux derniers secteurs sont pris");
        }

        [TestMethod]
        public void LeCatalogueNommeLeFichierEtPointerSaListe()
        {
            AppleDosVolumeOptions options = new AppleDosVolumeOptions(AppleDosVersion.Dos33);
            options.FileName = "HELLO";
            options.FileType = AppleDosProgram.Applesoft;
            byte[] file = AppleDosProgram.ApplesoftFile(new byte[300]);

            byte[] image = AppleDosVolume.Build(file, options);
            int catalog = At(options, AppleDosVolume.VtocTrack, options.FirstCatalogSector);
            int entry = catalog + AppleDosVolume.FirstEntryAt;

            Assert.AreEqual(0x00, image[catalog + 0x01], "un seul secteur de catalogue");
            Assert.AreEqual(0x00, image[catalog + 0x02]);

            int tsTrack = image[entry];
            int tsSector = image[entry + 1];
            Assert.AreEqual(17, tsTrack, "la liste des paires pistes secteurs est aussi sur 17");
            Assert.AreEqual(options.FirstCatalogSector - 1, tsSector, "juste avant le catalogue");
            Assert.AreEqual(AppleDosProgram.Applesoft, image[entry + 0x02], "un fichier type A");

            // Thirty characters of high ASCII, padded with blanks.
            string name = "";
            for (int i = 0; i < 30; i++)
                name += (char)(image[entry + 3 + i] & 0x7F);
            Assert.AreEqual("HELLO" + new string(' ', 25), name,
                "le nom est complete par des espaces jusqu'a trente caracteres");
            Assert.AreEqual(0xC8, image[entry + 3], "le nom est en ASCII haut");

            Assert.AreEqual(2, image[entry + 0x21], "300 octets tiennent en deux secteurs");
            Assert.AreEqual(0, image[entry + 0x22]);
        }

        [TestMethod]
        public void LaListeDesSecteursMenereAuxSecteursDuFichier()
        {
            byte[] payload = new byte[300];
            for (int i = 0; i < payload.Length; i++)
                payload[i] = (byte)(i & 0xFF);

            AppleDosVolumeOptions options = new AppleDosVolumeOptions(AppleDosVersion.Dos33);
            byte[] image = AppleDosVolume.Build(payload, options);
            int catalog = At(options, AppleDosVolume.VtocTrack, options.FirstCatalogSector);
            int entry = catalog + AppleDosVolume.FirstEntryAt;

            int ts = At(options, image[entry], image[entry + 1]);
            Assert.AreEqual(0x00, image[ts + 0x01], "une seule liste");
            Assert.AreEqual(0x00, image[ts + 0x02]);
            Assert.AreEqual(0x00, image[ts + 0x05], "le premier secteur de la liste est le premier du fichier");
            Assert.AreEqual(0x00, image[ts + 0x06]);

            int firstTrack = image[ts + 0x0C];
            int firstSector = image[ts + 0x0D];
            Assert.AreEqual(1, firstTrack, "l'allocation commence apres la piste 0, qui ne sert jamais");
            Assert.AreEqual(0, firstSector);

            int second = At(options, image[ts + 0x0E], image[ts + 0x0F]);
            Assert.AreEqual(0x00, image[ts + 0x10], "puis la liste s'arrete");

            // The sectors the list names hold the file, in order.
            for (int i = 0; i < 300; i++)
            {
                int at = i < 256 ? At(options, firstTrack, firstSector) : second;
                Assert.AreEqual(payload[i], image[at + i % 256],
                    "l'octet " + i + " du fichier est a sa place");
            }
        }

        [TestMethod]
        public void Le33AlloueSecteurParSecteur()
        {
            AppleDosVolumeOptions options = new AppleDosVolumeOptions(AppleDosVersion.Dos33);
            byte[] image = AppleDosVolume.Build(new byte[513], options);
            int entry = Entry(image, options);

            Assert.AreEqual(3, image[entry + 0x21], "trois secteurs pour un fichier impair");
            Assert.AreEqual(0, image[entry + 0x22]);
        }

        [TestMethod]
        public void Le32AlloueParPairesEtGardeUnSecteurDeTrop()
        {
            AppleDosVolumeOptions options = new AppleDosVolumeOptions(AppleDosVersion.Dos32);
            byte[] image = AppleDosVolume.Build(new byte[513], options);
            int entry = Entry(image, options);

            // Three sectors of file, four sectors allouees: une paire de trop.
            Assert.AreEqual(4, image[entry + 0x21], "DOS 3.2 alloue par paires de secteurs");
            Assert.AreEqual(0, image[entry + 0x22]);

            int ts = At(options, image[entry], image[entry + 1]);
            for (int i = 0; i < 4; i += 2)
            {
                int sector = image[ts + 0x0C + i * 2 + 1];
                Assert.AreEqual(0, sector % 2,
                    "le premier secteur d'une paire est pair, comme le veut DOS 3.2");
                Assert.AreEqual(sector + 1, image[ts + 0x0C + (i + 1) * 2 + 1],
                    "et le second est celui d'apres");
            }
        }

        [TestMethod]
        public void UnFichierDUneSeuleSectorResteUneSector()
        {
            AppleDosVolumeOptions options = new AppleDosVolumeOptions(AppleDosVersion.Dos33);
            byte[] image = AppleDosVolume.Build(new byte[1], options);
            int entry = Entry(image, options);

            Assert.AreEqual(1, image[entry + 0x21], "un fichier tient dans un seul secteur");
        }

        [TestMethod]
        public void UnFichierTropGrandPourUneListeEstRefuse()
        {
            AppleDosVolumeOptions options = new AppleDosVolumeOptions(AppleDosVersion.Dos33);
            byte[] huge = new byte[AppleDosVolume.TsPairs * AppleDosVolume.SectorSize + 1];

            try
            {
                AppleDosVolume.Build(huge, options);
                Assert.Fail("un fichier de 31233 octets ne tient pas dans une liste de 122 paires");
            }
            catch (ArgumentOutOfRangeException ex)
            {
                StringAssert.Contains(ex.Message, "122");
            }
        }

        [TestMethod]
        public void UnFichierVidePrendToutDeMemeUnSecteur()
        {
            AppleDosVolumeOptions options = new AppleDosVolumeOptions(AppleDosVersion.Dos33);
            byte[] image = AppleDosVolume.Build(new byte[0], options);
            int entry = Entry(image, options);

            Assert.AreEqual(1, image[entry + 0x21], "le catalogue a toujours quelque chose a nommer");
        }

        [TestMethod]
        public void LEntrelacementEstUnChoixEtNonUneProprieteDuVolume()
        {
            // DOS applique son entrelacement en parlant au lecteur, et le volume
            // ne le dit nulle part : une image ecrite en ordre logique se compare
            // directement avec ce que DOS lit sur un emulateur qui n'applique
            // rien. Une image physique, elle, a besoin de la table.
            AppleDosVolumeOptions identity = new AppleDosVolumeOptions(AppleDosVersion.Dos33);
            byte[] plain = AppleDosVolume.Build(new byte[300], identity);

            // Sans entrelacement, le catalogue est la ou le fichier dit qu'il est.
            Assert.AreEqual(0x00, plain[At(identity, 17, 15)], "la piste 17, secteur 15, porte le catalogue");
            Assert.AreEqual(1, plain[At(identity, 17, 14) + 0x0C],
                "et le secteur 14 porte la liste, dont la premiere paire commence a la piste 1");

            AppleDosVolumeOptions skewed = new AppleDosVolumeOptions(AppleDosVersion.Dos33);
            skewed.Skew = new byte[] { 0, 14, 12, 3, 1, 8, 10, 5, 7, 2, 13, 11, 4, 9, 6, 15 };
            byte[] scattered = AppleDosVolume.Build(new byte[300], skewed);

            Assert.AreEqual(plain.Length, scattered.Length, "l'image garde sa taille");
            Assert.AreEqual(plain[At(identity, 17, 14)], scattered[At(identity, 17, 9)],
                "le secteur logique 14 est parti au secteur physique 9");
            Assert.AreEqual(plain[At(identity, 17, 15)], scattered[At(identity, 17, 15)],
                "et le secteur 15 est reste sur place, la table le dit lui-meme");
        }

        [TestMethod]
        public void UnProgrammeApplesoftCommenceParSaLongueur()
        {
            byte[] program = new byte[0x100];
            byte[] file = AppleDosProgram.ApplesoftFile(program);

            Assert.AreEqual(0x00, file[0], "DOS rend un bloc a l'interpreteur, pas un programme");
            Assert.AreEqual(0x01, file[1], "et il lui dit d'abord combien il y a d'octets");
            Assert.AreEqual(0x100 + 2, file.Length);
        }

        [TestMethod]
        public void UnFichierBinaireCommenceParSonAdresse()
        {
            byte[] file = AppleDosProgram.BinaryFile(0x2003, new byte[] { 1, 2, 3 });

            Assert.AreEqual(0x03, file[0], "l'adresse de chargement est en petit boutiste");
            Assert.AreEqual(0x20, file[1]);
            Assert.AreEqual(5, file.Length);
        }

        [TestMethod]
        public void LesDeuxFormatsOntUnNomChacun()
        {
            Assert.AreEqual("dos32", new AppleDosFormat(AppleDosVersion.Dos32).Name);
            Assert.AreEqual("dos33", new AppleDosFormat(AppleDosVersion.Dos33).Name);
        }

        private static int Size(AppleDosVersion version)
        {
            return AppleDosVolume.Build(new byte[16], new AppleDosVolumeOptions(version)).Length;
        }

        private static byte[] Build(AppleDosVersion version, int length)
        {
            return AppleDosVolume.Build(new byte[length], new AppleDosVolumeOptions(version));
        }

        private static int At(byte[] image, int track, int sector)
        {
            AppleDosVolumeOptions options = new AppleDosVolumeOptions(AppleDosVersion.Dos33);
            return (track * options.SectorsPerTrack + sector) * AppleDosVolume.SectorSize;
        }

        /// <summary>Where a sector of a volume sits in an image, for either geometry.</summary>
        private static int At(AppleDosVolumeOptions options, int track, int sector)
        {
            return (track * options.SectorsPerTrack + sector) * AppleDosVolume.SectorSize;
        }

        /// <summary>Where the first catalog entry of an image starts.</summary>
        private static int Entry(byte[] image, AppleDosVolumeOptions options)
        {
            return At(options, AppleDosVolume.VtocTrack, options.FirstCatalogSector)
                + AppleDosVolume.FirstEntryAt;
        }
    }
}
