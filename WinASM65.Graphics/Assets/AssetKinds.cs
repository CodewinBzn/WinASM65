using System;
using System.IO;

namespace WinASM65.Graphics.Assets
{
    /// <summary>The kinds of asset file, told apart by their extension.</summary>
    public enum AssetKind
    {
        Unknown,

        /// <summary><c>*.chr</c>: binary tile data, in the machine's own format.</summary>
        Chr,

        /// <summary><c>*.pal.json</c>: the palettes.</summary>
        Palette,

        /// <summary><c>*.msprite.json</c>: metasprites and their animations.</summary>
        Metasprite,

        /// <summary><c>*.screen.json</c>: one full screen of background tiles.</summary>
        Screen,

        /// <summary><c>*.map.json</c>: a map of metatiles, any number of screens.</summary>
        Map
    }

    /// <summary>Extensions and file names of the asset kinds.</summary>
    public static class AssetKinds
    {
        public const string ChrExtension = ".chr";
        public const string PaletteExtension = ".pal.json";
        public const string MetaspriteExtension = ".msprite.json";
        public const string ScreenExtension = ".screen.json";
        public const string MapExtension = ".map.json";

        /// <summary>The kind of a file, from its name.</summary>
        public static AssetKind Of(string path)
        {
            if (string.IsNullOrEmpty(path))
                return AssetKind.Unknown;
            string name = Path.GetFileName(path);
            if (name.EndsWith(PaletteExtension, StringComparison.OrdinalIgnoreCase))
                return AssetKind.Palette;
            if (name.EndsWith(MetaspriteExtension, StringComparison.OrdinalIgnoreCase))
                return AssetKind.Metasprite;
            if (name.EndsWith(ScreenExtension, StringComparison.OrdinalIgnoreCase))
                return AssetKind.Screen;
            if (name.EndsWith(MapExtension, StringComparison.OrdinalIgnoreCase))
                return AssetKind.Map;
            if (name.EndsWith(ChrExtension, StringComparison.OrdinalIgnoreCase))
                return AssetKind.Chr;
            return AssetKind.Unknown;
        }

        /// <summary>The extension of a kind, with its leading dot.</summary>
        public static string ExtensionOf(AssetKind kind)
        {
            switch (kind)
            {
                case AssetKind.Chr: return ChrExtension;
                case AssetKind.Palette: return PaletteExtension;
                case AssetKind.Metasprite: return MetaspriteExtension;
                case AssetKind.Screen: return ScreenExtension;
                case AssetKind.Map: return MapExtension;
                default: return string.Empty;
            }
        }

        /// <summary>
        /// The path without the kind's extension: <c>gfx/title.screen.json</c> gives
        /// <c>gfx/title</c>. Exported files are this plus their own extension, so
        /// they land next to the asset.
        /// </summary>
        public static string BasePath(string path)
        {
            string extension = ExtensionOf(Of(path));
            return extension.Length == 0 ? path : path.Substring(0, path.Length - extension.Length);
        }

        /// <summary>
        /// A path an asset names (its <c>chr</c>, its <c>palettes</c>), resolved
        /// against the folder of the asset; null for none.
        /// </summary>
        public static string Resolve(string assetPath, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative))
                return null;
            if (Path.IsPathRooted(relative))
                return Path.GetFullPath(relative);
            string directory = string.IsNullOrEmpty(assetPath) ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(Path.GetFullPath(assetPath));
            return Path.GetFullPath(Path.Combine(directory, relative));
        }

        /// <summary>A path as an asset names it: relative to the asset's folder, with forward slashes.</summary>
        public static string Relative(string assetPath, string target)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(assetPath));
            return Path.GetRelativePath(directory, Path.GetFullPath(target)).Replace('\\', '/');
        }
    }
}
