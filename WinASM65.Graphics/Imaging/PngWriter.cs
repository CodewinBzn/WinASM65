using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace WinASM65.Graphics.Imaging
{
    /// <summary>
    /// Writes an <see cref="RgbaImage"/> as a PNG: 8-bit RGBA, not interlaced, every
    /// row with filter 0 (none). The renders are small and flat, so the filters
    /// would buy little; zlib does the work.
    /// </summary>
    public static class PngWriter
    {
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static byte[] Encode(RgbaImage image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));
            using (MemoryStream png = new MemoryStream())
            {
                png.Write(Signature, 0, Signature.Length);

                byte[] header = new byte[13];
                WriteBigEndian(header, 0, (uint)image.Width);
                WriteBigEndian(header, 4, (uint)image.Height);
                header[8] = 8;  // bits per sample
                header[9] = 6;  // colour type: RGBA
                header[10] = 0; // deflate
                header[11] = 0; // adaptive filtering
                header[12] = 0; // not interlaced
                WriteChunk(png, "IHDR", header);

                WriteChunk(png, "IDAT", Compress(image));
                WriteChunk(png, "IEND", new byte[0]);
                return png.ToArray();
            }
        }

        public static void Save(RgbaImage image, string path)
        {
            File.WriteAllBytes(path, Encode(image));
        }

        private static byte[] Compress(RgbaImage image)
        {
            int stride = image.Width * 4;
            using (MemoryStream compressed = new MemoryStream())
            {
                using (ZLibStream zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
                {
                    byte[] filter = { 0 };
                    for (int y = 0; y < image.Height; y++)
                    {
                        zlib.Write(filter, 0, 1);
                        zlib.Write(image.Pixels, y * stride, stride);
                    }
                }
                return compressed.ToArray();
            }
        }

        private static void WriteChunk(Stream png, string type, byte[] data)
        {
            byte[] length = new byte[4];
            WriteBigEndian(length, 0, (uint)data.Length);
            png.Write(length, 0, 4);

            byte[] typeAndData = new byte[4 + data.Length];
            Encoding.ASCII.GetBytes(type, 0, 4, typeAndData, 0);
            Buffer.BlockCopy(data, 0, typeAndData, 4, data.Length);
            png.Write(typeAndData, 0, typeAndData.Length);

            byte[] crc = new byte[4];
            WriteBigEndian(crc, 0, Crc32.Compute(typeAndData, 0, typeAndData.Length));
            png.Write(crc, 0, 4);
        }

        internal static void WriteBigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }
    }
}
