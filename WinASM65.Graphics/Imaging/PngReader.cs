using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace WinASM65.Graphics.Imaging
{
    /// <summary>
    /// Reads a PNG into an <see cref="RgbaImage"/>.
    ///
    /// <para>
    /// Takes what drawing programs save for pixel art: 8 bits per sample, not
    /// interlaced, colour types 0 (grey), 2 (RGB), 3 (indexed, with its PLTE and an
    /// optional tRNS), 4 (grey and alpha) and 6 (RGBA), any of the five row
    /// filters. Anything else is refused with an <see cref="InvalidDataException"/>
    /// saying what the file is and what would be accepted, rather than a picture
    /// read wrong.
    /// </para>
    /// </summary>
    public static class PngReader
    {
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static RgbaImage Load(string path)
        {
            return Decode(File.ReadAllBytes(path));
        }

        public static RgbaImage Decode(byte[] png)
        {
            if (png == null || png.Length < Signature.Length)
                throw new InvalidDataException("not a PNG file: too short");
            for (int i = 0; i < Signature.Length; i++)
            {
                if (png[i] != Signature[i])
                    throw new InvalidDataException("not a PNG file: wrong signature");
            }

            int width = 0, height = 0, bitDepth = 0, colourType = -1, interlace = 0;
            byte[] palette = null;
            byte[] transparency = null;
            bool seenHeader = false;
            MemoryStream idat = new MemoryStream();

            int offset = Signature.Length;
            while (true)
            {
                if (offset + 8 > png.Length)
                    throw new InvalidDataException("truncated PNG: no IEND chunk");
                int length = (int)ReadBigEndian(png, offset);
                string type = Encoding.ASCII.GetString(png, offset + 4, 4);
                if (length < 0 || offset + 12 + length > png.Length)
                    throw new InvalidDataException("truncated PNG: chunk " + type + " goes past the end of the file");
                uint expected = ReadBigEndian(png, offset + 8 + length);
                if (Crc32.Compute(png, offset + 4, 4 + length) != expected)
                    throw new InvalidDataException("corrupt PNG: bad CRC in chunk " + type);
                int data = offset + 8;

                switch (type)
                {
                    case "IHDR":
                        width = (int)ReadBigEndian(png, data);
                        height = (int)ReadBigEndian(png, data + 4);
                        bitDepth = png[data + 8];
                        colourType = png[data + 9];
                        interlace = png[data + 12];
                        seenHeader = true;
                        Check(width, height, bitDepth, colourType, interlace);
                        break;
                    case "PLTE":
                        palette = new byte[length];
                        Buffer.BlockCopy(png, data, palette, 0, length);
                        break;
                    case "tRNS":
                        transparency = new byte[length];
                        Buffer.BlockCopy(png, data, transparency, 0, length);
                        break;
                    case "IDAT":
                        idat.Write(png, data, length);
                        break;
                }
                offset += 12 + length;
                if (type == "IEND")
                    break;
            }

            if (!seenHeader)
                throw new InvalidDataException("corrupt PNG: no IHDR chunk");
            if (colourType == 3 && palette == null)
                throw new InvalidDataException("corrupt PNG: an indexed image without its PLTE chunk");

            int channels = Channels(colourType);
            int stride = width * channels;
            byte[] raw = Inflate(idat.ToArray(), (stride + 1) * height);
            byte[] samples = Unfilter(raw, stride, height, channels);
            return ToRgba(samples, width, height, colourType, palette, transparency);
        }

        private static void Check(int width, int height, int bitDepth, int colourType, int interlace)
        {
            if (width <= 0 || height <= 0)
                throw new InvalidDataException("corrupt PNG: size " + width + "x" + height);
            if (colourType != 0 && colourType != 2 && colourType != 3 && colourType != 4 && colourType != 6)
                throw new InvalidDataException("unsupported PNG colour type " + colourType + " (accepted: 0 grey, 2 RGB, 3 indexed, 4 grey+alpha, 6 RGBA)");
            if (bitDepth != 8)
                throw new InvalidDataException("unsupported PNG bit depth " + bitDepth + " (only 8 bits per sample is read; save the image as 8-bit)");
            if (interlace != 0)
                throw new InvalidDataException("unsupported interlaced PNG (save the image without interlacing)");
        }

        private static int Channels(int colourType)
        {
            switch (colourType)
            {
                case 0: return 1;
                case 2: return 3;
                case 3: return 1;
                case 4: return 2;
                default: return 4;
            }
        }

        private static byte[] Inflate(byte[] compressed, int expected)
        {
            byte[] raw = new byte[expected];
            try
            {
                using (ZLibStream zlib = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress))
                {
                    int read = 0;
                    while (read < expected)
                    {
                        int n = zlib.Read(raw, read, expected - read);
                        if (n <= 0)
                            throw new InvalidDataException("corrupt PNG: the image data is shorter than the image");
                        read += n;
                    }
                }
            }
            catch (InvalidDataException ex) when (!ex.Message.StartsWith("corrupt PNG", StringComparison.Ordinal))
            {
                throw new InvalidDataException("corrupt PNG: " + ex.Message, ex);
            }
            return raw;
        }

        private static byte[] Unfilter(byte[] raw, int stride, int height, int bpp)
        {
            byte[] output = new byte[stride * height];
            for (int y = 0; y < height; y++)
            {
                int filter = raw[y * (stride + 1)];
                int source = y * (stride + 1) + 1;
                int row = y * stride;
                int previous = row - stride;
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= bpp ? output[row + x - bpp] : 0;
                    int b = y > 0 ? output[previous + x] : 0;
                    int c = x >= bpp && y > 0 ? output[previous + x - bpp] : 0;
                    int value = raw[source + x];
                    switch (filter)
                    {
                        case 0: break;
                        case 1: value += a; break;
                        case 2: value += b; break;
                        case 3: value += (a + b) / 2; break;
                        case 4: value += Paeth(a, b, c); break;
                        default: throw new InvalidDataException("corrupt PNG: unknown filter " + filter + " on row " + y);
                    }
                    output[row + x] = (byte)value;
                }
            }
            return output;
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a);
            int pb = Math.Abs(p - b);
            int pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc)
                return a;
            return pb <= pc ? b : c;
        }

        private static RgbaImage ToRgba(byte[] samples, int width, int height, int colourType, byte[] palette, byte[] transparency)
        {
            RgbaImage image = new RgbaImage(width, height);
            byte[] rgba = image.Pixels;
            int count = width * height;
            for (int i = 0; i < count; i++)
            {
                byte r, g, b, alpha = 255;
                switch (colourType)
                {
                    case 0:
                        r = g = b = samples[i];
                        if (transparency != null && transparency.Length >= 2 && samples[i] == transparency[1] && transparency[0] == 0)
                            alpha = 0;
                        break;
                    case 2:
                        r = samples[i * 3];
                        g = samples[i * 3 + 1];
                        b = samples[i * 3 + 2];
                        if (transparency != null && transparency.Length >= 6
                            && transparency[1] == r && transparency[3] == g && transparency[5] == b
                            && transparency[0] == 0 && transparency[2] == 0 && transparency[4] == 0)
                            alpha = 0;
                        break;
                    case 3:
                        int index = samples[i];
                        if (index * 3 + 2 >= palette.Length)
                            throw new InvalidDataException("corrupt PNG: colour index " + index + " beyond the palette of " + (palette.Length / 3));
                        r = palette[index * 3];
                        g = palette[index * 3 + 1];
                        b = palette[index * 3 + 2];
                        if (transparency != null && index < transparency.Length)
                            alpha = transparency[index];
                        break;
                    case 4:
                        r = g = b = samples[i * 2];
                        alpha = samples[i * 2 + 1];
                        break;
                    default:
                        r = samples[i * 4];
                        g = samples[i * 4 + 1];
                        b = samples[i * 4 + 2];
                        alpha = samples[i * 4 + 3];
                        break;
                }
                rgba[i * 4] = r;
                rgba[i * 4 + 1] = g;
                rgba[i * 4 + 2] = b;
                rgba[i * 4 + 3] = alpha;
            }
            return image;
        }

        private static uint ReadBigEndian(byte[] buffer, int offset)
        {
            return ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16) | ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];
        }
    }
}
