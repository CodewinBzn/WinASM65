using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WinASM65.Graphics.Assets;
using WinASM65.Graphics.Systems;

namespace WinASM65.Graphics.Export
{
    /// <summary>What an export wrote and what it found wrong.</summary>
    public sealed class ExportResult
    {
        public ExportResult()
        {
            Written = new List<string>();
            Diagnostics = new List<AssetDiagnostic>();
        }

        /// <summary>Full paths of the files written, in the order they were written.</summary>
        public List<string> Written { get; private set; }

        public List<AssetDiagnostic> Diagnostics { get; private set; }

        public bool HasErrors { get { return AssetDiagnostic.AnyErrors(Diagnostics); } }
    }

    /// <summary>
    /// Turns assets into what a program includes: <c>.asm</c> sources of tables and
    /// constants, and binary files for <c>.incbin</c>.
    ///
    /// <para>
    /// Every output is written next to its asset (<c>title.screen.json</c> gives
    /// <c>title.nam</c> and <c>title.screen.asm</c>) and depends on nothing but the
    /// asset: same asset, same bytes, so generated files can be committed and
    /// diffed. An asset with errors exports nothing; its diagnostics say why.
    /// </para>
    /// </summary>
    public static class AssetExporter
    {
        /// <summary>Exports one asset file. A tile set needs no export and writes nothing.</summary>
        public static ExportResult Export(string path)
        {
            ExportResult result = new ExportResult();
            string full = Path.GetFullPath(path);
            AssetDocument document = AssetDocument.Load(full);
            result.Diagnostics.AddRange(document.Diagnostics);
            if (document.Asset == null || document.HasErrors || document.Kind == AssetKind.Chr)
                return result;

            string basePath = AssetKinds.BasePath(full);
            string sourceName = Path.GetFileName(full);
            switch (document.Kind)
            {
                case AssetKind.Palette:
                    PaletteAsset palette = (PaletteAsset)document.Asset;
                    WriteBytes(result, basePath + ".pal.bin", PaletteBinary(palette));
                    WriteText(result, basePath + ".pal.asm", PaletteSource(palette, sourceName));
                    break;
                case AssetKind.Metasprite:
                    WriteText(result, basePath + ".msprite.asm", MetaspriteSource((MetaspriteAsset)document.Asset, sourceName));
                    break;
                case AssetKind.Screen:
                    ScreenAsset screen = (ScreenAsset)document.Asset;
                    byte[] data = ScreenBinary(screen);
                    string dataPath = basePath + (IsRle(screen) ? ".rle" : ".nam");
                    WriteBytes(result, dataPath, data);
                    WriteText(result, basePath + ".screen.asm", ScreenSource(screen, sourceName, Path.GetFileName(dataPath), data.Length));
                    break;
                case AssetKind.Map:
                    WriteText(result, basePath + ".map.asm", MapSource((MapAsset)document.Asset, sourceName));
                    break;
            }
            return result;
        }

        /// <summary>
        /// Exports every asset under a folder, subfolders included, in ordinal order
        /// of their paths. One asset's errors do not stop the others.
        /// </summary>
        public static ExportResult ExportAll(string folder)
        {
            ExportResult result = new ExportResult();
            string root = Path.GetFullPath(folder);
            if (!Directory.Exists(root))
            {
                result.Diagnostics.Add(AssetDiagnostic.Error(root, "$", "folder not found"));
                return result;
            }
            List<string> files = new List<string>(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
            files.Sort(StringComparer.Ordinal);
            foreach (string file in files)
            {
                AssetKind kind = AssetKinds.Of(file);
                if (kind == AssetKind.Unknown || kind == AssetKind.Chr)
                    continue;
                ExportResult one = Export(file);
                result.Written.AddRange(one.Written);
                result.Diagnostics.AddRange(one.Diagnostics);
            }
            return result;
        }

        #region Palettes

        /// <summary>The palette memory image: 32 bytes on the NES, background palettes first.</summary>
        public static byte[] PaletteBinary(PaletteAsset palette)
        {
            return SystemOf(palette.System).EncodePalettes(palette.ColorLists());
        }

        /// <summary>
        /// <c>prefix + palettes:</c> and one <c>.byte</c> row per palette, then a
        /// constant per palette name with its index (<c>PAL_TEXT = 0</c>).
        /// </summary>
        public static string PaletteSource(PaletteAsset palette, string sourceName)
        {
            IGraphicsSystem system = SystemOf(palette.System);
            byte[] ram = system.EncodePalettes(palette.ColorLists());
            AsmWriter asm = new AsmWriter(sourceName);
            asm.Blank();
            asm.Label(AsmNames.Label(palette.Prefix, "palettes"));
            int per = system.ColorsPerPalette;
            for (int p = 0; p * per < ram.Length; p++)
            {
                List<int> row = new List<int>();
                for (int c = 0; c < per; c++)
                    row.Add(ram[p * per + c]);
                string kind = p < system.BackgroundPaletteCount ? "background " + p : "sprite " + (p - system.BackgroundPaletteCount);
                string name = p < palette.Palettes.Count ? palette.Palettes[p].Name : null;
                asm.HexBytes(row, string.IsNullOrEmpty(name) ? kind : kind + ": " + name);
            }
            asm.Blank();
            for (int p = 0; p < palette.Palettes.Count; p++)
                asm.Constant(AsmNames.Constant(palette.Prefix, "pal_" + AsmNames.Snake(palette.Palettes[p].Name)), p);
            return asm.ToString();
        }

        #endregion

        #region Metasprites

        /// <summary>
        /// One table per metasprite, rows of <c>.byte dy, tile, attributes, dx</c>
        /// (offsets from the origin, as unsigned bytes) ended by <c>.byte $80</c>;
        /// address tables split in low and high bytes, an index constant per
        /// metasprite; per animation, rows of <c>.byte metasprite, duration</c>
        /// ended by <c>.byte $FF, loop</c>.
        /// </summary>
        /// <remarks>
        /// The row order is OAM's (Y, tile, attributes, X), so a drawing loop adds
        /// the position and copies four bytes. $80 can end the list because -128 is
        /// refused as a Y offset by validation.
        /// </remarks>
        public static string MetaspriteSource(MetaspriteAsset asset, string sourceName)
        {
            IGraphicsSystem system = SystemOf(asset.System);
            AsmWriter asm = new AsmWriter(sourceName);
            List<string> labels = new List<string>();
            foreach (Metasprite sprite in asset.Metasprites)
            {
                string label = AsmNames.Label(asset.Prefix, sprite.Name);
                labels.Add(label);
                asm.Blank();
                asm.Label(label);
                foreach (MetaspriteTile tile in sprite.Tiles)
                {
                    int attributes = system.EncodeSpriteAttributes(tile.Palette, tile.FlipH, tile.FlipV, tile.Behind);
                    asm.HexBytes(new[] { tile.Y - sprite.OriginY, tile.Tile, attributes, tile.X - sprite.OriginX });
                }
                asm.HexBytes(new[] { 0x80 });
            }

            if (labels.Count > 0)
            {
                asm.Blank();
                asm.Label(AsmNames.Label(asset.Prefix, "metasprites_lo"));
                asm.Directive(".lobytes", labels);
                asm.Label(AsmNames.Label(asset.Prefix, "metasprites_hi"));
                asm.Directive(".hibytes", labels);
            }

            asm.Blank();
            for (int m = 0; m < asset.Metasprites.Count; m++)
                asm.Constant(AsmNames.Constant(asset.Prefix, "ms_" + AsmNames.Snake(asset.Metasprites[m].Name)), m);
            asm.Constant(AsmNames.Constant(asset.Prefix, "metasprite_count"), asset.Metasprites.Count);

            foreach (MetaspriteAnimation animation in asset.Animations ?? new List<MetaspriteAnimation>())
            {
                asm.Blank();
                asm.Label(AsmNames.Label(asset.Prefix, AsmNames.Snake(animation.Name) + "_anim"));
                foreach (AnimationFrame frame in animation.Frames)
                    asm.DecimalBytes(new[] { asset.IndexOf(frame.Metasprite), frame.Duration });
                asm.Directive(".byte", new[] { "$FF", animation.Loop ? "1" : "0" });
            }
            return asm.ToString();
        }

        #endregion

        #region Screens

        /// <summary>The screen as the video chip reads it, run-length encoded when the asset asks for it.</summary>
        public static byte[] ScreenBinary(ScreenAsset screen)
        {
            byte[] table = SystemOf(screen.System).EncodeScreen(screen.Tiles, screen.Attributes);
            return IsRle(screen) ? Rle.Encode(table) : table;
        }

        /// <summary><c>prefix + name:</c> with the data file included, and its size as a constant.</summary>
        public static string ScreenSource(ScreenAsset screen, string sourceName, string dataFileName, int size)
        {
            AsmWriter asm = new AsmWriter(sourceName);
            if (IsRle(screen))
                asm.Comment("Run-length encoded: pairs of count (1-255) and value, ended by a count of 0.");
            asm.Blank();
            asm.Label(AsmNames.Label(screen.Prefix, screen.Name));
            asm.Directive(".incbin", new[] { "\"" + dataFileName + "\"" });
            asm.Blank();
            asm.Constant(AsmNames.Constant(screen.Prefix, AsmNames.Snake(screen.Name) + "_size"), size);
            return asm.ToString();
        }

        private static bool IsRle(ScreenAsset screen)
        {
            return string.Equals(screen.Compression, ScreenAsset.CompressionRle, StringComparison.Ordinal);
        }

        #endregion

        #region Maps

        /// <summary>
        /// One table per corner of the metatiles and one of their palettes (a
        /// metatile's parts are at the same index in each), the cells one row per
        /// map row, and the map's dimensions.
        /// </summary>
        public static string MapSource(MapAsset map, string sourceName)
        {
            AsmWriter asm = new AsmWriter(sourceName);
            string[] corners = { "tl", "tr", "bl", "br" };
            for (int corner = 0; corner < 4; corner++)
            {
                List<int> column = new List<int>();
                foreach (Metatile metatile in map.Metatiles)
                    column.Add(metatile.Tiles[corner]);
                asm.Blank();
                asm.Label(AsmNames.Label(map.Prefix, "metatile_" + corners[corner]));
                asm.DecimalByteRows(column, 16);
            }
            List<int> palettes = new List<int>();
            foreach (Metatile metatile in map.Metatiles)
                palettes.Add(metatile.Palette);
            asm.Blank();
            asm.Label(AsmNames.Label(map.Prefix, "metatile_pal"));
            asm.DecimalByteRows(palettes, 16);

            asm.Blank();
            asm.Label(AsmNames.Label(map.Prefix, AsmNames.Snake(map.Name) + "_map"));
            for (int row = 0; row < map.Height; row++)
            {
                List<int> cells = new List<int>();
                for (int column = 0; column < map.Width; column++)
                    cells.Add(map.CellAt(column, row));
                asm.DecimalBytes(cells);
            }

            asm.Blank();
            string name = AsmNames.Snake(map.Name);
            asm.Constant(AsmNames.Constant(map.Prefix, name + "_width"), map.Width);
            asm.Constant(AsmNames.Constant(map.Prefix, name + "_height"), map.Height);
            asm.Constant(AsmNames.Constant(map.Prefix, "metatile_count"), map.Metatiles.Count);
            return asm.ToString();
        }

        #endregion

        private static IGraphicsSystem SystemOf(string id)
        {
            IGraphicsSystem system = GraphicsSystems.For(id);
            if (system == null)
                throw new ArgumentException("unknown graphics system '" + id + "'", nameof(id));
            return system;
        }

        private static void WriteText(ExportResult result, string path, string text)
        {
            File.WriteAllText(path, text, new UTF8Encoding(false));
            result.Written.Add(path);
        }

        private static void WriteBytes(ExportResult result, string path, byte[] data)
        {
            File.WriteAllBytes(path, data);
            result.Written.Add(path);
        }
    }
}
