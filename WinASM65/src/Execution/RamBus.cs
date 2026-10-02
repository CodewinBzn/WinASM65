using System;

namespace WinASM65.Execution
{
    /// <summary>
    /// The whole 64K address space as writable memory.
    /// <para>
    /// This is the default machine: no banking, no ROM, no side effects. It is
    /// the configuration in which every byte is readable and writable, which is
    /// what a unit test needs to be able to write a program, run it, and look at
    /// the result.
    /// </para>
    /// </summary>
    public sealed class RamBus : ICpuBus
    {
        public const int Size = 0x10000;

        private readonly byte[] _ram = new byte[Size];

        public byte Read(ushort address)
        {
            return _ram[address];
        }

        public void Write(ushort address, byte value)
        {
            _ram[address] = value;
        }

        /// <summary>Copies bytes in, wrapping at the top of the address space.</summary>
        public void Load(ushort address, byte[] data)
        {
            if (data == null)
                return;
            for (int i = 0; i < data.Length; i++)
                _ram[(ushort)(address + i)] = data[i];
        }

        /// <summary>Writes one value to every address in the range, both ends included.</summary>
        public void Fill(ushort start, ushort end, byte value)
        {
            if (start > end)
                throw new ArgumentException("the start address is above the end address");
            for (int address = start; address <= end; address++)
                _ram[address] = value;
        }
    }
}
