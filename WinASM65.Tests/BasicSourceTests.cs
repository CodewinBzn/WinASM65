// WinASM65 - the basic verb, and the text reader it is built on
//
// The verb is the point at which the token encoder becomes reachable at all, so
// what is tested here is what a person gets: a file that came out of it has to
// be the file the encoder would have written, and reading it back has to give
// the program again.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Segments;
using WinASM65.Targets;
using WinASM65.TextFormat;

namespace WinASM65.Tests
{
    [TestClass]
    public class BasicSourceTests
    {
        [TestMethod]
        public void UneLigneEstUnNombrePuisSonTexte()
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            IReadOnlyList<BasicLine> lines = BasicSource.Read("10 PRINT \"HI\"\n", diagnostics);

            Assert.AreEqual(0, diagnostics.Count);
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual(10, lines[0].Number);
            Assert.AreEqual("PRINT \"HI\"", lines[0].Body);
        }

        [TestMethod]
        public void LesLignesVidesSontSauteesEtLesRetoursAcceptees()
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            IReadOnlyList<BasicLine> lines = BasicSource.Read("10 A=1\n\r\n\r20 B=2\n\n", diagnostics);

            Assert.AreEqual(0, diagnostics.Count);
            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual(20, lines[1].Number);
        }

        [TestMethod]
        public void UnProgrammeSansNumeroEstNumeroteCommeUneMachineLeFait()
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            IReadOnlyList<BasicLine> lines = BasicSource.Read("PRINT 1\nPRINT 2\nPRINT 3\n", diagnostics);

            Assert.AreEqual(0, diagnostics.Count);
            Assert.AreEqual(3, lines.Count);
            Assert.AreEqual(10, lines[0].Number);
            Assert.AreEqual(30, lines[2].Number);
        }

        [TestMethod]
        public void UnProgrammeAChermelUnNumeroManquantEstRefuse()
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            BasicSource.Read("10 A=1\nB=2\n", diagnostics);

            Assert.AreEqual(1, diagnostics.Count);
            StringAssert.Contains(diagnostics[0].ToString(), "not at all");
        }

        [TestMethod]
        public void UneLigneQuiRevientEnArriereEstSignaleeEtLaieeEnPlace()
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            IReadOnlyList<BasicLine> lines = BasicSource.Read("20 A=1\n10 B=2\n", diagnostics);

            Assert.AreEqual(1, diagnostics.Count);
            Assert.AreEqual(20, lines[0].Number, "les lignes restent dans l'ordre du fichier");
            Assert.AreEqual(10, lines[1].Number, "et ne sont pas triees");
        }

        [TestMethod]
        public void UnChaineDeCaracteresNeCoupePasLaLigne()
        {
            // $0B, $0C et $1C to $1E sont des caracteres de controle legitimes
            // dans un VDU ou dans une chaine. SplitLines coupe dessus, et une
            // ligne coupee en deux donne deux lignes a l'encodeur.
            string body = "PRINT CHR$(11);CHR$(12);CHR$(28)";
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            IReadOnlyList<BasicLine> lines = BasicSource.Read("10 " + body + "\n", diagnostics);

            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual(body, lines[0].Body);
        }

        [TestMethod]
        public void LeTexteApresLeNombreEstPrisTelQuel()
        {
            // Les espaces d'un REM font partie du commentaire : l'encodeur ne
            // les touche pas, et il ne les touche pas parce qu'il les copie.
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            IReadOnlyList<BasicLine> lines = BasicSource.Read("10 REM   spaced   out\n", diagnostics);

            Assert.AreEqual("REM   spaced   out", lines[0].Body);
        }

        [TestMethod]
        public void UnChiffreColleAUnMotNEstPasUnNumeroDeLigne()
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            IReadOnlyList<BasicLine> lines = BasicSource.Read("10PRINT\n", diagnostics);

            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual(10, lines[0].Number, "la ligne est renumerotee, elle n'en avait pas");
            Assert.AreEqual("10PRINT", lines[0].Body, "et son texte n'a pas ete coupe en deux");
        }

        [TestMethod]
        public void UnTexteRenduRedonneLeProgramme()
        {
            string source = "10 PRINT \"A\"\n20 REM  deux  espaces\n30 GOTO 10\n";
            IReadOnlyList<BasicLine> lines = BasicSource.Read(source, new List<Diagnostic>());

            StringAssert.StartsWith(BasicSource.Render(lines), source);
        }
    }

    [TestClass]
    public class BasicVerbTests
    {
        [TestMethod]
        public void LeVerbeEcritLeProgrammeTokenise()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "hello.bas");
                string output = Path.Combine(temp.Path, "hello.bin");
                File.WriteAllText(source, "10 PRINT \"HI\"\n20 END\n");

                Recorder console = new Recorder();
                int code = Run(new[] { "basic", "-f", source, "-o", output }, console);

                Assert.AreEqual(0, code, console.Text);
                Assert.IsTrue(File.Exists(output));

                byte[] expected = new TokenEncoder(ApplesoftDialect.Create(), new List<Diagnostic>())
                    .Encode(new[]
                    {
                        new BasicLine(10, "PRINT \"HI\""),
                        new BasicLine(20, "END")
                    });
                CollectionAssert.AreEqual(expected, File.ReadAllBytes(output),
                    "le fichier ecrit est celui que l'encodeur produit");
            }
        }

        [TestMethod]
        public void LeVerbeRelitLeFichierEtLeRendEnTexte()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "round.bas");
                string output = Path.Combine(temp.Path, "round.bin");
                string listing = Path.Combine(temp.Path, "round.lst");
                File.WriteAllText(source, "10 PRINT \"HI\"\n20 GOTO 10\n");

                Recorder console = new Recorder();
                int code = Run(new[]
                {
                    "basic", "-f", source, "-o", output, "-dialect", "bbc", "-list", listing
                }, console);

                Assert.AreEqual(0, code, console.Text);
                string text = File.ReadAllText(listing);
                StringAssert.Contains(text, "PRINT \\x22 \\x48 \\x49 \\x22",
                    "BBC BASIC garde les guillemets, la liste se relit");
                StringAssert.Contains(text, "20 ", "et les numeros reviennent en clair");
            }
        }

        [TestMethod]
        public void UneReferenceDeLigneRevientSousFormeDOctets()
        {
            // Une reference BBC tient en trois octets qui portent plus
            // d'etats qu'il n'y a de lignes : un fichier peut donc en porter une
            // que l'encodeur n'ecrirait jamais. La liste rend les octets, et
            // l'aller-retour reste exact pour tous les fichiers.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "go.bas");
                string output = Path.Combine(temp.Path, "go.bin");
                string listing = Path.Combine(temp.Path, "go.lst");
                File.WriteAllText(source, "10 GOTO 30\n30 PRINT \"HI\"\n");

                Recorder console = new Recorder();
                Assert.AreEqual(0, Run(new[]
                {
                    "basic", "-f", source, "-o", output, "-dialect", "bbc", "-list", listing
                }, console), console.Text);

                StringAssert.Contains(File.ReadAllText(listing), "GOTO \\x8D",
                    "la reference revient comme les octets qu'elle est");

                Recorder again = new Recorder();
                Assert.AreEqual(0, Run(new[]
                {
                    "basic", "-f", listing, "-o", output + "2", "-dialect", "bbc"
                }, again), again.Text);
                CollectionAssert.AreEqual(File.ReadAllBytes(output), File.ReadAllBytes(output + "2"),
                    "et la liste se retokenise en exactement le meme fichier");
            }
        }

        [TestMethod]
        public void LaListeVientDuProgrammeEtNonDuConteneur()
        {
            // Deux octets nuls sont un programme vide, et le debut d'un volume
            // DOS en est fait de. Lire le conteneur au lieu du programme ne
            // donne donc aucune ligne et aucun avertissement non plus.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "p.bas");
                string output = Path.Combine(temp.Path, "p.dsk");
                string listing = Path.Combine(temp.Path, "p.lst");
                File.WriteAllText(source, "10 PRINT \"HI\"\n");

                Recorder console = new Recorder();
                int code = Run(new[]
                {
                    "basic", "-f", source, "-o", output, "-format", "dos33", "-list", listing
                }, console);

                Assert.AreEqual(0, code, console.Text);
                StringAssert.Contains(File.ReadAllText(listing), "PRINT",
                    "la liste est le programme, pas le volume");
            }
        }

        [TestMethod]
        public void UneListeApplesoftMontreLaChaineCommeLeFichierLaStocke()
        {
            // Applesoft ecrit une chaine par sa longueur et ses octets, sans
            // guillemets : la liste rend donc ce que le fichier contient, et
            // non ce que la personne a ecrit. C'est ce qui rend l'aller-retour
            // exact, et c'est pour cela qu'elle n'est pas la source d'origine.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "p.bas");
                string output = Path.Combine(temp.Path, "p.bin");
                string listing = Path.Combine(temp.Path, "p.lst");
                File.WriteAllText(source, "10 PRINT \"HI\"\n");

                Recorder console = new Recorder();
                int code = Run(new[]
                {
                    "basic", "-f", source, "-o", output, "-list", listing
                }, console);

                Assert.AreEqual(0, code, console.Text);
                StringAssert.Contains(File.ReadAllText(listing), "\\x02",
                    "la longueur de la chaine precede ses deux octets");

                // Et cette liste se re-tokenise en exactement le meme fichier.
                Recorder again = new Recorder();
                int reencoded = Run(new[]
                {
                    "basic", "-f", listing, "-o", output + "2", "-dialect", "applesoft"
                }, again);
                Assert.AreEqual(0, reencoded, again.Text);
                CollectionAssert.AreEqual(File.ReadAllBytes(output), File.ReadAllBytes(output + "2"),
                    "l'aller-retour par la liste est exact");
            }
        }

        [TestMethod]
        public void LeVerbeEcritUnConteneurProDos()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "prog.bas");
                string output = Path.Combine(temp.Path, "prog.po");
                File.WriteAllText(source, "10 PRINT \"HI\"\n");

                Recorder console = new Recorder();
                int code = Run(new[] { "basic", "-f", source, "-o", output, "-format", "prodos" }, console);

                Assert.AreEqual(0, code, console.Text);
                byte[] image = File.ReadAllBytes(output);
                Assert.AreEqual(280 * 512, image.Length, "un volume ProDOS de 143K");
            }
        }

        [TestMethod]
        public void LeVerbeEcritUnConteneurDos33()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "prog.bas");
                string output = Path.Combine(temp.Path, "prog.dsk");
                File.WriteAllText(source, "10 PRINT \"HI\"\n");

                Recorder console = new Recorder();
                int code = Run(new[] { "basic", "-f", source, "-o", output, "-format", "dos33" }, console);

                Assert.AreEqual(0, code, console.Text);
                Assert.AreEqual(143360, File.ReadAllBytes(output).Length, "un volume DOS 3.3");
            }
        }

        [TestMethod]
        public void LeVerbeRefuseUnConteneurPourUnAutreBasic()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "prog.bas");
                string output = Path.Combine(temp.Path, "prog.bin");
                File.WriteAllText(source, "10 PRINT \"HI\"\n");

                Recorder console = new Recorder();
                int code = Run(new[]
                {
                    "basic", "-f", source, "-o", output, "-dialect", "bbc", "-format", "prodos"
                }, console);

                Assert.AreEqual(1, code);
                StringAssert.Contains(console.Text, "BBC BASIC");
            }
        }

        [TestMethod]
        public void LeVerbeRefuseUnBasicQuiNexistePas()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "prog.bas");
                string output = Path.Combine(temp.Path, "prog.bin");
                File.WriteAllText(source, "10 PRINT\n");

                Recorder console = new Recorder();
                int code = Run(new[] { "basic", "-f", source, "-o", output, "-dialect", "gecos" }, console);

                Assert.AreEqual(1, code);
                StringAssert.Contains(console.Text, "gecos");
            }
        }

        [TestMethod]
        public void LeVerbeEcritUnProgrammeWaterloo()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "prog.bas");
                string output = Path.Combine(temp.Path, "prog.prg");
                File.WriteAllText(source, "10 LOOP\n20 ENDLOOP\n");

                Recorder console = new Recorder();
                int code = Run(new[] { "basic", "-f", source, "-o", output, "-dialect", "waterloo" }, console);

                Assert.AreEqual(0, code, console.Text);

                byte[] expected = new TokenEncoder(WaterlooDialect.Create(), new List<Diagnostic>())
                    .Encode(new[] { new BasicLine(10, "LOOP"), new BasicLine(20, "ENDLOOP") });
                CollectionAssert.AreEqual(expected, File.ReadAllBytes(output));
            }
        }

        private static int Run(string[] args, IConsoleOutput console)
        {
            CommandLineApplication application = new CommandLineApplication(
                new AssemblerFactory(),
                new JsonConfigurationReader(),
                new BinaryCombiner(),
                console,
                new ExecutablePublisher());
            return application.Run(args);
        }

        private sealed class Recorder : IConsoleOutput
        {
            public string Text { get; private set; }

            public Recorder() { Text = string.Empty; }

            public void WriteLine(string value) { Text += value + "\n"; }
            public void WriteError(string value) { Text += value + "\n"; }
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }

            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "WinASM65Tests_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public void Dispose()
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, true);
            }
        }
    }
}
