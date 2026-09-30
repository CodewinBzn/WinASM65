using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Modules;
using WinASM65.Output;

namespace WinASM65.Tests
{
    /// <summary>
    /// T12 : visualiseur de modules hors ligne.
    /// <para>
    /// Ces tests portent sur le HTML produit. Ils ne disent rien de ce que le
    /// navigateur affiche ensuite : une page peut etre bien formee et s'afficher
    /// mal. C'est la limite honnete de ce qui se verifie ici.
    /// </para>
    /// </summary>
    [TestClass]
    public class ModuleViewerTests
    {
        private static ModuleImage Sample()
        {
            ModuleImage image = new ModuleImage();
            image.ModuleName = "MAIN";
            ModuleSegment code = new ModuleSegment("CODE",
                new byte[] { 0xA9, 0x01, 0x60, 0xEA, 0xEA }, SegmentKind.Ro, 1, 0);
            code.OriginAddress = 0xC000;
            image.AddSegment(code);
            image.AddSegment(new ModuleSegment("VARS", new byte[4], SegmentKind.Bss, 1, 0xFF));
            image.AddExport(new ModuleExport("Reset", 0, 0));
            image.AddImport(new ModuleImport("DrawTile", "gfx"));
            image.AddRelocation(new RelocationRecord(0, "CODE", 2, 0xC002, 2,
                (RelocationType)2, new List<string> { "DrawTile" },
                new SourceLocation("main.asm", 12), string.Empty));
            return image;
        }

        // ------------------------------------------------------------- structure

        [TestMethod]
        public void UneVueDeModuleEstUnDocumentHtmlCompletEtSansRessourceExterne()
        {
            string html = ModuleViewer.Render(Sample(), "main.w65");

            StringAssert.StartsWith(html, "<!DOCTYPE html>", "un document, pas un fragment");
            StringAssert.Contains(html, "</html>", "ferme");
            StringAssert.Contains(html, "<meta charset=\"utf-8\">");

            // Aucune ressource externe : un visualiseur qui a besoin du reseau ou
            // d'un dossier voisin est inutile sur la machine ou le build a lieu.
            Assert.IsFalse(html.Contains("http://"), "aucune URL absolue");
            Assert.IsFalse(html.Contains("https://"), "aucune URL absolue");
            Assert.IsFalse(html.Contains("<script"), "rien a executer");
            Assert.IsFalse(html.Contains("<link"), "aucune feuille de style externe");
            Assert.IsFalse(html.Contains("src="), "aucune image ni ressource chargee");
        }

        [TestMethod]
        public void UneVueMontreSegmentsExportsImportsEtRelocations()
        {
            string html = ModuleViewer.Render(Sample(), "main.w65");

            StringAssert.Contains(html, "CODE", "le segment");
            StringAssert.Contains(html, "VARS", "le segment reserve");
            StringAssert.Contains(html, "Reset", "l'export");
            StringAssert.Contains(html, "DrawTile", "l'import");
            StringAssert.Contains(html, "gfx", "le module attendu pour l'import");
            StringAssert.Contains(html, "main.asm:12", "la provenance de la relocation");
            StringAssert.Contains(html, "$C002", "le site relocalisable");
            StringAssert.Contains(html, "Abs16", "le type de relocation");
        }

        [TestMethod]
        public void UnSegmentReserveNaPasDeBanqueAffichee()
        {
            // Un segment bss ne vit pas sur la piste, donc afficher sa banque
            // comme 255 ferait croire qu'il est pose quelque part.
            string html = ModuleViewer.Render(Sample(), "main.w65");
            StringAssert.Contains(html, "Bss", "le type est bien montre");
        }

        [TestMethod]
        public void UnModuleSansRelocationLeDitPLutotQueDafficherUneTableVide()
        {
            ModuleImage image = new ModuleImage();
            image.ModuleName = "FIXED";
            image.AddSegment(new ModuleSegment("DATA", new byte[] { 1, 2, 3 },
                SegmentKind.Ro, 1, 0));

            string html = ModuleViewer.Render(image, "fixed.w65");
            StringAssert.Contains(html, "No relocatable site",
                "une table vide se comprend mal, une phrase se comprend");
        }

        // ---------------------------------------------------------------- escaping

        [TestMethod]
        public void UnNomAvecBaliseEstNeutralise()
        {
            // Un nom vient du source, donc il peut contenir < et >. Sans echappement
            // il casse la page et, pire, une page qui execute du script executerait
            // ce qu'un fichier contenant ce nom lui passe.
            ModuleImage image = new ModuleImage();
            image.AddSegment(new ModuleSegment("<script>alert(1)</script>",
                new byte[] { 0x60 }, SegmentKind.Ro, 1, 0));
            image.AddExport(new ModuleExport("a\"b'c&d", 0, 0));

            string html = ModuleViewer.Render(image, "evil.w65");

            Assert.IsFalse(html.Contains("<script>alert"), "la balise ne doit pas survivre");
            StringAssert.Contains(html, "&lt;script&gt;", "elle est echappee");
            StringAssert.Contains(html, "&quot;", "le guillemet est echappe");
            StringAssert.Contains(html, "&#39;", "l'apostrophe est echappee");
            StringAssert.Contains(html, "&amp;", "l'esperluette est echappee");
        }

        // ---------------------------------------------------------------- archive

        [TestMethod]
        public void UneArchiveEstReconnueAvantUnModule()
        {
            // "W65A" commence par "W65". Tester le module d'abord ferait passer une
            // archive pour un module, et le lecteur Echouerait ensuite sur un
            // segment sorti du fichier, ce qui a l'air d'un fichier abime alors
            // que le test etait seulement dans le mauvais ordre.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                ModuleArchive archive = new ModuleArchive();
                archive.Members.Add(new ArchiveMember("M1", Sample()));
                archive.References.Add("base.w65a");

                string archivePath = Path.Combine(temp.Path, "lib.w65a");
                Assert.IsTrue(ArchiveFormat.Write(archivePath, archive).Success);

                byte[] archiveBytes = File.ReadAllBytes(archivePath);
                Assert.IsTrue(ModuleViewer.CanRead(archiveBytes), "une archive se lit");

                string modulePath = Path.Combine(temp.Path, "solo.w65");
                Assert.IsTrue(W65Format.Write(modulePath, Sample()).Success);
                Assert.IsTrue(ModuleViewer.CanRead(File.ReadAllBytes(modulePath)),
                    "un module se lit");

                string htmlPath = Path.Combine(temp.Path, "lib.html");
                OperationResult written = ModuleViewer.Write(htmlPath, archivePath);
                Assert.IsTrue(written.Success, "la vue d'archive s'ecrit");

                string html = File.ReadAllText(htmlPath);
                StringAssert.Contains(html, "lib", "le nom de l'archive");
                StringAssert.Contains(html, "base.w65a", "la reference");
                StringAssert.Contains(html, "Symbol index", "l'index de symboles");
                StringAssert.Contains(html, "Reset", "les exports des membres");
            }
        }

        [TestMethod]
        public void UnFichierInconnuEstRefuseAvecUnMessageQuiDitLesDeuxFormats()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "notes.txt");
                File.WriteAllText(path, "ceci n'est ni un module ni une archive");

                string htmlPath = Path.Combine(temp.Path, "out.html");
                OperationResult result = ModuleViewer.Write(htmlPath, path);

                Assert.IsFalse(result.Success);
                string message = string.Empty;
                for (int i = 0; i < result.Diagnostics.Count; i++)
                    message += result.Diagnostics[i].Message;

                StringAssert.Contains(message, "w65", "le message cite le format de module");
                StringAssert.Contains(message, "w65a", "et le format d'archive");
            }
        }

        [TestMethod]
        public void UneArchiveMontreQuelImportEstSatisfaitEtLequelNeLestPas()
        {
            // Le cas critique du plan : un import non resolu ne casse pas a la
            // compilation, il casse a l'execution. Il doit donc etre visible la.
            ModuleImage lib = new ModuleImage();
            lib.ModuleName = "LIB";
            lib.AddSegment(new ModuleSegment("CODE", new byte[] { 0x60 }, SegmentKind.Ro, 1, 0));
            lib.AddExport(new ModuleExport("DrawTile", 0, 0));
            lib.AddExport(new ModuleExport("UnusedHelper", 0, 0));

            ModuleImage app = new ModuleImage();
            app.ModuleName = "APP";
            app.AddSegment(new ModuleSegment("CODE", new byte[] { 0x60 }, SegmentKind.Ro, 1, 0));
            app.AddImport(new ModuleImport("DrawTile", "lib"));
            app.AddImport(new ModuleImport("MissingThing", "lib"));

            ModuleArchive archive = new ModuleArchive();
            archive.Members.Add(new ArchiveMember("LIB", lib));
            archive.Members.Add(new ArchiveMember("APP", app));

            string html = ModuleViewer.RenderArchive(archive, "lib.w65a");

            StringAssert.Contains(html, "satisfied", "l'import resolu est marque");
            StringAssert.Contains(html, "NOT SATISFIED", "l'import mort est signale");
            StringAssert.Contains(html, "MissingThing", "et il est nomme");
            StringAssert.Contains(html, "unused", "l'export que personne ne cite est marque");
            StringAssert.Contains(html, "UnusedHelper", "et il est nomme");
        }

        [TestMethod]
        public void UneArchiveMontreUnExportEnDoubleSansArreter()
        {
            // Deux membres qui exportent le meme nom : l'index du resolver refuse,
            // mais une page qui ne s'affiche pas ne sert a personne. Le conflit est
            // montre, la lecture continue.
            ModuleImage first = new ModuleImage();
            first.AddSegment(new ModuleSegment("CODE", new byte[] { 0x60 }, SegmentKind.Ro, 1, 0));
            first.AddExport(new ModuleExport("Twin", 0, 0));

            ModuleImage second = new ModuleImage();
            second.AddSegment(new ModuleSegment("CODE", new byte[] { 0x60 }, SegmentKind.Ro, 1, 0));
            second.AddExport(new ModuleExport("Twin", 0, 0));

            ModuleArchive archive = new ModuleArchive();
            archive.Members.Add(new ArchiveMember("FIRST", first));
            archive.Members.Add(new ArchiveMember("SECOND", second));

            string html = ModuleViewer.RenderArchive(archive, "dup.w65a");

            StringAssert.Contains(html, "Diagnostics", "le conflit est annonce");
            StringAssert.Contains(html, "exported twice", "la colonne le montre");
            StringAssert.Contains(html, "FIRST", "et les deux proprietaires sont nommes");
            StringAssert.Contains(html, "SECOND");
            StringAssert.Contains(html, "</html>", "la page est complete malgre le conflit");
        }

        [TestMethod]
        public void UneVueNeModifiePasLeFichierLu()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "solo.w65");
                Assert.IsTrue(W65Format.Write(path, Sample()).Success);
                byte[] before = File.ReadAllBytes(path);

                string htmlPath = Path.Combine(temp.Path, "solo.html");
                Assert.IsTrue(ModuleViewer.Write(htmlPath, path).Success);

                CollectionAssert.AreEqual(before, File.ReadAllBytes(path),
                    "le visualiseur est en lecture seule : le module doit etre intact");
            }
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }

            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "WinASM65View_" + Guid.NewGuid().ToString("N"));
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
