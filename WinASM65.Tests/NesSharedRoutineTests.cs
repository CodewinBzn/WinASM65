using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.Expressions;
using WinASM65.Linking;
using WinASM65.Modules;
using WinASM65.Segments;
using WinASM65.Output;
using WinASM65.Targets;

namespace WinASM65.Tests
{
    /// <summary>
    /// T7 : validation du linker sur NES, avec une routine partageee posee a deux
    /// adresses distinctes.
    /// <para>
    /// Ce que ces tests prouvent est limite et le dit : ils prouvent que le meme
    /// code source produit deux images correctes a deux adresses, et que chaque
    /// copie a ses relocations reecrites vers sa propre adresse. Ils ne prouvent
    /// pas que le jeu s'execute, ni qu'un NES le demarre : une comparaison
    /// d'octets ne dit rien de ce que fait la machine.
    /// </para>
    /// <para>
    /// La seconde moitie de la validation est dans
    /// <see cref="NesEmulatorTests"/> : la ou ces tests s'arretent a l'octet,
    /// celle-la publie chaque image en ROM iNES, la lance dans Mesen, et relit
    /// les temoins que la machine a ecrits en RAM.
    /// </para>
    /// </summary>
    [TestClass]
    public class NesSharedRoutineTests
    {
        // La routine partagee. Elle lit une table par adresse absolue, ce qui
        // donne au linker une relocation a reecrire dans chaque copie.
        private const string SharedSource =
            ".org $8000\n" +
            "Routine: lda Table\n" +
            "        rts\n" +
            "        .byte $DE, $AD\n" +
            ".import Table\n" +
            "        .export Routine\n";

        private const string MainSource =
            ".org $E000\n" +
            "Reset:  jsr Routine\n" +
            "        rts\n" +
            "        .import Routine, shared\n" +
            "        .export Reset\n";

        // La table partagee est en RAM, pas en page zero : le shift de l'image est
        // global (voir LeShiftDeplaceLaPageZero), donc une table en page zero
        // sortirait de la page zero et l'expec... ne serait pas tenable.
        private const string TableSource =
            ".org $0400\n" +
            "Table:  .byte 1, 2, 3\n" +
            "        .export Table\n";

        [TestMethod]
        public void LaMemeSourceDonneDeuxImagesACorrectes()
        {
            ModuleImage shared = Assemble(SharedSource, "shared");
            ModuleImage main = Assemble(MainSource, "main");
            ModuleImage table = Assemble(TableSource, "table");

            LinkedImage at8000 = Link(shared, main, table, 0x0000);
            LinkedImage at9000 = Link(shared, main, table, 0x1000);

            // La routine est a $8000 dans la premiere image et a $9000 dans la
            // seconde. Le shift porte sur toute l'image, pas sur un segment : le
            // module ne sait pas qu'il est reutilise.
            Assert.AreEqual(0x8000, SharedRoutineAddress(at8000), "copie 1 a $8000");
            Assert.AreEqual(0x9000, SharedRoutineAddress(at9000), "copie 2 a $9000");

            // Et surtout, chaque copie pointe vers la table qui est a elle.
            Assert.AreEqual(0x0400, WordAt(at8000, 0x8001), "copie 1 lit la table a $0400");
            Assert.AreEqual(0x1400, WordAt(at9000, 0x9001), "la table a suivi le shift : $1400");
        }

        [TestMethod]
        public void LeShiftDeplaceLaPageZero()
        {
            // Le shift porte sur tous les segments sans distinction, y compris ce
            // qui est en page zero. C'est la limite a connaitre avant de poser une
            // routine partagee a une seconde adresse : une table ou une variable
            // qui doit rester en page zero sort de la page zero, et sur une vraie
            // machine cela ne fonctionne pas.
            //
            // Le test est la pour que la limite soit ecrite, pas pour la valider.
            ModuleImage shared = Assemble(SharedSource, "shared");
            ModuleImage main = Assemble(MainSource, "main");
            ModuleImage table = Assemble(TableSource, "table");
            ModuleImage zero = Assemble(".org $0080\nZero:  .byte 1, 2\n        .export Zero\n", "zero");

            LinkedImage shifted = LinkAll(0x1000, table, zero, shared, main);

            Assert.AreEqual(0x1080, SegmentAddress(shifted, "zero"),
                "la variable de page zero a ete deplacee en page 1 : le shift est global");
        }

        [TestMethod]
        public void LesDeuxCopiesSontIdentiquesSaufPourLesRelocations()
        {
            ModuleImage shared = Assemble(SharedSource, "shared");
            ModuleImage main = Assemble(MainSource, "main");
            ModuleImage table = Assemble(TableSource, "table");

            LinkedImage first = Link(shared, main, table, 0x0000);
            LinkedImage second = Link(shared, main, table, 0x1000);

            // Sur les 4 octets de la routine, seul l'operande de LDA differe.
            // C'est ce qui distingue une vraie relocation d'un simple decalage.
            Assert.AreEqual(0xAD, ByteAt(first, 0x8000), "lda absolu");
            Assert.AreEqual(0xAD, ByteAt(second, 0x9000), "lda absolu, copie 2");
            Assert.AreEqual(0x60, ByteAt(first, 0x8003), "rts, copie 1");
            Assert.AreEqual(0x60, ByteAt(second, 0x9003), "rts, copie 2");
            Assert.AreEqual(0xDE, ByteAt(first, 0x8004), "octet de donnee preserve");
            Assert.AreEqual(0xDE, ByteAt(second, 0x9004), "octet de donnee preserve, copie 2");
            Assert.AreEqual(0xAD, ByteAt(first, 0x8005), "octet de donnee preserve");
            Assert.AreEqual(0xAD, ByteAt(second, 0x9005), "octet de donnee preserve, copie 2");
        }

        [TestMethod]
        public void LeAppelantEstRemplaceALaBonneAdresseDansChaqueImage()
        {
            ModuleImage shared = Assemble(SharedSource, "shared");
            ModuleImage main = Assemble(MainSource, "main");
            ModuleImage table = Assemble(TableSource, "table");

            LinkedImage first = Link(shared, main, table, 0x0000);
            LinkedImage second = Link(shared, main, table, 0x1000);

            // JSR est en $E000, suivi de son operable. Le JSR lui-meme ne change
            // pas, seule sa cible doit bouger avec la copie.
            Assert.AreEqual(0x20, ByteAt(first, 0xE000), "jsr absolu");
            Assert.AreEqual(0x20, ByteAt(second, 0xF000), "jsr absolu, copie 2");
            Assert.AreEqual(0x8000, WordAt(first, 0xE001), "la copie 1 appelle $8000");
            Assert.AreEqual(0x9000, WordAt(second, 0xF001), "la copie 2 appelle $9000");
        }

        [TestMethod]
        public void UneImageNESEstBienFormee()
        {
            // L'iNES est un en-tete de 16 octets puis des banques de taille fixe.
            // Un conteneur mal forme demarre sur une NES reelle et plantage, donc
            // la forme se verifie ici meme si l'execution ne se verifie pas.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                // Republie une image plate en iNES, comme le fait un build complet.
                byte[] rom = BuildRom(temp);

                Assert.IsTrue(rom.Length >= 16, "il faut au moins un en-tete");

                Assert.AreEqual(0x4E, rom[0], "magic 'N'");
                Assert.AreEqual(0x45, rom[1], "magic 'E'");
                Assert.AreEqual(0x53, rom[2], "magic 'S'");
                Assert.AreEqual(0x1A, rom[3], "magic $1A");

                int prg = rom[4] * 16384;
                int chr = rom[5] * 8192;
                Assert.AreEqual(16 + prg + chr, rom.Length,
                    "la taille du fichier doit etre exactement l'en-tete plus les banques annoncees");
            }
        }

        [TestMethod]
        public void UneCibleQuiDefinitUneEtiquetteUtiliseeParLeSourceResteAssemblable()
        {
            // example_bomberman-nes porte "JOY1:" comme etiquette alors que le
            // catalogue NES definit JOY1 a $4016. Refuser l'etiquette rendait le
            // programme du depot inassemblable des qu'on lui donnait une cible.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "joy.asm");
                File.WriteAllText(source, ".org $C000\nJOY1: nop\n        jmp JOY1\n");
                string output = Path.Combine(temp.Path, "joy.o");

                int exitCode = new CommandLineApplication(new AssemblerFactory(), new JsonConfigurationReader(),
                    new BinaryCombiner(), new SilentConsole(), new ExecutablePublisher())
                    .Run(new[] { "-t", "nes", "-format", "bin", "-f", source, "-o", output });

                Assert.AreEqual(0, exitCode, "une etiquette du source prime sur un symbole materiel");
                CollectionAssert.AreEqual(new byte[] { 0xEA, 0x4C, 0x00, 0xC0 },
                    File.ReadAllBytes(output), "jmp JOY1 pointe vers l etiquette, pas vers $4016");
            }
        }

        // ------------------------------------------------------------- helpers

        private static byte[] BuildRom(TemporaryDirectory temp)
        {
            string source = Path.Combine(temp.Path, "rom.asm");
            File.WriteAllText(source, ".org $8000\n        nop\n        rts\n");
            string image = Path.Combine(temp.Path, "rom.nes");
            new CommandLineApplication(new AssemblerFactory(), new JsonConfigurationReader(),
                new BinaryCombiner(), new SilentConsole(), new ExecutablePublisher())
                .Run(new[] { "-t", "nes", "-format", "ines", "-f", source, "-o", image });
            return File.ReadAllBytes(image);
        }

        private static ModuleImage Assemble(string source, string name)
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, name + ".asm");
                File.WriteAllText(path, source);
                AssemblerOptions options = new AssemblerOptions();
                options.Cpu = CpuFactory.Create("6502");
                AssemblyResult result = new AssemblerFactory().Create(options)
                    .Assemble(path, Path.Combine(temp.Path, name + ".o"));
                Assert.IsTrue(result.Success, Describe(result));
                Assert.IsNotNull(result.Module, name + " should produce a module");
                result.Module.ModuleName = name;
                return result.Module;
            }
        }

        private static LinkedImage Link(ModuleImage shared, ModuleImage main, ModuleImage table, int shift)
        {
            return LinkAll(shift, table, shared, main);
        }

        private static LinkedImage LinkAll(int shift, params ModuleImage[] modules)
        {
            LinkerOptions options = new LinkerOptions();
            options.AddressShift = shift;

            LinkedImage image;
            OperationResult result = new Linker().Link(modules, options, out image);
            Assert.IsTrue(result.Success, Describe(result));
            return image;
        }

        private static ushort SegmentAddress(LinkedImage image, string module)
        {
            for (int i = 0; i < image.Segments.Count; i++)
            {
                if (image.Segments[i].SourceModule == module)
                    return image.Segments[i].Address;
            }
            Assert.Fail("the " + module + " segment is missing from the image");
            return 0;
        }

        private static ushort SharedRoutineAddress(LinkedImage image)
        {
            return SegmentAddress(image, "shared");
        }

        private static byte ByteAt(LinkedImage image, ushort address)
        {
            int offset = address - image.OriginAddress;
            Assert.IsTrue(offset >= 0 && offset < image.Data.Length,
                "$" + address.ToString("X4") + " is outside the image");
            return image.Data[offset];
        }

        private static ushort WordAt(LinkedImage image, ushort address)
        {
            return (ushort)(ByteAt(image, address) | (ByteAt(image, (ushort)(address + 1)) << 8));
        }

        private static string Describe(AssemblyResult result)
        {
            string text = string.Empty;
            for (int i = 0; i < result.Diagnostics.Count; i++)
                text += result.Diagnostics[i].Message + " | ";
            return text;
        }

        private static string Describe(OperationResult result)
        {
            string text = string.Empty;
            for (int i = 0; i < result.Diagnostics.Count; i++)
                text += result.Diagnostics[i].Message + " | ";
            return text;
        }

        private sealed class SilentConsole : IConsoleOutput
        {
            public void WriteLine(string value) { }
            public void WriteError(string value) { }
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }

            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "WinASM65Nes_" + Guid.NewGuid().ToString("N"));
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
