using System;

namespace WinASM65.Monitor.Abstractions
{
    /// <summary>
    /// One region of an address space: what it is called, where it appears, and
    /// whether it can be written.
    ///
    /// Declared rather than computed because it is a fact about the machine, not a
    /// decision. A mapper is what decides how an address is decoded, so a monitor
    /// reading <c>$8000</c> without knowing the region it landed in is reading a
    /// window that may not be the one the mapper selects.
    ///
    /// <see cref="IsWritable"/> carries the asymmetry that a single
    /// <see cref="ExecutionCapability.MemoryWrite"/> bit cannot: on MesenCE 2.2.1
    /// <c>emu.write</c> exists, RAM accepts it, and a write into PRG ROM is dropped
    /// silently. That is not a missing capability, it is a read-only region, and it
    /// is the reason the ROM pane and the RAM pane are not the same pane.
    /// </summary>
    public sealed class MemoryRegion
    {
        /// <summary>
        /// Creates a region descriptor.
        /// </summary>
        /// <param name="name">Region name as the machine's own documentation spells it, such as PRG, RAM or CHR.</param>
        /// <param name="startAddress">CPU-visible address at which bank 0 appears.</param>
        /// <param name="length">Bytes visible per bank.</param>
        /// <param name="isWritable">False for a space that accepts no write at all, which is not a failure but a fact.</param>
        /// <param name="isBanked">True when more than one bank exists behind the same addresses.</param>
        /// <param name="bankCount">Banks behind those addresses, or 1 when not banked.</param>
        public MemoryRegion(string name, int startAddress, int length, bool isWritable, bool isBanked, int bankCount)
        {
            Name = name;
            StartAddress = startAddress;
            Length = length;
            IsWritable = isWritable;
            IsBanked = isBanked;
            BankCount = bankCount;
        }

        /// <summary>Region name, as the machine's own documentation spells it.</summary>
        public string Name { get; private set; }

        /// <summary>CPU-visible address at which bank 0 appears.</summary>
        public int StartAddress { get; private set; }

        /// <summary>Bytes visible per bank.</summary>
        public int Length { get; private set; }

        /// <summary>False for a read-only space. A write into one is refused, never silently accepted.</summary>
        public bool IsWritable { get; private set; }

        /// <summary>True when more than one bank exists behind the same addresses.</summary>
        public bool IsBanked { get; private set; }

        /// <summary>Banks behind those addresses, or 1 when not banked.</summary>
        public int BankCount { get; private set; }
    }

    /// <summary>
    /// A place to read or write: which region, which bank, and how far into it.
    ///
    /// A value type because it sits on the read path, where an allocation per access
    /// would be the adapter's most expensive mistake, and because it carries no
    /// behaviour to justify a heap object.
    ///
    /// Addresses are not flattened here on purpose. Whether <c>$8000</c> is bank 3 of
    /// PRG or the start of CHR is the mapper's business and the caller's, and a
    /// contract that resolved it would be a contract doing the adapter's work.
    /// </summary>
    public readonly struct RegionAddress
    {
        /// <summary>Creates an address inside a named region and bank.</summary>
        /// <param name="region">Index into the adapter's or profile's region list.</param>
        /// <param name="bank">Bank index, 0 for an unbanked region.</param>
        /// <param name="offset">Byte offset within that bank.</param>
        public RegionAddress(int region, int bank, int offset)
        {
            Region = region;
            Bank = bank;
            Offset = offset;
        }

        /// <summary>Index into the adapter's or profile's region list.</summary>
        public int Region { get; }

        /// <summary>Bank index, 0 for an unbanked region.</summary>
        public int Bank { get; }

        /// <summary>Byte offset within that bank.</summary>
        public int Offset { get; }
    }

    /// <summary>
    /// Which accesses a watchpoint is asked about.
    ///
    /// Separate from <see cref="ExecutionCapability.WatchpointRead"/> and
    /// <see cref="ExecutionCapability.WatchpointWrite"/>, which answer whether
    /// watching works at all; this answers which kind is being watched.
    /// </summary>
    public enum MemoryAccessKind
    {
        /// <summary>Reads.</summary>
        Read = 1,

        /// <summary>Writes.</summary>
        Write = 2,

        /// <summary>Reads and writes, where the machine offers no way to separate them.</summary>
        ReadWrite = Read | Write
    }
}