using System;
using System.Collections.Generic;
using System.IO;
using WinASM65.Graphics.Systems;

namespace WinASM65.Graphics.Assets
{
    /// <summary>
    /// A binary tile set (<c>*.chr</c>): the machine's tile data, as a program
    /// includes it, with no header.
    ///
    /// It stays binary rather than JSON because that is what the cartridge holds
    /// and what every other tool reads; the tile count is its length divided by the
    /// bytes of one tile. Bytes after the last whole tile are kept as they are.
    /// </summary>
    public sealed class ChrFile
    {
        private readonly List<byte> _data;

        /// <summary>An empty tile set.</summary>
        public ChrFile(IGraphicsSystem system)
            : this(system, new byte[0])
        {
        }

        /// <summary>A tile set over existing bytes (copied).</summary>
        public ChrFile(IGraphicsSystem system, byte[] data)
        {
            GraphicsSystem = system ?? throw new ArgumentNullException(nameof(system));
            _data = new List<byte>(data ?? new byte[0]);
        }

        public IGraphicsSystem GraphicsSystem { get; private set; }

        /// <summary>Whole tiles in the file.</summary>
        public int TileCount { get { return _data.Count / GraphicsSystem.BytesPerTile; } }

        /// <summary>The bytes of the file.</summary>
        public byte[] ToArray()
        {
            return _data.ToArray();
        }

        /// <summary>Reads a tile set; the system defaults to the NES.</summary>
        public static ChrFile Load(string path, IGraphicsSystem system = null)
        {
            return new ChrFile(system ?? GraphicsSystems.For(NesGraphics.SystemId), File.ReadAllBytes(path));
        }

        /// <summary>
        /// Reads a tile set and reports what is wrong with it instead of throwing: a
        /// missing file (no asset), a length that is not a whole number of tiles.
        /// </summary>
        public static AssetLoadResult<ChrFile> LoadWithDiagnostics(string path, IGraphicsSystem system = null)
        {
            List<AssetDiagnostic> diagnostics = new List<AssetDiagnostic>();
            ChrFile chr;
            try
            {
                chr = Load(path, system);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                diagnostics.Add(AssetDiagnostic.Error(path, "$", "cannot read the tile set: " + ex.Message));
                return new AssetLoadResult<ChrFile>(null, diagnostics);
            }
            diagnostics.AddRange(chr.Validate(path));
            return new AssetLoadResult<ChrFile>(chr, diagnostics);
        }

        /// <summary>A warning when the length is not a whole number of tiles, an error past the table.</summary>
        public List<AssetDiagnostic> Validate(string path)
        {
            List<AssetDiagnostic> diagnostics = new List<AssetDiagnostic>();
            int remainder = _data.Count % GraphicsSystem.BytesPerTile;
            if (remainder != 0)
                diagnostics.Add(AssetDiagnostic.Warning(path, "$", remainder + " bytes after the last whole tile"));
            if (TileCount > GraphicsSystem.MaxTiles * 2)
                diagnostics.Add(AssetDiagnostic.Warning(path, "$", TileCount + " tiles: more than two tile tables"));
            return diagnostics;
        }

        public void Save(string path)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, _data.ToArray());
        }

        /// <summary>The colour indexes of a tile, row-major.</summary>
        public byte[] GetTile(int index)
        {
            if (index < 0 || index >= TileCount)
                throw new ArgumentOutOfRangeException(nameof(index), "tile " + index + " of " + TileCount);
            return GraphicsSystem.DecodeTile(_data.GetRange(index * GraphicsSystem.BytesPerTile, GraphicsSystem.BytesPerTile).ToArray(), 0);
        }

        /// <summary>The colour indexes of a tile, or a blank tile when the index is outside the set.</summary>
        public byte[] GetTileOrBlank(int index)
        {
            if (index < 0 || index >= TileCount)
                return new byte[GraphicsSystem.TileWidth * GraphicsSystem.TileHeight];
            return GetTile(index);
        }

        /// <summary>Replaces a tile, growing the set with blank tiles when the index is past its end.</summary>
        public void SetTile(int index, byte[] pixels)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            EnsureTileCount(index + 1);
            byte[] encoded = GraphicsSystem.EncodeTile(pixels);
            int offset = index * GraphicsSystem.BytesPerTile;
            for (int i = 0; i < encoded.Length; i++)
                _data[offset + i] = encoded[i];
        }

        /// <summary>Appends a tile; its index.</summary>
        public int AddTile(byte[] pixels)
        {
            int index = TileCount;
            SetTile(index, pixels);
            return index;
        }

        /// <summary>Adds blank tiles.</summary>
        public void Grow(int tiles)
        {
            if (tiles < 0)
                throw new ArgumentOutOfRangeException(nameof(tiles));
            EnsureTileCount(TileCount + tiles);
        }

        /// <summary>Grows the set with blank tiles up to a count; never shrinks it.</summary>
        public void EnsureTileCount(int count)
        {
            // Bytes after the last whole tile are padding of a partial tile: they are
            // completed into a tile rather than shifted.
            int needed = count * GraphicsSystem.BytesPerTile;
            while (_data.Count < needed)
                _data.Add(0);
        }

        /// <summary>The first tile with these pixels, or -1.</summary>
        public int FindTile(byte[] pixels)
        {
            byte[] encoded = GraphicsSystem.EncodeTile(pixels);
            byte[] data = _data.ToArray();
            for (int t = 0; t < TileCount; t++)
            {
                int offset = t * GraphicsSystem.BytesPerTile;
                bool same = true;
                for (int i = 0; i < encoded.Length && same; i++)
                    same = data[offset + i] == encoded[i];
                if (same)
                    return t;
            }
            return -1;
        }
    }
}
