using System.Collections.Generic;

namespace WinASM65.Graphics.Systems
{
    /// <summary>
    /// What the designer needs to know about a machine's video hardware: the shape
    /// of a tile and how it is stored, how many palettes there are and how big, the
    /// size of a screen and of the area an attribute covers.
    ///
    /// Everything that differs between machines goes through this contract, so the
    /// asset model, the exporters, the renderer and the importer stay the same when
    /// a machine is added: a new machine is one class implementing it, registered in
    /// <see cref="GraphicsSystems"/>.
    /// </summary>
    public interface IGraphicsSystem
    {
        /// <summary>The id asset files name in their <c>system</c> field, such as <c>nes</c>.</summary>
        string Id { get; }

        string DisplayName { get; }

        /// <summary>Width of a tile in pixels.</summary>
        int TileWidth { get; }

        /// <summary>Height of a tile in pixels.</summary>
        int TileHeight { get; }

        int BitsPerPixel { get; }

        /// <summary>Bytes one tile takes in the machine's tile data (16 on the NES).</summary>
        int BytesPerTile { get; }

        /// <summary>Tiles one tile table addresses: the largest tile index is this minus one.</summary>
        int MaxTiles { get; }

        /// <summary>Colours of one palette, colour 0 included.</summary>
        int ColorsPerPalette { get; }

        int BackgroundPaletteCount { get; }

        int SpritePaletteCount { get; }

        /// <summary>
        /// The colours the machine can show, as 0xRRGGBB. A palette entry is an
        /// index into this list.
        /// </summary>
        IReadOnlyList<int> MasterPalette { get; }

        /// <summary>
        /// The master palette indexes an importer may choose. Some machines repeat a
        /// colour under several indexes, some of which are harmful (the NES's $0D
        /// "blacker than black"); an importer picks among these only.
        /// </summary>
        IReadOnlyList<int> SelectableColors { get; }

        /// <summary>Width of a screen in tiles.</summary>
        int ScreenWidthTiles { get; }

        /// <summary>Height of a screen in tiles.</summary>
        int ScreenHeightTiles { get; }

        /// <summary>Width, in tiles, of the area that shares one background palette.</summary>
        int AttributeBlockWidth { get; }

        /// <summary>Height, in tiles, of the area that shares one background palette.</summary>
        int AttributeBlockHeight { get; }

        /// <summary>Sprites the hardware shows on one scanline before it drops the others.</summary>
        int SpritesPerScanline { get; }

        /// <summary>
        /// The machine's bytes for one tile.
        /// </summary>
        /// <param name="pixels">TileWidth x TileHeight colour indexes, row-major, each below 2^BitsPerPixel.</param>
        byte[] EncodeTile(byte[] pixels);

        /// <summary>The colour indexes, row-major, of the tile stored at an offset.</summary>
        byte[] DecodeTile(byte[] data, int offset);

        /// <summary>
        /// The bytes the machine's palette memory holds for the given palettes, in
        /// the order it holds them (background palettes, then sprite palettes).
        /// </summary>
        byte[] EncodePalettes(IReadOnlyList<IReadOnlyList<int>> palettes);

        /// <summary>
        /// The attribute byte of one hardware sprite.
        /// </summary>
        /// <param name="palette">Sprite palette, 0 to SpritePaletteCount - 1.</param>
        int EncodeSpriteAttributes(int palette, bool flipH, bool flipV, bool behind);

        /// <summary>
        /// The bytes of one screen as the video chip reads them: the tile indexes
        /// and the palette choices, packed the machine's way.
        /// </summary>
        /// <param name="tiles">ScreenWidthTiles x ScreenHeightTiles tile indexes, row-major.</param>
        /// <param name="attributes">One background palette per attribute block, row-major.</param>
        byte[] EncodeScreen(IReadOnlyList<int> tiles, IReadOnlyList<int> attributes);
    }
}
