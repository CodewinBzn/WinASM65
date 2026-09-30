// WinASM65 - the ProDOS container
//
// These tests read the image the way ProDOS does: follow the directory block
// chain, find the entry, follow its key block, and read the data fork back out.
// Checking the fields in place would only prove the writer agrees with itself,
// so every test here goes back through the image and takes the long way round
// wherever the format offers one.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Targets;

namespace WinASM65.Tests
{
    [TestClass]
    public class ProDosContainerTests
    {
        [TestMethod]
        public void UnVolumeCommenceParDeuxBlocsReservesEtQuatreBlocsDeRepertoire()
        {
            ProDosVolumeOptions options = Volume();
            byte[] image = ProDosVolume.Build(new byte[] { 0x00 }, options);

            Assert.AreEqual(280 * 512, image.Length, "un volume de 143K fait 280 blocs de 512 octets");
            byte[] logical = Image.Logical(image, options);
            for (int i = 0; i < 2; i++)
                Assert.IsTrue(Image.IsBlank(logical, i), "le bloc " + i + " est reserve");
        }

        [TestMethod]
        public void LEntrelacementEstCeluiDuPlan()
        {
            // Within every group of sixteen, the blocks are laid down as
            // 0, 8, 1, 9, 2, 10, 3, 11, 4, 12, 5, 13, 6, 14, 7, 15.
            int[] expected = { 0, 8, 1, 9, 2, 10, 3, 11, 4, 12, 5, 13, 6, 14, 7, 15 };
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(i, ProDosVolume.PhysicalBlock(expected[i]),
                    "le bloc logique " + expected[i] + " est le bloc physique " + i);

            // And it is a permutation: no two blocks collide, none is lost.
            bool[] seen = new bool[16];
            for (int i = 0; i < 16; i++)
                seen[ProDosVolume.PhysicalBlock(i)] = true;
            foreach (bool block in seen)
                Assert.IsTrue(block, "chaque bloc logique a sa place");
        }

        [TestMethod]
        public void LEntrelacementSeDefaitEtSeRetrouve()
        {
            byte[] data = new byte[600];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(i & 0xFF);

            ProDosVolumeOptions interleaved = Volume();
            byte[] scattered = ProDosVolume.Build(data, interleaved);

            ProDosVolumeOptions plain = Volume();
            plain.Interleaved = false;
            byte[] ordered = ProDosVolume.Build(data, plain);

            CollectionAssert.AreEqual(ordered, Image.Logical(scattered, interleaved),
                "lire l'entrelacement rend exactement l'image en ordre logique");
            CollectionAssert.AreNotEqual(ordered, scattered,
                "et l'entrelacement change bien l'ordre des blocs");
        }

        [TestMethod]
        public void UnPetitFichierEstUnSeedlingContigu()
        {
            byte[] data = new byte[200];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(i & 0xFF);

            ProDosVolumeOptions options = Volume();
            byte[] image = ProDosVolume.Build(data, options);
            byte[] logical = Image.Logical(image, options);

            Image.Entry entry = Image.FirstFile(logical);
            Assert.AreEqual(0x10, entry.StorageType & 0xF0, "un fichier de 200 octets est un seedling");
            Assert.AreEqual("PROGRAM", entry.Name);
            Assert.AreEqual(1, entry.BlocksUsed, "200 octets tiennent dans un bloc");
            CollectionAssert.AreEqual(data, Image.ReadData(logical, entry),
                "la fournee de donnees revient telle quelle");
        }

        [TestMethod]
        public void UnFichierPlusGrandQuUnBlocEstUnSaplingAvecUnBlocDIndex()
        {
            byte[] data = new byte[1500];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(i & 0xFF);

            ProDosVolumeOptions options = Volume();
            byte[] image = ProDosVolume.Build(data, options);
            byte[] logical = Image.Logical(image, options);

            Image.Entry entry = Image.FirstFile(logical);
            Assert.AreEqual(0x20, entry.StorageType & 0xF0, "au-dela d'un bloc c'est un sapling");
            Assert.AreEqual(4, entry.BlocksUsed, "trois blocs de donnees et le bloc d'index");
            Assert.AreEqual(0x00000000 + 1500, entry.Eof, "l'EOF tient sur trois octets");
            CollectionAssert.AreEqual(data, Image.ReadData(logical, entry));
        }

        [TestMethod]
        public void LeRepertoireDitLeNomDuVolumeLeNombreDeBlocsEtOuEstLeBitmap()
        {
            ProDosVolumeOptions options = Volume();
            options.VolumeName = "W65DISK";
            byte[] image = ProDosVolume.Build(new byte[] { 0x01 }, options);
            byte[] logical = Image.Logical(image, options);

            Image.VolumeHeader header = Image.Volume(logical);
            Assert.AreEqual(0xF0, header.StorageType & 0xF0, "l'entete porte le type de stockage $F");
            Assert.AreEqual(7, header.NameLength, "le nom du volume fait sept lettres");
            Assert.AreEqual("W65DISK", header.Name);
            Assert.AreEqual(0x27, header.EntryLength, "Technical Reference B.2.2");
            Assert.AreEqual(13, header.EntriesPerBlock, "13 entrees de $27 octets dans un bloc");
            Assert.AreEqual(1, header.FileCount);
            Assert.AreEqual(6, header.BitmapPointer, "le bitmap suit le repertoire");
            Assert.AreEqual(280, header.TotalBlocks);
        }

        [TestMethod]
        public void LeRepertoireEstUneListeChainee()
        {
            ProDosVolumeOptions options = Volume();
            byte[] image = ProDosVolume.Build(new byte[] { 0x01 }, options);
            byte[] logical = Image.Logical(image, options);

            Assert.AreEqual(0, Image.Word(logical, 2 * 512), "le premier bloc n'a pas de precedent");
            Assert.AreEqual(3, Image.Word(logical, 2 * 512 + 2), "le bloc suivant est le 3");
            Assert.AreEqual(2, Image.Word(logical, 3 * 512), "le bloc 3 a le 2 pour precedent");
            Assert.AreEqual(4, Image.Word(logical, 3 * 512 + 2), "et le 4 pour suivant");
            Assert.AreEqual(4, Image.Word(logical, 5 * 512), "le bloc 5 a le 4 pour precedent");
            Assert.AreEqual(0, Image.Word(logical, 5 * 512 + 2), "le dernier bloc n'a pas de suivant");
        }

        [TestMethod]
        public void LeBitmapMarqueLesBlocsPrisEtLaisseLesAutresLibres()
        {
            byte[] data = new byte[1500];
            ProDosVolumeOptions options = Volume();
            byte[] image = ProDosVolume.Build(data, options);
            byte[] logical = Image.Logical(image, options);

            Image.Entry entry = Image.FirstFile(logical);
            int indexBlock = entry.KeyBlock;

            // Blocks 0 to 37 are the reserved blocks, the directory and the
            // bitmap; block 38 onwards holds the file and its index.
            for (int bit = 0; bit < 38; bit++)
                Assert.IsTrue(Image.BitIsSet(logical, bit), "le bloc " + bit + " est pris");
            for (int bit = 38; bit < 42; bit++)
                Assert.IsTrue(Image.BitIsSet(logical, bit), "le bloc " + bit + " porte le fichier");
            Assert.IsTrue(Image.BitIsSet(logical, indexBlock), "le bloc d'index est pris lui aussi");
            for (int bit = indexBlock + 1; bit < 280; bit++)
                Assert.IsFalse(Image.BitIsSet(logical, bit), "le bloc " + bit + " est libre");
        }

        [TestMethod]
        public void UnFichierTropGrandPourLeVolumeEstRefuse()
        {
            ProDosVolumeOptions options = Volume();
            try
            {
                ProDosVolume.Build(new byte[options.VolumeBlocks * 512], options);
                Assert.Fail("un fichier de la taille du volume ne devrait pas passer");
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        [TestMethod]
        public void UnLoadFileCommenceParUnEnTeteEtSeTermineParUnZero()
        {
            List<ProDosSegment> segments = new List<ProDosSegment>
            {
                new ProDosSegment { Number = 1, LoadAddress = 0x2003, Name = "PROGRAM", Data = new byte[] { 0xBA } }
            };
            int entry = ProDosLoadFile.EntryPointOf(segments, 0);
            byte[] file = ProDosLoadFile.Build(segments, entry);

            CollectionAssert.AreEqual(new byte[] { 0x00, 0x00, 0x00, 0x15, 0x00 }, Slice(file, 0, 5),
                "en-tete : type, version, version minimale, point d'entree sur deux octets");
            CollectionAssert.AreEqual(new byte[] { 0x00, 0x01, 0x01, 0x00, 0x03, 0x20, 0x07,
                    (byte)'P', (byte)'R', (byte)'O', (byte)'G', (byte)'R', (byte)'A', (byte)'M' },
                Slice(file, 5, 14), "segment 1, un octet de donnees, charge a $2003, nom de sept lettres");
            Assert.AreEqual(0x00, file[19], "le record de donnees commence par un zero");
            Assert.AreEqual(0x01, file[20], "puis le compte d'octets");
            Assert.AreEqual(0xBA, file[21], "puis le premier octet de donnees");
            Assert.AreEqual(0x00, file[22], "puis le record de fin de fichier");
            Assert.AreEqual(0x00, file[23]);
            Assert.AreEqual(21, entry, "le point d'entree est l'ouverture du segment");
        }

        [TestMethod]
        public void UnSegmentDePlusDe512OctetsUtilisePlusieursRecordsDeDonnees()
        {
            byte[] data = new byte[1200];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(i & 0xFF);

            List<ProDosSegment> segments = new List<ProDosSegment>
            {
                new ProDosSegment { Number = 1, LoadAddress = 0x2003, Name = string.Empty, Data = data }
            };
            byte[] file = ProDosLoadFile.Build(segments, 0);

            // Header of five bytes, a segment record of seven, then three data
            // records: two full ones that say so with a zero count, and one of
            // the last 176 bytes, and the terminator.
            int cursor = 5 + 7;
            Assert.AreEqual(cursor + (2 + 512) * 2 + (2 + 176) + 2, file.Length);
            Assert.AreEqual(0x00, file[cursor], "premier record de donnees");
            Assert.AreEqual(0x00, file[cursor + 1], "512 octets se disent par un compte de zero");
            Assert.AreEqual(data[0], file[cursor + 2], "et les donnees suivent");
            cursor += 2 + 512;
            Assert.AreEqual(0x00, file[cursor], "deuxieme record de donnees");
            Assert.AreEqual(0x00, file[cursor + 1]);
            cursor += 2 + 512;
            Assert.AreEqual(0x00, file[cursor], "troisieme record");
            Assert.AreEqual(0xB0, file[cursor + 1], "176 = $B0");
            Assert.AreEqual(data[1199], file[cursor + 2 + 175], "et les 176 derniers octets");
            Assert.AreEqual(0x00, file[cursor + 2 + 176], "puis le terminateur");
        }

        [TestMethod]
        public void LePointDEntreeEstLOuvertureDuSegment()
        {
            List<ProDosSegment> segments = new List<ProDosSegment>
            {
                new ProDosSegment { Number = 1, LoadAddress = 0x2003, Name = "UN", Data = new byte[10] },
                new ProDosSegment { Number = 2, LoadAddress = 0x4000, Name = "DEUX", Data = new byte[20] }
            };

            Assert.AreEqual(5 + 7 + 2 + 2, ProDosLoadFile.EntryPointOf(segments, 0),
                "l'entree du premier segment est son premier octet");
            Assert.AreEqual(5 + (7 + 2) + (2 + 10) + (7 + 4) + 2,
                ProDosLoadFile.EntryPointOf(segments, 1),
                "et celle du second se compte apres tous les records du premier");

            byte[] file = ProDosLoadFile.Build(segments, ProDosLoadFile.EntryPointOf(segments, 1));
            Assert.AreEqual(ProDosLoadFile.EntryPointOf(segments, 1) & 0xFF, file[3]);
            Assert.AreEqual((ProDosLoadFile.EntryPointOf(segments, 1) >> 8) & 0xFF, file[4]);
        }

        [TestMethod]
        public void UnSegmentTropLongEstRefuse()
        {
            List<ProDosSegment> segments = new List<ProDosSegment>
            {
                new ProDosSegment { Number = 1, LoadAddress = 0x2003, Data = new byte[70000] }
            };
            try
            {
                ProDosLoadFile.Build(segments, 0);
                Assert.Fail("un segment de plus de 64K ne devrait pas passer");
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        [TestMethod]
        public void UnProgrammeTokeniseVaDansUnVolumeProDOS()
        {
            List<WinASM65.TextFormat.BasicLine> lines = new List<WinASM65.TextFormat.BasicLine>
            {
                new WinASM65.TextFormat.BasicLine(10, "PRINT \"HELLO\""),
                new WinASM65.TextFormat.BasicLine(20, "END")
            };
            byte[] tokenised = new WinASM65.TextFormat.TokenEncoder(
                WinASM65.TextFormat.ApplesoftDialect.Create(), new List<WinASM65.Core.Diagnostic>())
                .Encode(lines);

            // What the volume has to hold is a load file with the program in one
            // segment, not the program on its own: ProDOS loads segments, and
            // Applesoft is run over the result.
            List<ProDosSegment> segments = new List<ProDosSegment>
            {
                new ProDosSegment
                {
                    Number = 1,
                    LoadAddress = 0x2003,
                    Name = "PROGRAM",
                    Data = tokenised
                }
            };
            byte[] loadFile = ProDosLoadFile.Build(segments,
                ProDosLoadFile.EntryPointOf(segments, 0));

            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "w65-prodos-" + Guid.NewGuid().ToString("N") + ".dsk");
            try
            {
                WinASM65.Core.OperationResult result = new ProDosFormat().Write(path, tokenised, null);
                Assert.IsTrue(result.Success);

                byte[] image = System.IO.File.ReadAllBytes(path);
                ProDosVolumeOptions options = new ProDosVolumeOptions();
                byte[] logical = Image.Logical(image, options);
                Image.Entry entry = Image.FirstFile(logical);
                Assert.AreEqual("PROGRAM", entry.Name);
                CollectionAssert.AreEqual(loadFile, Image.ReadData(logical, entry),
                    "le load file se relit dans le volume");

                // And the program itself is the segment's data, where the
                // interpreter would find it.
                byte[] segment = Slice(Image.ReadData(logical, entry),
                    ProDosLoadFile.EntryPointOf(segments, 0), tokenised.Length);
                CollectionAssert.AreEqual(tokenised, segment, "et le programme tokenise y est entier");
            }
            finally
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
        }

        private static ProDosVolumeOptions Volume()
        {
            ProDosVolumeOptions options = new ProDosVolumeOptions();
            options.VolumeName = "W65";
            options.FileName = "PROGRAM";
            return options;
        }

        private static byte[] Slice(byte[] data, int from, int count)
        {
            byte[] result = new byte[count];
            Array.Copy(data, from, result, 0, count);
            return result;
        }

        /// <summary>
        /// Reading the image the way ProDOS reads it. Nothing here knows how the
        /// writer lays blocks down, except that the interleave is the documented
        /// one.
        /// </summary>
        private static class Image
        {
            public sealed class VolumeHeader
            {
                public byte StorageType;
                public int NameLength;
                public string Name;
                public byte EntryLength;
                public byte EntriesPerBlock;
                public int FileCount;
                public int BitmapPointer;
                public int TotalBlocks;
            }

            public sealed class Entry
            {
                public byte StorageType;
                public string Name;
                public int KeyBlock;
                public int BlocksUsed;
                public int Eof;
            }

            public static byte[] Logical(byte[] image, ProDosVolumeOptions options)
            {
                if (!options.Interleaved)
                    return image;

                byte[] logical = new byte[image.Length];
                int blocks = image.Length / 512;
                for (int i = 0; i < blocks; i++)
                {
                    int target = ProDosVolume.PhysicalBlock(i);
                    if (target >= blocks)
                        continue;
                    Array.Copy(image, target * 512, logical, i * 512, 512);
                }
                return logical;
            }

            public static VolumeHeader Volume(byte[] logical)
            {
                int at = 2 * 512 + 4;
                VolumeHeader header = new VolumeHeader
                {
                    StorageType = (byte)(logical[at] & 0xF0),
                    NameLength = logical[at] & 0x0F,
                    Name = Text(logical, at + 1, logical[at] & 0x0F),
                    EntryLength = logical[at + 0x1F],
                    EntriesPerBlock = logical[at + 0x20],
                    FileCount = Word(logical, at + 0x21),
                    BitmapPointer = Word(logical, at + 0x23),
                    TotalBlocks = Word(logical, at + 0x25)
                };
                return header;
            }

            public static Entry FirstFile(byte[] logical)
            {
                int at = 2 * 512 + 4 + ProDosVolume.EntryLength;
                return new Entry
                {
                    StorageType = (byte)(logical[at] & 0xF0),
                    Name = Text(logical, at + 1, logical[at] & 0x0F),
                    KeyBlock = Word(logical, at + 0x11),
                    BlocksUsed = Word(logical, at + 0x13),
                    Eof = logical[at + 0x15] | (logical[at + 0x16] << 8) | (logical[at + 0x17] << 16)
                };
            }

            /// <summary>Follows the key block, and the index block when there is one.</summary>
            public static byte[] ReadData(byte[] logical, Entry entry)
            {
                int storage = entry.StorageType >> 4;
                int key = entry.KeyBlock;
                if (storage == ProDosVolume.StorageSapling)
                {
                    int first = Word(logical, key * 512);
                    int at = 0;
                    byte[] data = new byte[entry.Eof];
                    while (at < data.Length)
                    {
                        int count = Math.Min(512, data.Length - at);
                        Array.Copy(logical, first * 512, data, at, count);
                        at += count;
                        first++;
                    }
                    return data;
                }

                byte[] seedling = new byte[entry.Eof];
                Array.Copy(logical, entry.KeyBlock * 512, seedling, 0, seedling.Length);
                return seedling;
            }

            public static int Word(byte[] data, int at)
            {
                return data[at] | (data[at + 1] << 8);
            }

            public static bool BitIsSet(byte[] data, int bit)
            {
                // The bits start at the first byte of the first bitmap block.
                return (data[ProDosVolume.FirstBitmapBlock * 512 + (bit / 8)] & (1 << (bit % 8))) != 0;
            }

            public static bool IsBlank(byte[] logical, int block)
            {
                for (int i = 0; i < 512; i++)
                {
                    if (logical[block * 512 + i] != 0)
                        return false;
                }
                return true;
            }

            private static string Text(byte[] data, int at, int length)
            {
                string text = string.Empty;
                for (int i = 0; i < length; i++)
                    text += (char)data[at + i];
                return text;
            }
        }
    }
}