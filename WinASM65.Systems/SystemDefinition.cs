using System.Collections.Generic;
using WinASM65.Monitor.Abstractions;

namespace WinASM65.Systems
{
    /// <summary>How a program starts on a machine.</summary>
    public enum BootKind
    {
        /// <summary>The CPU starts at the address of its reset vector, as a cartridge does.</summary>
        ResetVector,

        /// <summary>The program is loaded at an address and run from there, as a disk or tape program is.</summary>
        LoadAndRun
    }

    /// <summary>
    /// A window on one memory that a memory panel or a monitor pane shows by default.
    /// </summary>
    public sealed class MemoryView
    {
        /// <summary>Creates a view.</summary>
        /// <param name="name">Title, such as RAM or Palettes.</param>
        /// <param name="space">The memory: <see cref="SystemDefinition.CpuSpace"/> or the name of a <see cref="MemorySpace"/>.</param>
        /// <param name="start">First address shown.</param>
        /// <param name="length">Bytes shown.</param>
        /// <param name="isWritable">Whether the panel offers to edit it.</param>
        public MemoryView(string name, string space, int start, int length, bool isWritable)
        {
            Name = name;
            Space = space;
            Start = start;
            Length = length;
            IsWritable = isWritable;
        }

        public string Name { get; private set; }
        public string Space { get; private set; }
        public int Start { get; private set; }
        public int Length { get; private set; }
        public bool IsWritable { get; private set; }
    }

    /// <summary>
    /// One machine, as the debugger and the graphics designer need to know it.
    ///
    /// The <see cref="ISystemProfile"/> half is description shared with plugins; the
    /// rest answers the questions a host asks of a built program: where it starts,
    /// which part of the image is the program, which memories to show.
    /// </summary>
    public abstract class SystemDefinition : ISystemProfile
    {
        /// <summary>The name of the processor's own address space, in views and memory requests.</summary>
        public const string CpuSpace = "CPU";

        public abstract string SystemId { get; }
        public abstract string DisplayName { get; }
        public abstract CpuDescription Cpu { get; }
        public abstract IReadOnlyList<MemoryRegion> AddressSpace { get; }
        public virtual IReadOnlyList<TileLayout> TileLayouts { get { return new TileLayout[0]; } }
        public virtual IReadOnlyList<NameTableLayout> NameTables { get { return new NameTableLayout[0]; } }
        public virtual IReadOnlyList<SystemAsset> Assets { get { return new SystemAsset[0]; } }
        public virtual IReadOnlyList<MemorySpace> MemorySpaces { get { return new MemorySpace[0]; } }

        /// <summary>How a program starts.</summary>
        public abstract BootKind Boot { get; }

        /// <summary>The memories a memory panel shows, first one first.</summary>
        public abstract IReadOnlyList<MemoryView> Views { get; }

        /// <summary>
        /// The colours the machine can show, as 0xRRGGBB, in the order its palette
        /// registers index them. Empty for a machine with fixed colours.
        /// </summary>
        public virtual IReadOnlyList<int> MasterPalette { get { return new int[0]; } }

        /// <summary>
        /// Name of the memory the program is stored in when the image has one, such
        /// as PRG. A stop reported as an offset into it is shown with this name.
        /// </summary>
        public virtual string ProgramMemoryName { get { return "ROM"; } }

        /// <summary>
        /// Bytes at the start of an image that are not program: a file header, a
        /// trainer. A source line's place in the program is its place in the image
        /// minus this.
        /// </summary>
        public virtual int ImageHeaderLength(byte[] image)
        {
            return 0;
        }

        /// <summary>Whether an image is recognisably for this machine, by its header.</summary>
        public virtual bool Recognizes(byte[] image)
        {
            return false;
        }

        /// <summary>The memory space of that name, or null. <see cref="CpuSpace"/> is not one of them.</summary>
        public MemorySpace SpaceNamed(string name)
        {
            foreach (MemorySpace space in MemorySpaces)
            {
                if (string.Equals(space.Name, name, System.StringComparison.OrdinalIgnoreCase))
                    return space;
            }
            return null;
        }
    }
}
