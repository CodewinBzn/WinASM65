// WinASM65 - BBC BASIC V
//
// The token table is the one thing here that cannot be checked by reading it
// back: an encoder and a reader that both hold the same wrong table agree with
// each other perfectly. So the tests start from the format itself: the two line
// numbers the reference derivation walks through, the record layout read back
// byte by byte, and a round trip for the rest.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.TextFormat;

namespace WinASM65.Tests
{
    [TestClass]
    public class BbcBasicTokenTests
    {
        [TestMethod]
        public void LaTableCommenceA7FEtLesTroisEchappementsSontReserves()
        {
            BasicDialect dialect = Dialect();
            byte token;
            string keyword;

            Assert.IsTrue(dialect.TryKeyword(0x7F, out keyword));
            Assert.AreEqual("OTHERWISE", keyword, "les tokens BBC commencent a $7F");
            Assert.IsTrue(dialect.TryKeyword(0x80, out keyword));
            Assert.AreEqual("AND", keyword);
            Assert.IsTrue(dialect.TryKeyword(0x8D, out keyword) == false,
                "$8D est la reference de ligne, pas un mot-cle");
            Assert.IsTrue(dialect.TryKeyword(0xF4, out keyword));
            Assert.AreEqual("REM", keyword);
            Assert.IsTrue(dialect.TryKeyword(0xFF, out keyword));
            Assert.AreEqual("OSCLI", keyword, "$FF est le dernier mot-cle");

            for (byte b = 0xC6; b <= 0xC8; b++)
            {
                Assert.IsFalse(dialect.TryKeyword(b, out keyword),
                    "$" + b.ToString("X2") + " est un echappement, pas un mot-cle");
            }

            Assert.IsTrue(dialect.TryToken("SUM", out token));
            Assert.AreEqual(0x8E, token, "les tokens etendus commencent a $8E");
        }

        [TestMethod]
        public void UnTokenEtenduSeEcritApresSonEchappement()
        {
            BasicDialect dialect = Dialect();
            byte escape;
            Assert.IsTrue(dialect.TryEscape("CASE", out escape));
            Assert.AreEqual(0xC8, escape, "CASE est un enonce etendu");
            Assert.IsTrue(dialect.TryEscape("RENUMBER", out escape));
            Assert.AreEqual(0xC7, escape, "RENUMBER est une commande etendue");
            Assert.IsTrue(dialect.TryEscape("SUM", out escape));
            Assert.AreEqual(0xC6, escape, "SUM est une fonction etendue");
            Assert.IsFalse(dialect.TryEscape("PRINT", out escape), "PRINT tient en un octet");
        }

        [TestMethod]
        public void UneLigneEstUnRetourALaLigneUnNumeroUneLongueurPuisLesTokens()
        {
            byte[] file = Encode(new BasicLine(10, "PRINT \"HI\""));

            CollectionAssert.AreEqual(new byte[]
            {
                0x0D, 0x00, 0x0A, 0x09, 0xF1, (byte)'"', (byte)'H', (byte)'I', (byte)'"'
            }, Slice(file, 0, 9), "ligne 10 : CR, $000A, longueur 9, PRINT, la chaine avec ses guillemets");

            Assert.AreEqual(0x0D, file[file.Length - 2], "le programme se termine par un retour a la ligne");
            Assert.AreEqual(0xFF, file[file.Length - 1], "et par $FF");
        }

        [TestMethod]
        public void UneChaineGardeSesGuillemets()
        {
            // Applesoft writes a length and the bytes. BBC BASIC keeps the
            // quotes and reads to the closing one.
            byte[] body = Body(10, "PRINT \"A\"");

            CollectionAssert.AreEqual(new byte[] { 0xF1, (byte)'"', (byte)'A', (byte)'"' }, body);
        }

        [TestMethod]
        public void UnGuillemetDoubleCompteUneSeuleFois()
        {
            byte[] body = Body(10, "PRINT \"A\"\"B\"");

            CollectionAssert.AreEqual(new byte[]
            {
                0xF1, (byte)'"', (byte)'A', (byte)'"', (byte)'B', (byte)'"'
            }, body, "le guillemet ecrit deux fois n'en occupe qu'un dans la chaine");
        }

        [TestMethod]
        public void UneReferenceALigneOccupeTroisOctetsQuiRestentEnASCII()
        {
            // The two numbers the reference derivation works through. The point of
            // the encoding is that no byte of it can be read as a token, so the
            // interpreter can scan a line for ELSE without tripping over a GOTO.
            Assert.AreEqual(139, Reference(139));
            CollectionAssert.AreEqual(new byte[] { 0x74, 0x4B, 0x40 }, Packed(139));
            CollectionAssert.AreEqual(new byte[] { 0x64, 0x4C, 0x40 }, Packed(204));
            Assert.AreEqual(204, Reference(204));

            foreach (int number in new int[] { 0, 1, 127, 128, 255, 256, 1000, 32767, 32768, 65279 })
            {
                Assert.AreEqual(number, Reference(number), "l'aller-retour de " + number);
                foreach (byte b in Packed(number))
                {
                    Assert.IsTrue(b >= 0x20 && b < 0x80,
                        "l'octet $" + b.ToString("X2") + " de " + number + " doit rester en ASCII");
                }
            }
        }

        [TestMethod]
        public void UnGotoEcritUneReferenceEtUnNombreOrdinaireResteDesChiffres()
        {
            byte[] body = Body(10, "GOTO 100");
            CollectionAssert.AreEqual(new byte[] { 0xE5, 0x8D, 0x44, 0x64, 0x40 }, body,
                "GOTO 100 : le token, puis les trois octets de la reference");

            body = Body(10, "GOSUB 100");
            Assert.AreEqual(0xE4, body[0], "GOSUB est bien un mot-cle");
            Assert.AreEqual(0x8D, body[1], "et sa cible aussi");

            body = Body(10, "X = 100");
            CollectionAssert.AreEqual(new byte[] { (byte)'X', (byte)'=', (byte)'1', (byte)'0', (byte)'0' }, body,
                "une valeur ordinaire reste des chiffres");

            body = Body(10, "GOTO 100 : PRINT 200");
            Assert.AreEqual(0x8D, body[1], "la premiere cible est une reference");
            Assert.AreEqual(0x3A, body[5], "puis le separateur d'instructions");
            Assert.AreEqual(0xF1, body[6], "PRINT est un mot-cle");
            Assert.AreEqual((byte)'2', body[7], "et 200 est une valeur");
        }

        [TestMethod]
        public void UneLigneTropLongueEstRefusee()
        {
            List<BasicLine> lines = new List<BasicLine>
            {
                new BasicLine(10, "REM " + new string('x', 300))
            };
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            new TokenEncoder(Dialect(), diagnostics).Encode(lines);

            Assert.AreEqual(1, diagnostics.Count, "une ligne BBC ne peut pas depasser 251 octets");
            StringAssert.Contains(diagnostics[0].ToString(), "251");
        }

        [TestMethod]
        public void UnNumeroDeLigneTropGrandEstRefuse()
        {
            List<BasicLine> lines = new List<BasicLine>
            {
                new BasicLine(10, "END"),
                new BasicLine(65280, "END")
            };
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            new TokenEncoder(Dialect(), diagnostics).Encode(lines);

            Assert.AreEqual(1, diagnostics.Count, "$FF dans l'octet haut marque la fin du programme");
            StringAssert.Contains(diagnostics[0].ToString(), "65279");
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
                "REPORT",
                "ATN(1)",
                "VDU 23,1,0;0;0;0;",
                "CASE X OF",
                "  WHEN 1 : PRINT \"ONE\"",
                "  ENDCASE",
                "MOUSE X,Y,B",
                "SYS \"OSCLI\", \"DIR\"",
                "RENUMBER",
                "DATA 1,2,3",
                "READ A$, B$",
                "REM  this is not code + 1",
                "SYS A$",
                "ENVELOPE 1,2,3,4,5,6,7,8,9,10,11,12",
                ""
            };

            List<BasicLine> original = new List<BasicLine>();
            for (int i = 0; i < bodies.Length; i++)
                original.Add(new BasicLine(10 * (i + 1), bodies[i]));

            byte[] file = new TokenEncoder(Dialect(), new List<Diagnostic>()).Encode(original);
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            List<BasicLine> back = new TokenDecoder(Dialect(), diagnostics).Decode(file);

            Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
            Assert.AreEqual(original.Count, back.Count);

            byte[] again = new TokenEncoder(Dialect(), new List<Diagnostic>()).Encode(back);
            Assert.AreEqual(Hex(file), Hex(again),
                "lire puis reecrire doit rendre le fichier octet pour octet");
        }

        [TestMethod]
        public void UnEnTeteDeCommandeEtenduEstReluSousSonNom()
        {
            byte[] file = Encode(new BasicLine(10, "RENUMBER"));
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            List<BasicLine> back = new TokenDecoder(Dialect(), diagnostics).Decode(file);

            Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
            Assert.AreEqual(1, back.Count);
            Assert.AreEqual("RENUMBER", back[0].Body);
        }

        [TestMethod]
        public void UneLigneSansRetourALaLigneEstSignalee()
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            new TokenDecoder(Dialect(), diagnostics).Decode(new byte[] { 0x00, 0x00, 0x0A, 0x08 });

            Assert.AreEqual(1, diagnostics.Count);
            StringAssert.Contains(diagnostics[0].ToString(), "carriage return");
        }

        [TestMethod]
        public void UneLongueurQuiDepasseLeFichierEstSignalee()
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            new TokenDecoder(Dialect(), diagnostics).Decode(
                new byte[] { 0x0D, 0x00, 0x0A, 0x40, 0xF0 });

            Assert.AreEqual(1, diagnostics.Count);
            StringAssert.Contains(diagnostics[0].ToString(), "length");
        }

        [TestMethod]
        public void LeDrapeauDeReferenceDebordeDeLaLigneSurLeSuivant()
        {
            // The ROM crunches its leading line number with the reference flag
            // already set, and encoding a number does not clear it. So a line
            // that opens with a value keyword stores its number as a reference,
            // and a second number behind a GOTO is one as well.
            byte[] body = Body(10, "TO 1");
            CollectionAssert.AreEqual(new byte[] { 0xB8, 0x8D, 0x54, 0x41, 0x40 }, body,
                "TO 1 : le 1 devient une reference");

            body = Body(10, "PRINT 200");
            CollectionAssert.AreEqual(new byte[] { 0xF1, (byte)'2', (byte)'0', (byte)'0' }, body,
                "PRINT leve le drapeau, donc 200 reste une valeur");

            body = Body(10, "GOTO 100 200");
            Assert.AreEqual(0x8D, body[1], "la premiere cible est une reference");
            Assert.AreEqual(0x8D, body[5], "et ecrire une reference n'a pas leve le drapeau");
        }

        [TestMethod]
        public void UnProgrammeEnTexteEstCeQuOnLit()
        {
            // The machine tokenises a text file when it loads it, so the text form
            // is the one a person reads.
            List<BasicLine> lines = new List<BasicLine>
            {
                new BasicLine(10, "REM hello"),
                new BasicLine(20, "PRINT \"HI\""),
                new BasicLine(30, "GOTO 20")
            };

            Assert.AreEqual("10 REM hello\n20 PRINT \"HI\"\n30 GOTO 20\n",
                BbcBasicText.Render(lines));

            // And the tokenised form is a different thing, of the same program.
            byte[] file = new TokenEncoder(Dialect(), new List<Diagnostic>()).Encode(lines);
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            List<BasicLine> back = new TokenDecoder(Dialect(), diagnostics).Decode(file);

            Assert.AreEqual(0, diagnostics.Count, Describe(diagnostics));
            Assert.AreEqual(3, back.Count);
            Assert.AreEqual(10, back[0].Number);
            Assert.AreEqual(30, back[2].Number);
        }

        private static BasicDialect Dialect()
        {
            return BbcBasicDialect.Create();
        }

        private static byte[] Encode(params BasicLine[] lines)
        {
            return new TokenEncoder(Dialect(), new List<Diagnostic>()).Encode(lines);
        }

        private static byte[] Slice(byte[] data, int from, int count)
        {
            byte[] result = new byte[count];
            Array.Copy(data, from, result, 0, count);
            return result;
        }

        private static byte[] Body(int lineNumber, string body)
        {
            byte[] file = Encode(new BasicLine(lineNumber, body));

            // Skip the carriage return, the two byte line number and the length,
            // and the two bytes that end the program.
            List<byte> only = new List<byte>();
            for (int i = 4; i < file.Length - 2; i++)
                only.Add(file[i]);
            return only.ToArray();
        }

        private static byte[] Packed(int number)
        {
            List<byte> body = new List<byte>();
            BbcBasicDialect.EncodeReference(number, body);

            List<byte> three = new List<byte>();
            for (int i = 1; i < body.Count; i++)
                three.Add(body[i]);
            return three.ToArray();
        }

        private static int Reference(int number)
        {
            List<byte> body = new List<byte>();
            BbcBasicDialect.EncodeReference(number, body);
            return BbcBasicDialect.DecodeReference(body[1], body[2], body[3]);
        }

        private static string Hex(byte[] data)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < data.Length; i++)
                parts.Add(data[i].ToString("X2"));
            return string.Join(" ", parts.ToArray());
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