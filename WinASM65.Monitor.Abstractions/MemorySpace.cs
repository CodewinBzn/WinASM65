namespace WinASM65.Monitor.Abstractions
{
    /// <summary>
    /// A memory the processor does not address directly, such as video memory.
    ///
    /// The CPU's own address space is described by <see cref="ISystemProfile.AddressSpace"/>;
    /// a machine also has memories behind its video chip, its sprite table or its
    /// palette, which a debugger reads and a graphics editor writes. They are named
    /// here so the host can offer them without knowing the machine: the NES has
    /// <c>PPU</c>, <c>PAL</c>, <c>OAM</c> and <c>CHR</c>, a C64 would have its colour
    /// RAM, an Apple II has none.
    ///
    /// The name is the one a bridge is asked for (<c>READ PAL:$00 32</c>), so it is
    /// short, upper-case and stable.
    /// </summary>
    public sealed class MemorySpace
    {
        /// <summary>Creates a memory space descriptor.</summary>
        /// <param name="name">Short stable name, as a bridge is asked for it, such as PAL.</param>
        /// <param name="length">Bytes in the space, addressed from 0.</param>
        /// <param name="isWritable">False for a space that accepts no write.</param>
        /// <param name="description">One line for a tooltip or a document.</param>
        public MemorySpace(string name, int length, bool isWritable, string description)
        {
            Name = name;
            Length = length;
            IsWritable = isWritable;
            Description = description;
        }

        /// <summary>Short stable name, as a bridge is asked for it.</summary>
        public string Name { get; private set; }

        /// <summary>Bytes in the space, addressed from 0.</summary>
        public int Length { get; private set; }

        /// <summary>False for a space that accepts no write.</summary>
        public bool IsWritable { get; private set; }

        /// <summary>One line for a tooltip or a document.</summary>
        public string Description { get; private set; }
    }
}
