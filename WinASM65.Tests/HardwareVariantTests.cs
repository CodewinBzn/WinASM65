using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Segments;
using WinASM65.Targets;

namespace WinASM65.Tests
{
    /// <summary>
    /// T14 : les variantes materielles comme options de cible.
    /// <para>
    /// Le plan annoncait que le 8580 du C64C « change les espaces memoire
    /// visibles ». Il ne les change pas : le C64C a bien un VIC-II a $D000 et
    /// bien sa memoire de couleurs a $D800. Ce qui change, c'est la revision de
    /// la puce de son, et elle ne deplace aucune adresse. Les tests ici
    /// verrouillent la difference reelle, qui est ailleurs.
    /// </para>
    /// </summary>
    [TestClass]
    public class HardwareVariantTests
    {
        private static long Value(string system, string symbol)
        {
            ResolvedTarget target;
            Assert.IsTrue(SystemCatalog.TryGet(system, out target), "cible inconnue : " + system);
            Assert.IsTrue(target.HardwareSymbols.ContainsKey(symbol),
                system + " ne declare pas " + symbol);
            return target.HardwareSymbols[symbol];
        }

        // ------------------------------------------------------- une seule table

        [TestMethod]
        public void UneVarianteNEcritPasSaPropreTableDeSymboles()
        {
            // Le principe de la tache : une cible nommee ne fait que preselectionner
            // des options. Deux tables pour la meme machine, c'est deux endroits
            // ou oublier la meme correction.
            ResolvedTarget plain;
            ResolvedTarget flat;
            Assert.IsTrue(SystemCatalog.TryGet("c64", out plain));
            Assert.IsTrue(SystemCatalog.TryGet("c64c", out flat));

            Assert.AreEqual(HardwareOptions.ModelC64, plain.Hardware.Model);
            Assert.AreEqual(HardwareOptions.ModelC64C, flat.Hardware.Model);
            Assert.IsNotNull(flat.Hardware);

            // Le bloc d'entree-sortie est le meme, et c'est bien ce que dit la
            // documentation du materiel.
            Assert.AreEqual(Value("c64", "VIC"), Value("c64c", "VIC"));
            Assert.AreEqual(Value("c64", "COLORRAM"), Value("c64c", "COLORRAM"),
                "le C64C a bien sa memoire de couleurs, contrary a ce que dit le plan");
            Assert.AreEqual(Value("c64", "SID"), Value("c64c", "SID"),
                "le 8580 est a la meme adresse que le 6581");
        }

        [TestMethod]
        public void LeC128AjouteLeMMUEtLeVDCEtDeplaceRienDautre()
        {
            // CIA 2 reste a $DD00 dans les deux modes : le bloc d'entree-sortie
            // est un decodage plat, rien n'y est multiplexe.
            Assert.AreEqual(0xD000, Value("c128", "VIC"));
            Assert.AreEqual(0xD400, Value("c128", "SID"));
            Assert.AreEqual(0xD500, Value("c128", "MMU"));
            Assert.AreEqual(0xD600, Value("c128", "VDC"));
            Assert.AreEqual(0xD601, Value("c128", "VDCDATA"));
            Assert.AreEqual(Value("c64", "CIA2"), Value("c128", "CIA2"));
        }

        [TestMethod]
        public void LePlus4NAcestPasUnC64AvecUneAutrePuceVideo()
        {
            // C'est le cas que la tache visait, et il est brutal : un programme
            // ecrit pour le C64 lit de la RAM la ou il attend un registre, et
            // rien ne le signale.
            Assert.IsFalse(Has("plus4", "VIC"), "le Plus/4 n'a pas de VIC-II");
            Assert.IsFalse(Has("plus4", "SID"), "ni de SID");
            Assert.IsFalse(Has("plus4", "CIA1"));
            Assert.AreEqual(0xFF00, Value("plus4", "TED"), "ses registres video sont tout en haut");
            Assert.AreEqual(0xFD00, Value("plus4", "ACIA"));
        }

        [TestMethod]
        public void LePetNANestNiCRTCNiACIA()
        {
            // Le 2001 et le 2001-N n'ont ni controleur video (la temporisation
            // est en logique discrete) ni ACIA. C'etait une croyance repetee
            // dans les documents du plan.
            Assert.IsFalse(Has("pet2001", "CRTC"));
            Assert.IsFalse(Has("pet2001", "ACIA"));
            Assert.IsFalse(Has("pet2001n", "ACIA"));
            Assert.AreEqual(0xE810, Value("pet2001", "PIA1"));
            Assert.AreEqual(0xE820, Value("pet2001", "PIA2"));
            Assert.AreEqual(0xE840, Value("pet2001", "VIA"));
            Assert.AreEqual(0x8000, Value("pet2001", "SCREEN"));
        }

        [TestMethod]
        public void SurUnCbmIIleChoix40Ou80ColonnesChangeLaPuceQuiRepond()
        {
            // Le seul cas du catalogue ou une option d'ecran deplace un
            // registre : c'est ce qui justifie qu'elle existe.
            Assert.AreEqual(0xD800, Value("cbm2-80", "CRTC"));
            Assert.IsFalse(Has("cbm2-80", "VIC"));
            Assert.AreEqual(0xD800, Value("cbm2-40", "VIC"));
            Assert.IsFalse(Has("cbm2-40", "CRTC"));
            Assert.AreEqual(0xDD00, Value("cbm2-40", "ACIA"), "l'ACIA est le meme sur les deux series");
        }

        // ------------------------------------------------------------- PAL / NTSC

        [TestMethod]
        public void PalEtNtscNeDeplacentAucunRegistreCommodore()
        {
            foreach (string symbol in new string[] { "VIC", "SID", "CIA1", "CIA2" })
                Assert.AreEqual(Value("c64", symbol), Value("c64-pal", symbol), symbol);

            Assert.AreEqual(Value("vic20", "VIC"), Value("vic20-pal", "VIC"),
                "sur le VIC-20, la norme ne change que la revision de la puce");
        }

        [TestMethod]
        public void SurLeNesLaNormeChangeLeNombreDeLignes()
        {
            // Ici la norme change quelque chose de mesurable, et c'est ce qu'un
            // programme de synchronisation doit savoir.
            Assert.AreEqual(262, Value("nes-ntsc", "FRAME_LINES"));
            Assert.AreEqual(224, Value("nes-ntsc", "VISIBLE_LINES"));
            Assert.AreEqual(312, Value("nes-pal", "FRAME_LINES"),
                "la frame PAL compte 312 lignes, dont 240 visibles");
            Assert.AreEqual(240, Value("nes-pal", "VISIBLE_LINES"));
            Assert.AreEqual(0, Value("nes-ntsc", "IS_PAL"));
            Assert.AreEqual(1, Value("nes-pal", "IS_PAL"));
        }

        [TestMethod]
        public void UneCibleNommeeNeDifferesDUneAutreQueParCeQuEllePreselectionne()
        {
            ResolvedTarget a;
            ResolvedTarget b;
            Assert.IsTrue(SystemCatalog.TryGet("c64", out a));
            Assert.IsTrue(SystemCatalog.TryGet("c64-pal", out b));

            Assert.AreEqual(a.FormatName, b.FormatName);
            Assert.AreEqual(a.LoadAddress, b.LoadAddress);
            Assert.IsNull(a.Hardware.VideoStandard, "la cible de base ne tranche pas");
            Assert.AreEqual(HardwareOptions.StandardPal, b.Hardware.VideoStandard);
        }

        [TestMethod]
        public void LeCatalogueSansMachineNePerdPasLaTableDuC64()
        {
            // Une table materielle fixee par la cible doit survivre a l'ajout
            // des options : le C64 a toujours un VIC a $D000.
            Assert.AreEqual(0xD000, Value("c64", "VIC"));
            Assert.AreEqual(0xD400, Value("c64", "SID"));
            Assert.AreEqual(0x2000, Value("nes", "PPUCTRL"));
        }

        // --------------------------------------------------------- contradictions

        [TestMethod]
        public void UnCRTCSurUnPetEstRefuse()
        {
            // Ce n'est pas un avertissement : l'adresse existe sur d'autres
            // machines et repondrait ici par autre chose.
            TargetConf config = new TargetConf
            {
                System = "pet2001",
                Hardware = new HardwareConf { Model = "pet2001", VideoChip = "crtc" }
            };

            string refused = Refusal(config);
            StringAssert.Contains(refused, "no video controller");
        }

        [TestMethod]
        public void UnVDCSurUnC64EstRefuse()
        {
            TargetConf config = new TargetConf
            {
                System = "c64",
                Hardware = new HardwareConf { Model = "c64", VideoChip = "vdc" }
            };

            StringAssert.Contains(Refusal(config), "8563");
        }

        [TestMethod]
        public void UnSIDSurUnPlus4EstRefuse()
        {
            TargetConf config = new TargetConf
            {
                System = "plus4",
                Hardware = new HardwareConf { Model = "plus4", SoundChip = "6581" }
            };

            StringAssert.Contains(Refusal(config), "TED");
        }

        [TestMethod]
        public void UneNormeInconnueEstRefuseeAvecLaListeDesDeuxPossibles()
        {
            TargetConf config = new TargetConf
            {
                System = "c64",
                Hardware = new HardwareConf { VideoStandard = "secam" }
            };

            StringAssert.Contains(Refusal(config), "pal");
            StringAssert.Contains(Refusal(config), "ntsc");
        }

        [TestMethod]
        public void UneCombinaisonPossibleEstAcceptee()
        {
            TargetConf config = new TargetConf
            {
                System = "c128",
                Hardware = new HardwareConf
                {
                    Model = "c128",
                    VideoChip = "vdc",
                    SoundChip = "8580",
                    VideoStandard = "pal"
                }
            };

            ResolvedTarget target = TargetResolver.Resolve(config, null, null, null);
            Assert.AreEqual(0xD600, target.HardwareSymbols["VDC"]);
            Assert.AreEqual(HardwareOptions.Sid8580, target.Hardware.SoundChip);
        }

        [TestMethod]
        public void LaConfigurationPeutRendreUneCibleNommeeDifferente()
        {
            // Le modele l'emporte sur le preset : c'est la meme logique que pour
            // le format, et c'est ce qui evite d'avoir un 'c64-vdc' a maintenir.
            TargetConf config = new TargetConf
            {
                System = "c128",
                Hardware = new HardwareConf { Model = "c128", VideoChip = "vdc" }
            };

            ResolvedTarget target = TargetResolver.Resolve(config, null, null, null);
            Assert.AreEqual(0xD600, target.HardwareSymbols["VDC"]);
        }

        // -------------------------------------------------------- adresse de PRG

        [TestMethod]
        public void UneMachineBanqueeSansAdresseDeclareeEstRefusee()
        {
            // Les deux premiers octets d'un PRG sont l'adresse de chargement.
            // Mettre celle d'un C64 sur une machine qui banque sa memoire
            // produirait une image que rien ne signale comme fausse.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                ResolvedTarget target;
                Assert.IsTrue(SystemCatalog.TryGet("cbm2-80", out target));
                Assert.IsFalse(target.LoadAddress.HasValue, "le CBM-II n'a pas d'adresse unique");

                Core.OperationResult result = new CommodorePrgFormat()
                    .Write(Path.Combine(temp.Path, "out.prg"), new byte[4], target);

                Assert.IsFalse(result.Success);
                string message = string.Empty;
                for (int i = 0; i < result.Diagnostics.Count; i++)
                    message += result.Diagnostics[i].Message;
                StringAssert.Contains(message, "banked");
            }
        }

        [TestMethod]
        public void UneAdresseDeclareeSurUneMachineBanqueeSuffit()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                ResolvedTarget target;
                Assert.IsTrue(SystemCatalog.TryGet("cbm2-80", out target));
                target.LoadAddress = 0x0400;

                string path = Path.Combine(temp.Path, "out.prg");
                Assert.IsTrue(new CommodorePrgFormat().Write(path, new byte[4], target).Success);

                byte[] file = File.ReadAllBytes(path);
                Assert.AreEqual(0x00, file[0]);
                Assert.AreEqual(0x04, file[1]);
            }
        }

        // ------------------------------------------------------------- catalogue

        [TestMethod]
        public void LeCatalogueAnnonceLaMachineDeChaqueVariante()
        {
            // Un build PAL et un build NTSC produisent les memes octets ici :
            // sans cette ligne, rien dans la sortie ne dirait lequel a ete fait.
            string text = SystemCatalog.Describe();
            StringAssert.Contains(text, "hw=c64/6581/pal");
            StringAssert.Contains(text, "hw=nes/pal");
            StringAssert.Contains(text, "hw=c128/vdc/8580");
        }

        [TestMethod]
        public void ModifierLesOptionsDUneCibleNeContaminePasLeCatalogue()
        {
            ResolvedTarget first;
            ResolvedTarget second;
            Assert.IsTrue(SystemCatalog.TryGet("c64", out first));
            first.Hardware.Model = "c128d";
            first.HardwareSymbols["VIC"] = 0x1234;

            Assert.IsTrue(SystemCatalog.TryGet("c64", out second));
            Assert.AreEqual(HardwareOptions.ModelC64, second.Hardware.Model,
                "le preset a ete modifie a travers la copie");
            Assert.AreEqual(0xD000, second.HardwareSymbols["VIC"]);
        }

        private static bool Has(string system, string symbol)
        {
            ResolvedTarget target;
            Assert.IsTrue(SystemCatalog.TryGet(system, out target), "cible inconnue : " + system);
            return target.HardwareSymbols.ContainsKey(symbol);
        }

        private static string Refusal(TargetConf config)
        {
            try
            {
                TargetResolver.Resolve(config, null, null, null);
            }
            catch (ArgumentException ex)
            {
                return ex.Message;
            }
            Assert.Fail("la combinaison aurait du ete refusee");
            return null;
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }

            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "WinASM65Hw_" + Guid.NewGuid().ToString("N"));
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
