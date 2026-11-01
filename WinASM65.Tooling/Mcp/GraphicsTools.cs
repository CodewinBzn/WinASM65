using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using WinASM65.Graphics;
using WinASM65.Graphics.Assets;
using WinASM65.Graphics.Export;
using WinASM65.Graphics.Imaging;
using WinASM65.Graphics.Import;
using WinASM65.Graphics.Rendering;
using WinASM65.Graphics.Systems;
using static WinASM65.Tooling.Mcp.McpTool;

namespace WinASM65.Tooling.Mcp
{
    /// <summary>
    /// The graphics designer as tools (WinASM65.Graphics): list the assets of a
    /// project, look at them as pictures, read and change them, import a PNG, and
    /// export them to the files the assembler includes. An assistant edits the same
    /// files the VS Code designer does, with the same validation.
    /// </summary>
    public static class GraphicsTools
    {
        private const string Formats =
            "Asset files: .chr (tiles, binary), .pal.json (palettes), .msprite.json (metasprites and animations), "
            + ".screen.json (a screen of tiles with palettes), .map.json (metatiles and a map of several screens).";

        public static IEnumerable<McpTool> All()
        {
            yield return new McpTool("list_assets", "List graphics assets",
                "Lists the graphics assets under a folder with their kind, size and problems. " + Formats,
                McpEffect.ReadOnly,
                Schema(Arg.Required_("folder", "string", "Folder to search (recursively)")),
                ListAssets);

            yield return new McpTool("render_asset", "Render an asset as an image",
                "Draws an asset as a PNG image the assistant can look at: the tiles of a .chr (with a palette: item "
                + "\"palettes.pal.json#0\"), the palettes, a screen, a map, a metasprite (item = its name) or an animation frame "
                + "(item = \"animation#frame\").",
                McpEffect.ReadOnly,
                Schema(Arg.Required_("path", "string", "The asset file"),
                    Arg.Optional("item", "string", "What to draw inside the file (see the description)"),
                    Arg.Optional("scale", "integer", "Pixel scale, 1 to 8 (default 2)")),
                RenderAsset);

            yield return new McpTool("get_asset", "Read an asset",
                "Returns an asset's JSON (for a .chr: its system and tile count; use get_tiles for pixels) and its problems.",
                McpEffect.ReadOnly,
                Schema(Arg.Required_("path", "string", "The asset file")),
                GetAsset);

            yield return new McpTool("update_asset", "Write an asset",
                "Replaces a JSON asset (.pal.json, .msprite.json, .screen.json, .map.json) with the given JSON after "
                + "validating it, written in the designer's format. Nothing is written when it has errors; they are returned "
                + "with their JSON path. Read the asset first with get_asset.",
                McpEffect.Writes,
                Schema(Arg.Required_("path", "string", "The asset file (created if it does not exist)"),
                    Arg.Required_("asset", "object", "The whole asset, as get_asset returns it")),
                UpdateAsset);

            yield return new McpTool("get_tiles", "Read tile pixels",
                "Returns tiles of a .chr as rows of colour indices (one digit per pixel, 0 = background/transparent).",
                McpEffect.ReadOnly,
                Schema(Arg.Required_("path", "string", "The .chr file"),
                    Arg.Optional("from", "integer", "First tile (default 0)"),
                    Arg.Optional("count", "integer", "Number of tiles (default 16, at most 256)")),
                GetTiles);

            yield return new McpTool("edit_tiles", "Draw tiles",
                "Sets the pixels of tiles in a .chr: each tile is {index, rows: [8 strings of 8 digits 0-3]}. A tile past the "
                + "end grows the file. Writes the file.",
                McpEffect.Writes,
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["path"] = new JsonObject { ["type"] = "string", ["description"] = "The .chr file (created if missing)" },
                        ["tiles"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["description"] = "Tiles to set",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["index"] = new JsonObject { ["type"] = "integer" },
                                    ["rows"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } }
                                },
                                ["required"] = new JsonArray("index", "rows")
                            }
                        }
                    },
                    ["required"] = new JsonArray("path", "tiles")
                },
                EditTiles);

            yield return new McpTool("import_png", "Import a PNG",
                "Converts a PNG into the machine's graphics: mode \"background\" makes tiles, palettes and a screen (256x240 at "
                + "most), mode \"sprites\" makes tiles, palettes and one metasprite. Colours are mapped to the nearest the "
                + "machine has, within its limits; warnings say where the picture had to change.",
                McpEffect.Writes,
                Schema(Arg.Required_("png", "string", "The PNG file"),
                    Arg.Required_("mode", "string", "background or sprites"),
                    Arg.Required_("out", "string", "Folder to write the assets into"),
                    Arg.Required_("name", "string", "Base name of the assets"),
                    Arg.Optional("chr", "string", "An existing .chr to add the tiles to")),
                ImportPng);

            yield return new McpTool("export_assets", "Export assets",
                "Writes the files the assembler includes (.bin, .nam, .rle and .asm with labels) next to an asset, or for "
                + "every asset under a folder. Assets with errors are not exported; their problems are returned.",
                McpEffect.Writes,
                Schema(Arg.Required_("path", "string", "An asset file or a folder")),
                ExportAssets);

            yield return new McpTool("describe_graphics_system", "Describe a graphics system",
                "The graphics rules of a machine: tile size and depth, palettes and colours, screen size, attribute blocks, "
                + "sprite limits, and its master palette.",
                McpEffect.ReadOnly,
                Schema(Arg.Optional("system", "string", "System id (default nes)")),
                arguments =>
                {
                    IGraphicsSystem system = GraphicsSystems.For(Text(arguments, "system", false) ?? "nes");
                    if (system == null)
                        return McpResult.Fail("no graphics system \"" + Text(arguments, "system", false) + "\"; known: "
                            + string.Join(", ", GraphicsSystems.All.Select(s => s.Id)));
                    return McpResult.Ok(GraphicsJson.Describe(system).ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                });
        }

        // ---------------------------------------------------------------- tools

        private static string Existing(JsonObject arguments, string name)
        {
            string path = Path.GetFullPath(Text(arguments, name));
            if (!File.Exists(path) && !Directory.Exists(path))
                throw new McpArgumentException("no such file: " + path);
            return path;
        }

        private static McpResult ListAssets(JsonObject arguments)
        {
            string folder = Existing(arguments, "folder");
            StringBuilder text = new StringBuilder();
            int count = 0;
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(f => AssetKinds.Of(f) != AssetKind.Unknown && !f.Contains(Path.DirectorySeparatorChar + "node_modules" + Path.DirectorySeparatorChar))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                AssetDocument document = AssetDocument.Load(file);
                text.Append(Path.GetRelativePath(folder, file)).Append("  ").Append(document.Kind).Append("  ").Append(Summary(document));
                int errors = document.Diagnostics.Count(d => d.IsError);
                int warnings = document.Diagnostics.Count - errors;
                if (errors + warnings > 0)
                    text.Append("  (").Append(errors).Append(" error(s), ").Append(warnings).Append(" warning(s))");
                text.AppendLine();
                if (++count >= 500)
                    break;
            }
            return McpResult.Ok(count == 0 ? "No graphics asset under " + folder + ". " + Formats : text.ToString());
        }

        private static string Summary(AssetDocument document)
        {
            switch (document.Asset)
            {
                case ChrFile chr: return chr.TileCount + " tiles";
                case PaletteAsset palettes: return palettes.Palettes.Count + " palettes";
                case MetaspriteAsset sprites: return sprites.Metasprites.Count + " metasprites, " + sprites.Animations.Count + " animations";
                case ScreenAsset screen: return "screen " + screen.Name;
                case MapAsset map: return map.Width + "x" + map.Height + " metatiles, " + map.Metatiles.Count + " metatile(s)";
                default: return "unreadable";
            }
        }

        private static McpResult RenderAsset(JsonObject arguments)
        {
            string path = Existing(arguments, "path");
            int scale = Math.Max(1, Math.Min(8, Number(arguments, "scale", false) ?? 2));
            RenderResult result = AssetRenderer.Render(path, Text(arguments, "item", false), scale);
            if (result.Image == null)
                return McpResult.Fail("cannot draw " + path + ":\n" + string.Join("\n", result.Diagnostics));
            McpResult reply = McpResult.Ok(Path.GetFileName(path) + ": " + result.Image.Width + "x" + result.Image.Height
                + " pixels at scale " + scale + (result.Diagnostics.Count > 0 ? "\n" + string.Join("\n", result.Diagnostics) : string.Empty));
            return reply.AddImage(PngWriter.Encode(result.Image));
        }

        private static McpResult GetAsset(JsonObject arguments)
        {
            string path = Existing(arguments, "path");
            AssetDocument document = AssetDocument.Load(path);
            StringBuilder text = new StringBuilder();
            if (document.Asset is ChrFile chr)
                text.AppendLine("{ \"kind\": \"chr\", \"tiles\": " + chr.TileCount + " }  (use get_tiles for pixels)");
            else if (document.Asset != null)
                text.AppendLine(File.ReadAllText(path));
            foreach (AssetDiagnostic diagnostic in document.Diagnostics)
                text.AppendLine(diagnostic.ToString());
            return document.Asset == null ? McpResult.Fail(text.ToString()) : McpResult.Ok(text.ToString());
        }

        private static McpResult UpdateAsset(JsonObject arguments)
        {
            string path = Path.GetFullPath(Text(arguments, "path"));
            if (!(arguments["asset"] is JsonObject asset))
                throw new McpArgumentException("\"asset\" must be the asset's JSON object");
            string json = asset.ToJsonString();
            List<AssetDiagnostic> diagnostics = new List<AssetDiagnostic>();

            Action save;
            switch (AssetKinds.Of(path))
            {
                case AssetKind.Palette:
                    save = Validated(AssetJson.Parse<PaletteAsset>(json, path, diagnostics), a => a.Validate(path), a => a.Save(path), diagnostics);
                    break;
                case AssetKind.Metasprite:
                    save = Validated(AssetJson.Parse<MetaspriteAsset>(json, path, diagnostics), a => a.Validate(path), a => a.Save(path), diagnostics);
                    break;
                case AssetKind.Screen:
                    save = Validated(AssetJson.Parse<ScreenAsset>(json, path, diagnostics), a => a.Validate(path), a => a.Save(path), diagnostics);
                    break;
                case AssetKind.Map:
                    save = Validated(AssetJson.Parse<MapAsset>(json, path, diagnostics), a => a.Validate(path), a => a.Save(path), diagnostics);
                    break;
                default:
                    throw new McpArgumentException("update_asset writes .pal.json, .msprite.json, .screen.json and .map.json files; use edit_tiles for a .chr");
            }

            if (save == null)
                return McpResult.Fail("Not written:\n" + string.Join("\n", diagnostics));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            save();
            return McpResult.Ok("Wrote " + path + (diagnostics.Count > 0 ? "\n" + string.Join("\n", diagnostics) : string.Empty));
        }

        // The save of a parsed asset, or null when it does not parse or validate.
        private static Action Validated<T>(T asset, Func<T, List<AssetDiagnostic>> validate, Action<T> save,
            List<AssetDiagnostic> diagnostics) where T : class
        {
            if (asset == null)
                return null;
            diagnostics.AddRange(validate(asset));
            return AssetDiagnostic.AnyErrors(diagnostics) ? null : (Action)(() => save(asset));
        }

        private static McpResult GetTiles(JsonObject arguments)
        {
            string path = Existing(arguments, "path");
            ChrFile chr = ChrFile.Load(path);
            int from = Math.Max(0, Number(arguments, "from", false) ?? 0);
            int count = Math.Max(1, Math.Min(256, Number(arguments, "count", false) ?? 16));
            int width = chr.GraphicsSystem.TileWidth;
            StringBuilder text = new StringBuilder();
            text.AppendLine(Path.GetFileName(path) + ": " + chr.TileCount + " tiles of " + width + "x" + chr.GraphicsSystem.TileHeight);
            for (int index = from; index < Math.Min(chr.TileCount, from + count); index++)
            {
                byte[] pixels = chr.GetTile(index);
                text.AppendLine("tile " + index + " ($" + index.ToString("X2") + "):");
                for (int row = 0; row < pixels.Length / width; row++)
                    text.AppendLine("  " + string.Concat(pixels.Skip(row * width).Take(width).Select(p => (char)('0' + p))));
            }
            return McpResult.Ok(text.ToString());
        }

        private static McpResult EditTiles(JsonObject arguments)
        {
            string path = Path.GetFullPath(Text(arguments, "path"));
            ChrFile chr = File.Exists(path) ? ChrFile.Load(path) : new ChrFile(GraphicsSystems.For("nes"));
            IGraphicsSystem system = chr.GraphicsSystem;
            if (!(arguments["tiles"] is JsonArray tiles) || tiles.Count == 0)
                throw new McpArgumentException("\"tiles\" is a list of {index, rows}");

            List<int> changed = new List<int>();
            foreach (JsonNode tile in tiles)
            {
                JsonObject entry = tile as JsonObject ?? throw new McpArgumentException("each tile is {index, rows}");
                int index = Number(entry, "index").Value;
                if (index < 0 || index >= system.MaxTiles)
                    throw new McpArgumentException("tile index " + index + " is outside 0.." + (system.MaxTiles - 1));
                JsonArray rows = entry["rows"] as JsonArray;
                if (rows == null || rows.Count != system.TileHeight)
                    throw new McpArgumentException("tile " + index + ": rows must be " + system.TileHeight + " strings");
                byte[] pixels = new byte[system.TileWidth * system.TileHeight];
                for (int y = 0; y < rows.Count; y++)
                {
                    string row = (string)rows[y] ?? string.Empty;
                    if (row.Length != system.TileWidth)
                        throw new McpArgumentException("tile " + index + ", row " + y + ": " + system.TileWidth + " digits expected");
                    for (int x = 0; x < row.Length; x++)
                    {
                        int value = row[x] == '.' ? 0 : row[x] - '0';
                        if (value < 0 || value >= system.ColorsPerPalette)
                            throw new McpArgumentException("tile " + index + ", row " + y + ": digits are 0.." + (system.ColorsPerPalette - 1));
                        pixels[y * system.TileWidth + x] = (byte)value;
                    }
                }
                chr.SetTile(index, pixels);
                changed.Add(index);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            chr.Save(path);
            return McpResult.Ok("Wrote tile(s) " + string.Join(", ", changed) + " of " + path + " (" + chr.TileCount + " tiles).");
        }

        private static McpResult ImportPng(JsonObject arguments)
        {
            string png = Existing(arguments, "png");
            string mode = (Text(arguments, "mode") ?? string.Empty).Trim().ToLowerInvariant();
            ImportMode importMode = mode == "background" ? ImportMode.Background
                : mode == "sprites" ? ImportMode.Sprites
                : throw new McpArgumentException("mode is background or sprites");
            string chr = Text(arguments, "chr", false);
            ImportResult result = PngImporter.ImportFile(png, importMode, Path.GetFullPath(Text(arguments, "out")), Text(arguments, "name"),
                string.IsNullOrWhiteSpace(chr) ? null : Path.GetFullPath(chr));

            StringBuilder text = new StringBuilder();
            text.AppendLine(result.HasErrors ? "Import failed." : "Imported " + Path.GetFileName(png) + ".");
            text.AppendLine(GraphicsJson.Statistics(result.Statistics).ToJsonString());
            foreach (string written in result.Written)
                text.AppendLine("Wrote " + written);
            foreach (AssetDiagnostic diagnostic in result.Diagnostics)
                text.AppendLine(diagnostic.ToString());
            return result.HasErrors ? McpResult.Fail(text.ToString()) : McpResult.Ok(text.ToString());
        }

        private static McpResult ExportAssets(JsonObject arguments)
        {
            string path = Existing(arguments, "path");
            ExportResult result = Directory.Exists(path) ? AssetExporter.ExportAll(path) : AssetExporter.Export(path);
            StringBuilder text = new StringBuilder();
            foreach (string written in result.Written)
                text.AppendLine("Wrote " + written);
            foreach (AssetDiagnostic diagnostic in result.Diagnostics)
                text.AppendLine(diagnostic.ToString());
            if (text.Length == 0)
                text.AppendLine("Nothing to export at " + path + ". " + Formats);
            return result.HasErrors ? McpResult.Fail(text.ToString()) : McpResult.Ok(text.ToString());
        }
    }
}
