using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Targets;

namespace WinASM65.Tests
{
    /// <summary>
    /// Golden tests octet-pour-octet du mode de gravage direct, couvrant les huit
    /// formats de sortie. Ces attentes sont derivees des specifications de format
    /// (en-tetes, endianness, tailles de banques), pas de la sortie observee.
    /// </summary>
    [TestClass]
    public class DirectBurnGoldenTests
    {
        private static readonly byte[] Payload = new byte[] { 0xA9, 0x00, 0x60 };

        [TestMethod]
        public void RawBinary_EcritLaChargeSansEnTete()
        {
            using (Sandbox sandbox = new Sandbox())
            {
                byte[] bytes = Publish(sandbox, "raw.bin", "bin", new ResolvedTarget());
                CollectionAssert.AreEqual(new byte[] { 0xA9, 0x00, 0x60 }, bytes);
            }
        }

        [TestMethod]
        public void Prg_EcritChargePuisAdresseDeDepartEnLittleEndian()
        {
            using (Sandbox sandbox = new Sandbox())
            {
                ResolvedTarget target = new ResolvedTarget { FormatName = "prg", OriginAddress = 0x0800 };
                byte[] bytes = Publish(sandbox, "game.prg", "prg", target);
                CollectionAssert.AreEqual(new byte[] { 0x00, 0x08, 0xA9, 0x00, 0x60 }, bytes);
            }
        }

        [TestMethod]
        public void A2Bin_EcritEnTeteAdressePuisLongueurEnLittleEndian()
        {
            using (Sandbox sandbox = new Sandbox())
            {
                ResolvedTarget target = new ResolvedTarget { FormatName = "a2bin", OriginAddress = 0x0803 };
                byte[] bytes = Publish(sandbox, "out.a2bin", "a2bin", target);
                CollectionAssert.AreEqual(new byte[] { 0x03, 0x08, 0x03, 0x00, 0xA9, 0x00, 0x60 }, bytes);
            }
        }

        [TestMethod]
        public void AtariXex_EcritMagicFFPUISBornesEnLittleEndian()
        {
            using (Sandbox sandbox = new Sandbox())
            {
                ResolvedTarget target = new ResolvedTarget { FormatName = "xex", OriginAddress = 0x0600 };
                byte[] bytes = Publish(sandbox, "out.xex", "xex", target);
                CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFF, 0x00, 0x06, 0x02, 0x06, 0xA9, 0x00, 0x60 }, bytes);
            }
        }

        [TestMethod]
        public void Ines_EcritEnTeteNESPUISChargementA16Octets()
        {
            using (Sandbox sandbox = new Sandbox())
            {
                // Cible par defaut : 1 banque PRG de 16 KiB, 0 banque CHR, miroitrage vertical.
                ResolvedTarget target = new ResolvedTarget { FormatName = "ines" };
                byte[] bytes = Publish(sandbox, "game.nes", "ines", target);

                Assert.AreEqual(16 + 16384, bytes.Length, "16 octets d'en-tete + 1 banque PRG de 16 KiB.");
                CollectionAssert.AreEqual(new byte[] { 0x4E, 0x45, 0x53, 0x1A }, Slice(bytes, 0, 4), "magic NES\\x1A");
                Assert.AreEqual(1, bytes[4], "nombre de banques PRG");
                Assert.AreEqual(0, bytes[5], "nombre de banques CHR");
                Assert.AreEqual(0x01, bytes[6], "flags6 : bit0 = miroitrage vertical, mapper 0");
                Assert.AreEqual(0x00, bytes[7], "flags7 : mapper 0");
                for (int i = 8; i < 16; i++)
                    Assert.AreEqual(0, bytes[i], "octet d'en-tete reserve " + i);
                CollectionAssert.AreEqual(new byte[] { 0xA9, 0x00, 0x60 }, Slice(bytes, 16, 3), "charge a l'offset 16");
            }
        }

        [TestMethod]
        public void PaddedRom_CompleteJusquALaTailleDemandee()
        {
            using (Sandbox sandbox = new Sandbox())
            {
                ResolvedTarget target = new ResolvedTarget { FormatName = "rom", RomSize = 0x2000 };
                byte[] bytes = Publish(sandbox, "out.rom", "rom", target);

                Assert.AreEqual(8192, bytes.Length);
                CollectionAssert.AreEqual(Payload, Slice(bytes, 0, 3));
                for (int i = 3; i < bytes.Length; i++)
                    Assert.AreEqual(0, bytes[i], "octet de remplissage " + i);
            }
        }

        [TestMethod]
        public void PaddedRom_ParDefautArronditALaPuissanceDeDeuxSuperieure()
        {
            using (Sandbox sandbox = new Sandbox())
            {
                ResolvedTarget target = new ResolvedTarget { FormatName = "rom" };
                byte[] bytes = Publish(sandbox, "out.rom", "rom", target);

                Assert.AreEqual(4, bytes.Length, "3 octets arrondis a la puissance de deux superieure.");
                CollectionAssert.AreEqual(new byte[] { 0xA9, 0x00, 0x60, 0x00 }, bytes);
            }
        }

        [TestMethod]
        public void IntelHex_EcritLesEnregistrementsExacts()
        {
            using (Sandbox sandbox = new Sandbox())
            {
                ResolvedTarget target = new ResolvedTarget { FormatName = "ihex", OriginAddress = 0x0801 };
                string path = sandbox.File("out.ihex");
                Assert.IsTrue(new ExecutablePublisher().Publish(path, Payload, target).Success);

                CollectionAssert.AreEqual(new[]
                {
                    ":0400000300000108F0",   // S3 : adresse de depart $0801
                    ":03080100A90060EB",     // donnees a $0801
                    ":00000001FF"            // fin de fichier
                }, ReadLines(path));
            }
        }

        [TestMethod]
        public void MotorolaSrec_EcritLesEnregistrementsExacts()
        {
            using (Sandbox sandbox = new Sandbox())
            {
                ResolvedTarget target = new ResolvedTarget { FormatName = "srec", OriginAddress = 0x0600 };
                string path = sandbox.File("out.srec");
                Assert.IsTrue(new ExecutablePublisher().Publish(path, Payload, target).Success);

                CollectionAssert.AreEqual(new[]
                {
                    "S0030600F6",            // S0 : en-tete
                    "S1060600A90060EA",      // S1 : donnees a $0600
                    "S9030600F6"             // S9 : fin
                }, ReadLines(path));
            }
        }

        [TestMethod]
        public void TousLesFormatsRestentGravesDirectement()
        {
            string[] formats = new[] { "bin", "prg", "a2bin", "xex", "ines", "rom", "ihex", "srec" };
            foreach (string format in formats)
            {
                using (Sandbox sandbox = new Sandbox())
                {
                    string path = sandbox.File("out." + format);
                    ResolvedTarget target = new ResolvedTarget { FormatName = format, OriginAddress = 0x0800 };
                    OperationResult result = new ExecutablePublisher().Publish(path, Payload, target);
                    Assert.IsTrue(result.Success, format);
                    Assert.IsTrue(File.Exists(path), format);
                    Assert.IsTrue(new FileInfo(path).Length > 0, format);
                }
            }
        }

        private static byte[] Publish(Sandbox sandbox, string fileName, string format, ResolvedTarget target)
        {
            string path = sandbox.File(fileName);
            OperationResult result = new ExecutablePublisher().Publish(path, Payload, target);
            Assert.IsTrue(result.Success, format + " : " + Describe(result));
            return File.ReadAllBytes(path);
        }

        private static byte[] Slice(byte[] source, int offset, int count)
        {
            byte[] slice = new byte[count];
            Array.Copy(source, offset, slice, 0, count);
            return slice;
        }

        private static string[] ReadLines(string path)
        {
            List<string> lines = new List<string>();
            foreach (string line in File.ReadAllLines(path))
            {
                if (!string.IsNullOrEmpty(line))
                    lines.Add(line);
            }
            return lines.ToArray();
        }

        private static string Describe(OperationResult result)
        {
            if (result == null || result.Diagnostics == null || result.Diagnostics.Count == 0)
                return "aucun diagnostic";
            string text = string.Empty;
            foreach (Diagnostic diagnostic in result.Diagnostics)
                text += diagnostic.ToString() + " | ";
            return text;
        }

        private sealed class Sandbox : IDisposable
        {
            public string Path { get; private set; }

            public Sandbox()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinASM65Golden_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string File(string name)
            {
                return System.IO.Path.Combine(Path, name);
            }

            public void Dispose()
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, true);
            }
        }
    }
}
