// WinASM65 - Waterloo Structured BASIC
//
// Waterloo Structured BASIC is Commodore BASIC V2 plus twelve words written
// after $FF $FF, so most of what is checked here is the host's table and the
// host's container. The tests start from the format rather than from the
// encoder: the token bytes are compared with the published table one by one,
// the line chain is walked back out of the bytes, and a round trip covers the
// rest.
//
// The one thing that cannot be checked by reading the file back is whether the
// table is the cartridge's. It is transcribed from the reverse engineering of
// the VIC-20 cartridge and from the C64 wiki, and nothing here executes on a
// Commodore.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.TextFormat;

namespace WinASM65.Tests
{
    [TestClass]
    public class WaterlooBasicTokenTests
    {
        [TestMethod]
        public void LaTableDeLHoteEstCelleDeCommodoreBasicV2()
        {
            BasicDialect dialect = Dialect();
            string keyword;

            Assert.IsTrue(dialect.TryKeyword(0x80, out keyword));
            Assert.AreEqual("END", keyword);
            Assert.IsTrue(dialect.TryKeyword(0x89, out keyword));
            Assert.AreEqual("GOTO", keyword);
            Assert.IsTrue(dialect.TryKeyword(0x8D, out keyword));
            Assert.AreEqual("GOSUB", keyword);
            Assert.IsTrue(dialect.TryKeyword(0x99, out keyword));
            Assert.AreEqual("PRINT", keyword);
            Assert.IsTrue(dialect.TryKeyword(0xA7, out keyword));
            Assert.AreEqual("THEN", keyword);
            Assert.IsTrue(dialect.TryKeyword(0xCB, out keyword));
            Assert.AreEqual("GO", keyword, "$CB est le dernier mot-cle de l'hote");

            // The host's IF is left out on purpose: the cartridge has its own IF,
            // and a table gives one word one byte.
            Assert.IsFalse(dialect.TryKeyword(0x8B, out keyword),
                "le IF de l'hote n'est pas dans la table");

            byte token;
            Assert.IsTrue(dialect.TryToken("IF", out token));
            Assert.AreEqual(0xF3, token, "IF est le mot du cartouch");
        }

        [TestMethod]
        public void LesDouzeMotsDuCartouchSontEcritsApresFF()
        {
            BasicDialect dialect = Dialect();
            string[] names = new string[]
            {
                "IF", "CALL", "LOOP", "ENDLOOP", "UNTIL", "WHILE", "ELSEIF",
                "ELSE", "ENDIF", "PROC", "ENDPROC", "QUIT"
            };

            for (int i = 0; i < names.Length; i++)
            {
                byte[] written = dialect.ExtendedBytes(names[i], (byte)(0xF3 + i));
                CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFF, (byte)(0xF3 + i) }, written,
                    names[i] + " est ecrit apres FF FF");
            }

            byte token;
            Assert.IsTrue(dialect.TryToken("ENDIF", out token));
            Assert.AreEqual(0xFB, token);
            Assert.AreEqual(3, dialect.TokenLength(0xFF), "un mot du cartouch tient en trois octets");
        }

        [TestMethod]
        public void UneLigneEstUnEnregistrementDeLHote()
        {
            byte[] file = Encode(new[] { new BasicLine(10, "PRINT") });

            // The chain: the address of the next line, the number, the token, and
            // the $00. It starts at $0801, so the first address is $0807, and the
            // file ends with the two zero bytes of an address, which is how the
            // interpreter knows it has reached the end. The two bytes of the load
            // address itself belong to the container, as they do for a PRG.
            CollectionAssert.AreEqual(new byte[]
            {
                0x07, 0x08, 0x0A, 0x00, 0x99, 0x00,
                0x00, 0x00
            }, file, "la chaine de lignes commence a $0801");
        }

        [TestMethod]
        public void LesAdressesDeLaChaineSeSuiventDansLOrdre()
        {
            byte[] file = Encode(new[]
            {
                new BasicLine(10, "END"),
                new BasicLine(20, "FOR I = 1 TO 3"),
                new BasicLine(30, "NEXT I")
            });

            // Three records of 5 bytes plus their tokens, then the terminator.
            Assert.AreEqual(26, file.Length, "6 + 11 + 7 + 2");
            Assert.AreEqual(0x08, file[1], "la premiere ligne commence a $0807");
            Assert.AreEqual(0x0A, file[2], "la premiere ligne est la 10");
            Assert.AreEqual(0x12, file[6], "la deuxieme ligne commence a $0812");
            Assert.AreEqual(0x14, file[8], "la deuxieme ligne est la 20");
            Assert.AreEqual(0x19, file[17], "la troisieme ligne commence a $0819");
            Assert.AreEqual(0x1E, file[19], "la troisieme ligne est la 30");
            Assert.AreEqual(0x82, file[21], "puis le NEXT de l'hote");
            Assert.AreEqual(0x00, file[23], "le $00 qui finit la ligne");
            Assert.AreEqual(0x00, file[24], "puis l'adresse nulle de la fin");
        }

        [TestMethod]
        public void UneChaineSansLigneEstJusteLAdresseNulle()
        {
            byte[] file = Encode(new BasicLine[0]);
            CollectionAssert.AreEqual(new byte[] { 0x00, 0x00 }, file,
                "un programme vide ne fait que pointer sur la fin");
        }

        [TestMethod]
        public void UneChaineInterrompueEstRefusee()
        {
            BasicRecord record;
            string problem;

            // The next-line address points at the start of the file, so the
            // record would end before it begins.
            byte[] data = new byte[] { 0x01, 0x08, 0xFF, 0xFF, 0x0A, 0x00, 0x99, 0x00, 0x00, 0x00 };
            Assert.IsFalse(Dialect().TryReadRecord(data, 0, out record, out problem));
            Assert.IsNotNull(problem);
            StringAssert.Contains(problem, "not inside the file");
        }

        [TestMethod]
        public void UneLigneTropLongueEstRefusee()
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < 260; i++)
                text.Append("PRINT");

            List<Diagnostic> diagnostics = new List<Diagnostic>();
            byte[] file = new TokenEncoder(Dialect(), diagnostics)
                .Encode(new[] { new BasicLine(10, text.ToString()) });

            // A refused line leaves the address of the end, and the file says so
            // rather than holding half a line.
            Assert.AreEqual(2, file.Length, "il ne reste que l'adresse de la fin");
            Assert.AreEqual(1, diagnostics.Count, Describe(diagnostics));
            StringAssert.Contains(diagnostics[0].ToString(), "255");
        }

        [TestMethod]
        public void UneChaineGardeSesGuillemetsEtPasDeLongueur()
        {
            CollectionAssert.AreEqual(new byte[] { 0x99, 0x22, 0x48, 0x49, 0x22 },
                Body(10, "PRINT\"HI\""), "l'hote garde les guillemets et n'ecrit pas de longueur");
        }

        [TestMethod]
        public void LesMotsDuCartouchALInterieurDUneLigne()
        {
            CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFF, 0xFA }, Body(10, "ELSE"),
                "ELSE est un mot du cartouch");

            // ENDLOOP is written ENDLOOP: the table tries the longest word
            // first, so a prefix cannot take the place of a word.
            CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFF, 0xF6 }, Body(10, "ENDLOOP"),
                "ENDLOOP ne se confond pas avec END");
            CollectionAssert.AreEqual(new byte[] { 0x80 }, Body(10, "END"),
                "END reste le END de l'hote");
            CollectionAssert.AreEqual(new byte[] { 0x80, 0x80 }, Body(10, "END END"),
                "et le mot le plus long n'attrape que lui");
        }

        [TestMethod]
        public void UnIFDeLHoteEstReluCommeLOctetQuIlEst()
        {
            // The cartridge has no room for two IFs: its own is $F3. A file that
            // still holds $8B, the host's IF, has therefore to come back as the
            // byte it is rather than as a word it does not have.
            byte[] file = Encode(new[] { new BasicLine(10, "\x8B") });
            List<BasicLine> back = new TokenDecoder(Dialect(), new List<Diagnostic>()).Decode(file);

            Assert.AreEqual(1, back.Count);
            Assert.AreEqual("\\x8B", back[0].Body, "l'octet revient comme l'octet");

            byte[] again = Encode(back);
            CollectionAssert.AreEqual(file, again, "et il se reecrit a l'identique");
        }

        [TestMethod]
        public void UnNombreResteUnNombreDansUnGOTO()
        {
            // Commodore BASIC writes every number out in full, even after a GOTO:
            // there is no compact reference to rebuild.
            CollectionAssert.AreEqual(new byte[] { 0x89, (byte)'1', (byte)'0', (byte)'0' },
                Body(10, "GOTO 100"), "GOTO 100 s'ecrit en clair");
        }

        [TestMethod]
        public void UneVariableChaineGardeSonDollar()
        {
            CollectionAssert.AreEqual(new byte[] { 0x41, 0x24, 0xB2, 0x22, 0x58, 0x22 },
                Body(10, "A$=\"X\""), "le $ fait partie du nom de variable et = est un jeton");
        }

        [TestMethod]
        public void UnAllerRetourRedonneLeMemeFichier()
        {
            BasicLine[] lines = new[]
            {
                new BasicLine(10, "PRINT \"HELLO\""),
                new BasicLine(20, "REM this is not code + 1"),
                new BasicLine(30, "IF A > 10"),
                new BasicLine(40, "  PRINT \"BIG\""),
                new BasicLine(50, "ELSE"),
                new BasicLine(60, "  PRINT \"SMALL\""),
                new BasicLine(70, "ENDIF"),
                new BasicLine(80, "A = 0"),
                new BasicLine(90, "LOOP"),
                new BasicLine(100, "A = A + 1"),
                new BasicLine(110, "IF A > 5 THEN QUIT"),
                new BasicLine(120, "ENDLOOP"),
                new BasicLine(130, "PROC SETUP"),
                new BasicLine(140, "ENDPROC"),
                new BasicLine(150, "CALL SETUP"),
                new BasicLine(160, "GOSUB 200"),
                new BasicLine(170, "END"),
                new BasicLine(180, "DATA 1,2,3")
            };

            byte[] file = Encode(lines);
            List<BasicLine> back = new TokenDecoder(Dialect(), new List<Diagnostic>()).Decode(file);
            byte[] again = Encode(back);

            CollectionAssert.AreEqual(file, again, "l'aller-retour est exact");
            Assert.AreEqual(lines.Length, back.Count, "chaque ligne revient");
            Assert.AreEqual(130, back[12].Number, "les numeros ne bougent pas");
        }

        private static BasicDialect Dialect()
        {
            return WaterlooDialect.Create();
        }

        private static byte[] Encode(IReadOnlyList<BasicLine> lines)
        {
            return new TokenEncoder(Dialect(), new List<Diagnostic>()).Encode(lines);
        }

        /// <summary>The tokens of one line, without the header and the terminator.</summary>
        private static byte[] Body(int lineNumber, string body)
        {
            byte[] file = Encode(new[] { new BasicLine(lineNumber, body) });

            List<byte> only = new List<byte>();
            for (int i = 4; i < file.Length - 3; i++)
                only.Add(file[i]);
            return only.ToArray();
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
