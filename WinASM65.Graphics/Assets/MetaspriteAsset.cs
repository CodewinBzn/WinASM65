using System.Collections.Generic;
using WinASM65.Graphics.Systems;

namespace WinASM65.Graphics.Assets
{
    /// <summary>
    /// Metasprites and their animations (<c>*.msprite.json</c>).
    ///
    /// <para>
    /// A metasprite is a group of hardware sprites drawn together, placed relative
    /// to an origin (the point the game positions: a character's feet, say). Each
    /// tile is at a pixel position in the drawing, so moving the origin moves where
    /// the group is anchored without moving a tile. An animation is a list of
    /// metasprites by name, each shown for a number of frames.
    /// </para>
    /// <code>
    /// { "version": 1, "system": "nes", "chr": "tiles.chr", "palettes": "game.pal.json", "prefix": "",
    ///   "metasprites": [ { "name": "ship", "originX": 8, "originY": 8,
    ///     "tiles": [ { "x": 0, "y": 0, "tile": 1, "palette": 0, "flipH": false, "flipV": false, "behind": false } ] } ],
    ///   "animations": [ { "name": "fly", "loop": true, "frames": [ { "metasprite": "ship", "duration": 8 } ] } ] }
    /// </code>
    /// </summary>
    public sealed class MetaspriteAsset
    {
        public int Version { get; set; } = AssetJson.CurrentVersion;
        public string System { get; set; } = NesGraphics.SystemId;

        /// <summary>The tile set, relative to this file.</summary>
        public string Chr { get; set; }

        /// <summary>The palette file, relative to this file; the sprite palettes are its last ones.</summary>
        public string Palettes { get; set; }

        public string Prefix { get; set; } = string.Empty;
        public List<Metasprite> Metasprites { get; set; } = new List<Metasprite>();
        public List<MetaspriteAnimation> Animations { get; set; } = new List<MetaspriteAnimation>();

        public static AssetLoadResult<MetaspriteAsset> Load(string path)
        {
            List<AssetDiagnostic> diagnostics = new List<AssetDiagnostic>();
            MetaspriteAsset asset = AssetJson.Read<MetaspriteAsset>(path, diagnostics);
            if (asset != null)
                diagnostics.AddRange(asset.Validate(path));
            return new AssetLoadResult<MetaspriteAsset>(asset, diagnostics);
        }

        public void Save(string path)
        {
            AssetJson.Write(path, this, null);
        }

        /// <summary>The index of the metasprite of that name (compared as a label), or -1.</summary>
        public int IndexOf(string name)
        {
            string wanted = Export.AsmNames.Snake(name ?? string.Empty);
            for (int i = 0; Metasprites != null && i < Metasprites.Count; i++)
            {
                if (Metasprites[i] != null && Export.AsmNames.Snake(Metasprites[i].Name ?? string.Empty) == wanted)
                    return i;
            }
            return -1;
        }

        /// <summary>The animation of that name, or null.</summary>
        public MetaspriteAnimation AnimationNamed(string name)
        {
            string wanted = Export.AsmNames.Snake(name ?? string.Empty);
            foreach (MetaspriteAnimation animation in Animations ?? new List<MetaspriteAnimation>())
            {
                if (animation != null && Export.AsmNames.Snake(animation.Name ?? string.Empty) == wanted)
                    return animation;
            }
            return null;
        }

        public List<AssetDiagnostic> Validate(string path)
        {
            AssetValidator check = new AssetValidator(path);
            IGraphicsSystem system = check.Header(Version, System);
            int tileCount = check.TileCount(path, Chr, system);
            check.PaletteReference(path, Palettes);
            if (Metasprites == null)
            {
                check.Error("$.metasprites", "missing metasprites");
                return check.Diagnostics;
            }

            HashSet<string> names = new HashSet<string>();
            for (int m = 0; m < Metasprites.Count; m++)
            {
                string at = "$.metasprites[" + m + "]";
                Metasprite sprite = Metasprites[m];
                if (sprite == null)
                {
                    check.Error(at, "missing metasprite");
                    continue;
                }
                check.Name(at + ".name", sprite.Name, names, "metasprite");
                if (sprite.Tiles == null)
                {
                    check.Error(at + ".tiles", "missing tiles");
                    continue;
                }
                for (int t = 0; t < sprite.Tiles.Count; t++)
                {
                    string tileAt = at + ".tiles[" + t + "]";
                    MetaspriteTile tile = sprite.Tiles[t];
                    if (tile == null)
                    {
                        check.Error(tileAt, "missing tile");
                        continue;
                    }
                    check.Tile(tileAt + ".tile", tile.Tile, tileCount, system);
                    if (system != null)
                        check.Range(tileAt + ".palette", tile.Palette, 0, system.SpritePaletteCount - 1, "sprite palette");
                    int dx = tile.X - sprite.OriginX;
                    int dy = tile.Y - sprite.OriginY;
                    check.Range(tileAt + ".x", dx, -128, 127, "offset from the origin");
                    if (dy == -128)
                        check.Error(tileAt + ".y", "an offset of -128 from the origin reads as the end marker $80");
                    else
                        check.Range(tileAt + ".y", dy, -128, 127, "offset from the origin");
                }
            }

            HashSet<string> animationNames = new HashSet<string>();
            for (int a = 0; Animations != null && a < Animations.Count; a++)
            {
                string at = "$.animations[" + a + "]";
                MetaspriteAnimation animation = Animations[a];
                if (animation == null)
                {
                    check.Error(at, "missing animation");
                    continue;
                }
                check.Name(at + ".name", animation.Name, animationNames, "animation");
                if (animation.Frames == null || animation.Frames.Count == 0)
                {
                    check.Error(at + ".frames", "an animation needs at least one frame");
                    continue;
                }
                for (int f = 0; f < animation.Frames.Count; f++)
                {
                    string frameAt = at + ".frames[" + f + "]";
                    AnimationFrame frame = animation.Frames[f];
                    if (frame == null)
                    {
                        check.Error(frameAt, "missing frame");
                        continue;
                    }
                    int index = IndexOf(frame.Metasprite);
                    if (index < 0)
                        check.Error(frameAt + ".metasprite", "no metasprite named '" + frame.Metasprite + "'");
                    else if (index > 254)
                        check.Error(frameAt + ".metasprite", "metasprite index " + index + " does not fit the frame byte (0..254)");
                    check.Range(frameAt + ".duration", frame.Duration, 1, 255, "duration");
                }
            }
            return check.Diagnostics;
        }
    }

    /// <summary>A group of sprites drawn together, around an origin.</summary>
    public sealed class Metasprite
    {
        public string Name { get; set; }

        /// <summary>X of the origin, in the same pixels as the tiles' positions.</summary>
        public int OriginX { get; set; }

        /// <summary>Y of the origin.</summary>
        public int OriginY { get; set; }

        public List<MetaspriteTile> Tiles { get; set; } = new List<MetaspriteTile>();
    }

    /// <summary>One hardware sprite of a metasprite.</summary>
    public sealed class MetaspriteTile
    {
        /// <summary>Left of the tile, in pixels.</summary>
        public int X { get; set; }

        /// <summary>Top of the tile, in pixels.</summary>
        public int Y { get; set; }

        public int Tile { get; set; }

        /// <summary>Sprite palette, 0 to 3 on the NES (not the index in the palette file).</summary>
        public int Palette { get; set; }

        public bool FlipH { get; set; }
        public bool FlipV { get; set; }

        /// <summary>Drawn behind the background's opaque pixels.</summary>
        public bool Behind { get; set; }
    }

    /// <summary>A sequence of metasprites.</summary>
    public sealed class MetaspriteAnimation
    {
        public string Name { get; set; }

        /// <summary>Whether the animation starts again after its last frame.</summary>
        public bool Loop { get; set; } = true;

        public List<AnimationFrame> Frames { get; set; } = new List<AnimationFrame>();
    }

    /// <summary>A metasprite, by name, and how long it stays.</summary>
    public sealed class AnimationFrame
    {
        public string Metasprite { get; set; }

        /// <summary>Video frames the metasprite is shown, 1 to 255.</summary>
        public int Duration { get; set; } = 1;
    }
}
