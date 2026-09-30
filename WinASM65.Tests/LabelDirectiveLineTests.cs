using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;

namespace WinASM65.Tests
{
    /// <summary>
    /// Une etiquette qui partage sa ligne avec une directive.
    /// <para>
    /// DirectiveRegex n'etait pas ancre, donc sur <c>Data: .byte $AA</c> il trouvait
    /// la directive au milieu de la ligne et gagnait le dispatch : la directive
    /// emettait ses octets et le nom disparaisait. Une reference ulterieure
    /// resolvait alors vers $0000, sans aucun diagnostic. Ces tests verrouillent le
    /// comportement correct, pas seulement l'absence de plantage.
    /// </para>
    /// </summary>
    [TestClass]
    public class LabelDirectiveLineTests
    {
        [TestMethod]
        public void EtiquetteAvecByte_EstDefinieALaBonneAdresse()
        {
            // Le nop initial place l'etiquette une adresse apres .org : une etiquette
            // posee exactement sur l'adresse de .org est un defaut distinct, et ces
            // tests ne doivent pas le confondre avec celui qu'ils couvrent.
            byte[] output = Assemble("        .org $C000\n        nop\nData:   .byte $AA\n        nop\n        lda Data\n");
            CollectionAssert.AreEqual(
                new byte[] { 0xEA, 0xAA, 0xEA, 0xAD, 0x01, 0xC0 },
                output,
                "Data vaut $C001, l'adresse ou le .byte est ecrit");
        }

        [TestMethod]
        public void EtiquetteAvecWord_EstDefinieALaBonneAdresse()
        {
            // "Table" et non un nom de trois lettres : un operande de trois lettres
            // exactement est un defaut distinct du analyseur d'instruction, et ces
            // tests ne doivent pas le melanger avec celui qu'ils couvrent.
            byte[] output = Assemble("        .org $C000\n        nop\nTable:  .word $BBCC\n        lda Table\n");
            CollectionAssert.AreEqual(
                new byte[] { 0xEA, 0xCC, 0xBB, 0xAD, 0x01, 0xC0 },
                output,
                "le .word est ecrit en petit-boutiste et Table vaut $C001");
        }

        [TestMethod]
        public void EtiquetteAvecByteSansEspace_EstDefinie()
        {
            byte[] output = Assemble("        .org $C000\n        nop\nFlag:   .byte $01\n        lda Flag\n");
            CollectionAssert.AreEqual(new byte[] { 0xEA, 0x01, 0xAD, 0x01, 0xC0 }, output);
        }

        [TestMethod]
        public void DeuxEtiquettesSurUneDirective_SontChacuneDefinies()
        {
            byte[] output = Assemble("        .org $C000\n        nop\nA:      .byte $01\nB:      .byte $02\n        lda A\n        lda B\n");
            CollectionAssert.AreEqual(
                new byte[] { 0xEA, 0x01, 0x02, 0xAD, 0x01, 0xC0, 0xAD, 0x02, 0xC0 },
                output,
                "A vaut $C001 et B vaut $C002 : l etiquette precede l emission de sa directive");
        }

        [TestMethod]
        public void UneEtiquetteSeuleContinueDeValoir()
        {
            byte[] output = Assemble("        .org $C000\n        nop\nSolo:\n        .byte $07\n        lda Solo\n");
            CollectionAssert.AreEqual(new byte[] { 0xEA, 0x07, 0xAD, 0x01, 0xC0 }, output,
                "l forme sur une ligne seule ne change pas");
        }

        [TestMethod]
        public void UneEtiquetteAvecInstructionContinueDEtreEmise()
        {
            byte[] output = Assemble("        .org $C000\n        nop\n        nop\n");
            CollectionAssert.AreEqual(new byte[] { 0xEA, 0xEA }, output,
                "le correctif ne doit rien changer aux lignes d'instruction");
        }

        [TestMethod]
        public void EtiquetteAvecInstruction_EmetLECode()
        {
            // LabelDeclareRegex n'etait pas ancre : sur "Start: sei" il consommait
            // "Start:" et laissait "sei" sans plus rien a matcher. La ligne entiere
            // etait alors perdue en silence, l'etiquette restant definie a la bonne
            // adresse : l'assemblage reussissait et produisait un octet de moins.
            byte[] output = Assemble("        .org $C000\nStart:  sei\n        rts\n");
            CollectionAssert.AreEqual(new byte[] { 0x78, 0x60 }, output,
                "sei doit etre emis, pas seulement l etiquette qu'il porte");
        }

        [TestMethod]
        public void EtiquetteAvecInstruction_EtiquetteViseeALaBonneAdresse()
        {
            byte[] output = Assemble("        .org $C000\nStart:  sei\nAgain:  rts\n        lda Again\n");
            CollectionAssert.AreEqual(new byte[] { 0x78, 0x60, 0xAD, 0x01, 0xC0 }, output,
                "Again vaut $C001, l adresse reelle ou rts est ecrit");
        }

        [TestMethod]
        public void EtiquetteAvecInstructionEtOperande_EmetLECode()
        {
            byte[] output = Assemble("        .org $C000\nValue:  lda #$01\n        rts\n");
            CollectionAssert.AreEqual(new byte[] { 0xA9, 0x01, 0x60 }, output,
                "l operande ne doit pas etre perdu avec l'instruction");
        }

        [TestMethod]
        public void EtiquetteAvecInstructionSansEspace_EstEmise()
        {
            byte[] output = Assemble("        .org $C000\nTight:  nop\n        rts\n");
            CollectionAssert.AreEqual(new byte[] { 0xEA, 0x60 }, output);
        }

        [TestMethod]
        public void InstructionAvecEtiquetteSansDeuxPointContinueDEtreEmise()
        {
            byte[] output = Assemble("        .org $C000\nPlain  nop\n        rts\n");
            CollectionAssert.AreEqual(new byte[] { 0xEA, 0x60 }, output,
                "l forme sans deux-point existait deja et doit rester inchangee");
        }

        [TestMethod]
        public void DeuxEtiquettesAvecInstruction_SontChacuneDefinies()
        {
            byte[] output = Assemble("        .org $C000\nA:      nop\nB:      rts\n        lda A\n        lda B\n");
            CollectionAssert.AreEqual(
                new byte[] { 0xEA, 0x60, 0xAD, 0x00, 0xC0, 0xAD, 0x01, 0xC0 },
                output,
                "A vaut $C000 et B vaut $C001");
        }

        private static byte[] Assemble(string source)
        {
            string directory = Path.Combine(Path.GetTempPath(), "WinASM65Label_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string sourcePath = Path.Combine(directory, "unit.asm");
                File.WriteAllText(sourcePath, source);
                AssemblyResult result = new AssemblerEngine().Assemble(sourcePath, Path.Combine(directory, "unit.o"));
                Assert.IsTrue(result.Success, Describe(result.Diagnostics));
                Assert.AreEqual(0, result.Diagnostics.Count, "aucun diagnostic attendu");
                return result.OutputBytes;
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string Describe(System.Collections.Generic.IReadOnlyList<Diagnostic> diagnostics)
        {
            string text = string.Empty;
            foreach (Diagnostic diagnostic in diagnostics)
                text += diagnostic.ToString() + " | ";
            return text;
        }
    }
}
