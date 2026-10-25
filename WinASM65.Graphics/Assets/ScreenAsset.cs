using System;
using System.Collections.Generic;
using WinASM65.Graphics.Systems;

namespace WinASM65.Graphics.Assets
{
    /// <summary>
    /// One screen of background (<c>*.screen.json</c>): a tile per cell and a
    /// background palette per attribute block.
    /// <code>
    /// { "version": 1, "system": "nes", "chr": "tiles.chr", "palettes": "game.pal.json", "prefix": "",
    ///   "name": "title", "compression": "none",
    ///   "tiles": [ 960 tile indexes, 32 per row ],
    ///   "attributes": [ 240 palette indexes 0-3, one per 16x16 block, 16 per row ] }
    /// </code>
    /// Attributes are kept one per block, as people think of them, not packed four
    /// to a byte as the PPU reads them: the packing is the exporter's job.
    /// </summary>
    public sealed class ScreenAsset
    {
        public const string CompressionNone = "none";
        public const string CompressionRle = "rle";

        public int Version { get; set; } = AssetJson.CurrentVersion;
        public string System { get; set; } = NesGraphics.SystemId;
        public string Chr { get; set; }
        public string Palettes { get; set; }
        public string Prefix { get; set; } = string.Empty;
        public string Name { get; set; }

        /// <summary><c>none</c> exports the nametable as it is (.nam), <c>rle</c> run-length encoded (.rle).</summary>
        public string Compression { get; set; } = CompressionNone;

        public List<int> Tiles { get; set; } = new List<int>();
        public List<int> Attributes { get; set; } = new List<int>();

        /// <summary>Attribute blocks across a screen.</summary>
        public static int AttributeColumns(IGraphicsSystem system)
        {
            return (system.ScreenWidthTiles + system.AttributeBlockWidth - 1) / system.AttributeBlockWidth;
        }

        /// <summary>Attribute blocks down a screen.</summary>
        public static int AttributeRows(IGraphicsSystem system)
        {
            return (system.ScreenHeightTiles + system.AttributeBlockHeight - 1) / system.AttributeBlockHeight;
        }

        /// <summary>A screen of tile 0 and palette 0.</summary>
        public static ScreenAsset Blank(IGraphicsSystem system, string name)
        {
            ScreenAsset screen = new ScreenAsset { System = system.Id, Name = name };
            for (int i = 0; i < system.ScreenWidthTiles * system.ScreenHeightTiles; i++)
                screen.Tiles.Add(0);
            for (int i = 0; i < AttributeColumns(system) * AttributeRows(system); i++)
                screen.Attributes.Add(0);
            return screen;
        }

        public static AssetLoadResult<ScreenAsset> Load(string path)
        {
            List<AssetDiagnostic> diagnostics = new List<AssetDiagnostic>();
            ScreenAsset asset = AssetJson.Read<ScreenAsset>(path, diagnostics);
            if (asset != null)
                diagnostics.AddRange(asset.Validate(path));
            return new AssetLoadResult<ScreenAsset>(asset, diagnostics);
        }

        /// <summary>Writes the file with tiles one screen row per line and attributes one block row per line.</summary>
        public void Save(string path)
        {
            IGraphicsSystem system = GraphicsSystems.For(System);
            int tileRow = system == null ? 32 : system.ScreenWidthTiles;
            int attributeRow = system == null ? 16 : AttributeColumns(system);
            AssetJson.Write(path, this, property =>
                property == "tiles" ? tileRow : property == "attributes" ? attributeRow : 0);
        }

        /// <summary>The tile at a cell, 0 outside the list.</summary>
        public int TileAt(int column, int row, IGraphicsSystem system)
        {
            int index = row * system.ScreenWidthTiles + column;
            return Tiles != null && index >= 0 && index < Tiles.Count ? Tiles[index] : 0;
        }

        /// <summary>The background palette of the block a cell is in, 0 outside the list.</summary>
        public int PaletteAt(int column, int row, IGraphicsSystem system)
        {
            int index = (row / system.AttributeBlockHeight) * AttributeColumns(system) + column / system.AttributeBlockWidth;
            return Attributes != null && index >= 0 && index < Attributes.Count ? Attributes[index] : 0;
        }

        public List<AssetDiagnostic> Validate(string path)
        {
            AssetValidator check = new AssetValidator(path);
            IGraphicsSystem system = check.Header(Version, System);
            int tileCount = check.TileCount(path, Chr, system);
            check.PaletteReference(path, Palettes);
            if (string.IsNullOrWhiteSpace(Name))
                check.Error("$.name", "the screen has no name");
            if (!string.Equals(Compression ?? CompressionNone, CompressionNone, StringComparison.Ordinal)
                && !string.Equals(Compression, CompressionRle, StringComparison.Ordinal))
                check.Error("$.compression", "compression '" + Compression + "' is not none or rle");
            if (system == null)
                return check.Diagnostics;

            if (check.Length("$.tiles", Tiles, system.ScreenWidthTiles * system.ScreenHeightTiles, "tiles"))
            {
                for (int i = 0; i < Tiles.Count; i++)
                    check.Tile("$.tiles[" + i + "]", Tiles[i], tileCount, system);
            }
            if (check.Length("$.attributes", Attributes, AttributeColumns(system) * AttributeRows(system), "attributes"))
            {
                for (int i = 0; i < Attributes.Count; i++)
                    check.Range("$.attributes[" + i + "]", Attributes[i], 0, system.BackgroundPaletteCount - 1, "background palette");
            }
            return check.Diagnostics;
        }
    }
}
