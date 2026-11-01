using System;
using System.Collections.Generic;
using System.IO;
using WinASM65.Graphics.Assets;
using WinASM65.Graphics.Imaging;
using WinASM65.Graphics.Systems;

namespace WinASM65.Graphics.Import
{
    public enum ImportMode
    {
        /// <summary>A screen: shared background colour, a palette per attribute block, tiles without flips.</summary>
        Background,

        /// <summary>A metasprite: transparent colour 0, a palette per tile, tiles deduplicated with their flips.</summary>
        Sprites
    }

    /// <summary>What to import and into what.</summary>
    public sealed class ImportOptions
    {
        public ImportMode Mode { get; set; } = ImportMode.Background;

        /// <summary>Name of the screen or metasprite, and of the palette file.</summary>
        public string Name { get; set; } = "imported";

        /// <summary>The machine; the NES by default.</summary>
        public IGraphicsSystem System { get; set; }

        /// <summary>A tile set to reuse and append to; a new one when null. It is modified in place.</summary>
        public ChrFile Chr { get; set; }
    }

    /// <summary>Counts of an import, for the person who ran it.</summary>
    public sealed class ImportStatistics
    {
        /// <summary>Tile cells of the picture that were imported (empty sprite cells are not).</summary>
        public int Cells { get; set; }

        /// <summary>Tiles added to the tile set.</summary>
        public int NewTiles { get; set; }

        /// <summary>Cells drawn with a tile that was already there (a repeat, or a flip of one for sprites).</summary>
        public int ReusedTiles { get; set; }

        /// <summary>Tiles in the tile set after the import.</summary>
        public int TileCount { get; set; }

        /// <summary>Palettes the picture uses.</summary>
        public int Palettes { get; set; }

        /// <summary>Distinct master palette colours after matching, the background or transparent colour excluded.</summary>
        public int Colors { get; set; }

        public int Warnings { get; set; }
    }

    /// <summary>
    /// The assets an import produced. Nothing is on disk until <see cref="WriteTo"/>.
    /// </summary>
    public sealed class ImportResult
    {
        public ImportResult()
        {
            Diagnostics = new List<AssetDiagnostic>();
            Statistics = new ImportStatistics();
            Written = new List<string>();
        }

        public ImportMode Mode { get; set; }
        public string Name { get; set; }
        public ChrFile Chr { get; set; }
        public PaletteAsset Palettes { get; set; }

        /// <summary>The screen, in background mode.</summary>
        public ScreenAsset Screen { get; set; }

        /// <summary>The metasprite file, in sprite mode.</summary>
        public MetaspriteAsset Metasprites { get; set; }

        public ImportStatistics Statistics { get; private set; }
        public List<AssetDiagnostic> Diagnostics { get; private set; }

        /// <summary>Files written by <see cref="WriteTo"/>.</summary>
        public List<string> Written { get; private set; }

        public bool HasErrors { get { return AssetDiagnostic.AnyErrors(Diagnostics); } }

        /// <summary>
        /// Writes <c>name.chr</c> (or the given tile set), <c>name.pal.json</c> and
        /// <c>name.screen.json</c> or <c>name.msprite.json</c> into a folder, the
        /// assets naming each other by relative paths.
        /// </summary>
        /// <param name="folder">Where the assets go; created when missing.</param>
        /// <param name="chrPath">The tile set to write, such as the one the import appended to; <c>folder/name.chr</c> when null.</param>
        public List<string> WriteTo(string folder, string chrPath = null)
        {
            if (Chr == null || Palettes == null)
                throw new InvalidOperationException("the import produced no assets: " + string.Join("; ", Diagnostics));
            Directory.CreateDirectory(folder);
            string chr = Path.GetFullPath(chrPath ?? Path.Combine(folder, Name + AssetKinds.ChrExtension));
            string palettes = Path.GetFullPath(Path.Combine(folder, Name + AssetKinds.PaletteExtension));
            Chr.Save(chr);
            Written.Add(chr);
            Palettes.Save(palettes);
            Written.Add(palettes);
            if (Screen != null)
            {
                string screen = Path.GetFullPath(Path.Combine(folder, Name + AssetKinds.ScreenExtension));
                Screen.Chr = AssetKinds.Relative(screen, chr);
                Screen.Palettes = AssetKinds.Relative(screen, palettes);
                Screen.Save(screen);
                Written.Add(screen);
            }
            if (Metasprites != null)
            {
                string sprites = Path.GetFullPath(Path.Combine(folder, Name + AssetKinds.MetaspriteExtension));
                Metasprites.Chr = AssetKinds.Relative(sprites, chr);
                Metasprites.Palettes = AssetKinds.Relative(sprites, palettes);
                Metasprites.Save(sprites);
                Written.Add(sprites);
            }
            return Written;
        }
    }

    /// <summary>
    /// Turns a picture into tiles, palettes and a screen or a metasprite.
    ///
    /// <para>
    /// Every pixel is matched to the nearest colour of the machine (straight RGB
    /// distance, among <see cref="IGraphicsSystem.SelectableColors"/>). Then the
    /// hardware's limits are met, in the order that loses the least:
    /// </para>
    /// <list type="bullet">
    /// <item>background: the most frequent colour becomes the shared colour 0; each
    /// attribute block keeps at most 3 other colours (the most frequent ones; the
    /// others go to the nearest kept colour, with a warning); blocks are grouped
    /// into at most 4 palettes, the blocks with the most colours first, each into
    /// the palette it adds the fewest colours to; a block no palette can take uses
    /// the palette that changes it least, with a warning.</item>
    /// <item>sprites: pixels with alpha below 128 are transparent (colour 0); each
    /// 8x8 cell is a tile of at most 3 colours, grouped into the sprite palettes the
    /// same way; a cell equal to a tile already made, or to its mirror image
    /// horizontally, vertically or both, reuses it with the flip bits.</item>
    /// </list>
    /// <para>
    /// Identical tiles are stored once, including tiles already in the tile set
    /// the import appends to. The result is deterministic for a given picture.
    /// </para>
    /// </summary>
    public static class PngImporter
    {
        /// <summary>Opaque from this alpha up; below it a pixel is transparent.</summary>
        public const int AlphaThreshold = 128;

        /// <summary>
        /// Imports a PNG file and writes the assets: the whole operation of the
        /// <c>import</c> command. An existing tile set is appended to and saved in place.
        /// </summary>
        public static ImportResult ImportFile(string pngPath, ImportMode mode, string outFolder, string name, string chrPath = null)
        {
            ImportResult failed = new ImportResult { Mode = mode, Name = name };
            RgbaImage image;
            try
            {
                image = PngReader.Load(pngPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                failed.Diagnostics.Add(AssetDiagnostic.Error(pngPath, "$", ex.Message));
                return failed;
            }

            IGraphicsSystem system = GraphicsSystems.For(NesGraphics.SystemId);
            ChrFile chr = null;
            if (!string.IsNullOrEmpty(chrPath) && File.Exists(chrPath))
                chr = ChrFile.Load(chrPath, system);

            ImportResult result = Import(image, new ImportOptions { Mode = mode, Name = name, System = system, Chr = chr }, pngPath);
            if (result.Chr != null && result.Palettes != null)
                result.WriteTo(outFolder, string.IsNullOrEmpty(chrPath) ? null : chrPath);
            return result;
        }

        /// <summary>Imports a picture into assets, in memory.</summary>
        /// <param name="image">The picture.</param>
        /// <param name="options">Mode, name, machine and tile set.</param>
        /// <param name="sourceName">The file the diagnostics name, or null.</param>
        public static ImportResult Import(RgbaImage image, ImportOptions options, string sourceName = null)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));
            options = options ?? new ImportOptions();
            IGraphicsSystem system = options.System ?? GraphicsSystems.For(NesGraphics.SystemId);
            string name = string.IsNullOrWhiteSpace(options.Name) ? "imported" : options.Name;
            ImportResult result = new ImportResult { Mode = options.Mode, Name = name };
            ColourMatcher matcher = new ColourMatcher(system);
            if (options.Mode == ImportMode.Background)
                ImportBackground(image, system, name, options.Chr, matcher, result, sourceName);
            else
                ImportSprites(image, system, name, options.Chr, matcher, result, sourceName);

            foreach (AssetDiagnostic diagnostic in result.Diagnostics)
            {
                if (!diagnostic.IsError)
                    result.Statistics.Warnings++;
            }
            if (result.Chr != null)
                result.Statistics.TileCount = result.Chr.TileCount;
            return result;
        }

        /// <summary>The master palette index nearest to an RGB colour, among the selectable ones.</summary>
        public static int NearestColor(IGraphicsSystem system, int rgb)
        {
            return new ColourMatcher(system).Nearest(rgb);
        }

        #region Background

        private static void ImportBackground(RgbaImage image, IGraphicsSystem system, string name, ChrFile existing, ColourMatcher matcher, ImportResult result, string source)
        {
            int tw = system.TileWidth;
            int th = system.TileHeight;
            int screenWidth = system.ScreenWidthTiles * tw;
            int screenHeight = system.ScreenHeightTiles * th;
            if (image.Width > screenWidth || image.Height > screenHeight || image.Width % tw != 0 || image.Height % th != 0)
            {
                result.Diagnostics.Add(AssetDiagnostic.Error(source, "$", "a background image is at most " + screenWidth + "x" + screenHeight
                    + " and a multiple of " + tw + "x" + th + " pixels; this one is " + image.Width + "x" + image.Height));
                return;
            }

            // Colours of the picture, the screen beyond it being background.
            int[] colours = matcher.Index(image);
            Dictionary<int, int> frequency = new Dictionary<int, int>();
            foreach (int colour in colours)
            {
                if (colour >= 0)
                    frequency[colour] = (frequency.TryGetValue(colour, out int n) ? n : 0) + 1;
            }
            int background = MostFrequent(frequency, 0x0F);
            for (int i = 0; i < colours.Length; i++)
            {
                if (colours[i] < 0)
                    colours[i] = background;
            }

            int blockWidth = system.AttributeBlockWidth * tw;
            int blockHeight = system.AttributeBlockHeight * th;
            int blockColumns = ScreenAsset.AttributeColumns(system);
            int blockRows = ScreenAsset.AttributeRows(system);
            int perPalette = system.ColorsPerPalette - 1;

            List<Group> blocks = new List<Group>();
            for (int by = 0; by < blockRows; by++)
            {
                for (int bx = 0; bx < blockColumns; bx++)
                {
                    Group block = new Group(bx * blockWidth, by * blockHeight, "block " + bx + "," + by);
                    for (int y = block.Y; y < Math.Min(block.Y + blockHeight, image.Height); y++)
                    {
                        for (int x = block.X; x < Math.Min(block.X + blockWidth, image.Width); x++)
                            block.Count(colours[y * image.Width + x], background);
                    }
                    blocks.Add(block);
                }
            }

            List<List<int>> palettes = GroupPalettes(blocks, system.BackgroundPaletteCount, perPalette, background, matcher, result, source);

            ChrFile chr = existing ?? new ChrFile(system);
            TileStore store = new TileStore(chr, false);
            ScreenAsset screen = ScreenAsset.Blank(system, name);
            for (int b = 0; b < blocks.Count; b++)
                screen.Attributes[b] = blocks[b].Palette;

            for (int row = 0; row < system.ScreenHeightTiles; row++)
            {
                for (int column = 0; column < system.ScreenWidthTiles; column++)
                {
                    int block = (row / system.AttributeBlockHeight) * blockColumns + column / system.AttributeBlockWidth;
                    List<int> palette = palettes[blocks[block].Palette];
                    byte[] pixels = new byte[tw * th];
                    for (int y = 0; y < th; y++)
                    {
                        for (int x = 0; x < tw; x++)
                        {
                            int px = column * tw + x;
                            int py = row * th + y;
                            int colour = px < image.Width && py < image.Height ? colours[py * image.Width + px] : background;
                            pixels[y * tw + x] = (byte)matcher.IndexIn(colour, background, palette);
                        }
                    }
                    TileRef tile = store.Find(pixels, out bool added);
                    screen.Tiles[row * system.ScreenWidthTiles + column] = tile.Index;
                    if (column * tw < image.Width && row * th < image.Height)
                    {
                        result.Statistics.Cells++;
                        if (added)
                            result.Statistics.NewTiles++;
                        else
                            result.Statistics.ReusedTiles++;
                    }
                    else if (added)
                    {
                        result.Statistics.NewTiles++;
                    }
                }
            }

            if (chr.TileCount > system.MaxTiles)
                result.Diagnostics.Add(AssetDiagnostic.Error(source, "$", "the tile set now holds " + chr.TileCount
                    + " tiles; a screen can only address " + system.MaxTiles));

            PaletteAsset paletteAsset = PaletteAsset.Blank(system, name, background);
            for (int p = 0; p < palettes.Count; p++)
            {
                for (int c = 0; c < palettes[p].Count; c++)
                    paletteAsset.Palettes[p].Colors[c + 1] = palettes[p][c];
            }

            result.Chr = chr;
            result.Palettes = paletteAsset;
            result.Screen = screen;
            result.Statistics.Palettes = palettes.Count;
            result.Statistics.Colors = DistinctColours(palettes);
        }

        #endregion

        #region Sprites

        private static void ImportSprites(RgbaImage image, IGraphicsSystem system, string name, ChrFile existing, ColourMatcher matcher, ImportResult result, string source)
        {
            int tw = system.TileWidth;
            int th = system.TileHeight;
            int width = (image.Width + tw - 1) / tw * tw;
            int height = (image.Height + th - 1) / th * th;
            if (width != image.Width || height != image.Height)
                result.Diagnostics.Add(AssetDiagnostic.Warning(source, "$", "the image (" + image.Width + "x" + image.Height
                    + ") is not a multiple of " + tw + "x" + th + "; it is padded with transparent pixels to " + width + "x" + height));

            int[] opaque = matcher.Index(image);
            int[] colours = new int[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    colours[y * width + x] = x < image.Width && y < image.Height ? opaque[y * image.Width + x] : -1;
            }

            List<Group> cells = new List<Group>();
            for (int cy = 0; cy < height / th; cy++)
            {
                for (int cx = 0; cx < width / tw; cx++)
                {
                    Group cell = new Group(cx * tw, cy * th, "tile " + cx + "," + cy);
                    bool any = false;
                    for (int y = cell.Y; y < cell.Y + th; y++)
                    {
                        for (int x = cell.X; x < cell.X + tw; x++)
                        {
                            int colour = colours[y * width + x];
                            if (colour >= 0)
                            {
                                any = true;
                                cell.Count(colour, -1);
                            }
                        }
                    }
                    if (any)
                        cells.Add(cell);
                }
            }

            List<List<int>> palettes = GroupPalettes(cells, system.SpritePaletteCount, system.ColorsPerPalette - 1, -1, matcher, result, source);

            ChrFile chr = existing ?? new ChrFile(system);
            TileStore store = new TileStore(chr, true);
            Metasprite sprite = new Metasprite { Name = name, OriginX = width / 2, OriginY = height / 2 };
            int[] perRow = new int[height / th];
            foreach (Group cell in cells)
            {
                List<int> palette = palettes[cell.Palette];
                byte[] pixels = new byte[tw * th];
                for (int y = 0; y < th; y++)
                {
                    for (int x = 0; x < tw; x++)
                        pixels[y * tw + x] = (byte)matcher.IndexIn(colours[(cell.Y + y) * width + cell.X + x], -1, palette);
                }
                TileRef tile = store.Find(pixels, out bool added);
                result.Statistics.Cells++;
                if (added)
                    result.Statistics.NewTiles++;
                else
                    result.Statistics.ReusedTiles++;
                sprite.Tiles.Add(new MetaspriteTile
                {
                    X = cell.X,
                    Y = cell.Y,
                    Tile = tile.Index,
                    Palette = cell.Palette,
                    FlipH = tile.FlipH,
                    FlipV = tile.FlipV,
                });
                perRow[cell.Y / th]++;
            }

            for (int row = 0; row < perRow.Length; row++)
            {
                if (perRow[row] > system.SpritesPerScanline)
                    result.Diagnostics.Add(AssetDiagnostic.Warning(source, "$", "row of tiles at y=" + (row * th) + " has " + perRow[row]
                        + " sprites side by side; the hardware shows " + system.SpritesPerScanline + " per scanline"));
            }
            if (chr.TileCount > system.MaxTiles)
                result.Diagnostics.Add(AssetDiagnostic.Error(source, "$", "the tile set now holds " + chr.TileCount
                    + " tiles; a sprite can only address " + system.MaxTiles));
            if (width / 2 > 128 || height / 2 > 127)
                result.Diagnostics.Add(AssetDiagnostic.Warning(source, "$", "the image (" + width + "x" + height
                    + ") is too large for one metasprite: tile offsets from the centre are -128..127; split it before exporting"));

            PaletteAsset paletteAsset = PaletteAsset.Blank(system, name, 0x0F);
            for (int p = 0; p < palettes.Count; p++)
            {
                for (int c = 0; c < palettes[p].Count; c++)
                    paletteAsset.Palettes[system.BackgroundPaletteCount + p].Colors[c + 1] = palettes[p][c];
            }

            MetaspriteAsset asset = new MetaspriteAsset { System = system.Id };
            asset.Metasprites.Add(sprite);

            result.Chr = chr;
            result.Palettes = paletteAsset;
            result.Metasprites = asset;
            result.Statistics.Palettes = palettes.Count;
            result.Statistics.Colors = DistinctColours(palettes);
        }

        #endregion

        #region Palettes

        /// <summary>
        /// Gives each group (attribute block or sprite tile) a palette, building at
        /// most <paramref name="maxPalettes"/> palettes of <paramref name="perPalette"/>
        /// colours (colour 0 aside). Groups are reduced to that many colours first.
        /// </summary>
        private static List<List<int>> GroupPalettes(List<Group> groups, int maxPalettes, int perPalette, int background, ColourMatcher matcher, ImportResult result, string source)
        {
            foreach (Group group in groups)
            {
                if (group.Colours.Count > perPalette)
                {
                    int before = group.Colours.Count;
                    group.Reduce(perPalette, background, matcher);
                    result.Diagnostics.Add(AssetDiagnostic.Warning(source, "$", group.Describe() + " has " + before
                        + " colours" + (background >= 0 ? " besides the background" : "") + "; reduced to the " + perPalette
                        + " most frequent, the others drawn with the nearest of them"));
                }
            }

            List<int> order = new List<int>();
            for (int i = 0; i < groups.Count; i++)
                order.Add(i);
            order.Sort((a, b) =>
            {
                int bySize = groups[b].Colours.Count.CompareTo(groups[a].Colours.Count);
                return bySize != 0 ? bySize : a.CompareTo(b);
            });

            List<List<int>> palettes = new List<List<int>>();
            foreach (int g in order)
            {
                Group group = groups[g];
                List<int> wanted = group.SortedColours();
                int best = -1;
                int bestAdded = int.MaxValue;
                for (int p = 0; p < palettes.Count; p++)
                {
                    int added = 0;
                    foreach (int colour in wanted)
                    {
                        if (!palettes[p].Contains(colour))
                            added++;
                    }
                    if (palettes[p].Count + added <= perPalette && added < bestAdded)
                    {
                        best = p;
                        bestAdded = added;
                    }
                }

                if (best >= 0)
                {
                    foreach (int colour in wanted)
                    {
                        if (!palettes[best].Contains(colour))
                            palettes[best].Add(colour);
                    }
                    group.Palette = best;
                    continue;
                }
                if (palettes.Count < maxPalettes)
                {
                    palettes.Add(new List<int>(wanted));
                    group.Palette = palettes.Count - 1;
                    continue;
                }

                // No palette can take the group: the one that changes it least.
                long bestCost = long.MaxValue;
                for (int p = 0; p < palettes.Count; p++)
                {
                    long cost = 0;
                    foreach (KeyValuePair<int, int> pair in group.Colours)
                    {
                        int nearest = matcher.NearestOf(pair.Key, background, palettes[p]);
                        cost += (long)pair.Value * matcher.Distance(pair.Key, nearest);
                    }
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        best = p;
                    }
                }
                group.Palette = best;
                result.Diagnostics.Add(AssetDiagnostic.Warning(source, "$", group.Describe() + " needs colours no palette has room for ("
                    + maxPalettes + " palettes used); drawn with palette " + best + " and the nearest colours"));
            }

            if (palettes.Count == 0)
                palettes.Add(new List<int>());
            return palettes;
        }

        private static int MostFrequent(Dictionary<int, int> frequency, int fallback)
        {
            int best = fallback;
            int bestCount = 0;
            foreach (KeyValuePair<int, int> pair in frequency)
            {
                if (pair.Value > bestCount || (pair.Value == bestCount && pair.Key < best))
                {
                    best = pair.Key;
                    bestCount = pair.Value;
                }
            }
            return best;
        }

        private static int DistinctColours(List<List<int>> palettes)
        {
            HashSet<int> all = new HashSet<int>();
            foreach (List<int> palette in palettes)
                all.UnionWith(palette);
            return all.Count;
        }

        #endregion

        #region Helpers

        /// <summary>An area that takes one palette: an attribute block, or a sprite tile.</summary>
        private sealed class Group
        {
            public Group(int x, int y, string name)
            {
                X = x;
                Y = y;
                Name = name;
                Colours = new Dictionary<int, int>();
            }

            public int X { get; private set; }
            public int Y { get; private set; }
            public string Name { get; private set; }

            /// <summary>Pixel count of each colour, colour 0 excluded.</summary>
            public Dictionary<int, int> Colours { get; private set; }

            public int Palette { get; set; }

            public void Count(int colour, int background)
            {
                if (colour == background)
                    return;
                Colours[colour] = (Colours.TryGetValue(colour, out int n) ? n : 0) + 1;
            }

            /// <summary>Most frequent first, then by colour index: a stable order.</summary>
            public List<int> SortedColours()
            {
                List<int> sorted = new List<int>(Colours.Keys);
                sorted.Sort((a, b) =>
                {
                    int byCount = Colours[b].CompareTo(Colours[a]);
                    return byCount != 0 ? byCount : a.CompareTo(b);
                });
                return sorted;
            }

            /// <summary>Keeps the most frequent colours, adding the pixels of the others to the nearest kept one.</summary>
            public void Reduce(int keep, int background, ColourMatcher matcher)
            {
                List<int> sorted = SortedColours();
                List<int> kept = sorted.GetRange(0, keep);
                Dictionary<int, int> reduced = new Dictionary<int, int>();
                foreach (int colour in kept)
                    reduced[colour] = Colours[colour];
                for (int i = keep; i < sorted.Count; i++)
                {
                    int nearest = matcher.NearestOf(sorted[i], background, kept);
                    if (nearest != background)
                        reduced[nearest] += Colours[sorted[i]];
                }
                Colours = reduced;
            }

            public string Describe()
            {
                return Name + " (x=" + X + ", y=" + Y + ")";
            }
        }

        /// <summary>A tile of the set and the flips that make it show a given cell.</summary>
        private struct TileRef
        {
            public int Index;
            public bool FlipH;
            public bool FlipV;
        }

        /// <summary>The tiles of a set by their pixels, so a repeated tile is stored once.</summary>
        private sealed class TileStore
        {
            private readonly ChrFile _chr;
            private readonly bool _flips;
            private readonly Dictionary<string, TileRef> _known = new Dictionary<string, TileRef>();

            public TileStore(ChrFile chr, bool flips)
            {
                _chr = chr;
                _flips = flips;
                for (int t = 0; t < chr.TileCount; t++)
                    Remember(chr.GetTile(t), t);
            }

            public TileRef Find(byte[] pixels, out bool added)
            {
                if (_known.TryGetValue(Key(pixels), out TileRef found))
                {
                    added = false;
                    return found;
                }
                int index = _chr.AddTile(pixels);
                Remember(pixels, index);
                added = true;
                return new TileRef { Index = index };
            }

            // Every variant maps to the tile and the flips that produce it; the
            // unflipped form is registered first so it wins for symmetric tiles.
            private void Remember(byte[] pixels, int index)
            {
                Add(pixels, index, false, false);
                if (!_flips)
                    return;
                Add(Flip(pixels, true, false), index, true, false);
                Add(Flip(pixels, false, true), index, false, true);
                Add(Flip(pixels, true, true), index, true, true);
            }

            private void Add(byte[] pixels, int index, bool flipH, bool flipV)
            {
                string key = Key(pixels);
                if (!_known.ContainsKey(key))
                    _known[key] = new TileRef { Index = index, FlipH = flipH, FlipV = flipV };
            }

            private byte[] Flip(byte[] pixels, bool horizontal, bool vertical)
            {
                int w = _chr.GraphicsSystem.TileWidth;
                int h = _chr.GraphicsSystem.TileHeight;
                byte[] flipped = new byte[pixels.Length];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                        flipped[y * w + x] = pixels[(vertical ? h - 1 - y : y) * w + (horizontal ? w - 1 - x : x)];
                }
                return flipped;
            }

            private static string Key(byte[] pixels)
            {
                return Convert.ToBase64String(pixels);
            }
        }

        /// <summary>Matches RGB colours to the machine's colours, with a cache: pictures repeat their colours.</summary>
        private sealed class ColourMatcher
        {
            private readonly IGraphicsSystem _system;
            private readonly Dictionary<int, int> _cache = new Dictionary<int, int>();

            public ColourMatcher(IGraphicsSystem system)
            {
                _system = system;
            }

            /// <summary>The master index of every pixel, -1 for a transparent one.</summary>
            public int[] Index(RgbaImage image)
            {
                int[] colours = new int[image.Width * image.Height];
                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                        colours[y * image.Width + x] = image.GetAlpha(x, y) < AlphaThreshold ? -1 : Nearest(image.GetRgb(x, y));
                }
                return colours;
            }

            public int Nearest(int rgb)
            {
                if (_cache.TryGetValue(rgb, out int known))
                    return known;
                int best = _system.SelectableColors[0];
                long bestDistance = long.MaxValue;
                foreach (int index in _system.SelectableColors)
                {
                    long distance = RgbDistance(rgb, _system.MasterPalette[index]);
                    if (distance < bestDistance)
                    {
                        best = index;
                        bestDistance = distance;
                    }
                }
                _cache[rgb] = best;
                return best;
            }

            /// <summary>Squared RGB distance between two master palette colours.</summary>
            public long Distance(int a, int b)
            {
                return RgbDistance(_system.MasterPalette[a], _system.MasterPalette[b]);
            }

            /// <summary>The colour among the background (when there is one, 0 or more) and a palette nearest to a colour.</summary>
            public int NearestOf(int colour, int background, List<int> palette)
            {
                int best = background;
                long bestDistance = background >= 0 ? Distance(colour, background) : long.MaxValue;
                foreach (int candidate in palette)
                {
                    long distance = Distance(colour, candidate);
                    if (distance < bestDistance)
                    {
                        best = candidate;
                        bestDistance = distance;
                    }
                }
                return best;
            }

            /// <summary>The pixel value of a colour in a palette: 0 for the background or transparent, else 1 + its place.</summary>
            public int IndexIn(int colour, int background, List<int> palette)
            {
                if (colour < 0 || colour == background)
                    return 0;
                int place = palette.IndexOf(colour);
                if (place < 0)
                {
                    if (palette.Count == 0)
                        return 0;
                    int nearest = NearestOf(colour, background, palette);
                    if (nearest == background)
                        return 0;
                    place = palette.IndexOf(nearest);
                }
                return place + 1;
            }

            private static long RgbDistance(int a, int b)
            {
                long dr = ((a >> 16) & 0xFF) - ((b >> 16) & 0xFF);
                long dg = ((a >> 8) & 0xFF) - ((b >> 8) & 0xFF);
                long db = (a & 0xFF) - (b & 0xFF);
                return dr * dr + dg * dg + db * db;
            }
        }

        #endregion
    }
}
