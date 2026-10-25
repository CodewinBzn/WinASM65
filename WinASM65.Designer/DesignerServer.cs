using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using WinASM65.Designer.Rpc;
using WinASM65.Graphics;
using WinASM65.Graphics.Assets;
using WinASM65.Graphics.Export;
using WinASM65.Graphics.Import;
using WinASM65.Graphics.Imaging;
using WinASM65.Graphics.Rendering;
using WinASM65.Graphics.Systems;

namespace WinASM65.Designer
{
    /// <summary>
    /// JSON-RPC 2.0 over a <see cref="MessageStream"/>: the designer as a service for
    /// the VS Code designer and the MCP tools.
    ///
    /// <para>
    /// Methods (params → result):
    /// </para>
    /// <list type="bullet">
    /// <item><c>describe { system? }</c> → the system's description; every system as <c>{ systems: [...] }</c> without one.</item>
    /// <item><c>load { path }</c> → <c>{ kind, asset, diagnostics }</c>.</item>
    /// <item><c>validate { path }</c> → <c>{ diagnostics, errors }</c>.</item>
    /// <item><c>export { path }</c> (file or folder) → <c>{ written, diagnostics }</c>.</item>
    /// <item><c>render { path, item?, scale? }</c> → <c>{ png (base64), width, height, originX, originY, diagnostics }</c>.</item>
    /// <item><c>import { png, mode, out, name, chr? }</c> → <c>{ written, statistics, diagnostics }</c>.</item>
    /// <item><c>format { kind, asset, path? }</c> → <c>{ text, diagnostics }</c>: the text the library would
    /// write for a JSON asset (kind <c>palette</c>, <c>metasprite</c>, <c>screen</c> or <c>map</c>), so an
    /// editor that changes an asset in memory saves exactly what the library saves; with a path, the
    /// diagnostics are those of the asset at that place (its references resolved from there).</item>
    /// <item><c>shutdown</c> → null, and the server stops.</item>
    /// </list>
    /// <para>
    /// An asset with problems is a normal answer (diagnostics in the result), not
    /// an error; a request that cannot produce its result at all (no image to
    /// render, no import) is error -32001 with the diagnostics in <c>error.data</c>.
    /// </para>
    /// </summary>
    public sealed class DesignerServer
    {
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;
        public const int AssetError = -32001;

        private readonly MessageStream _stream;

        public DesignerServer(MessageStream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        /// <summary>Set once a <c>shutdown</c> request has been answered.</summary>
        public bool ShutdownRequested { get; private set; }

        /// <summary>Answers requests until <c>shutdown</c> or the end of the input.</summary>
        public void Run()
        {
            while (!ShutdownRequested)
            {
                JsonObject message;
                try
                {
                    message = _stream.Read();
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is JsonException)
                {
                    _stream.Write(Error(null, ParseError, ex.Message, null));
                    continue;
                }
                if (message == null)
                    return;
                JsonObject response = Handle(message);
                if (response != null)
                    _stream.Write(response);
            }
        }

        /// <summary>The response to one message; null for a notification (no id).</summary>
        public JsonObject Handle(JsonObject request)
        {
            JsonNode id = request["id"]?.DeepClone();
            bool notification = !request.ContainsKey("id");
            string method = request["method"] is JsonValue m && m.TryGetValue(out string name) ? name : null;
            if (method == null)
                return notification ? null : Error(id, InvalidRequest, "a request needs a method", null);

            JsonObject parameters = request["params"] as JsonObject ?? new JsonObject();
            try
            {
                JsonNode result = Dispatch(method, parameters);
                if (notification)
                    return null;
                return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
            }
            catch (RpcException ex)
            {
                return notification ? null : Error(id, ex.Code, ex.Message, ex.Data);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException
                || ex is InvalidOperationException || ex is NotSupportedException)
            {
                return notification ? null : Error(id, InternalError, ex.Message, null);
            }
        }

        private JsonNode Dispatch(string method, JsonObject parameters)
        {
            switch (method)
            {
                case "describe":
                    return Describe(OptionalString(parameters, "system"));
                case "load":
                    return Load(RequiredString(parameters, "path"));
                case "validate":
                    return Validate(RequiredString(parameters, "path"));
                case "export":
                    return Export(RequiredString(parameters, "path"));
                case "render":
                    return Render(RequiredString(parameters, "path"), OptionalString(parameters, "item"), OptionalInt(parameters, "scale", 1));
                case "import":
                    return Import(parameters);
                case "format":
                    return Format(RequiredString(parameters, "kind"), parameters["asset"], OptionalString(parameters, "path"));
                case "shutdown":
                    ShutdownRequested = true;
                    return null;
                default:
                    throw new RpcException(MethodNotFound, "unknown method '" + method + "'", null);
            }
        }

        private static JsonNode Describe(string system)
        {
            if (string.IsNullOrWhiteSpace(system))
                return new JsonObject { ["systems"] = GraphicsJson.DescribeAll() };
            IGraphicsSystem graphics = GraphicsSystems.For(system);
            if (graphics == null)
                throw new RpcException(InvalidParams, "unknown graphics system '" + system + "'", null);
            return GraphicsJson.Describe(graphics);
        }

        private static JsonNode Load(string path)
        {
            AssetDocument document = AssetDocument.Load(path);
            return new JsonObject
            {
                ["kind"] = document.Kind.ToString().ToLowerInvariant(),
                ["asset"] = document.ToJson(),
                ["diagnostics"] = GraphicsJson.Diagnostics(document.Diagnostics),
            };
        }

        private static JsonNode Validate(string path)
        {
            AssetDocument document = AssetDocument.Load(path);
            int errors = 0;
            foreach (AssetDiagnostic diagnostic in document.Diagnostics)
            {
                if (diagnostic.IsError)
                    errors++;
            }
            return new JsonObject
            {
                ["diagnostics"] = GraphicsJson.Diagnostics(document.Diagnostics),
                ["errors"] = errors,
            };
        }

        private static JsonNode Export(string path)
        {
            ExportResult result = Directory.Exists(path) ? AssetExporter.ExportAll(path) : AssetExporter.Export(path);
            return new JsonObject
            {
                ["written"] = GraphicsJson.Paths(result.Written),
                ["diagnostics"] = GraphicsJson.Diagnostics(result.Diagnostics),
            };
        }

        private static JsonNode Render(string path, string item, int scale)
        {
            RenderResult result = AssetRenderer.Render(path, item, scale);
            if (result.Image == null)
                throw new RpcException(AssetError, "cannot render " + path, new JsonObject { ["diagnostics"] = GraphicsJson.Diagnostics(result.Diagnostics) });
            return new JsonObject
            {
                ["png"] = Convert.ToBase64String(PngWriter.Encode(result.Image)),
                ["width"] = result.Image.Width,
                ["height"] = result.Image.Height,
                ["originX"] = result.OriginX,
                ["originY"] = result.OriginY,
                ["diagnostics"] = GraphicsJson.Diagnostics(result.Diagnostics),
            };
        }

        private static JsonNode Import(JsonObject parameters)
        {
            string png = RequiredString(parameters, "png");
            string modeName = RequiredString(parameters, "mode");
            string folder = RequiredString(parameters, "out");
            string name = RequiredString(parameters, "name");
            string chr = OptionalString(parameters, "chr");
            ImportMode mode;
            if (!Cli.TryParseMode(modeName, out mode))
                throw new RpcException(InvalidParams, "mode must be background or sprites", null);

            ImportResult result = PngImporter.ImportFile(png, mode, folder, name, chr);
            if (result.Written.Count == 0)
                throw new RpcException(AssetError, "cannot import " + png, new JsonObject { ["diagnostics"] = GraphicsJson.Diagnostics(result.Diagnostics) });
            return new JsonObject
            {
                ["written"] = GraphicsJson.Paths(result.Written),
                ["statistics"] = GraphicsJson.Statistics(result.Statistics),
                ["diagnostics"] = GraphicsJson.Diagnostics(result.Diagnostics),
            };
        }

        private static JsonNode Format(string kindName, JsonNode asset, string path)
        {
            if (!(asset is JsonObject))
                throw new RpcException(InvalidParams, "parameter 'asset' must be a JSON object", null);
            AssetKind kind;
            if (!Enum.TryParse(kindName, true, out kind) || kind == AssetKind.Unknown || kind == AssetKind.Chr)
                throw new RpcException(InvalidParams, "kind must be palette, metasprite, screen or map", null);

            string json = asset.ToJsonString();
            System.Collections.Generic.List<AssetDiagnostic> diagnostics = new System.Collections.Generic.List<AssetDiagnostic>();
            string text;
            switch (kind)
            {
                case AssetKind.Palette:
                {
                    PaletteAsset palette = Parse<PaletteAsset>(json, path);
                    text = AssetJson.Serialize(palette, null);
                    if (path != null)
                        diagnostics.AddRange(palette.Validate(path));
                    break;
                }
                case AssetKind.Metasprite:
                {
                    MetaspriteAsset metasprite = Parse<MetaspriteAsset>(json, path);
                    text = AssetJson.Serialize(metasprite, null);
                    if (path != null)
                        diagnostics.AddRange(metasprite.Validate(path));
                    break;
                }
                case AssetKind.Screen:
                {
                    // The rows ScreenAsset.Save lays out: a screen row of tiles, a block row of attributes.
                    ScreenAsset screen = Parse<ScreenAsset>(json, path);
                    IGraphicsSystem system = GraphicsSystems.For(screen.System);
                    int tileRow = system == null ? 32 : system.ScreenWidthTiles;
                    int attributeRow = system == null ? 16 : ScreenAsset.AttributeColumns(system);
                    text = AssetJson.Serialize(screen, property => property == "tiles" ? tileRow : property == "attributes" ? attributeRow : 0);
                    if (path != null)
                        diagnostics.AddRange(screen.Validate(path));
                    break;
                }
                default:
                {
                    // MapAsset.Save: one map row per line of cells.
                    MapAsset map = Parse<MapAsset>(json, path);
                    int width = map.Width;
                    text = AssetJson.Serialize(map, property => property == "cells" ? width : 0);
                    if (path != null)
                        diagnostics.AddRange(map.Validate(path));
                    break;
                }
            }
            return new JsonObject
            {
                ["text"] = text,
                ["diagnostics"] = GraphicsJson.Diagnostics(diagnostics),
            };
        }

        private static T Parse<T>(string json, string path) where T : class
        {
            System.Collections.Generic.List<AssetDiagnostic> diagnostics = new System.Collections.Generic.List<AssetDiagnostic>();
            T asset = AssetJson.Parse<T>(json, path, diagnostics);
            if (asset == null)
                throw new RpcException(InvalidParams, diagnostics.Count > 0 ? diagnostics[0].Path + ": " + diagnostics[0].Message : "not an asset",
                    new JsonObject { ["diagnostics"] = GraphicsJson.Diagnostics(diagnostics) });
            return asset;
        }

        private static string RequiredString(JsonObject parameters, string name)
        {
            string value = OptionalString(parameters, name);
            if (string.IsNullOrWhiteSpace(value))
                throw new RpcException(InvalidParams, "missing parameter '" + name + "'", null);
            return value;
        }

        private static string OptionalString(JsonObject parameters, string name)
        {
            JsonNode node = parameters[name];
            if (node == null)
                return null;
            if (node is JsonValue value && value.TryGetValue(out string text))
                return text;
            throw new RpcException(InvalidParams, "parameter '" + name + "' must be a string", null);
        }

        private static int OptionalInt(JsonObject parameters, string name, int fallback)
        {
            JsonNode node = parameters[name];
            if (node == null)
                return fallback;
            if (node is JsonValue value && value.TryGetValue(out int number))
                return number;
            throw new RpcException(InvalidParams, "parameter '" + name + "' must be an integer", null);
        }

        private static JsonObject Error(JsonNode id, int code, string message, JsonNode data)
        {
            JsonObject error = new JsonObject { ["code"] = code, ["message"] = message };
            if (data != null)
                error["data"] = data;
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = error };
        }

        /// <summary>A request that fails with a JSON-RPC error code.</summary>
        private sealed class RpcException : Exception
        {
            public RpcException(int code, string message, JsonNode data)
                : base(message)
            {
                Code = code;
                Data = data;
            }

            public int Code { get; private set; }
            public new JsonNode Data { get; private set; }
        }
    }
}
