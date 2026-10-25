using System.Collections.Generic;
using WinASM65.Graphics.Systems;

namespace WinASM65.Graphics.Assets
{
    /// <summary>
    /// A map of metatiles (<c>*.map.json</c>): blocks of 2x2 tiles with one
    /// background palette, laid on a grid of any size.
    ///
    /// <para>
    /// A metatile is the size of an NES attribute block, so a map never asks for
    /// two palettes in one block, and a screen of it is 16x15 metatiles. Width and
    /// height count metatiles; cells are metatile indexes, row-major.
    /// </para>
    /// <code>
    /// { "version": 1, "system": "nes", "chr": "tiles.chr", "palettes": "game.pal.json", "prefix": "",
    ///   "name": "level1",
    ///   "metatiles": [ { "name": "sky", "tiles": [0, 0, 0, 0], "palette": 0 } ],
    ///   "width": 32, "height": 15, "cells": [ width * height metatile indexes ] }
    /// </code>
    /// </summary>
    public sealed class MapAsset
    {
        public int Version { get; set; } = AssetJson.CurrentVersion;
        public string System { get; set; } = NesGraphics.SystemId;
        public string Chr { get; set; }
        public string Palettes { get; set; }
        public string Prefix { get; set; } = string.Empty;
        public string Name { get; set; }
        public List<Metatile> Metatiles { get; set; } = new List<Metatile>();

        /// <summary>Metatiles across.</summary>
        public int Width { get; set; }

        /// <summary>Metatiles down.</summary>
        public int Height { get; set; }

        public List<int> Cells { get; set; } = new List<int>();

        public static AssetLoadResult<MapAsset> Load(string path)
        {
            List<AssetDiagnostic> diagnostics = new List<AssetDiagnostic>();
            MapAsset asset = AssetJson.Read<MapAsset>(path, diagnostics);
            if (asset != null)
                diagnostics.AddRange(asset.Validate(path));
            return new AssetLoadResult<MapAsset>(asset, diagnostics);
        }

        /// <summary>Writes the file with one map row per line of cells.</summary>
        public void Save(string path)
        {
            int width = Width;
            AssetJson.Write(path, this, property => property == "cells" ? width : 0);
        }

        /// <summary>The metatile index of a cell, 0 outside the map.</summary>
        public int CellAt(int column, int row)
        {
            int index = row * Width + column;
            return Cells != null && column >= 0 && column < Width && index >= 0 && index < Cells.Count ? Cells[index] : 0;
        }

        public List<AssetDiagnostic> Validate(string path)
        {
            AssetValidator check = new AssetValidator(path);
            IGraphicsSystem system = check.Header(Version, System);
            int tileCount = check.TileCount(path, Chr, system);
            check.PaletteReference(path, Palettes);
            if (string.IsNullOrWhiteSpace(Name))
                check.Error("$.name", "the map has no name");

            if (Metatiles == null)
            {
                check.Error("$.metatiles", "missing metatiles");
            }
            else
            {
                if (Metatiles.Count > 256)
                    check.Error("$.metatiles", Metatiles.Count + " metatiles: a cell is one byte, 256 at most");
                HashSet<string> names = new HashSet<string>();
                for (int m = 0; m < Metatiles.Count; m++)
                {
                    string at = "$.metatiles[" + m + "]";
                    Metatile metatile = Metatiles[m];
                    if (metatile == null)
                    {
                        check.Error(at, "missing metatile");
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(metatile.Name))
                        check.Name(at + ".name", metatile.Name, names, "metatile");
                    if (system != null)
                        check.Range(at + ".palette", metatile.Palette, 0, system.BackgroundPaletteCount - 1, "background palette");
                    if (!check.Length(at + ".tiles", metatile.Tiles, 4, "tiles (top left, top right, bottom left, bottom right)"))
                        continue;
                    for (int t = 0; t < 4; t++)
                        check.Tile(at + ".tiles[" + t + "]", metatile.Tiles[t], tileCount, system);
                }
            }

            if (Width <= 0 || Height <= 0)
            {
                check.Error(Width <= 0 ? "$.width" : "$.height", "a map is at least one metatile wide and high");
                return check.Diagnostics;
            }
            if (check.Length("$.cells", Cells, Width * Height, "cells"))
            {
                int count = Metatiles == null ? 0 : Metatiles.Count;
                for (int i = 0; i < Cells.Count; i++)
                    check.Range("$.cells[" + i + "]", Cells[i], 0, count - 1, "metatile");
            }
            return check.Diagnostics;
        }
    }

    /// <summary>Four tiles and the background palette they share.</summary>
    public sealed class Metatile
    {
        public string Name { get; set; }

        /// <summary>Top left, top right, bottom left, bottom right.</summary>
        public List<int> Tiles { get; set; } = new List<int>();

        public int Palette { get; set; }
    }
}
