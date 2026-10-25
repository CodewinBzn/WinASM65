using System;

namespace WinASM65.Graphics.Imaging
{
    /// <summary>
    /// A picture as 8-bit RGBA, row-major, 4 bytes a pixel: what the renderer
    /// draws, the PNG writer encodes, the PNG reader decodes and the importer reads.
    /// </summary>
    public sealed class RgbaImage
    {
        public RgbaImage(int width, int height)
            : this(width, height, new byte[checked(width * height * 4)])
        {
        }

        /// <summary>An image over existing pixels (not copied).</summary>
        public RgbaImage(int width, int height, byte[] pixels)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(width <= 0 ? nameof(width) : nameof(height), "an image is at least 1x1");
            if (pixels == null || pixels.Length != width * height * 4)
                throw new ArgumentException("an RGBA image of " + width + "x" + height + " is " + (width * height * 4) + " bytes", nameof(pixels));
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>R, G, B, A of each pixel, row after row.</summary>
        public byte[] Pixels { get; private set; }

        /// <summary>The colour of a pixel as 0xRRGGBB.</summary>
        public int GetRgb(int x, int y)
        {
            int i = Offset(x, y);
            return (Pixels[i] << 16) | (Pixels[i + 1] << 8) | Pixels[i + 2];
        }

        public byte GetAlpha(int x, int y)
        {
            return Pixels[Offset(x, y) + 3];
        }

        /// <summary>Sets a pixel from 0xRRGGBB and an alpha.</summary>
        public void SetPixel(int x, int y, int rgb, byte alpha = 255)
        {
            int i = Offset(x, y);
            Pixels[i] = (byte)(rgb >> 16);
            Pixels[i + 1] = (byte)(rgb >> 8);
            Pixels[i + 2] = (byte)rgb;
            Pixels[i + 3] = alpha;
        }

        /// <summary>Fills a rectangle, clipped to the image.</summary>
        public void Fill(int x, int y, int width, int height, int rgb, byte alpha = 255)
        {
            for (int row = Math.Max(0, y); row < Math.Min(Height, y + height); row++)
            {
                for (int column = Math.Max(0, x); column < Math.Min(Width, x + width); column++)
                    SetPixel(column, row, rgb, alpha);
            }
        }

        /// <summary>The image enlarged by a whole factor, each pixel a square (nearest neighbour).</summary>
        public RgbaImage Scale(int factor)
        {
            if (factor < 1)
                throw new ArgumentOutOfRangeException(nameof(factor), "the scale is at least 1");
            if (factor == 1)
                return this;
            RgbaImage scaled = new RgbaImage(Width * factor, Height * factor);
            for (int y = 0; y < scaled.Height; y++)
            {
                for (int x = 0; x < scaled.Width; x++)
                    Buffer.BlockCopy(Pixels, Offset(x / factor, y / factor), scaled.Pixels, (y * scaled.Width + x) * 4, 4);
            }
            return scaled;
        }

        private int Offset(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
                throw new ArgumentOutOfRangeException(x < 0 || x >= Width ? nameof(x) : nameof(y), "pixel " + x + "," + y + " outside " + Width + "x" + Height);
            return (y * Width + x) * 4;
        }
    }
}
