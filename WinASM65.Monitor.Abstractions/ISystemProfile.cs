using System.Collections.Generic;

namespace WinASM65.Monitor.Abstractions
{
    /// <summary>
    /// What a system is: which processor it has, how its address space is laid out,
    /// how its screen is tiled, and what resources ship with it.
    ///
    /// A profile is description and nothing else. It opens no file, starts no
    /// machine and reaches no emulator, which is what lets a target catalogue be
    /// built and shipped without any of it running. The 6502 in a profile is a name
    /// and a clock; the CPU itself belongs to the adapter or to the execution core.
    ///
    /// The payoff is that a frame becomes a structured screen rather than a bitmap.
    /// Given <see cref="TileLayouts"/> and <see cref="NameTables"/>, the host can read
    /// a captured frame as tiles and nametable entries, which is what makes tile
    /// editing and generated graphics possible at all.
    ///
    /// No 65816: it is out of scope, and a profile that listed one would be a claim
    /// nothing measured.
    /// </summary>
    public interface ISystemProfile
    {
        /// <summary>Stable identifier, matching the target catalogue's own key, such as <c>nes</c>.</summary>
        string SystemId { get; }

        /// <summary>Name for the status line and for the documentation.</summary>
        string DisplayName { get; }

        /// <summary>Processor description. A name and a clock, never an implementation.</summary>
        CpuDescription Cpu { get; }

        /// <summary>The address space: every region, where it sits, and whether it accepts writes.</summary>
        IReadOnlyList<MemoryRegion> AddressSpace { get; }

        /// <summary>Tile geometry, empty for a machine with no tiles.</summary>
        IReadOnlyList<TileLayout> TileLayouts { get; }

        /// <summary>Nametable geometry, empty for a machine that lays its screen out another way.</summary>
        IReadOnlyList<NameTableLayout> NameTables { get; }

        /// <summary>
        /// Resources the system ships with, already in memory: palettes, tile data,
        /// character sets. Carried as content rather than as paths, because resolving
        /// a path is I/O and this assembly does none.
        /// </summary>
        IReadOnlyList<SystemAsset> Assets { get; }
    }

    /// <summary>
    /// The processor a system runs on, described and not implemented.
    /// </summary>
    public sealed class CpuDescription
    {
        /// <summary>Creates a processor description.</summary>
        /// <param name="name">Processor family, such as 6502.</param>
        /// <param name="variant">The specific part, such as 65C02.</param>
        /// <param name="clockHz">Nominal clock in hertz.</param>
        public CpuDescription(string name, string variant, int clockHz)
        {
            Name = name;
            Variant = variant;
            ClockHz = clockHz;
        }

        /// <summary>Processor family, such as 6502.</summary>
        public string Name { get; private set; }

        /// <summary>The specific part, such as 65C02.</summary>
        public string Variant { get; private set; }

        /// <summary>Nominal clock in hertz.</summary>
        public int ClockHz { get; private set; }
    }

    /// <summary>
    /// How one layer of graphics is divided into tiles.
    ///
    /// Enough to turn a pixel grid back into tile indices: how large a tile is, how
    /// many exist, how they are stored and how deep a pixel goes. The NES and the
    /// Apple II disagree on all four, which is why this is a list of layouts rather
    /// than a set of constants.
    /// </summary>
    public sealed class TileLayout
    {
        /// <summary>Creates a tile layout.</summary>
        /// <param name="name">Layer name, such as background or sprite.</param>
        /// <param name="tileWidth">Tile width in pixels.</param>
        /// <param name="tileHeight">Tile height in pixels.</param>
        /// <param name="tileCount">Tiles in this layer.</param>
        /// <param name="planeCount">Bit planes, 1 for monochrome character data.</param>
        /// <param name="bitsPerPixel">Bits one pixel occupies.</param>
        public TileLayout(string name, int tileWidth, int tileHeight, int tileCount, int planeCount, int bitsPerPixel)
        {
            Name = name;
            TileWidth = tileWidth;
            TileHeight = tileHeight;
            TileCount = tileCount;
            PlaneCount = planeCount;
            BitsPerPixel = bitsPerPixel;
        }

        /// <summary>Layer name, such as background or sprite.</summary>
        public string Name { get; private set; }

        /// <summary>Tile width in pixels.</summary>
        public int TileWidth { get; private set; }

        /// <summary>Tile height in pixels.</summary>
        public int TileHeight { get; private set; }

        /// <summary>Tiles in this layer.</summary>
        public int TileCount { get; private set; }

        /// <summary>Bit planes, 1 for monochrome character data.</summary>
        public int PlaneCount { get; private set; }

        /// <summary>Bits one pixel occupies.</summary>
        public int BitsPerPixel { get; private set; }
    }

    /// <summary>
    /// One nametable: where it lives and how many tiles wide and high it is.
    ///
    /// A separate type from <see cref="TileLayout"/> because a tile is a picture and
    /// a nametable is a map of tiles, and the address a nametable is found at is the
    /// thing a debugger needs.
    /// </summary>
    public sealed class NameTableLayout
    {
        /// <summary>Creates a nametable layout.</summary>
        /// <param name="name">Nametable name, such as background or sprite.</param>
        /// <param name="startAddress">Address the nametable begins at.</param>
        /// <param name="widthInTiles">Nametable width in tiles.</param>
        /// <param name="heightInTiles">Nametable height in tiles.</param>
        public NameTableLayout(string name, int startAddress, int widthInTiles, int heightInTiles)
        {
            Name = name;
            StartAddress = startAddress;
            WidthInTiles = widthInTiles;
            HeightInTiles = heightInTiles;
        }

        /// <summary>Nametable name, such as background or sprite.</summary>
        public string Name { get; private set; }

        /// <summary>Address the nametable begins at.</summary>
        public int StartAddress { get; private set; }

        /// <summary>Nametable width in tiles.</summary>
        public int WidthInTiles { get; private set; }

        /// <summary>Nametable height in tiles.</summary>
        public int HeightInTiles { get; private set; }
    }

    /// <summary>
    /// A resource the system ships with, held in memory rather than named by path.
    ///
    /// Generated graphics has to land somewhere the target profile can load, and a
    /// profile that reaches the filesystem would be doing I/O inside a contract.
    /// Content in, name to find it by.
    /// </summary>
    public sealed class SystemAsset
    {
        /// <summary>Creates an asset.</summary>
        /// <param name="name">Name the host looks the asset up by.</param>
        /// <param name="content">The bytes, never null and possibly empty.</param>
        public SystemAsset(string name, byte[] content)
        {
            Name = name;
            Content = content;
        }

        /// <summary>Name the host looks the asset up by.</summary>
        public string Name { get; private set; }

        /// <summary>The bytes.</summary>
        public byte[] Content { get; private set; }
    }
}