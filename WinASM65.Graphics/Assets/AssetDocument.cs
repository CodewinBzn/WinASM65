using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace WinASM65.Graphics.Assets
{
    /// <summary>
    /// Any asset file, loaded by its kind: what a command or a protocol request
    /// that takes "a path" works with before it knows which kind it has.
    /// </summary>
    public sealed class AssetDocument
    {
        private AssetDocument(string path, AssetKind kind, object asset, IReadOnlyList<AssetDiagnostic> diagnostics)
        {
            Path = path;
            Kind = kind;
            Asset = asset;
            Diagnostics = diagnostics;
        }

        public string Path { get; private set; }
        public AssetKind Kind { get; private set; }

        /// <summary>
        /// A <see cref="ChrFile"/>, <see cref="PaletteAsset"/>, <see cref="MetaspriteAsset"/>,
        /// <see cref="ScreenAsset"/> or <see cref="MapAsset"/>; null when the file could not be read.
        /// </summary>
        public object Asset { get; private set; }

        public IReadOnlyList<AssetDiagnostic> Diagnostics { get; private set; }

        public bool HasErrors { get { return AssetDiagnostic.AnyErrors(Diagnostics); } }

        /// <summary>Loads and validates a file of any asset kind; an unknown extension is an error diagnostic.</summary>
        public static AssetDocument Load(string path)
        {
            AssetKind kind = AssetKinds.Of(path);
            switch (kind)
            {
                case AssetKind.Chr:
                    return From(path, kind, ChrFile.LoadWithDiagnostics(path));
                case AssetKind.Palette:
                    return From(path, kind, PaletteAsset.Load(path));
                case AssetKind.Metasprite:
                    return From(path, kind, MetaspriteAsset.Load(path));
                case AssetKind.Screen:
                    return From(path, kind, ScreenAsset.Load(path));
                case AssetKind.Map:
                    return From(path, kind, MapAsset.Load(path));
                default:
                    return new AssetDocument(path, kind, null, new[]
                    {
                        AssetDiagnostic.Error(path, "$", "not an asset file (.chr, .pal.json, .msprite.json, .screen.json, .map.json)"),
                    });
            }
        }

        /// <summary>
        /// The asset as JSON, as the file holds it; a tile set is described instead:
        /// <c>{ "system", "tileCount", "bytesPerTile", "data": base64 }</c>.
        /// </summary>
        public JsonNode ToJson()
        {
            if (Asset == null)
                return null;
            if (Asset is ChrFile chr)
            {
                return new JsonObject
                {
                    ["system"] = chr.GraphicsSystem.Id,
                    ["tileCount"] = chr.TileCount,
                    ["bytesPerTile"] = chr.GraphicsSystem.BytesPerTile,
                    ["data"] = Convert.ToBase64String(chr.ToArray()),
                };
            }
            return AssetJson.ToNode(Asset);
        }

        private static AssetDocument From<T>(string path, AssetKind kind, AssetLoadResult<T> result) where T : class
        {
            return new AssetDocument(path, kind, result.Asset, result.Diagnostics);
        }
    }
}
