using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.TextFormat;

namespace WinASM65.Tests
{
    /// <summary>
    /// L'encodeur de tokens Applesoft.
    /// <para>
    /// Les tests couvrent deux choses. La premiere est que la table est celle du
    /// materiel, verifiee sur quelques valeurs publiees. La seconde est
    /// l'aller-retour : ce qu'un lecteur ecrit depuis le format lit doit
    /// redonner le source, ce qui prouve que le fichier dit ce qu'il pretend.
    /// </para>
    /// <para>
    /// Ce que cela ne prouve pas : qu'Applesoft accepte le fichier. Ca demande
    /// l'interpreteur sur une machine.
    /// </para>
    /// </summary>
    [TestClass]
    public class ApplesoftTokenTests
    {
        private static BasicDialect Dialect()
        {
            return ApplesoftDialect.Create();
        }

        private static byte[] Encode(params BasicLine[] lines)
        {
            return new TokenEncoder(Dialect(), new List<Diagnostic>()).Encode(lines);
        }

        private static byte[] Body(int lineNumber, string body)
        {
            byte[] file = Encode(new BasicLine(lineNumber, body));

            // Skip the two byte next-address, the two byte line number, and the
            // two zero bytes the file ends on.
            List<byte> only = new List<byte>();
            for (int i = 4; i < file.Length - 2; i++)
                only.Add(file[i]);
            return only.ToArray();
        }

        [TestMethod]
        public void LaTableEstCelleDuMateriel()
        {
            // A handful of published values. A table built from memory would be
            // plausible everywhere and wrong in one place, and one place is
            // enough.
            BasicDialect dialect = Dialect();

            byte token;
            string keyword;

            Assert.IsTrue(dialect.TryToken("END", out token));
            Assert.AreEqual(0x80, token, "END");
            Assert.IsTrue(dialect.TryToken("GOTO", out token));
            Assert.AreEqual(0xAB, token, "GOTO");
            Assert.IsTrue(dialect.TryToken("GOSUB", out token));
            Assert.AreEqual(0xB0, token, "GOSUB");
            Assert.IsTrue(dialect.TryToken("PRINT", out token));
            Assert.AreEqual(0xBA, token, "PRINT");
            Assert.IsTrue(dialect.TryToken("REM", out token));
            Assert.AreEqual(0xB2, token, "REM");
            Assert.IsTrue(dialect.TryToken("FOR", out token));
            Assert.AreEqual(0x81, token, "FOR");
            Assert.IsTrue(dialect.TryToken("MID$", out token));
            Assert.AreEqual(0xEA, token, "MID$ est le dernier mot-cle");
            Assert.IsTrue(dialect.TryToken("DEF FN", out token));
            Assert.AreEqual(0xB8, token, "DEF FN est un seul token");
            Assert.IsTrue(dialect.TryToken("FN", out token));
            Assert.AreEqual(0xC2, token, "FN seul a son propre token");
            Assert.IsTrue(dialect.TryToken("SPC(", out token));
            Assert.AreEqual(0xC3, token, "SPC( inclut la parenthese");
            Assert.IsTrue(dialect.TryToken("&", out token));
            Assert.AreEqual(0xAF, token, "& est un token");

            Assert.IsTrue(dialect.TryKeyword(0x96, out keyword));
            Assert.AreEqual("HTAB", keyword, "HTAB occupe la valeur que la ligne binaire utilise");

            // $EB to $FF are not defined. Inventing names for them would let the
            // encoder emit a token the interpreter has no meaning for.
            for (int b = 0xEB; b <= 0xFF; b++)
            {
                Assert.IsFalse(dialect.TryKeyword((byte)b, out keyword),
                    "$" + b.ToString("X2") + " ne doit pas avoir de nom");
            }
        }

        [TestMethod]
        public void UneInstructionCourteEstLeMotCleSuiviDeLaChaine()
        {
            // 10 PRINT "HELLO" -> 0A BA 05 48 45 4C 4C 4F 00
            byte[] body = Body(10, "PRINT \"HELLO\"");

            CollectionAssert.AreEqual(new byte[]
            {
                0xBA, 0x05, 0x48, 0x45, 0x4C, 0x4C, 0x4F, 0x00
            }, body);
        }

        [TestMethod]
        public void LePointDInterrogationEstLaFormeCourteDePrint()
        {
            // Applesoft spends no byte on '?', so the parser has to be the one
            // that knows. The file must be identical either way.
            CollectionAssert.AreEqual(Body(10, "PRINT 1"), Body(10, "? 1"));
        }

        [TestMethod]
        public void LaParentheseEstLaFormeCourteDeGotoEnDebutDInstruction()
        {
            CollectionAssert.AreEqual(Body(10, "GOTO 100"), Body(10, "(100)"),
                "la parenthese fermante apartient a la forme courte, pas a la ligne");
        }

        [TestMethod]
        public void UneParentheseSeuleAuMilieuResteUneParenthese()
        {
            byte[] body = Body(10, "PRINT (1+2)");
            CollectionAssert.AreEqual(new byte[]
            {
                0xBA, 0x28, 0x31, 0xC8, 0x32, 0x29, 0x00
            }, body);
        }

        [TestMethod]
        public void LesOperateursDeviennentDesTokensEtLaVirguleNon()
        {
            // '+' spends a byte and ':' does not, which is the sort of thing an
            // encoder gets wrong by treating every character the same.
            byte[] body = Body(10, "A = B + C : D = 2 * 3");
            CollectionAssert.AreEqual(new byte[]
            {
                (byte)'A', 0xD0, (byte)'B', 0xC8, (byte)'C', 0x3A,
                (byte)'D', 0xD0, (byte)'2', 0xCA, (byte)'3', 0x00
            }, body);
        }

        [TestMethod]
        public void UnMotCleQuiPrefixeUnAutreEstLeBon()
        {
            // ATN is the arc tangent, AT N is the AT statement, ONERR GOTO is
            // one keyword and ON is another. A parser that takes the first match
            // gets all three wrong, and the program fails at run time where
            // nothing in the file looks unusual.
            CollectionAssert.AreEqual(new byte[] { 0xC5, (byte)'N', 0x00 }, Body(10, "AT N"));
            CollectionAssert.AreEqual(new byte[] { 0xE1, 0x00 }, Body(10, "ATN"));
            CollectionAssert.AreEqual(new byte[] { 0xA5, 0xAB, (byte)'0', 0x00 }, Body(10, "ONERR GOTO 0"));
            CollectionAssert.AreEqual(new byte[] { 0xB4, 0x00 }, Body(10, "ON"));

            // A TO is A, then TO: the space is the only thing that says so.
            byte[] shifted = Body(10, "A TO B");
            CollectionAssert.AreEqual(new byte[] { (byte)'A', 0xC1, (byte)'B', 0x00 }, shifted);
        }

        [TestMethod]
        public void UnNomDeVariableGardeSaChaineEtSesSuffixes()
        {
            CollectionAssert.AreEqual(new byte[]
            {
                (byte)'A', 0xD0, (byte)'B', 0xC8, (byte)'1', (byte)'0', 0x00
            }, Body(10, "A = B+10"), "le + est un token, pas du texte");

            CollectionAssert.AreEqual(new byte[]
            {
                (byte)'A', (byte)'$', 0xD0, (byte)'B', 0xD1, (byte)'C', (byte)'$', 0x00
            }, Body(10, "A$ = B<C$"), "le $ du nom fait partie du nom");
        }

        [TestMethod]
        public void LesNombresRestentDuTexte()
        {
            // Numbers are stored as written. A binary float would be smaller and
            // would not be what the interpreter expects to find.
            byte[] body = Body(10, "X = 1.5E10");
            CollectionAssert.AreEqual(new byte[]
            {
                (byte)'X', 0xD0, (byte)'1', (byte)'.', (byte)'5', (byte)'E', (byte)'1', (byte)'0', 0x00
            }, body);
        }

        [TestMethod]
        public void UneChaineCompteSesOctetsPasSesGuillemets()
        {
            byte[] body = Body(10, "PRINT \"A\"\"B\"");
            // Three bytes: A, quote, B. The doubled quote in the source is one
            // byte in the file, so the length must count it once.
            CollectionAssert.AreEqual(new byte[]
            {
                0xBA, 0x03, 0x41, 0x22, 0x42, 0x00
            }, body);
        }

        [TestMethod]
        public void UneChaineJamaisFermeeEstSignalee()
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            new TokenEncoder(Dialect(), diagnostics).Encode(new List<BasicLine>
            {
                new BasicLine(10, "PRINT \"OUI")
            });

            Assert.AreEqual(1, diagnostics.Count, "une chaine ouverte doit etre signalee");
            StringAssert.Contains(diagnostics[0].ToString(), "never closed");
        }

        [TestMethod]
        public void LeResteDUneLigneRemEstDuTexte()
        {
            // REM takes the line as it stands, spaces included. Tokenising it
            // would change what the program prints.
            byte[] body = Body(10, "REM  PRINT \"HI\" + 1");
            CollectionAssert.AreEqual(new byte[]
            {
                0xB2, (byte)' ', (byte)' ', (byte)'P', (byte)'R', (byte)'I', (byte)'N', (byte)'T',
                (byte)' ', 0x22, (byte)'H', (byte)'I', 0x22, (byte)' ', (byte)'+', (byte)' ', (byte)'1', 0x00
            }, body);
        }

        [TestMethod]
        public void UneLigneBinaireEstEmiseTelleQuelle()
        {
            // $96 opens a line of raw bytes, and it is the same value as HTAB,
            // so it is written in the source and only the position tells them
            // apart.
            byte[] file = Encode(BasicLine.Raw_(10, new byte[] { 0x01, 0x02, 0xFF }));

            // The two byte next-address, the two byte line number, and the two
            // zero bytes the file ends on.
            byte[] body = new byte[file.Length - 6];
            Array.Copy(file, 4, body, 0, body.Length);

            CollectionAssert.AreEqual(new byte[] { 0x96, 0x01, 0x02, 0xFF, 0x00 }, body);
        }

        [TestMethod]
        public void LaChaineDAdressesEstTraiteeCommeUnNombre()
        {
            // POKE 16384,0 is POKE $4000,0, and the address is two bytes, not a
            // variable named by a dollar sign.
            byte[] body = Body(10, "POKE $4000,0");
            CollectionAssert.AreEqual(new byte[]
            {
                0xB9, (byte)'$', (byte)'4', (byte)'0', (byte)'0', (byte)'0', (byte)',', (byte)'0', 0x00
            }, body);
        }

        [TestMethod]
        public void UnNumeroDeLigneHorsLimiteEstRefuse()
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            new TokenEncoder(Dialect(), diagnostics).Encode(new List<BasicLine>
            {
                new BasicLine(10, "END"),
                new BasicLine(70000, "END")
            });

            Assert.AreEqual(1, diagnostics.Count, "63999 est le maximum d'Applesoft");
            StringAssert.Contains(diagnostics[0].ToString(), "63999");
        }

        [TestMethod]
        public void LaChaineDAdressesDesLignesEstContinue()
        {
            // The chain is the interpreter's index. A wrong next address gives a
            // file that loads and does nothing.
            List<BasicLine> lines = new List<BasicLine>
            {
                new BasicLine(10, "PRINT \"A\""),
                new BasicLine(20, "PRINT \"B\""),
                new BasicLine(30, "END")
            };
            byte[] file = Encode(lines.ToArray());

            // PRINT "A" is BA 01 41 00, so that record is eight bytes; END is
            // $80 and the $00 that ends the line, so that one is six.
            int first = 0x0801;
            int second = first + 8;
            int third = second + 8;
            int end = third + 6;

            Assert.AreEqual(0x0801, ApplesoftDialect.Create().ProgramBase,
                "un programme Applesoft commence a $0801");
            Assert.AreEqual(end - first + 2, file.Length,
                "le fichier va de la premiere ligne a l'adresse nulle de fin");
            Assert.AreEqual(second & 0xFF, file[0], "pointeur de la premiere ligne");
            Assert.AreEqual((second >> 8) & 0xFF, file[1]);
            Assert.AreEqual(third & 0xFF, file[8], "pointeur de la deuxieme ligne");
            Assert.AreEqual((third >> 8) & 0xFF, file[9]);
            Assert.AreEqual(end & 0xFF, file[16], "la derniere ligne pointe sur la fin");
            Assert.AreEqual((end >> 8) & 0xFF, file[17]);
            Assert.AreEqual(0, file[file.Length - 2], "la fin du fichier est une adresse nulle");
            Assert.AreEqual(0, file[file.Length - 1]);
        }

        [TestMethod]
        public void UnAllerRetourRedonneLeMemeFichier()
        {
            string[] bodies = new string[]
            {
                "PRINT \"HELLO\"",
                "PRINT \"FOR\"",
                "FOR I = 1 TO 10 : NEXT I",
                "IF A = B THEN PRINT \"SAME\" : GOTO 100",
                "A$ = \"QUOTE\"\" HERE\"",
                "GOSUB 1000 : RETURN",
                "? 1 + 2 * 3",
                "(100)",
                "AT N : ATN(X)",
                "HOME : VTAB 10 : INVERSE : NORMAL",
                "HIMEM: 16384 : LOMEM: 1024",
                "DATA 1,2,3",
                "REM  this is not code + 1",
                "READ A$, B$ : POKE -16368,0 : CALL -936",
                ""
            };

            List<BasicLine> original = new List<BasicLine>();
            for (int i = 0; i < bodies.Length; i++)
                original.Add(new BasicLine(10 * (i + 1), bodies[i]));
            original.Insert(5, BasicLine.Raw_(200, new byte[] { 0x00, 0x7F, 0x96, 0xFF }));

            byte[] file = new TokenEncoder(Dialect(), new List<Diagnostic>()).Encode(original);
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            List<BasicLine> back = new TokenDecoder(Dialect(), diagnostics).Decode(file);

            Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
            Assert.AreEqual(original.Count, back.Count);

            byte[] again = new TokenEncoder(Dialect(), new List<Diagnostic>()).Encode(back);
            Assert.AreEqual(Hex(file), Hex(again), "lire puis reecrire doit rendre le fichier octet pour octet");
        }

        private static string Hex(byte[] data)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < data.Length; i++)
                parts.Add(data[i].ToString("X2"));
            return string.Join(" ", parts.ToArray());
        }

        [TestMethod]
        public void UneLigneBinaireEstRelue()
        {
            List<BasicLine> original = new List<BasicLine>
            {
                new BasicLine(10, "PRINT \"START\""),
                BasicLine.Raw_(20, new byte[] { 0x00, 0x7F, 0x80, 0xFF }),
                new BasicLine(30, "END")
            };

            byte[] file = new TokenEncoder(Dialect(), new List<Diagnostic>()).Encode(original);
            List<BasicLine> back = new TokenDecoder(Dialect(), new List<Diagnostic>()).Decode(file);

            Assert.AreEqual(3, back.Count);
            Assert.AreEqual("", back[1].Body, "une ligne binaire revient comme des octets, pas comme du texte");
            CollectionAssert.AreEqual(new byte[] { 0x00, 0x7F, 0x80, 0xFF }, back[1].Raw);
            Assert.AreEqual(20, back[1].Number);
        }

        [TestMethod]
        public void UneLigneSansTerminateurEstSignalee()
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            // A well formed chain whose first line does not end in $00.
            new TokenDecoder(Dialect(), diagnostics).Decode(
                new byte[] { 0x07, 0x08, 0x0A, 0x00, 0xBA, 0x41, 0xFF, 0x00, 0x00 });

            Assert.AreEqual(1, diagnostics.Count, "une ligne sans $00 n'est pas relisible");
            StringAssert.Contains(diagnostics[0].ToString(), "terminated");
        }

        [TestMethod]
        public void UneChaineDAdressesHorsDuFichierEstSignalee()
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            new TokenDecoder(Dialect(), diagnostics).Decode(new byte[] { 0x0A, 0x08, 0x0A, 0x00, 0xBA });

            Assert.AreEqual(1, diagnostics.Count);
            StringAssert.Contains(diagnostics[0].ToString(), "080A");
        }

        [TestMethod]
        public void UnTokenInconnuEstSignaleEtNonDevine()
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            new TokenDecoder(Dialect(), diagnostics).Decode(new byte[] { 0x07, 0x08, 0x0A, 0x00, 0xF3, 0x00, 0x00, 0x00 });

            Assert.AreEqual(1, diagnostics.Count, "$F3 n'a pas de nom dans cette table");
            StringAssert.Contains(diagnostics[0].ToString(), "F3");
        }

        private static string Describe(IReadOnlyList<Diagnostic> diagnostics)
        {
            string text = string.Empty;
            foreach (Diagnostic diagnostic in diagnostics)
                text += diagnostic.ToString() + " | ";
            return text;
        }
    }
}
