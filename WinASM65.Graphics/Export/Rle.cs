using System;
using System.Collections.Generic;
using System.IO;

namespace WinASM65.Graphics.Export
{
    /// <summary>
    /// The run-length encoding of exported screens (<c>*.rle</c>).
    ///
    /// <para>
    /// The data is a list of pairs <c>count, value</c>: <c>count</c> (1 to 255)
    /// copies of the byte <c>value</c>. A count of 0 ends the data, and has no
    /// value after it. A run longer than 255 is cut into several pairs.
    /// </para>
    /// <para>
    /// It is the simplest format a 6502 routine can unpack straight into PPUDATA:
    /// <code>
    /// @pair:  ldy #0
    ///         lda (src),y     ; count
    ///         beq @done
    ///         tax
    ///         iny
    ///         lda (src),y     ; value
    /// @copy:  sta PPUDATA
    ///         dex
    ///         bne @copy
    ///         ; advance src by 2, then jmp @pair
    /// </code>
    /// A nametable of large flat areas (a title screen) shrinks a lot; a busy one
    /// may grow, up to twice its size plus one byte.
    /// </para>
    /// </summary>
    public static class Rle
    {
        public static byte[] Encode(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            List<byte> output = new List<byte>();
            int i = 0;
            while (i < data.Length)
            {
                byte value = data[i];
                int run = 1;
                while (i + run < data.Length && data[i + run] == value && run < 255)
                    run++;
                output.Add((byte)run);
                output.Add(value);
                i += run;
            }
            output.Add(0);
            return output.ToArray();
        }

        /// <summary>Unpacks encoded data; malformed data (no end marker, a missing value) throws.</summary>
        public static byte[] Decode(byte[] encoded)
        {
            if (encoded == null)
                throw new ArgumentNullException(nameof(encoded));
            List<byte> output = new List<byte>();
            int i = 0;
            while (true)
            {
                if (i >= encoded.Length)
                    throw new InvalidDataException("RLE data without its end marker (a count of 0)");
                int count = encoded[i++];
                if (count == 0)
                    return output.ToArray();
                if (i >= encoded.Length)
                    throw new InvalidDataException("RLE pair without its value at offset " + (i - 1));
                byte value = encoded[i++];
                for (int k = 0; k < count; k++)
                    output.Add(value);
            }
        }
    }
}
