using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Targets;

namespace WinASM65.Tests
{
    /// <summary>
    /// T13 : la BBC Micro et son second processeur 6502.
    /// <para>
    /// Ces tests portent sur le fichier produit, pas sur ce qu'un
    /// emulateur en fera. Ce qui est verifie ici, c'est la structure que la
    /// machine reconnait ; le fait qu'elle l'execute reste a verifier sur une
    /// machine ou dans VICE.
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

        // ---------------------------------------------------------- code header

        [TestMethod]
        public void UnFichierBBCCommenceParUnBranchementAuCode()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "prog");
                Assert.IsTrue(new BbcFormat().Write(path, Payload(8), new ResolvedTarget()).Success);

                byte[] file = Read(path);
                Assert.AreEqual(0x4C, file[0], "le client entre sur un branchement inconditionnel");

                int entry = file[1] | (file[2] << 8);
                Assert.AreEqual(file.Length - 8, entry,
                    "l'entree doit viser le premier octet de code, a la fin de l'en-tete");
                Assert.AreEqual(0xEA, file[entry], "et ce doit etre le debut du code");
            }
        }

        [TestMethod]
        public void LOctetDeTypeAnnonceLeProcesseurEtLaPresenceDUneAdresse()
        {
            // Le client refuse de lancer un fichier dont le type annonce un autre
            // processeur, et interprets le bit 5 : absent, il charge a $8000.
            // Ecrire le mauvais bit ne produit pas un fichier invalide, il
            // produit un fichier charge au mauvais endroit.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "prog");
                Assert.IsTrue(new BbcFormat().Write(path, Payload(4), new ResolvedTarget()).Success);

                byte[] file = Read(path);
                Assert.AreEqual(0x60 | BbcFormat.Cpu6502, file[6]);
                Assert.AreEqual(0, file[6] & 0x80, "un fichier n'a pas de service, un ROM image si");
                Assert.AreEqual(0x40, file[6] & 0x40, "bit 6 : contient du code");
                Assert.AreEqual(0x20, file[6] & 0x20, "bit 5 : adresse de chargement presente");
                Assert.AreEqual(BbcFormat.Cpu6502, file[6] & 0x0F, "nibble bas : processeur");
            }
        }

        [TestMethod]
        public void LOctet7MeneseAuMarqueurDeCopyright()
        {
            // C'est tout ce qui distingue un fichier avec en-tete du code nu : le
            // client cherche un zero suivi de "(C)". Un marqueur mal place rend le
            // fichier inexistant aux yeux du client, sans le moindre avertissement.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "prog");
                ResolvedTarget target = new ResolvedTarget { Title = "1.00 (test)" };
                Assert.IsTrue(new BbcFormat().Write(path, Payload(4), target).Success);

                byte[] file = Read(path);
                int at = file[7];
                Assert.AreEqual(0, file[at], "le marqueur est un octet nul");
                Assert.AreEqual('(', (char)file[at + 1]);
                Assert.AreEqual('C', (char)file[at + 2]);
                Assert.AreEqual(')', (char)file[at + 3]);
            }
        }

        [TestMethod]
        public void LADresseDeChargementEstEcritesurQuatreOctetsApressLeCopyright()
        {
            // Le client lit 32 bits, meme sur un 6502 16 bits : les octets hauts
            // servent a distinguer la memoire du second processeur de celle du
            // processeur principal, et c'est le seul endroit ou cela s'ecrit.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "prog");
                ResolvedTarget target = new ResolvedTarget { OriginAddress = 0x2000 };
                Assert.IsTrue(new BbcFormat().Write(path, Payload(4), target).Success);

                byte[] file = Read(path);
                int at = file[7] + 1;
                while (file[at] != 0)
                    at++;
                at++;

                Assert.AreEqual(0x00, file[at], "octet bas");
                Assert.AreEqual(0x20, file[at + 1], "octet haut");
                Assert.AreEqual(0x00, file[at + 2], "octets hauts nuls : memoire du langage");
                Assert.AreEqual(0x00, file[at + 3]);
                Assert.AreEqual(at + 4, file[1] | (file[2] << 8), "le code suit immediatement");
            }
        }

        [TestMethod]
        public void UnCodeQuiPorteDejaSonBranchementNEstPasDecale()
        {
            // Un source qui ecrit son propre point d'entree donne un fichier
            // valide mais decale de la taille de l'en-tete, et dont l'adresse
            // publiee n'est pas celle du source. Le client entrerait dans le
            // milieu du code sans rien signaler.
            byte[] code = new byte[] { 0xA9, 0x01, 0x8D, 0x00, 0x20, 0x60 };

            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "prog");
                byte[] withStub = new byte[3 + code.Length];
                withStub[0] = 0x4C;
                withStub[1] = 0x00;
                withStub[2] = 0x20;
                System.Array.Copy(code, 0, withStub, 3, code.Length);

                Assert.IsTrue(new BbcFormat().Write(path, withStub, new ResolvedTarget()).Success);

                byte[] file = Read(path);
                int entry = file[1] | (file[2] << 8);
                for (int i = 0; i < code.Length; i++)
                    Assert.AreEqual(code[i], file[entry + i], "octet " + i + " du code");

                int at = file[7] + 1;
                while (file[at] != 0)
                    at++;
                at++;
                Assert.AreEqual(0x20, file[at + 1], "l'adresse du source est conservee");
            }
        }

        [TestMethod]
        public void UnFichierTexteEstUnBareFFSansRienDautre()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "text");
                ResolvedTarget target = new ResolvedTarget { BbcFileType = "text" };
                Assert.IsTrue(new BbcFormat().Write(path, Payload(20, 0x41), target).Success);

                byte[] file = Read(path);
                Assert.AreEqual(21, file.Length);
                Assert.AreEqual(0xFF, file[0]);
                Assert.AreEqual(0x41, file[1], "le texte suit immediatement");
            }
        }

        [TestMethod]
        public void UnGenreDeFichierInconnuRetombeSurCode()
        {
            Assert.AreEqual("code", BbcFormat.NormalizeKind(null));
            Assert.AreEqual("code", BbcFormat.NormalizeKind("exec"), "le vieux nom n'est plus un genre");
            Assert.AreEqual("flat", BbcFormat.NormalizeKind("raw"));
            Assert.AreEqual("text", BbcFormat.NormalizeKind(" TEXT "));
        }

        // ------------------------------------------------------------ second CPU

        [TestMethod]
        public void UnFichierTubeEstLaChargeEtRienDAutre()
        {
            // Un loader qui recoit un bloc en connait deja la longueur et
            // l'adresse : un octet de plus serait execute comme une instruction.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "tube");
                byte[] code = Payload(64);
                Assert.IsTrue(new TubeFormat().Write(path, code, new ResolvedTarget()).Success);

                CollectionAssert.AreEqual(code, Read(path));
            }
        }

        [TestMethod]
        public void LeSecondProcesseurNADeclareQueCeQuiEstDejaPris()
        {
            // Aucun E/S n'est visible : les registres du tube sont ceux du
            // processeur principal, et les nommer ici enverrait un programme
            // lire du materiel qui n'est pas dans son espace d'adressage.
            ResolvedTarget tube;
            Assert.IsTrue(SystemCatalog.TryGet("tube", out tube));

            Assert.IsTrue(tube.HardwareSymbols.ContainsKey("HIMEM"), "la borne haute du BASIC");
            Assert.IsTrue(tube.HardwareSymbols.ContainsKey("SPA_OS"), "et le systeme du second processeur");
            Assert.IsFalse(tube.HardwareSymbols.ContainsKey("OSWRCH"),
                "il n'y a pas d'appel systeme ici : le systeme est sur l'autre processeur");
            Assert.IsFalse(tube.HardwareSymbols.ContainsKey("CRTC"), "ni de video");
        }

        [TestMethod]
        public void LesDeuxProcesseursNontPasLaMemeTableDeSymboles()
        {
            ResolvedTarget bbc;
            ResolvedTarget tube;
            Assert.IsTrue(SystemCatalog.TryGet("bbc", out bbc));
            Assert.IsTrue(SystemCatalog.TryGet("tube", out tube));

            Assert.AreNotEqual(bbc.HardwareSymbols.Count, tube.HardwareSymbols.Count);
            foreach (string name in bbc.HardwareSymbols.Keys)
            {
                Assert.IsFalse(tube.HardwareSymbols.ContainsKey(name),
                    "'" + name + "' appartient a la machine principale");
            }
        }

        [TestMethod]
        public void LeProcesseurDuSecondProcesseurEstExposeDansLesDeuxVariantes()
        {
            // Le guide de l'utilisateur dit 6502B, le manuel de service dit
            // 65C02, et les cartes survivantes portent des 65C02. Les deux
            // existent : le catalogue propose les deux plutot que d'en designer
            // un comme vrai.
            ResolvedTarget plain;
            ResolvedTarget turbo;
            Assert.IsTrue(SystemCatalog.TryGet("tube", out plain));
            Assert.IsTrue(SystemCatalog.TryGet("tube65c02", out turbo));

            Assert.AreEqual("6502", plain.CpuName);
            Assert.AreEqual("65c02", turbo.CpuName);
            Assert.AreEqual(plain.FormatName, turbo.FormatName, "seul le processeur change");
        }

        // ---------------------------------------------------------------- catalogue

        [TestMethod]
        public void LaBbcEcritUnFichierBBCEtLeSecondProcesseurEcritUnBlocNu()
        {
            ResolvedTarget bbc;
            Assert.IsTrue(SystemCatalog.TryGet("bbc", out bbc));
            Assert.AreEqual("bbc", bbc.FormatName, "la BBC a son propre format de fichier");
            Assert.AreEqual("code", bbc.BbcFileType);

            ResolvedTarget tube;
            Assert.IsTrue(SystemCatalog.TryGet("tube", out tube));
            Assert.AreEqual("tube", tube.FormatName);
            Assert.IsFalse(tube.LoadAddress.HasValue,
                "l'adresse est fournie par le loader, pas inscrite dans le fichier");
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
        public void LaConfigurationPeutChoisirLeGenreDeFichierEtLeTitre()
        {
            Segments.TargetConf config = new Segments.TargetConf
            {
                System = "bbc",
                BbcFileType = "flat",
                Title = "1.00 (mon programme)",
                Author = "moi"
            };
            ResolvedTarget target = TargetResolver.Resolve(config, null, null, null);

            Assert.AreEqual("flat", target.BbcFileType);
            Assert.AreEqual("1.00 (mon programme)", target.Title);
            Assert.AreEqual("moi", target.Author);
        }

        [TestMethod]
        public void UnTitreVideNeProduitPasUnEnTeteIncomplet()
        {
            byte[] file = BbcFormat.BuildHeader(0x2000, Payload(2), new ResolvedTarget());
            Assert.AreEqual(0, file[file[7]], "le marqueur existe toujours");
            Assert.AreEqual('(', (char)file[file[7] + 1]);
        }

        private static string Message(OperationResult result)
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < result.Diagnostics.Count; i++)
                text.Append(result.Diagnostics[i].Message);
            return text.ToString();
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
