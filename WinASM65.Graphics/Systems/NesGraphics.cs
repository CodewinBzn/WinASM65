using System;
using System.Collections.Generic;
using WinASM65.Systems;

namespace WinASM65.Graphics.Systems
{
    /// <summary>
    /// The NES picture processing unit as the designer sees it.
    ///
    /// <para>
    /// Tiles are 8x8 pixels of 2 bits stored as two planes: 8 bytes of bit 0 (one
    /// byte per row, the most significant bit is the leftmost pixel), then 8 bytes
    /// of bit 1. Palette memory holds 4 background then 4 sprite palettes of 4
    /// colours; colour 0 of every palette shows the universal background colour
    /// (for sprites, it is transparent). A screen is 32x30 tiles; one palette is
    /// chosen per 16x16 pixel block, and four blocks share one attribute byte.
    /// </para>
    /// </summary>
    public sealed class NesGraphics : IGraphicsSystem
    {
        public const string SystemId = "nes";

        // Indexes the importer must not pick: $0D is "blacker than black" (it can
        // upset a television's sync), and $xE/$xF and $1D repeat black.
        private static readonly HashSet<int> Avoided = new HashSet<int> { 0x0D, 0x0E, 0x1D, 0x1E, 0x1F, 0x2E, 0x2F, 0x3E, 0x3F };

        private readonly IReadOnlyList<int> _masterPalette;
        private readonly int[] _selectable;

        public NesGraphics()
        {
            _masterPalette = new NesSystem().MasterPalette;
            List<int> selectable = new List<int>();
            for (int i = 0; i < _masterPalette.Count; i++)
            {
                if (!Avoided.Contains(i))
                    selectable.Add(i);
            }
            _selectable = selectable.ToArray();
        }

        public string Id { get { return SystemId; } }
        public string DisplayName { get { return "Nintendo Entertainment System"; } }
        public int TileWidth { get { return 8; } }
        public int TileHeight { get { return 8; } }
        public int BitsPerPixel { get { return 2; } }
        public int BytesPerTile { get { return 16; } }
        public int MaxTiles { get { return 256; } }
        public int ColorsPerPalette { get { return 4; } }
        public int BackgroundPaletteCount { get { return 4; } }
        public int SpritePaletteCount { get { return 4; } }
        public IReadOnlyList<int> MasterPalette { get { return _masterPalette; } }
        public IReadOnlyList<int> SelectableColors { get { return _selectable; } }
        public int ScreenWidthTiles { get { return 32; } }
        public int ScreenHeightTiles { get { return 30; } }
        public int AttributeBlockWidth { get { return 2; } }
        public int AttributeBlockHeight { get { return 2; } }
        public int SpritesPerScanline { get { return 8; } }

        public byte[] EncodeTile(byte[] pixels)
        {
            if (pixels == null || pixels.Length != 64)
                throw new ArgumentException("an NES tile is 64 pixels", nameof(pixels));
            byte[] data = new byte[16];
            for (int y = 0; y < 8; y++)
            {
                int plane0 = 0;
                int plane1 = 0;
                for (int x = 0; x < 8; x++)
                {
                    int colour = pixels[y * 8 + x] & 3;
                    plane0 = (plane0 << 1) | (colour & 1);
                    plane1 = (plane1 << 1) | (colour >> 1);
                }
                data[y] = (byte)plane0;
                data[8 + y] = (byte)plane1;
            }
            return data;
        }

        public byte[] DecodeTile(byte[] data, int offset)
        {
            if (data == null || offset < 0 || offset + 16 > data.Length)
                throw new ArgumentOutOfRangeException(nameof(offset), "an NES tile is 16 bytes");
            byte[] pixels = new byte[64];
            for (int y = 0; y < 8; y++)
            {
                int plane0 = data[offset + y];
                int plane1 = data[offset + 8 + y];
                for (int x = 0; x < 8; x++)
                {
                    int bit = 7 - x;
                    pixels[y * 8 + x] = (byte)(((plane0 >> bit) & 1) | (((plane1 >> bit) & 1) << 1));
                }
            }
            return pixels;
        }

        /// <summary>Palette RAM at $3F00: 8 palettes of 4 bytes, missing entries are $0F (black).</summary>
        public byte[] EncodePalettes(IReadOnlyList<IReadOnlyList<int>> palettes)
        {
            byte[] ram = new byte[32];
            for (int i = 0; i < ram.Length; i++)
                ram[i] = 0x0F;
            for (int p = 0; p < 8 && palettes != null && p < palettes.Count; p++)
            {
                IReadOnlyList<int> colours = palettes[p];
                for (int c = 0; c < 4 && colours != null && c < colours.Count; c++)
                    ram[p * 4 + c] = (byte)(colours[c] & 0x3F);
            }
            return ram;
        }

        /// <summary>OAM byte 2: palette in bits 0-1, priority (behind the background) in bit 5, flips in bits 6 and 7.</summary>
        public int EncodeSpriteAttributes(int palette, bool flipH, bool flipV, bool behind)
        {
            return (palette & 3) | (behind ? 0x20 : 0) | (flipH ? 0x40 : 0) | (flipV ? 0x80 : 0);
        }

        /// <summary>
        /// A nametable: 960 tile bytes, then 64 attribute bytes. Each attribute byte
        /// covers a 32x32 pixel area, four 16x16 blocks: top left in bits 0-1, top
        /// right in 2-3, bottom left in 4-5, bottom right in 6-7. The screen is 15
        /// blocks high, so the last row of attribute bytes only covers top halves;
        /// their bottom bits are 0.
        /// </summary>
        public byte[] EncodeScreen(IReadOnlyList<int> tiles, IReadOnlyList<int> attributes)
        {
            byte[] table = new byte[1024];
            for (int i = 0; i < 960 && tiles != null && i < tiles.Count; i++)
                table[i] = (byte)tiles[i];

            const int blocksWide = 16;
            const int blocksHigh = 15;
            for (int ay = 0; ay < 8; ay++)
            {
                for (int ax = 0; ax < 8; ax++)
                {
                    int bx = ax * 2;
                    int by = ay * 2;
                    int value = Block(attributes, bx, by, blocksWide, blocksHigh)
                        | (Block(attributes, bx + 1, by, blocksWide, blocksHigh) << 2)
                        | (Block(attributes, bx, by + 1, blocksWide, blocksHigh) << 4)
                        | (Block(attributes, bx + 1, by + 1, blocksWide, blocksHigh) << 6);
                    table[960 + ay * 8 + ax] = (byte)value;
                }
            }
            return table;
        }

        private static int Block(IReadOnlyList<int> attributes, int bx, int by, int wide, int high)
        {
            if (attributes == null || bx >= wide || by >= high)
                return 0;
            int index = by * wide + bx;
            return index < attributes.Count ? attributes[index] & 3 : 0;
        }
    }
}
