using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using WinASM65.Graphics.Assets;
using WinASM65.Graphics.Imaging;
using WinASM65.Graphics.Systems;

namespace WinASM65.Graphics.Rendering
{
    /// <summary>An image of an asset and, for a sprite, where its origin is in it.</summary>
    public sealed class RenderResult
    {
        public RenderResult(RgbaImage image, int originX, int originY, List<AssetDiagnostic> diagnostics)
        {
            Image = image;
            OriginX = originX;
            OriginY = originY;
            Diagnostics = diagnostics ?? new List<AssetDiagnostic>();
        }

        /// <summary>The picture, or null when the asset could not be drawn.</summary>
        public RgbaImage Image { get; private set; }

        /// <summary>X of the metasprite's origin in the image (scaled), or -1 for an asset without one.</summary>
        public int OriginX { get; private set; }

        /// <summary>Y of the origin, or -1.</summary>
        public int OriginY { get; private set; }

        public List<AssetDiagnostic> Diagnostics { get; private set; }

        public bool HasErrors { get { return AssetDiagnostic.AnyErrors(Diagnostics); } }
    }

    /// <summary>
    /// Draws assets the way the machine would show them: tile sheets, palettes,
    /// screens, maps, metasprites and animation frames.
    ///
    /// <para>
    /// Background colour 0 is the universal background (the first colour of the
    /// first palette), as on the NES; sprite colour 0 is transparent. Without a
    /// palette file, tiles are drawn in four greys so that a tile set alone is
    /// still readable. <c>scale</c> enlarges by whole pixels (nearest neighbour):
    /// pixel art must not be smoothed.
    /// </para>
    /// </summary>
    public static class AssetRenderer
    {
        /// <summary>Tiles per row of a tile sheet.</summary>
        public const int SheetColumns = 16;

        /// <summary>Side, in pixels, of one colour of a palette swatch (before scaling).</summary>
        public const int SwatchSize = 16;

        /// <summary>The marker drawn at a metasprite's origin, where the sprite leaves the pixel transparent.</summary>
        public const int OriginMarkerColor = 0xFF00FF;

        private static readonly int[] Greys = { 0x000000, 0x555555, 0xAAAAAA, 0xFFFFFF };

        #region From the asset objects

        /// <summary>Every tile of a tile set, <see cref="SheetColumns"/> to a row, in one palette.</summary>
        /// <param name="palettes">The palette file, or null for greys.</param>
        /// <param name="palette">Index of the palette in the file (0-3 background, 4-7 sprite on the NES).</param>
        public static RgbaImage RenderTileSheet(ChrFile chr, PaletteAsset palettes, int palette, int scale = 1)
        {
            IGraphicsSystem system = chr.GraphicsSystem;
            int count = Math.Max(1, chr.TileCount);
            int rows = (count + SheetColumns - 1) / SheetColumns;
            RgbaImage image = new RgbaImage(SheetColumns * system.TileWidth, rows * system.TileHeight);
            int[] colours = Colours(system, palettes, palette);
            for (int t = 0; t < chr.TileCount; t++)
                DrawTile(image, system, chr.GetTile(t), (t % SheetColumns) * system.TileWidth, (t / SheetColumns) * system.TileHeight, colours, false, false, false);
            return image.Scale(scale);
        }

        /// <summary>One row per palette, one <see cref="SwatchSize"/> square per colour.</summary>
        public static RgbaImage RenderPaletteSwatch(PaletteAsset palettes, int scale = 1)
        {
            IGraphicsSystem system = GraphicsSystems.For(palettes.System) ?? throw new ArgumentException("unknown system '" + palettes.System + "'");
            int rows = Math.Max(1, palettes.Palettes.Count);
            RgbaImage image = new RgbaImage(system.ColorsPerPalette * SwatchSize, rows * SwatchSize);
            for (int p = 0; p < palettes.Palettes.Count; p++)
            {
                for (int c = 0; c < system.ColorsPerPalette; c++)
                    image.Fill(c * SwatchSize, p * SwatchSize, SwatchSize, SwatchSize, Master(system, palettes.ColorOr(p, c, 0)));
            }
            return image.Scale(scale);
        }

        /// <summary>The machine's whole master palette, 16 colours to a row: what a colour picker shows.</summary>
        public static RgbaImage RenderMasterPalette(IGraphicsSystem system, int scale = 1)
        {
            int count = system.MasterPalette.Count;
            RgbaImage image = new RgbaImage(16 * SwatchSize, ((count + 15) / 16) * SwatchSize);
            for (int i = 0; i < count; i++)
                image.Fill((i % 16) * SwatchSize, (i / 16) * SwatchSize, SwatchSize, SwatchSize, system.MasterPalette[i]);
            return image.Scale(scale);
        }

        /// <summary>A whole screen with its attributes.</summary>
        public static RgbaImage RenderScreen(ScreenAsset screen, ChrFile chr, PaletteAsset palettes, int scale = 1)
        {
            IGraphicsSystem system = SystemOf(screen.System);
            RgbaImage image = new RgbaImage(system.ScreenWidthTiles * system.TileWidth, system.ScreenHeightTiles * system.TileHeight);
            for (int row = 0; row < system.ScreenHeightTiles; row++)
            {
                for (int column = 0; column < system.ScreenWidthTiles; column++)
                {
                    int[] colours = BackgroundColours(system, palettes, screen.PaletteAt(column, row, system));
                    byte[] pixels = TilePixels(chr, system, screen.TileAt(column, row, system));
                    DrawTile(image, system, pixels, column * system.TileWidth, row * system.TileHeight, colours, false, false, false);
                }
            }
            return image.Scale(scale);
        }

        /// <summary>The whole map, every screen of it, metatile by metatile.</summary>
        public static RgbaImage RenderMap(MapAsset map, ChrFile chr, PaletteAsset palettes, int scale = 1)
        {
            IGraphicsSystem system = SystemOf(map.System);
            int metaWidth = system.TileWidth * 2;
            int metaHeight = system.TileHeight * 2;
            RgbaImage image = new RgbaImage(Math.Max(1, map.Width) * metaWidth, Math.Max(1, map.Height) * metaHeight);
            for (int row = 0; row < map.Height; row++)
            {
                for (int column = 0; column < map.Width; column++)
                {
                    int index = map.CellAt(column, row);
                    Metatile metatile = index >= 0 && index < map.Metatiles.Count ? map.Metatiles[index] : null;
                    int[] colours = BackgroundColours(system, palettes, metatile == null ? 0 : metatile.Palette);
                    for (int part = 0; part < 4; part++)
                    {
                        int tile = metatile != null && metatile.Tiles != null && part < metatile.Tiles.Count ? metatile.Tiles[part] : 0;
                        DrawTile(image, system, TilePixels(chr, system, tile),
                            column * metaWidth + (part % 2) * system.TileWidth, row * metaHeight + (part / 2) * system.TileHeight,
                            colours, false, false, false);
                    }
                }
            }
            return image.Scale(scale);
        }

        /// <summary>
        /// One metasprite on a transparent background, cropped to its tiles and its
        /// origin; the origin is marked when <paramref name="markOrigin"/> is set.
        /// </summary>
        public static RenderResult RenderMetasprite(MetaspriteAsset asset, string name, ChrFile chr, PaletteAsset palettes, int scale = 1, bool markOrigin = true)
        {
            int index = asset.IndexOf(name);
            if (index < 0)
                throw new ArgumentException("no metasprite named '" + name + "'", nameof(name));
            Metasprite sprite = asset.Metasprites[index];
            return DrawSprites(asset, new[] { sprite }, sprite, chr, palettes, scale, markOrigin);
        }

        /// <summary>
        /// One frame of an animation. The image is the size that holds every frame of
        /// the animation, with the origin at the same place, so frames shown one after
        /// the other do not jump.
        /// </summary>
        public static RenderResult RenderAnimationFrame(MetaspriteAsset asset, string animation, int frame, ChrFile chr, PaletteAsset palettes, int scale = 1, bool markOrigin = true)
        {
            MetaspriteAnimation found = asset.AnimationNamed(animation);
            if (found == null)
                throw new ArgumentException("no animation named '" + animation + "'", nameof(animation));
            if (frame < 0 || frame >= found.Frames.Count)
                throw new ArgumentOutOfRangeException(nameof(frame), "animation '" + animation + "' has " + found.Frames.Count + " frames");
            List<Metasprite> all = new List<Metasprite>();
            foreach (AnimationFrame f in found.Frames)
            {
                int index = asset.IndexOf(f.Metasprite);
                if (index >= 0)
                    all.Add(asset.Metasprites[index]);
            }
            int shown = asset.IndexOf(found.Frames[frame].Metasprite);
            if (shown < 0)
                throw new ArgumentException("frame " + frame + " names no metasprite of the file");
            return DrawSprites(asset, all, asset.Metasprites[shown], chr, palettes, scale, markOrigin);
        }

        #endregion

        #region From files

        /// <summary>
        /// Draws an asset file, loading the tile set and palettes it names.
        /// </summary>
        /// <param name="path">Any asset file.</param>
        /// <param name="item">
        /// For a metasprite file: a metasprite name, an animation name (its first
        /// frame) or <c>animation#frame</c>; the first metasprite by default. For a
        /// tile set: <c>palettes.pal.json</c> or <c>palettes.pal.json#index</c>,
        /// relative to the tile set; greys by default. Ignored otherwise.
        /// </param>
        /// <param name="scale">Whole enlargement, 1 or more.</param>
        public static RenderResult Render(string path, string item, int scale = 1)
        {
            List<AssetDiagnostic> diagnostics = new List<AssetDiagnostic>();
            if (scale < 1 || scale > 64)
            {
                diagnostics.Add(AssetDiagnostic.Error(path, "$", "scale " + scale + " is out of range 1..64"));
                return new RenderResult(null, -1, -1, diagnostics);
            }
            AssetDocument document = AssetDocument.Load(path);
            diagnostics.AddRange(document.Diagnostics);
            if (document.Asset == null)
                return new RenderResult(null, -1, -1, diagnostics);

            try
            {
                switch (document.Kind)
                {
                    case AssetKind.Chr:
                        return RenderChrFile(path, (ChrFile)document.Asset, item, scale, diagnostics);
                    case AssetKind.Palette:
                        return new RenderResult(RenderPaletteSwatch((PaletteAsset)document.Asset, scale), -1, -1, diagnostics);
                    case AssetKind.Screen:
                    {
                        ScreenAsset screen = (ScreenAsset)document.Asset;
                        ChrFile chr = LoadChr(path, screen.Chr, screen.System, diagnostics);
                        PaletteAsset palettes = LoadPalettes(path, screen.Palettes, diagnostics);
                        return new RenderResult(RenderScreen(screen, chr, palettes, scale), -1, -1, diagnostics);
                    }
                    case AssetKind.Map:
                    {
                        MapAsset map = (MapAsset)document.Asset;
                        ChrFile chr = LoadChr(path, map.Chr, map.System, diagnostics);
                        PaletteAsset palettes = LoadPalettes(path, map.Palettes, diagnostics);
                        return new RenderResult(RenderMap(map, chr, palettes, scale), -1, -1, diagnostics);
                    }
                    case AssetKind.Metasprite:
                        return RenderMetaspriteFile(path, (MetaspriteAsset)document.Asset, item, scale, diagnostics);
                }
            }
            catch (ArgumentException ex)
            {
                diagnostics.Add(AssetDiagnostic.Error(path, "$", ex.Message));
            }
            return new RenderResult(null, -1, -1, diagnostics);
        }

        private static RenderResult RenderChrFile(string path, ChrFile chr, string item, int scale, List<AssetDiagnostic> diagnostics)
        {
            PaletteAsset palettes = null;
            int palette = 0;
            if (!string.IsNullOrWhiteSpace(item))
            {
                string file = item;
                int hash = item.LastIndexOf('#');
                if (hash >= 0)
                {
                    file = item.Substring(0, hash);
                    if (!int.TryParse(item.Substring(hash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out palette))
                        diagnostics.Add(AssetDiagnostic.Error(path, "$", "palette index '" + item.Substring(hash + 1) + "' is not a number"));
                }
                palettes = LoadPalettes(path, file, diagnostics);
            }
            return new RenderResult(RenderTileSheet(chr, palettes, palette, scale), -1, -1, diagnostics);
        }

        private static RenderResult RenderMetaspriteFile(string path, MetaspriteAsset asset, string item, int scale, List<AssetDiagnostic> diagnostics)
        {
            ChrFile chr = LoadChr(path, asset.Chr, asset.System, diagnostics);
            PaletteAsset palettes = LoadPalettes(path, asset.Palettes, diagnostics);
            RenderResult drawn;
            if (string.IsNullOrWhiteSpace(item))
            {
                if (asset.Metasprites.Count == 0)
                {
                    diagnostics.Add(AssetDiagnostic.Error(path, "$.metasprites", "the file has no metasprite to draw"));
                    return new RenderResult(null, -1, -1, diagnostics);
                }
                drawn = RenderMetasprite(asset, asset.Metasprites[0].Name, chr, palettes, scale);
            }
            else
            {
                string name = item;
                int frame = 0;
                int hash = item.LastIndexOf('#');
                if (hash >= 0)
                {
                    name = item.Substring(0, hash);
                    if (!int.TryParse(item.Substring(hash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out frame))
                        throw new ArgumentException("frame '" + item.Substring(hash + 1) + "' is not a number");
                }
                if (hash < 0 && asset.IndexOf(name) >= 0)
                    drawn = RenderMetasprite(asset, name, chr, palettes, scale);
                else if (asset.AnimationNamed(name) != null)
                    drawn = RenderAnimationFrame(asset, name, frame, chr, palettes, scale);
                else
                    throw new ArgumentException("no metasprite or animation named '" + name + "'");
            }
            return new RenderResult(drawn.Image, drawn.OriginX, drawn.OriginY, diagnostics);
        }

        /// <summary>The tile set an asset names, or null (with a warning) when it names none or it is missing.</summary>
        private static ChrFile LoadChr(string assetPath, string chr, string systemId, List<AssetDiagnostic> diagnostics)
        {
            string path = AssetKinds.Resolve(assetPath, chr);
            if (path == null)
                return null;
            if (!File.Exists(path))
            {
                diagnostics.Add(AssetDiagnostic.Warning(assetPath, "$.chr", "tile set '" + chr + "' not found: drawing blank tiles"));
                return null;
            }
            return ChrFile.Load(path, GraphicsSystems.For(systemId));
        }

        private static PaletteAsset LoadPalettes(string assetPath, string palettes, List<AssetDiagnostic> diagnostics)
        {
            string path = AssetKinds.Resolve(assetPath, palettes);
            if (path == null)
                return null;
            if (!File.Exists(path))
            {
                diagnostics.Add(AssetDiagnostic.Warning(assetPath, "$.palettes", "palette file '" + palettes + "' not found: drawing in greys"));
                return null;
            }
            AssetLoadResult<PaletteAsset> loaded = PaletteAsset.Load(path);
            diagnostics.AddRange(loaded.Diagnostics);
            return loaded.Asset;
        }

        #endregion

        #region Drawing

        private static RenderResult DrawSprites(MetaspriteAsset asset, IList<Metasprite> frame, Metasprite shown, ChrFile chr, PaletteAsset palettes, int scale, bool markOrigin)
        {
            IGraphicsSystem system = SystemOf(asset.System);

            // The box holds every tile of every sprite given, relative to its origin,
            // and the origin itself.
            int minX = 0, minY = 0, maxX = 1, maxY = 1;
            foreach (Metasprite sprite in frame)
            {
                foreach (MetaspriteTile tile in sprite.Tiles)
                {
                    minX = Math.Min(minX, tile.X - sprite.OriginX);
                    minY = Math.Min(minY, tile.Y - sprite.OriginY);
                    maxX = Math.Max(maxX, tile.X - sprite.OriginX + system.TileWidth);
                    maxY = Math.Max(maxY, tile.Y - sprite.OriginY + system.TileHeight);
                }
            }
            RgbaImage image = new RgbaImage(maxX - minX, maxY - minY);
            int originX = -minX;
            int originY = -minY;

            // Earlier tiles have priority on the NES (lower OAM index wins), so draw
            // in reverse and let the first ones cover the others.
            for (int t = shown.Tiles.Count - 1; t >= 0; t--)
            {
                MetaspriteTile tile = shown.Tiles[t];
                int[] colours = Colours(system, palettes, system.BackgroundPaletteCount + tile.Palette);
                DrawTile(image, system, TilePixels(chr, system, tile.Tile),
                    originX + tile.X - shown.OriginX, originY + tile.Y - shown.OriginY, colours, true, tile.FlipH, tile.FlipV);
            }

            if (markOrigin)
            {
                for (int d = -2; d <= 2; d++)
                {
                    MarkIfClear(image, originX + d, originY);
                    MarkIfClear(image, originX, originY + d);
                }
            }
            return new RenderResult(image.Scale(scale), originX * scale, originY * scale, null);
        }

        private static void MarkIfClear(RgbaImage image, int x, int y)
        {
            if (x >= 0 && y >= 0 && x < image.Width && y < image.Height && image.GetAlpha(x, y) == 0)
                image.SetPixel(x, y, OriginMarkerColor);
        }

        private static void DrawTile(RgbaImage image, IGraphicsSystem system, byte[] pixels, int left, int top, int[] colours, bool transparentZero, bool flipH, bool flipV)
        {
            int w = system.TileWidth;
            int h = system.TileHeight;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int sx = flipH ? w - 1 - x : x;
                    int sy = flipV ? h - 1 - y : y;
                    int colour = pixels[sy * w + sx];
                    if (transparentZero && colour == 0)
                        continue;
                    int px = left + x;
                    int py = top + y;
                    if (px < 0 || py < 0 || px >= image.Width || py >= image.Height)
                        continue;
                    image.SetPixel(px, py, colours[Math.Min(colour, colours.Length - 1)]);
                }
            }
        }

        private static byte[] TilePixels(ChrFile chr, IGraphicsSystem system, int tile)
        {
            return chr == null ? new byte[system.TileWidth * system.TileHeight] : chr.GetTileOrBlank(tile);
        }

        /// <summary>A background palette's colours, colour 0 being the universal background.</summary>
        private static int[] BackgroundColours(IGraphicsSystem system, PaletteAsset palettes, int palette)
        {
            int[] colours = Colours(system, palettes, palette);
            if (palettes != null)
                colours[0] = Master(system, palettes.ColorOr(0, 0, 0x0F));
            return colours;
        }

        /// <summary>The RGB colours of a palette of the file, or greys without a file.</summary>
        private static int[] Colours(IGraphicsSystem system, PaletteAsset palettes, int palette)
        {
            int count = 1 << system.BitsPerPixel;
            int[] colours = new int[count];
            for (int c = 0; c < count; c++)
            {
                colours[c] = palettes == null
                    ? Greys[Math.Min(c * (Greys.Length - 1) / Math.Max(1, count - 1), Greys.Length - 1)]
                    : Master(system, palettes.ColorOr(palette, c, 0x0F));
            }
            return colours;
        }

        private static int Master(IGraphicsSystem system, int index)
        {
            return index >= 0 && index < system.MasterPalette.Count ? system.MasterPalette[index] : 0;
        }

        private static IGraphicsSystem SystemOf(string id)
        {
            return GraphicsSystems.For(id) ?? throw new ArgumentException("unknown graphics system '" + id + "'");
        }

        #endregion
    }
}
