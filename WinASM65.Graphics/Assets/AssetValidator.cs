using System.Collections.Generic;
using System.IO;
using WinASM65.Graphics.Systems;

namespace WinASM65.Graphics.Assets
{
    /// <summary>
    /// The checks the asset kinds share: version, system, names, ranges, and the
    /// tile count of the referenced tile set.
    /// </summary>
    internal sealed class AssetValidator
    {
        private readonly string _file;

        public AssetValidator(string file)
        {
            _file = file;
            Diagnostics = new List<AssetDiagnostic>();
        }

        public List<AssetDiagnostic> Diagnostics { get; private set; }

        public void Error(string path, string message)
        {
            Diagnostics.Add(AssetDiagnostic.Error(_file, path, message));
        }

        public void Warning(string path, string message)
        {
            Diagnostics.Add(AssetDiagnostic.Warning(_file, path, message));
        }

        /// <summary>Checks the version and the system; the system, or null when it is unknown.</summary>
        public IGraphicsSystem Header(int version, string system)
        {
            if (version != AssetJson.CurrentVersion)
                Error("$.version", "unsupported version " + version + " (expected " + AssetJson.CurrentVersion + ")");
            IGraphicsSystem graphics = GraphicsSystems.For(system);
            if (graphics == null)
                Error("$.system", string.IsNullOrWhiteSpace(system) ? "missing system" : "unknown system '" + system + "'");
            return graphics;
        }

        public void Range(string path, int value, int min, int max, string what)
        {
            if (value < min || value > max)
                Error(path, what + " " + value + " is out of range " + min + ".." + max);
        }

        public bool Length<T>(string path, ICollection<T> items, int expected, string what)
        {
            if (items == null)
            {
                Error(path, "missing " + what);
                return false;
            }
            if (items.Count != expected)
            {
                Error(path, what + " has " + items.Count + " entries, expected " + expected);
                return false;
            }
            return true;
        }

        /// <summary>Reports an empty name and a name used twice in the same list.</summary>
        public void Name(string path, string name, HashSet<string> seen, string what)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                Error(path, what + " has no name");
                return;
            }
            if (!seen.Add(Export.AsmNames.Snake(name)))
                Error(path, what + " name '" + name + "' is used twice (names are compared as labels)");
        }

        /// <summary>
        /// Tiles in the tile set an asset names, or -1 when it names none or the file
        /// is missing (a warning: the asset may be drawn before its tiles).
        /// </summary>
        public int TileCount(string assetPath, string chr, IGraphicsSystem system)
        {
            if (string.IsNullOrWhiteSpace(chr) || system == null)
                return -1;
            string path = AssetKinds.Resolve(assetPath, chr);
            if (!File.Exists(path))
            {
                Warning("$.chr", "tile set '" + chr + "' not found");
                return -1;
            }
            return (int)(new FileInfo(path).Length / system.BytesPerTile);
        }

        public void PaletteReference(string assetPath, string palettes)
        {
            if (string.IsNullOrWhiteSpace(palettes))
                return;
            if (!File.Exists(AssetKinds.Resolve(assetPath, palettes)))
                Warning("$.palettes", "palette file '" + palettes + "' not found");
        }

        /// <summary>A tile index: below the tile set's count when it is known, and within the table.</summary>
        public void Tile(string path, int tile, int tileCount, IGraphicsSystem system)
        {
            if (system == null)
                return;
            if (tile < 0 || tile >= system.MaxTiles)
                Error(path, "tile " + tile + " is out of range 0.." + (system.MaxTiles - 1));
            else if (tileCount >= 0 && tile >= tileCount)
                Error(path, "tile " + tile + " is beyond the tile set (" + tileCount + " tiles)");
        }
    }
}
