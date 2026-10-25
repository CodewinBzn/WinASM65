using System.Collections.Generic;
using System.Text.Json.Nodes;
using WinASM65.Graphics.Assets;
using WinASM65.Graphics.Import;
using WinASM65.Graphics.Systems;

namespace WinASM65.Graphics
{
    /// <summary>
    /// The JSON shapes the designer's front ends exchange (the CLI, the stdio
    /// server, editor and MCP tools), kept in the library so they are the same
    /// everywhere.
    /// </summary>
    public static class GraphicsJson
    {
        /// <summary>
        /// A graphics system: <c>{ id, displayName, tileWidth, tileHeight, bitsPerPixel,
        /// bytesPerTile, maxTiles, colorsPerPalette, backgroundPaletteCount,
        /// spritePaletteCount, screenWidthTiles, screenHeightTiles, attributeBlockWidth,
        /// attributeBlockHeight, spritesPerScanline, masterPalette: ["#RRGGBB", ...],
        /// selectableColors: [indexes] }</c>.
        /// </summary>
        public static JsonObject Describe(IGraphicsSystem system)
        {
            JsonArray master = new JsonArray();
            foreach (int rgb in system.MasterPalette)
                master.Add("#" + rgb.ToString("X6"));
            JsonArray selectable = new JsonArray();
            foreach (int index in system.SelectableColors)
                selectable.Add(index);
            return new JsonObject
            {
                ["id"] = system.Id,
                ["displayName"] = system.DisplayName,
                ["tileWidth"] = system.TileWidth,
                ["tileHeight"] = system.TileHeight,
                ["bitsPerPixel"] = system.BitsPerPixel,
                ["bytesPerTile"] = system.BytesPerTile,
                ["maxTiles"] = system.MaxTiles,
                ["colorsPerPalette"] = system.ColorsPerPalette,
                ["backgroundPaletteCount"] = system.BackgroundPaletteCount,
                ["spritePaletteCount"] = system.SpritePaletteCount,
                ["screenWidthTiles"] = system.ScreenWidthTiles,
                ["screenHeightTiles"] = system.ScreenHeightTiles,
                ["attributeBlockWidth"] = system.AttributeBlockWidth,
                ["attributeBlockHeight"] = system.AttributeBlockHeight,
                ["spritesPerScanline"] = system.SpritesPerScanline,
                ["masterPalette"] = master,
                ["selectableColors"] = selectable,
            };
        }

        /// <summary>Every graphics system, as an array of <see cref="Describe"/>.</summary>
        public static JsonArray DescribeAll()
        {
            JsonArray all = new JsonArray();
            foreach (IGraphicsSystem system in GraphicsSystems.All)
                all.Add(Describe(system));
            return all;
        }

        /// <summary><c>{ severity: "error"|"warning", message, path, file }</c>.</summary>
        public static JsonObject Diagnostic(AssetDiagnostic diagnostic)
        {
            return new JsonObject
            {
                ["severity"] = diagnostic.IsError ? "error" : "warning",
                ["message"] = diagnostic.Message,
                ["path"] = diagnostic.Path,
                ["file"] = diagnostic.File,
            };
        }

        public static JsonArray Diagnostics(IEnumerable<AssetDiagnostic> diagnostics)
        {
            JsonArray array = new JsonArray();
            foreach (AssetDiagnostic diagnostic in diagnostics)
                array.Add(Diagnostic(diagnostic));
            return array;
        }

        public static JsonArray Paths(IEnumerable<string> paths)
        {
            JsonArray array = new JsonArray();
            foreach (string path in paths)
                array.Add(path);
            return array;
        }

        /// <summary><c>{ cells, newTiles, reusedTiles, tileCount, palettes, colors, warnings }</c>.</summary>
        public static JsonObject Statistics(ImportStatistics statistics)
        {
            return new JsonObject
            {
                ["cells"] = statistics.Cells,
                ["newTiles"] = statistics.NewTiles,
                ["reusedTiles"] = statistics.ReusedTiles,
                ["tileCount"] = statistics.TileCount,
                ["palettes"] = statistics.Palettes,
                ["colors"] = statistics.Colors,
                ["warnings"] = statistics.Warnings,
            };
        }
    }
}
