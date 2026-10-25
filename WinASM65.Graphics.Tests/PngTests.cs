using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Graphics.Imaging;

namespace WinASM65.Graphics.Tests
{
    [TestClass]
    public class PngTests
    {
        [TestMethod]
        public void AnImageSurvivesAWriteAndARead()
        {
            RgbaImage image = new RgbaImage(5, 3);
            Random random = new Random(42);
            random.NextBytes(image.Pixels);

            RgbaImage back = PngReader.Decode(PngWriter.Encode(image));

            Assert.AreEqual(5, back.Width);
            Assert.AreEqual(3, back.Height);
            CollectionAssert.AreEqual(image.Pixels, back.Pixels);
        }

        [TestMethod]
        public void TheCrcIsTheStandardOne()
        {
            byte[] iend = Encoding.ASCII.GetBytes("IEND");
            Assert.AreEqual(0xAE426082u, Crc32.Compute(iend, 0, 4), "the CRC every IEND chunk carries");
        }

        [TestMethod]
        public void AHandMadeIndexedPngWithTransparencyDecodes()
        {
            // 2x2, colour type 3: red, green (transparent through tRNS), blue.
            // Row 0 unfiltered: indexes 0, 1. Row 1 filtered "up": 2, 1 encodes 2, 2.
            byte[] png = Png(2, 2, 3,
                new[] { Chunk("PLTE", 0xFF, 0, 0, 0, 0xFF, 0, 0, 0, 0xFF), Chunk("tRNS", 0xFF, 0x00) },
                new byte[] { 0, 0, 1, 2, 2, 1 });

            RgbaImage image = PngReader.Decode(png);

            Assert.AreEqual(0xFF0000, image.GetRgb(0, 0));
            Assert.AreEqual(255, image.GetAlpha(0, 0));
            Assert.AreEqual(0x00FF00, image.GetRgb(1, 0));
            Assert.AreEqual(0, image.GetAlpha(1, 0));
            Assert.AreEqual(0x0000FF, image.GetRgb(0, 1));
            Assert.AreEqual(0x0000FF, image.GetRgb(1, 1));
        }

        [TestMethod]
        public void EveryFilterTypeDecodes()
        {
            // An RGB image of 4x5, one row per filter type 0..4, each filtered here.
            const int width = 4, height = 5, bpp = 3;
            byte[] raw = new byte[width * height * bpp];
            Random random = new Random(7);
            random.NextBytes(raw);
            List<byte> filtered = new List<byte>();
            for (int y = 0; y < height; y++)
            {
                int filter = y;
                filtered.Add((byte)filter);
                for (int x = 0; x < width * bpp; x++)
                {
                    int a = x >= bpp ? raw[y * width * bpp + x - bpp] : 0;
                    int b = y > 0 ? raw[(y - 1) * width * bpp + x] : 0;
                    int c = x >= bpp && y > 0 ? raw[(y - 1) * width * bpp + x - bpp] : 0;
                    int predictor = filter == 1 ? a : filter == 2 ? b : filter == 3 ? (a + b) / 2 : filter == 4 ? Paeth(a, b, c) : 0;
                    filtered.Add((byte)(raw[y * width * bpp + x] - predictor));
                }
            }

            RgbaImage image = PngReader.Decode(Png(width, height, 2, new byte[0][], filtered.ToArray()));

            for (int i = 0; i < width * height; i++)
            {
                Assert.AreEqual(raw[i * 3], image.Pixels[i * 4], "red of pixel " + i);
                Assert.AreEqual(raw[i * 3 + 1], image.Pixels[i * 4 + 1]);
                Assert.AreEqual(raw[i * 3 + 2], image.Pixels[i * 4 + 2]);
                Assert.AreEqual(255, image.Pixels[i * 4 + 3]);
            }
        }

        [TestMethod]
        public void GreyAndGreyAlphaDecode()
        {
            RgbaImage grey = PngReader.Decode(Png(2, 1, 0, new byte[0][], new byte[] { 0, 0x10, 0xF0 }));
            Assert.AreEqual(0x101010, grey.GetRgb(0, 0));
            Assert.AreEqual(0xF0F0F0, grey.GetRgb(1, 0));

            RgbaImage greyAlpha = PngReader.Decode(Png(1, 1, 4, new byte[0][], new byte[] { 0, 0x80, 0x40 }));
            Assert.AreEqual(0x808080, greyAlpha.GetRgb(0, 0));
            Assert.AreEqual(0x40, greyAlpha.GetAlpha(0, 0));
        }

        [TestMethod]
        public void UnsupportedFilesAreRefusedClearly()
        {
            byte[] sixteenBit = Png(1, 1, 2, new byte[0][], new byte[7], 16);
            InvalidDataException depth = Assert.ThrowsException<InvalidDataException>(() => PngReader.Decode(sixteenBit));
            StringAssert.Contains(depth.Message, "bit depth 16");

            byte[] interlaced = Png(1, 1, 6, new byte[0][], new byte[5], 8, 1);
            StringAssert.Contains(Assert.ThrowsException<InvalidDataException>(() => PngReader.Decode(interlaced)).Message, "interlaced");

            StringAssert.Contains(Assert.ThrowsException<InvalidDataException>(() => PngReader.Decode(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 })).Message, "signature");
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        /// <summary>A PNG assembled by hand: signature, IHDR, extra chunks, one IDAT of the given scanlines, IEND.</summary>
        internal static byte[] Png(int width, int height, int colourType, byte[][] extra, byte[] scanlines, int bitDepth = 8, int interlace = 0)
        {
            MemoryStream png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            png.Write(Chunk("IHDR",
                (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
                (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
                (byte)bitDepth, (byte)colourType, 0, 0, (byte)interlace));
            foreach (byte[] chunk in extra)
                png.Write(chunk);
            MemoryStream compressed = new MemoryStream();
            using (ZLibStream zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
                zlib.Write(scanlines);
            png.Write(Chunk("IDAT", compressed.ToArray()));
            png.Write(Chunk("IEND"));
            return png.ToArray();
        }

        internal static byte[] Chunk(string type, params byte[] data)
        {
            byte[] typeAndData = new byte[4 + data.Length];
            Encoding.ASCII.GetBytes(type).CopyTo(typeAndData, 0);
            data.CopyTo(typeAndData, 4);
            uint crc = Crc32.Compute(typeAndData, 0, typeAndData.Length);
            MemoryStream chunk = new MemoryStream();
            chunk.Write(new[] { (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length });
            chunk.Write(typeAndData);
            chunk.Write(new[] { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc });
            return chunk.ToArray();
        }
    }
}
