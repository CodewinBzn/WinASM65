using System.Collections.Generic;
using WinASM65.Monitor.Abstractions;

namespace WinASM65.Systems
{
    /// <summary>
    /// The Nintendo Entertainment System (and the Famicom): a 2A03 at 1.79 MHz,
    /// 2 KB of work RAM, a cartridge mapped at $8000, and a PPU with its own memory.
    /// </summary>
    public sealed class NesSystem : SystemDefinition
    {
        public const string Id = "nes";

        // The NES palette as emulators commonly show it: 64 entries, 0xRRGGBB.
        private static readonly int[] Palette =
        {
            0x7C7C7C, 0x0000FC, 0x0000BC, 0x4428BC, 0x940084, 0xA80020, 0xA81000, 0x881400,
            0x503000, 0x007800, 0x006800, 0x005800, 0x004058, 0x000000, 0x000000, 0x000000,
            0xBCBCBC, 0x0078F8, 0x0058F8, 0x6844FC, 0xD800CC, 0xE40058, 0xF83800, 0xE45C10,
            0xAC7C00, 0x00B800, 0x00A800, 0x00A844, 0x008888, 0x000000, 0x000000, 0x000000,
            0xF8F8F8, 0x3CBCFC, 0x6888FC, 0x9878F8, 0xF878F8, 0xF85898, 0xF87858, 0xFCA044,
            0xF8B800, 0xB8F818, 0x58D854, 0x58F898, 0x00E8D8, 0x787878, 0x000000, 0x000000,
            0xFCFCFC, 0xA4E4FC, 0xB8B8F8, 0xD8B8F8, 0xF8B8F8, 0xF8A4C0, 0xF0D0B0, 0xFCE0A8,
            0xF8D878, 0xD8F878, 0xB8F8B8, 0xB8F8D8, 0x00FCFC, 0xF8D8F8, 0x000000, 0x000000,
        };

        private static readonly MemoryRegion[] Regions =
        {
            new MemoryRegion("RAM", 0x0000, 0x0800, true, false, 1),
            new MemoryRegion("PPU", 0x2000, 0x0008, true, false, 1),
            new MemoryRegion("APU/IO", 0x4000, 0x0020, true, false, 1),
            new MemoryRegion("SRAM", 0x6000, 0x2000, true, false, 1),
            new MemoryRegion("PRG", 0x8000, 0x8000, false, true, 1),
        };

        private static readonly MemorySpace[] Spaces =
        {
            new MemorySpace("PPU", 0x4000, true, "The PPU's bus: pattern tables, nametables, palettes"),
            new MemorySpace("PAL", 0x20, true, "Palette RAM: 4 background and 4 sprite palettes of 4 colours"),
            new MemorySpace("OAM", 0x100, true, "Sprite table: 64 sprites of 4 bytes (Y, tile, attributes, X)"),
            new MemorySpace("CHR", 0x2000, true, "Tiles of the cartridge's character ROM or RAM"),
        };

        private static readonly MemoryView[] DefaultViews =
        {
            new MemoryView("RAM", CpuSpace, 0x0000, 0x0800, true),
            new MemoryView("PRG ROM", CpuSpace, 0x8000, 0x8000, false),
            new MemoryView("Nametables", "PPU", 0x2000, 0x1000, true),
            new MemoryView("Palettes", "PAL", 0x00, 0x20, true),
            new MemoryView("Sprites (OAM)", "OAM", 0x00, 0x100, true),
            new MemoryView("CHR", "CHR", 0x0000, 0x2000, true),
        };

        private static readonly TileLayout[] Tiles =
        {
            new TileLayout("background", 8, 8, 256, 2, 2),
            new TileLayout("sprite", 8, 8, 256, 2, 2),
        };

        private static readonly NameTableLayout[] Tables =
        {
            new NameTableLayout("nametable 0", 0x2000, 32, 30),
            new NameTableLayout("nametable 1", 0x2400, 32, 30),
            new NameTableLayout("nametable 2", 0x2800, 32, 30),
            new NameTableLayout("nametable 3", 0x2C00, 32, 30),
        };

        public override string SystemId { get { return Id; } }
        public override string DisplayName { get { return "Nintendo Entertainment System"; } }
        public override CpuDescription Cpu { get { return new CpuDescription("6502", "2A03", 1789773); } }
        public override IReadOnlyList<MemoryRegion> AddressSpace { get { return Regions; } }
        public override IReadOnlyList<TileLayout> TileLayouts { get { return Tiles; } }
        public override IReadOnlyList<NameTableLayout> NameTables { get { return Tables; } }
        public override IReadOnlyList<MemorySpace> MemorySpaces { get { return Spaces; } }
        public override BootKind Boot { get { return BootKind.ResetVector; } }
        public override IReadOnlyList<MemoryView> Views { get { return DefaultViews; } }
        public override IReadOnlyList<int> MasterPalette { get { return Palette; } }
        public override string ProgramMemoryName { get { return "PRG"; } }

        /// <summary>The iNES header (16 bytes), and the 512-byte trainer when its flag is set.</summary>
        public override int ImageHeaderLength(byte[] image)
        {
            return Recognizes(image) ? 16 + ((image[6] & 0x04) != 0 ? 512 : 0) : 0;
        }

        /// <summary>An iNES file starts with "NES" and $1A.</summary>
        public override bool Recognizes(byte[] image)
        {
            return image != null && image.Length >= 16
                && image[0] == (byte)'N' && image[1] == (byte)'E' && image[2] == (byte)'S' && image[3] == 0x1A;
        }
    }
}
