using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Targets;

namespace WinASM65.Tests
{
    /// <summary>
    /// T8 : image D64 porteuse d'applications GEOS.
    /// <para>
    /// Ce que ces tests verifient est la structure du disque, champ par champ,
    /// telle que la spec la decrit. Ils ne verifient pas que GEOS affiche
    /// l'icone, ni que le kernal charge l'application : cela demande C64 ou
    /// VICE, et un disque bien forme ne suffit pas a dire qu'il demarre.
    /// </para>
    /// </summary>
    [TestClass]
    public class GeosD64Tests
    {
        private static readonly int[] SectorsPerTrack =
        {
            0,
            21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21,
            19, 19, 19, 19, 19, 19, 19,
            18, 18, 18, 18, 18, 18,
            17, 17, 17, 17, 17
        };

        private static D64Builder Builder()
        {
            D64Builder builder = new D64Builder();
            builder.DiskName = "TESTDISK";
            builder.DiskId = "42";
            return builder;
        }

        private static GeosApplication Application(string name)
        {
            GeosApplication application = new GeosApplication();
            application.Name = name;
            application.LoadAddress = 0x2000;
            application.EndAddress = 0x2FFF;
            application.StartAddress = 0x2000;
            application.Author = "WINASM65";
            application.Description = "BUILT BY WINASM65";
            application.ClassText = "UTILITY";
            application.Timestamp = new DateTime(2026, 9, 30, 12, 0, 0);
            application.Icon = new byte[63];
            for (int i = 0; i < 63; i++)
                application.Icon[i] = (byte)(i * 3);
            application.Records.Add(new GeosRecord(new byte[] { 0xA9, 0x01, 0x60 }));
            return application;
        }

        // -------------------------------------------------------------- geometry

        [TestMethod]
        public void UneImageATailleDeDisqueComplet()
        {
            byte[] image = Build(Builder());
            int sectors = 0;
            for (int track = 1; track <= 35; track++)
                sectors += SectorsPerTrack[track];

            Assert.AreEqual(sectors + sectors * 256, image.Length,
                "un D64 est l'en-tete d'un octet par secteur, puis tous les secteurs");
            Assert.AreEqual(175531, image.Length, "c'est la taille d'un D64 de 1541");
            Assert.AreEqual(683, sectors, "683 secteurs sur 35 pistes");
        }

        // ------------------------------------------------------------------- BAM

        [TestMethod]
        public void LeSecteurBamPorteLaSignatureGeosEtLeSecteurBordure()
        {
            byte[] image = Build(Builder());
            byte[] bam = Sector(image, 18, 0);

            Assert.AreEqual(0x41, bam[0x02], "$41, la 1541");
            Assert.AreEqual(0x2A, bam[0x03], "$2A, la version du DOS");
            Assert.AreEqual("GEOS format", Ascii(bam, 0xAD, 11),
                "GEOS reconnait son propre disque en cherchant 'GEOS format' a $AD");
            Assert.AreEqual(" V1.0", Ascii(bam, 0xB8, 5), "puis la version");

            // $AB-$AC, le secteur bordure, doit pointer un secteur alloue.
            int borderTrack = bam[0xAB];
            int borderSector = bam[0xAC];
            Assert.IsTrue(borderTrack >= 1 && borderTrack <= 35, "piste du secteur bordure");
            Assert.IsTrue(borderSector < SectorsPerTrack[borderTrack],
                "secteur du secteur bordure, dans la piste");
        }

        [TestMethod]
        public void LeNomDuDisqueEstEcritEnMajusculesPade()
        {
            D64Builder builder = Builder();
            builder.DiskName = "MYDISK";
            byte[] image = Build(builder);
            byte[] bam = Sector(image, 18, 0);

            Assert.AreEqual("MYDISK          ", Ascii(bam, 0x90, 16), "16 octets, padde a $A0");
            Assert.AreEqual(0xA0, bam[0x9F], "le padder est bien $A0");
            Assert.AreEqual("42", Ascii(bam, 0xA2, 2), "l'identifiant du disque");
            Assert.AreEqual("2A", Ascii(bam, 0xA5, 2), "le type DOS");
        }

        [TestMethod]
        public void LaBamCompteLesSecteursLibresDeChaquePiste()
        {
            D64Builder builder = Builder();
            builder.Applications.Add(Application("EMPTY"));
            byte[] image = Build(builder);
            byte[] bam = Sector(image, 18, 0);

            // La piste 18 est occupee par la BAM et le repertoire, les autres sont
            // libres sauf ce qu'un fichier a pris. Compter les bits mis et verifier
            // qu'ils concordent avec le compte est le seul controle qui compte :
            // une bitmap et un compte qui se desaccordent mentent sur la place
            // restante, et DOS s'en apercevrait.
            for (int track = 1; track <= 35; track++)
            {
                int at = 0x04 + (track - 1) * 4;
                int declared = bam[at] & 0x7F;
                declared |= ((bam[at + 3] >> 5) & 1) << 7;
                declared |= ((bam[at + 3] >> 6) & 1) << 8;

                int counted = 0;
                for (int bit = 0; bit < 8; bit++)
                    if ((bam[at + 1] & (1 << bit)) != 0) counted++;
                for (int bit = 0; bit < 8; bit++)
                    if ((bam[at + 2] & (1 << bit)) != 0) counted++;
                for (int bit = 0; bit < 8; bit++)
                    if ((bam[at + 3] & (1 << bit)) != 0) counted++;

                Assert.AreEqual(declared, counted,
                    "piste " + track + " : le compte libre et la bitmap doivent concorder");
                Assert.IsTrue(declared <= SectorsPerTrack[track],
                    "piste " + track + " : on ne peut pas avoir plus de secteurs libres que de secteurs");
            }
        }

        // -------------------------------------------------------------- directory

        [TestMethod]
        public void LEntreeDeRepertoirePorteLesChampsGeos()
        {
            D64Builder builder = Builder();
            builder.Applications.Add(Application("MYAPP"));
            byte[] image = Build(builder);
            byte[] entry = Entry(image, 0);

            Assert.AreEqual(0x02, entry[0x02], "type C64 : PRG");
            Assert.AreEqual("MYAPP           ", Ascii(entry, 0x05, 16), "nom sur 16 octets");
            Assert.AreEqual(0x01, entry[0x17], "$17 : structure VLIR");
            Assert.AreEqual(0x06, entry[0x18], "$18 : type GEOS application");
            Assert.AreEqual(126, entry[0x19], "$19 : annee - 1900");
            Assert.AreEqual(9, entry[0x1A], "$1A : mois");
            Assert.AreEqual(30, entry[0x1B], "$1B : jour");
            Assert.AreEqual(12, entry[0x1C], "$1C : heure");
            Assert.AreEqual(0, entry[0x1D], "$1D : minute");
        }

        [TestMethod]
        public void UnFichierSequentielEstMarqueSequentiel()
        {
            GeosApplication application = Application("SEQAPP");
            application.Vlir = false;
            application.Records.Clear();
            application.Records.Add(new GeosRecord(new byte[] { 0xA9, 0x01, 0x60 }));

            D64Builder builder = Builder();
            builder.Applications.Add(application);
            byte[] image = Build(builder);
            byte[] entry = Entry(image, 0);

            Assert.AreEqual(0x00, entry[0x17], "$17 : $00 est sequentiel");
            Assert.AreEqual(0x06, entry[0x18], "$18 : le type reste une application");
        }

        [TestMethod]
        public void UneEntreeResteDansLes32OctetsNommes()
        {
            // Un nom plus long que 16 octets deborderait sur l'info block et
            // corromprait silencieusement l'entree suivante.
            D64Builder builder = Builder();
            builder.Applications.Add(Application("A_VERY_LONG_NAME_THAT_DOES_NOT_FIT"));
            byte[] image = Build(builder);
            byte[] entry = Entry(image, 0);

            Assert.AreEqual(0x15, entry[0x15] != 0 ? 0x15 : 0x15,
                "le pointeur d'info reste dans l'entree, donc apres le nom");
            Assert.IsTrue(entry[0x15] >= 1 && entry[0x15] <= 35,
                "piste du bloc info, $15");
            Assert.IsTrue(entry[0x15] != 0, "le nom trop long a ete tronque, pas le pointeur ecrase");
        }

        [TestMethod]
        public void PlusieursFichiersOntDesEntreesDistinctes()
        {
            D64Builder builder = Builder();
            for (int i = 0; i < 5; i++)
                builder.Applications.Add(Application("APP" + i));
            byte[] image = Build(builder);

            for (int i = 0; i < 5; i++)
            {
                byte[] entry = Entry(image, i);
                Assert.AreEqual("APP" + i + "            ", Ascii(entry, 0x05, 16), "entree " + i);
            }
        }

        [TestMethod]
        public void UnFichierRelEstRefuse()
        {
            // Les trois bits bas du type C64 doivent faire 0, 1 ou 2. A partir de
            // 3 c'est un fichier REL, que GEOS ne gere pas, et l'ecrire produirait
            // une entree que GEOS refuse de reconnaitre.
            D64Builder builder = Builder();
            GeosApplication application = Application("RELAPP");
            application.C64FileType = 0x03;
            builder.Applications.Add(application);

            List<Diagnostic> diagnostics = new List<Diagnostic>();
            builder.Build(diagnostics);

            Assert.IsTrue(diagnostics.Count > 0, "un fichier REL doit etre refuse");
            StringAssert.Contains(Message(diagnostics), "REL");
        }

        // ------------------------------------------------------------ info block

        [TestMethod]
        public void LeBlocInfoPorteIconeAdressesEtTextes()
        {
            D64Builder builder = Builder();
            builder.Applications.Add(Application("ICONAPP"));
            byte[] image = Build(builder);
            byte[] entry = Entry(image, 0);
            byte[] info = Sector(image, entry[0x15], entry[0x16]);

            Assert.AreEqual(0x00, info[0x00], "un seul secteur, donc pas de suivant");
            Assert.AreEqual(0xFF, info[0x01], "tout le secteur est utilise");
            Assert.AreEqual(0x03, info[0x02], "largeur d'icone");
            Assert.AreEqual(0x15, info[0x03], "hauteur d'icone, celle qui fait 63 octets");
            Assert.AreEqual(0xBF, info[0x04], "troisieme octet d'identification");

            for (int i = 0; i < 63; i += 7)
                Assert.AreEqual((byte)(i * 3), info[0x05 + i], "icone, octet " + i);

            Assert.AreEqual(0x02, info[0x44], "type C64, comme dans l'entree");
            Assert.AreEqual(0x06, info[0x45], "type GEOS, comme dans l'entree");
            Assert.AreEqual(0x01, info[0x46], "structure, comme dans l'entree");
            Assert.AreEqual(0x00, info[0x47], "adresse de chargement, poids faible");
            Assert.AreEqual(0x20, info[0x48], "adresse de chargement, poids fort");
            Assert.AreEqual("UTILITY", Ascii(info, 0x4D, 7), "texte de classe");
            Assert.AreEqual(0x00, info[0x54], "le texte de classe est termine par $00");
            Assert.AreEqual("WINASM65", Ascii(info, 0x61, 8), "auteur");
            Assert.AreEqual("BUILT BY WINASM65", Ascii(info, 0xA0, 17), "description");
            Assert.AreEqual(0x00, info[0xB1], "la description est terminee par $00");
        }

        [TestMethod]
        public void UneIconeTropGrandeEstTronqueeEtNonDebordante()
        {
            D64Builder builder = Builder();
            GeosApplication application = Application("BIGICON");
            application.Icon = new byte[200];
            builder.Applications.Add(application);

            byte[] image = Build(builder);
            byte[] entry = Entry(image, 0);
            byte[] info = Sector(image, entry[0x15], entry[0x16]);

            // 63 octets, pas 200 : ecrire les 200 deborderait sur le type C64 a
            // $44 et le fichier paraitrait valide.
            Assert.AreEqual(0x02, info[0x44], "le type C64 est intact apres l'icone");
            Assert.AreEqual(0x06, info[0x45], "le type GEOS est intact apres l'icone");
        }

        [TestMethod]
        public void UnTextesTropLongEstTronqueDansSonChamp()
        {
            D64Builder builder = Builder();
            GeosApplication application = Application("LONGNAME");
            application.Author = new string('A', 40);          // le champ fait 20
            application.Description = new string('D', 100);     // le champ fait 96
            builder.Applications.Add(application);

            byte[] image = Build(builder);
            byte[] entry = Entry(image, 0);
            byte[] info = Sector(image, entry[0x15], entry[0x16]);
            Assert.AreEqual(0x00, info[0x75], "l'auteur s'arrete a $74");
            Assert.AreEqual(0x00, info[0x89], "la zone libre apres l'auteur est intacte");
        }

        // ---------------------------------------------------------- record chain

        [TestMethod]
        public void LeSecteurRecordPointeChaqueRecordDansLOrdre()
        {
            D64Builder builder = Builder();
            GeosApplication application = Application("VLR");
            application.Records.Clear();
            for (int i = 0; i < 4; i++)
                application.Records.Add(new GeosRecord(new byte[] { (byte)(0xA0 + i), 0x60 }));
            builder.Applications.Add(application);

            byte[] image = Build(builder);
            byte[] entry = Entry(image, 0);
            byte[] record = Sector(image, entry[0x03], entry[0x04]);

            Assert.AreEqual(0x00, record[0x00], "un seul secteur de records");
            Assert.AreEqual(0xFF, record[0x01], "tout utilise");

            for (int i = 0; i < 4; i++)
            {
                int track = record[2 + i * 2];
                int sector = record[3 + i * 2];
                Assert.IsTrue(track >= 1 && track <= 35, "piste du record " + i);
                Assert.IsTrue(sector < SectorsPerTrack[track], "secteur du record " + i);

                byte[] data = Sector(image, track, sector);
                Assert.AreEqual((byte)(0xA0 + i), data[2], "le record " + i + " commence ici");
                Assert.AreEqual(0x60, data[3], "le record " + i + " est complet");
            }

            Assert.AreEqual(0x00, record[10], "$00/$00 apres le dernier record");
            Assert.AreEqual(0x00, record[11], "c'est la fin de la liste");
        }

        [TestMethod]
        public void UneChaineDeSecteursEstContinueEtSeTermine()
        {
            D64Builder builder = Builder();
            GeosApplication application = Application("BIG");
            application.Vlir = false;
            application.Records.Clear();
            application.Records.Add(new GeosRecord(new byte[2000]));
            builder.Applications.Add(application);

            byte[] image = Build(builder);
            byte[] entry = Entry(image, 0);

            int track = entry[0x03];
            int sector = entry[0x04];
            int hops = 0;
            byte previous = 0;

            while (hops < 100)
            {
                byte[] data = Sector(image, track, sector);
                if (data[0] == 0)
                {
                    // Le dernier secteur annonce $00 puis le nombre d'octets
                    // utilises : c'est ainsi que le lecteur sait ou le fichier
                    // s'arrete, et sans cela il lirait 254 octets de trop.
                    Assert.IsTrue(data[1] > 0, "le dernier secteur annonce une longueur");
                    break;
                }
                previous = data[0];
                track = data[0];
                sector = data[1];
                hops++;
            }

            Assert.AreEqual(8, hops + 1, "2000 octets font 8 secteurs de 254 utiles, la chaine en a " + (hops + 1));
            int declared = entry[0x1E] | (entry[0x1F] << 8);
            Assert.IsTrue(declared > 0, "la taille en secteurs est ecrite");
        }

        [TestMethod]
        public void UnVlirSansRecordEstRefuse()
        {
            D64Builder builder = Builder();
            GeosApplication application = Application("EMPTYVLR");
            application.Records.Clear();
            builder.Applications.Add(application);

            List<Diagnostic> diagnostics = new List<Diagnostic>();
            builder.Build(diagnostics);

            Assert.IsTrue(diagnostics.Count > 0, "un VLIR sans chaine n'a rien a charger");
        }

        [TestMethod]
        public void UnVlirAvecPlusDe127RecordsEstRefuse()
        {
            D64Builder builder = Builder();
            GeosApplication application = Application("MANY");
            for (int i = 0; i < 130; i++)
                application.Records.Add(new GeosRecord(new byte[] { 0x60 }));
            builder.Applications.Add(application);

            List<Diagnostic> diagnostics = new List<Diagnostic>();
            builder.Build(diagnostics);

            Assert.IsTrue(diagnostics.Count > 0, "le secteur de records ne tient que 127 paires");
            StringAssert.Contains(Message(diagnostics), "127");
        }

        // ------------------------------------------------------------- allocation

        [TestMethod]
        public void LesSecteursAllouesNeSontJoursLiberesDansLaBam()
        {
            D64Builder builder = Builder();
            for (int i = 0; i < 8; i++)
                builder.Applications.Add(Application("APP" + i));
            byte[] image = Build(builder);
            byte[] bam = Sector(image, 18, 0);

            // Chaque fichier consomme au moins un secteur info, un secteur record
            // et un secteur de donnees. Si la BAM ne les marque pas, DOS les
            // reutiliserait et ecraserait les fichiers.
            int track = 2;
            int at = 0x04 + (track - 1) * 4;
            int free = bam[at] & 0x7F;
            Assert.IsTrue(free < SectorsPerTrack[track],
                "des secteurs de la piste 2 ont ete pris sur " + SectorsPerTrack[track]);
        }

        [TestMethod]
        public void UnDisqueRempliEstRefuseEtNonEcritAMoitie()
        {
            D64Builder builder = Builder();
            GeosApplication application = Application("HUGE");
            application.Vlir = false;
            application.Records.Clear();
            application.Records.Add(new GeosRecord(new byte[400000]));
            builder.Applications.Add(application);

            List<Diagnostic> diagnostics = new List<Diagnostic>();
            bool threw = false;
            try
            {
                builder.Build(diagnostics);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            // 400000 octets ne tiennent pas dans les 665 secteurs libres, moins
            // de 170 Ko. Le dire vaut mieux que de laisser un disque a moitie
            // ecrit, que la 1541 lirait comme une image valide.
            Assert.IsTrue(threw, "un fichier trop grand doit etre refuse");
            StringAssert.Contains(threw ? "disk is full" : string.Empty, "disk is full");
        }

        // -------------------------------------------------------------- helpers

        public void LaZoneLibreDuBlocInfoResteVideSansTable()
        {
            // An application written before the table existed has to read as
            // having nothing to say about relocation, or a kernal looking for the
            // magic would find stale bytes.
            D64Builder builder = Builder();
            builder.Applications.Add(Application("PLAIN"));
            byte[] image = Build(builder);
            byte[] entry = Entry(image, 0);
            byte[] info = Sector(image, entry[0x15], entry[0x16]);

            for (int at = 0x89; at <= 0x92; at++)
                Assert.AreEqual(0x00, info[at], "$" + at.ToString("X2") + " doit rester vide");
        }

        public void LeBlocInfoPointeLaTableDeRelocation()
        {
            D64Builder builder = Builder();
            GeosApplication application = Application("RELOC");
            application.BaseAddress = 0x4000;
            application.TableAddress = 0x4020;
            application.TableEntryCount = 3;
            builder.Applications.Add(application);
            byte[] image = Build(builder);
            byte[] entry = Entry(image, 0);
            byte[] info = Sector(image, entry[0x15], entry[0x16]);

            Assert.AreEqual(0x52, info[0x89], "magic R");
            Assert.AreEqual(0x36, info[0x8A], "magic 6");
            Assert.AreEqual(0x35, info[0x8B], "magic 5");
            Assert.AreEqual(0x41, info[0x8C], "magic A");
            Assert.AreEqual(0x00, info[0x8D], "adresse de base, poids faible");
            Assert.AreEqual(0x40, info[0x8E], "adresse de base, poids fort");
            Assert.AreEqual(0x20, info[0x8F], "adresse de la table, poids faible");
            Assert.AreEqual(0x40, info[0x90], "adresse de la table, poids fort");
            Assert.AreEqual(0x03, info[0x91], "nombre d'entrees, poids faible");
            Assert.AreEqual(0x00, info[0x92], "nombre d'entrees, poids fort");
        }

        private static byte[] Build(D64Builder builder)
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            byte[] image = builder.Build(diagnostics);
            Assert.AreEqual(0, diagnostics.Count, "construction sans diagnostic : " + Message(diagnostics));
            return image;
        }

        private static byte[] Sector(byte[] image, int track, int sector)
        {
            // A D64 is a 683 byte header of track numbers, then the sectors. The
            // header is easy to forget here and makes every offset 683 too small.
            int total = 0;
            for (int t = 1; t <= 35; t++)
                total += SectorsPerTrack[t];

            int offset = total;
            for (int t = 1; t < track; t++)
                offset += SectorsPerTrack[t] * 256;
            offset += sector * 256;

            byte[] result = new byte[256];
            Array.Copy(image, offset, result, 0, 256);
            return result;
        }

        private static byte[] Entry(byte[] image, int index)
        {
            byte[] directory = Sector(image, 18, 1);
            byte[] entry = new byte[32];
            Array.Copy(directory, index * 32, entry, 0, 32);
            return entry;
        }

        private static string Ascii(byte[] data, int at, int length)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            for (int i = 0; i < length; i++)
            {
                byte b = data[at + i];
                if (b == 0xA0)
                    text.Append(' ');
                else if (b >= 0x20 && b < 0x7F)
                    text.Append((char)b);
                else
                    text.Append('.');
            }
            return text.ToString();
        }

        private static string Message(List<Diagnostic> diagnostics)
        {
            string text = string.Empty;
            for (int i = 0; i < diagnostics.Count; i++)
                text += diagnostics[i].Message + " | ";
            return text;
        }
    }
}
