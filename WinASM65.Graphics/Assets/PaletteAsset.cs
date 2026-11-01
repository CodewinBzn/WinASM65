using System.Collections.Generic;
using WinASM65.Graphics.Systems;

namespace WinASM65.Graphics.Assets
{
    /// <summary>
    /// The palettes of a game (<c>*.pal.json</c>): every background palette, then
    /// every sprite palette, each a list of master palette indexes.
    /// <code>
    /// { "version": 1, "system": "nes", "name": "game", "prefix": "",
    ///   "palettes": [ { "name": "text", "colors": [15, 48, 22, 40] }, ... ] }
    /// </code>
    /// On the NES there are exactly 8 palettes of 4 colours, 0 to 63.
    /// </summary>
    public sealed class PaletteAsset
    {
        public int Version { get; set; } = AssetJson.CurrentVersion;
        public string System { get; set; } = NesGraphics.SystemId;
        public string Name { get; set; }
        public string Prefix { get; set; } = string.Empty;
        public List<PaletteEntry> Palettes { get; set; } = new List<PaletteEntry>();

        /// <summary>
        /// The palettes of a system all set to one colour, named <c>bg0</c>.. and
        /// <c>sprite0</c>..: the starting point of a new file and of an import.
        /// </summary>
        public static PaletteAsset Blank(IGraphicsSystem system, string name, int color)
        {
            PaletteAsset asset = new PaletteAsset { System = system.Id, Name = name };
            for (int p = 0; p < system.BackgroundPaletteCount + system.SpritePaletteCount; p++)
            {
                bool sprite = p >= system.BackgroundPaletteCount;
                PaletteEntry entry = new PaletteEntry
                {
                    Name = sprite ? "sprite" + (p - system.BackgroundPaletteCount) : "bg" + p,
                };
                for (int c = 0; c < system.ColorsPerPalette; c++)
                    entry.Colors.Add(color);
                asset.Palettes.Add(entry);
            }
            return asset;
        }

        public static AssetLoadResult<PaletteAsset> Load(string path)
        {
            List<AssetDiagnostic> diagnostics = new List<AssetDiagnostic>();
            PaletteAsset asset = AssetJson.Read<PaletteAsset>(path, diagnostics);
            if (asset != null)
                diagnostics.AddRange(asset.Validate(path));
            return new AssetLoadResult<PaletteAsset>(asset, diagnostics);
        }

        public void Save(string path)
        {
            AssetJson.Write(path, this, null);
        }

        /// <summary>The palettes as lists of colours, in palette memory order.</summary>
        public List<IReadOnlyList<int>> ColorLists()
        {
            List<IReadOnlyList<int>> lists = new List<IReadOnlyList<int>>();
            foreach (PaletteEntry entry in Palettes)
                lists.Add(entry == null || entry.Colors == null ? new List<int>() : entry.Colors);
            return lists;
        }

        /// <summary>The colour at an index of a palette, or the fallback when the palette does not have it.</summary>
        public int ColorOr(int palette, int index, int fallback)
        {
            if (palette < 0 || palette >= Palettes.Count || Palettes[palette] == null || Palettes[palette].Colors == null)
                return fallback;
            List<int> colors = Palettes[palette].Colors;
            return index >= 0 && index < colors.Count ? colors[index] : fallback;
        }

        public List<AssetDiagnostic> Validate(string path)
        {
            AssetValidator check = new AssetValidator(path);
            IGraphicsSystem system = check.Header(Version, System);
            if (Palettes == null)
            {
                check.Error("$.palettes", "missing palettes");
                return check.Diagnostics;
            }
            if (system != null)
            {
                int expected = system.BackgroundPaletteCount + system.SpritePaletteCount;
                if (Palettes.Count != expected)
                    check.Error("$.palettes", Palettes.Count + " palettes, expected " + expected
                        + " (" + system.BackgroundPaletteCount + " background then " + system.SpritePaletteCount + " sprite)");
            }
            HashSet<string> names = new HashSet<string>();
            for (int p = 0; p < Palettes.Count; p++)
            {
                string at = "$.palettes[" + p + "]";
                PaletteEntry entry = Palettes[p];
                if (entry == null)
                {
                    check.Error(at, "missing palette");
                    continue;
                }
                check.Name(at + ".name", entry.Name, names, "palette");
                if (system == null)
                    continue;
                if (!check.Length(at + ".colors", entry.Colors, system.ColorsPerPalette, "colors"))
                    continue;
                for (int c = 0; c < entry.Colors.Count; c++)
                    check.Range(at + ".colors[" + c + "]", entry.Colors[c], 0, system.MasterPalette.Count - 1, "colour");
            }
            return check.Diagnostics;
        }
    }

    /// <summary>One palette: its name (for the generated constant) and its colours.</summary>
    public sealed class PaletteEntry
    {
        public string Name { get; set; }
        public List<int> Colors { get; set; } = new List<int>();
    }
}
