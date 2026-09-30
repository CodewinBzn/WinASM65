using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Segments;
using WinASM65.Targets;

namespace WinASM65.Tests
{
    /// <summary>
    /// T13 : la BBC Micro et son second processeur 6502.
    /// <para>
    /// La machine deduit tout d'un seul octet. Ces tests vérifient ce
    /// decodage octet par octet, parce qu'une erreur ici ne se voit pas : le
    /// fichier produit est bien un fichier, il est seulement deplace, tronque
    /// ou mal charge par la machine.
    /// </para>
    /// </summary>
    [TestClass]
    public class BbcTargetTests
    {
        private static byte[] Payload(int length, byte fill = 0xEA)
        {
            byte[] data = new byte[length];
            for (int i = 0; i < length; i++)
                data[i] = fill;
            return data;
        }

        private static byte[] Read(string path)
        {
            return File.ReadAllBytes(path);
        }

        // ------------------------------------------------------------ le type byte

        [TestMethod]
        public void UnExecutablesEcritLeTypeExecutableEtLaLongueurComplete()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "prog");
                ResolvedTarget target = new ResolvedTarget { BbcFileType = "exec" };
                Assert.IsTrue(new BbcFormat().Write(path, Payload(10), target).Success);

                byte[] file = Read(path);
                // 2 octets d'en-tete plus 4 pour l'adresse donne 16, pas 10 : la
                // longueur annoncee est celle du fichier, adresse comprise.
                int total = 10 + 4 + 2;
                Assert.AreEqual(total, file.Length, "la longueur annoncee doit etre celle du fichier");
                Assert.AreEqual(0x80 | (total >> 8), file[0], "bit 7 pose, bit 6 absent");
                Assert.AreEqual(total & 0xFF, file[1]);
                Assert.AreEqual(0x4F, file[2], "le marqueur d'adresse");
                Assert.AreEqual(0x4F, file[3]);
            }
        }

        [TestMethod]
        public void UnBinaireEcritUnEnTeteDeSixOctetsALAdresseDeChargement()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "data");
                ResolvedTarget target = new ResolvedTarget
                {
                    BbcFileType = "binary",
                    LoadAddress = 0x0E00
                };
                Assert.IsTrue(new BbcFormat().Write(path, Payload(100), target).Success);

                byte[] file = Read(path);
                int total = 100 + 6;
                Assert.AreEqual(total, file.Length);
                Assert.AreEqual(total >> 8, file[0], "un binaire n'a pas le bit executable");
                Assert.AreEqual(total & 0xFF, file[1]);
                Assert.AreEqual(0x4F, file[2], "le marqueur est dans l'en-tete, pas dans les donnees");
                Assert.AreEqual(0x4F, file[3]);
                Assert.AreEqual(0x00, file[4], "adresse de chargement, octet bas");
                Assert.AreEqual(0x0E, file[5], "adresse de chargement, octet haut");
                Assert.AreEqual(0xEA, file[6], "les donnees commencent apres l'adresse");
            }
        }

        [TestMethod]
        public void UnFichierTexteEstUnBareFFSansLongueurNiAdresse()
        {
            // La machine lit jusqu'a la fin du fichier : ajouter une longueur ou
            // une adresse les afficherait comme du texte.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "text");
                ResolvedTarget target = new ResolvedTarget { BbcFileType = "text" };
                Assert.IsTrue(new BbcFormat().Write(path, Payload(20, 0x41), target).Success);

                byte[] file = Read(path);
                Assert.AreEqual(21, file.Length);
                Assert.AreEqual(0xFF, file[0]);
                Assert.AreEqual(0x41, file[1], "le texte suit immediatement");
                Assert.AreEqual(0x41, file[20]);
            }
        }

        [TestMethod]
        public void UnCodeQuiPorteDejaLeMarqueurNEscritPasUneDeuxiemeAdresse()
        {
            // Un source qui ecrit lui-meme $4F $4F et son adresse a la main
            // produirait un fichier dont chaque instruction est decalee de
            // quatre octets, toujours parfaitement valide en apparence.
            byte[] code = new byte[] { 0x4F, 0x4F, 0x00, 0xC0, 0xA9, 0x01, 0x60 };

            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "prog");
                ResolvedTarget target = new ResolvedTarget
                {
                    BbcFileType = "exec",
                    LoadAddress = 0x0E00
                };
                Assert.IsTrue(new BbcFormat().Write(path, code, target).Success);

                byte[] file = Read(path);
                Assert.AreEqual(2 + code.Length, file.Length, "aucune adresse ajoutee");
                Assert.AreEqual(0x80, file[0], "le type n'a pas d'entete de plus");
                Assert.AreEqual(0x4F, file[2]);
                Assert.AreEqual(0x00, file[4], "adresse du source, octet bas");
                Assert.AreEqual(0xC0, file[5], "et haut : celle du source, pas celle de la cible");
            }
        }

        [TestMethod]
        public void LOrigineDuSourcePrimeSurLAdresseDeChargement()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "prog");
                ResolvedTarget target = new ResolvedTarget
                {
                    BbcFileType = "binary",
                    LoadAddress = 0x0E00,
                    OriginAddress = 0x1900
                };
                Assert.IsTrue(new BbcFormat().Write(path, Payload(4), target).Success);

                byte[] file = Read(path);
                Assert.AreEqual(0x00, file[4]);
                Assert.AreEqual(0x19, file[5], "le .org fait foi");
            }
        }

        // --------------------------------------------------------------- les bornes

        [TestMethod]
        public void UnBinaireTropGrandEstRefuseEtNonTronque()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "huge");
                ResolvedTarget target = new ResolvedTarget { BbcFileType = "binary" };
                OperationResult result = new BbcFormat()
                    .Write(path, Payload(BbcFormat.MaxBinaryPayload + 1), target);

                Assert.IsFalse(result.Success);
                Assert.IsFalse(File.Exists(path), "rien n'est ecrit quand l'ecriture echoue");
                StringAssert.Contains(Message(result), "32767", "la limite est nommee");
            }
        }

        [TestMethod]
        public void UnExecutableNeDepassePasQuatorzeBitsDeLongueur()
        {
            // Le bit 6 de l'octet de type ne porte pas de longueur : c'est
            // pourquoi un executable s'arrete a 16 Ko la ou un binaire va a 32.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "huge");
                ResolvedTarget target = new ResolvedTarget { BbcFileType = "exec" };
                OperationResult result = new BbcFormat()
                    .Write(path, Payload(BbcFormat.MaxExecPayload + 1), target);

                Assert.IsFalse(result.Success);
                StringAssert.Contains(Message(result), "16383");
            }
        }

        [TestMethod]
        public void LaPlusGrandeChargeAcceptablePasse()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "max");
                ResolvedTarget target = new ResolvedTarget { BbcFileType = "binary" };
                Assert.IsTrue(new BbcFormat().Write(path, Payload(BbcFormat.MaxBinaryPayload), target).Success);

                byte[] file = Read(path);
                Assert.AreEqual(0x7FFF, file.Length);
                Assert.AreEqual(0x7F, file[0], "le type binaire maximal");
            }
        }

        // ------------------------------------------------------------ second CPU

        [TestMethod]
        public void UnFichierTubeEstLaChargeEtRienDAutre()
        {
            // OSLOAD ne lit aucun en-tete : un octet de plus serait execute
            // comme une instruction.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "tube");
                byte[] code = Payload(64);
                Assert.IsTrue(new TubeFormat().Write(path, code, new ResolvedTarget()).Success);

                CollectionAssert.AreEqual(code, Read(path),
                    "le fichier est la charge, octet pour octet");
            }
        }

        [TestMethod]
        public void UnFichierTubeTropGrandPourLaRamEstRefuse()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "tube");
                OperationResult result = new TubeFormat()
                    .Write(path, Payload(TubeFormat.SecondProcessorRamSize + 1), new ResolvedTarget());

                Assert.IsFalse(result.Success);
                StringAssert.Contains(Message(result), "OSLOAD", "le message nomme le service qui echoue");
            }
        }

        // ---------------------------------------------------------------- catalogue

        [TestMethod]
        public void LaBbcEcritUnFichierBBcEtLeSecondProcesseurEcritUnFichierOSLoad()
        {
            ResolvedTarget bbc;
            Assert.IsTrue(SystemCatalog.TryGet("bbc", out bbc));
            Assert.AreEqual("bbc", bbc.FormatName, "la BBC a son propre format de fichier");
            Assert.AreEqual("exec", bbc.BbcFileType);

            ResolvedTarget tube;
            Assert.IsTrue(SystemCatalog.TryGet("tube", out tube));
            Assert.AreEqual("tube", tube.FormatName, "OSLOAD n'a pas d'en-tete");
            Assert.AreEqual(0x2000, tube.LoadAddress.Value, "l'adresse de chargement est celle du service");
        }

        [TestMethod]
        public void LesDeuxProcesseursNontPasLaMemeTableDeSymboles()
        {
            // C'est la distinction qui fait qu'ils sont deux cibles et non une
            // seule cible avec deux adresses.
            ResolvedTarget bbc;
            ResolvedTarget tube;
            Assert.IsTrue(SystemCatalog.TryGet("bbc", out bbc));
            Assert.IsTrue(SystemCatalog.TryGet("tube", out tube));

            Assert.IsTrue(bbc.HardwareSymbols.ContainsKey("OSWRCH"),
                "le program principal appelle le systeme d'exploitation");
            Assert.IsFalse(tube.HardwareSymbols.ContainsKey("OSWRCH"),
                "un second processeur n'a pas d'appel systeme : il n'a pas de systeme");

            foreach (string name in bbc.HardwareSymbols.Keys)
            {
                Assert.IsFalse(tube.HardwareSymbols.ContainsKey(name),
                    "'" + name + "' appartient a la machine principale, pas au second processeur");
            }
        }

        [TestMethod]
        public void LeSecondProcesseurNADeclareQueSaCarteMemoire()
        {
            // Les registres du tube appartiennent au processeur principal : les
            // nommer ici enverrait un programme vers du materiel qu'il ne voit pas.
            ResolvedTarget tube;
            Assert.IsTrue(SystemCatalog.TryGet("tube", out tube));

            Assert.AreEqual(0x0200, tube.HardwareSymbols["TUBERAM"]);
            Assert.AreEqual(0x7FFF, tube.HardwareSymbols["TUBERAMEND"]);
            Assert.AreEqual(0x8000, tube.HardwareSymbols["TUBEROM"]);
            foreach (string name in tube.HardwareSymbols.Keys)
            {
                Assert.IsTrue(name.StartsWith("TUBE", StringComparison.OrdinalIgnoreCase),
                    "'" + name + "' ne devrait pas etre un registre de la machine principale");
            }
        }

        [TestMethod]
        public void LesAliasDeLaBbcPointentSurLaMemeCible()
        {
            ResolvedTarget a;
            ResolvedTarget b;
            ResolvedTarget c;
            Assert.IsTrue(SystemCatalog.TryGet("bbc", out a));
            Assert.IsTrue(SystemCatalog.TryGet("bbcmicro", out b));
            Assert.IsTrue(SystemCatalog.TryGet("BBC-Micro", out c), "la normalisation ignore casse et separateurs");

            Assert.AreEqual(a.FormatName, b.FormatName);
            Assert.AreEqual(a.FormatName, c.FormatName);
        }

        [TestMethod]
        public void UnFormatBbcInconnuRetombeSurExecEtNonSurUneErreur()
        {
            // "exe" et "exec" ne sont pas le meme mot ; refuser le premier ferait
            // echouer un fichier qui ne peut pas etre autre chose qu'un executable.
            Assert.AreEqual("exec", BbcFormat.NormalizeKind("exe"));
            Assert.AreEqual("exec", BbcFormat.NormalizeKind(null));
            Assert.AreEqual("text", BbcFormat.NormalizeKind(" TEXT "));
            Assert.AreEqual("binary", BbcFormat.NormalizeKind("Binary"));
        }

        [TestMethod]
        public void LaConfigurationPeutChoisirLeGenreDeFichierBbc()
        {
            TargetConf config = new TargetConf { System = "bbc", BbcFileType = "binary" };
            ResolvedTarget target = TargetResolver.Resolve(config, null, null, null);

            Assert.AreEqual("binary", target.BbcFileType);
        }

        private static string Message(OperationResult result)
        {
            string text = string.Empty;
            for (int i = 0; i < result.Diagnostics.Count; i++)
                text += result.Diagnostics[i].Message;
            return text;
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }

            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "WinASM65Bbc_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
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
