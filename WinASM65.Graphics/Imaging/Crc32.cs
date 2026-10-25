namespace WinASM65.Graphics.Imaging
{
    /// <summary>
    /// The CRC-32 of PNG chunks (ISO 3309, polynomial $EDB88320 reflected), written
    /// here because the framework only ships it in a package this library does not take.
    /// </summary>
    public static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        /// <summary>The CRC of a range of bytes.</summary>
        public static uint Compute(byte[] data, int offset, int count)
        {
            return Update(0xFFFFFFFFu, data, offset, count) ^ 0xFFFFFFFFu;
        }

        /// <summary>Continues a running CRC (start with $FFFFFFFF, finish by inverting).</summary>
        public static uint Update(uint crc, byte[] data, int offset, int count)
        {
            for (int i = offset; i < offset + count; i++)
                crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        private static uint[] BuildTable()
        {
            uint[] table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }
    }
}
