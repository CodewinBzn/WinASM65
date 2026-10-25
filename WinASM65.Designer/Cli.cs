using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WinASM65.Graphics;
using WinASM65.Graphics.Assets;
using WinASM65.Graphics.Export;
using WinASM65.Graphics.Import;
using WinASM65.Graphics.Imaging;
using WinASM65.Graphics.Rendering;

namespace WinASM65.Designer
{
    /// <summary>
    /// The command line, taking its writers as arguments so tests run it in memory.
    ///
    /// <para>
    /// Exit codes: 0 done, 1 done with errors in the diagnostics (or nothing could
    /// be produced), 2 bad usage. Diagnostics are printed one per line as
    /// <c>file: error: $.path: message</c>, the form a problem matcher reads.
    /// </para>
    /// </summary>
    public static class Cli
    {
        public const int Ok = 0;
        public const int Failed = 1;
        public const int Usage = 2;

        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            if (args == null || args.Length == 0)
                return PrintUsage(error);

            Dictionary<string, string> options;
            List<string> positional;
            if (!ParseOptions(args, 1, out options, out positional, error))
                return PrintUsage(error);

            switch (args[0])
            {
                case "export":
                    return positional.Count == 1 && options.Count == 0 ? Export(positional[0], output) : PrintUsage(error);
                case "render":
                    return positional.Count == 1 ? Render(positional[0], options, output, error) : PrintUsage(error);
                case "import":
                    return positional.Count == 1 ? Import(positional[0], options, output, error) : PrintUsage(error);
                case "systems":
                    if (positional.Count != 0 || options.Count != 0)
                        return PrintUsage(error);
                    output.WriteLine(GraphicsJson.DescribeAll().ToJsonString(new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() }));
                    return Ok;
                case "validate":
                    return positional.Count == 1 && options.Count == 0 ? Validate(positional[0], output) : PrintUsage(error);
                default:
                    error.WriteLine("unknown command '" + args[0] + "'");
                    return PrintUsage(error);
            }
        }

        /// <summary>Reads <c>background</c> or <c>sprites</c> (also <c>bg</c>, <c>sprite</c>).</summary>
        public static bool TryParseMode(string text, out ImportMode mode)
        {
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "background":
                case "bg":
                    mode = ImportMode.Background;
                    return true;
                case "sprites":
                case "sprite":
                    mode = ImportMode.Sprites;
                    return true;
                default:
                    mode = ImportMode.Background;
                    return false;
            }
        }

        private static int Export(string path, TextWriter output)
        {
            ExportResult result = Directory.Exists(path) ? AssetExporter.ExportAll(path) : AssetExporter.Export(path);
            foreach (string written in result.Written)
                output.WriteLine("wrote " + written);
            Print(result.Diagnostics, output);
            return result.HasErrors ? Failed : Ok;
        }

        private static int Validate(string path, TextWriter output)
        {
            List<AssetDiagnostic> diagnostics = new List<AssetDiagnostic>();
            int files = 0;
            if (Directory.Exists(path))
            {
                List<string> all = new List<string>(Directory.GetFiles(path, "*", SearchOption.AllDirectories));
                all.Sort(StringComparer.Ordinal);
                foreach (string file in all)
                {
                    if (AssetKinds.Of(file) == AssetKind.Unknown)
                        continue;
                    diagnostics.AddRange(AssetDocument.Load(file).Diagnostics);
                    files++;
                }
            }
            else
            {
                diagnostics.AddRange(AssetDocument.Load(path).Diagnostics);
                files = 1;
            }
            Print(diagnostics, output);
            bool failed = AssetDiagnostic.AnyErrors(diagnostics);
            output.WriteLine(files + (files == 1 ? " file, " : " files, ") + diagnostics.Count + " diagnostic" + (diagnostics.Count == 1 ? "" : "s"));
            return failed ? Failed : Ok;
        }

        private static int Render(string path, Dictionary<string, string> options, TextWriter output, TextWriter error)
        {
            string target;
            if (!options.TryGetValue("o", out target) && !options.TryGetValue("output", out target))
            {
                error.WriteLine("render needs -o <out.png>");
                return PrintUsage(error);
            }
            int scale = 1;
            string scaleText;
            if (options.TryGetValue("scale", out scaleText)
                && (!int.TryParse(scaleText, NumberStyles.None, CultureInfo.InvariantCulture, out scale) || scale < 1))
            {
                error.WriteLine("--scale takes a whole number from 1");
                return Usage;
            }
            string item;
            options.TryGetValue("item", out item);
            foreach (string key in options.Keys)
            {
                if (key != "o" && key != "output" && key != "scale" && key != "item")
                {
                    error.WriteLine("render does not take --" + key);
                    return PrintUsage(error);
                }
            }

            RenderResult result = AssetRenderer.Render(path, item, scale);
            Print(result.Diagnostics, output);
            if (result.Image == null)
                return Failed;
            string directory = Path.GetDirectoryName(Path.GetFullPath(target));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            PngWriter.Save(result.Image, target);
            output.WriteLine("wrote " + Path.GetFullPath(target) + " (" + result.Image.Width + "x" + result.Image.Height + ")");
            return result.HasErrors ? Failed : Ok;
        }

        private static int Import(string png, Dictionary<string, string> options, TextWriter output, TextWriter error)
        {
            string modeText, folder, name, chr;
            options.TryGetValue("mode", out modeText);
            options.TryGetValue("out", out folder);
            options.TryGetValue("name", out name);
            options.TryGetValue("chr", out chr);
            ImportMode mode;
            if (!TryParseMode(modeText, out mode) || string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(name))
            {
                error.WriteLine("import needs --mode background|sprites, --out <folder> and --name <name>");
                return PrintUsage(error);
            }
            foreach (string key in options.Keys)
            {
                if (key != "mode" && key != "out" && key != "name" && key != "chr")
                {
                    error.WriteLine("import does not take --" + key);
                    return PrintUsage(error);
                }
            }

            ImportResult result = PngImporter.ImportFile(png, mode, folder, name, chr);
            foreach (string written in result.Written)
                output.WriteLine("wrote " + written);
            Print(result.Diagnostics, output);
            ImportStatistics s = result.Statistics;
            output.WriteLine("tiles: " + s.Cells + " cells, " + s.NewTiles + " new, " + s.ReusedTiles + " reused, " + s.TileCount
                + " in the tile set; palettes: " + s.Palettes + "; colours: " + s.Colors + "; warnings: " + s.Warnings);
            return result.Written.Count == 0 || result.HasErrors ? Failed : Ok;
        }

        private static void Print(IEnumerable<AssetDiagnostic> diagnostics, TextWriter output)
        {
            foreach (AssetDiagnostic diagnostic in diagnostics)
                output.WriteLine(diagnostic.ToString());
        }

        /// <summary>
        /// Splits <c>--name value</c> and <c>-o value</c> from positional arguments.
        /// Every option takes a value.
        /// </summary>
        private static bool ParseOptions(string[] args, int start, out Dictionary<string, string> options, out List<string> positional, TextWriter error)
        {
            options = new Dictionary<string, string>(StringComparer.Ordinal);
            positional = new List<string>();
            for (int i = start; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.StartsWith("-", StringComparison.Ordinal) && arg.Length > 1)
                {
                    string key = arg.TrimStart('-');
                    if (i + 1 >= args.Length)
                    {
                        error.WriteLine(arg + " needs a value");
                        return false;
                    }
                    options[key] = args[++i];
                }
                else
                {
                    positional.Add(arg);
                }
            }
            return true;
        }

        private static int PrintUsage(TextWriter error)
        {
            error.WriteLine("WinASM65 graphics designer");
            error.WriteLine("  export <asset|folder>                         write the .asm/.bin files of assets");
            error.WriteLine("  render <asset> -o <out.png> [--item <name>] [--scale n]");
            error.WriteLine("                                                draw an asset as a PNG");
            error.WriteLine("  import <png> --mode background|sprites --out <folder> --name <name> [--chr <existing.chr>]");
            error.WriteLine("                                                make tiles, palettes and a screen or metasprite");
            error.WriteLine("  validate <asset|folder>                       report what is wrong with assets");
            error.WriteLine("  systems                                       describe the graphics systems as JSON");
            error.WriteLine("  --server                                      JSON-RPC 2.0 on stdin/stdout");
            return Usage;
        }
    }
}
