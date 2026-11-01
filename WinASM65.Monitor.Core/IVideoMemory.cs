using System.Collections.Generic;

namespace WinASM65.Monitor
{
    /// <summary>
    /// Access to the memories a machine has besides the CPU's: on the NES the PPU's
    /// bus, the palettes, the sprite table and the tiles.
    ///
    /// Optional, like <see cref="ICpuStateSource"/>: a backend implements it when its
    /// bridge named spaces in its HELLO, and a caller asks before relying on it. The
    /// names are the ones the system definition declares (WinASM65.Systems).
    /// </summary>
    public interface IVideoMemory
    {
        /// <summary>The spaces this backend can read and write, empty when it has none.</summary>
        IReadOnlyList<string> MemorySpaces { get; }

        byte[] ReadSpace(string space, int address, int length);

        void WriteSpace(string space, int address, byte[] bytes);
    }
}
