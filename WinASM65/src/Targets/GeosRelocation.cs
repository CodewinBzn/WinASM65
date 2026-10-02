// WinASM65 - relocation table of a GEOS application
//
// What the kernal actually does, from the Programmer's Reference Guide and from
// the reverse engineered kernal: LOAD ($C208) takes the address to load at from
// the information sector, and LOAD2 ($C211) will load at an address the caller
// supplies in $886C-$886D instead. Either way the bytes land verbatim. The
// kernal has no notion of a base relocation table, so an application that is
// not loaded where it was assembled has to correct itself, and WinASM65's job
// is to hand it the list of places to correct.
//
// That is what this is: data, not code. A 6502 cannot read its own program
// counter without a JSR whose target is a runtime address, which a link time
// constant is not, so a stub that relocates itself cannot be written without
// the loader passing its address. Emitting one anyway would produce a stub that
// works at exactly one load address and corrupts memory at every other, which
// is worse than no stub at all.
//
// The table is therefore consumable in three ways, and all three are the same
// loop over the same entries:
//   - the application, from a stub the application brings itself,
//   - a loader or installer that loads the file somewhere else,
//   - WinASM65 itself, when asked to write the relocated image out.
//
// Layout, recorded in the information sector's free area so that a kernal which
// ignores that area still loads the file:
//
//   $00-$03  "R65A", so the area cannot be mistaken for empty padding
//   $04-$05  the address the image was assembled for
//   $06-$07  the number of entries
//   $08-$09  the entry point of the program
//   $0A-     entries, three bytes each: address low, address high, width
//
// The width is a byte rather than an implied constant because a one byte
// absolute reference is a real thing, and a table that cannot describe one
// would be a table that is wrong on the day someone writes it.

using System;
using System.Collections.Generic;
using System.Globalization;
using WinASM65.Expressions;
using WinASM65.Linking;

namespace WinASM65.Targets
{
    /// <summary>
    /// The list of absolute references in a linked image, and the pass that
    /// applies a load bias to them.
    /// </summary>
    public sealed class GeosRelocationTable
    {
        /// <summary>Marks the block as ours, in the area GEOS leaves to the application.</summary>
        public static readonly byte[] Magic = new byte[] { 0x52, 0x36, 0x35, 0x41 };

        public const int HeaderSize = 10;
        public const int EntrySize = 3;

        /// <summary>
        /// Why a high byte cannot go in a table of this shape, said once so the
        /// refusal and the documentation cannot drift apart.
        /// </summary>
        public const string UNREPRESENTABLE_REFERENCE =
            "Relocation site ${0:X4} holds the high byte of an address, and this table cannot move it: " +
            "the new high byte depends on the carry out of the low byte, which the table does not carry. " +
            "Keep the whole address ('lda label') instead of a half ('lda #>label') if the image must be relocatable.";

        /// <summary>The address the image was assembled for.</summary>
        public ushort BaseAddress { get; internal set; }

        /// <summary>Where the program starts, as assembled.</summary>
        public ushort EntryAddress { get; internal set; }

        /// <summary>Where the table is loaded, for a caller that has to find it.</summary>
        public ushort TableAddress { get; internal set; }

        /// <summary>The absolute references, in link order.</summary>
        public IReadOnlyList<LinkedReference> Entries { get; internal set; }

        /// <summary>The whole table: the header, then the entries.</summary>
        public byte[] Data { get; internal set; }

        public GeosRelocationTable()
        {
            Entries = new List<LinkedReference>();
            Data = new byte[0];
        }

        public int Size
        {
            get { return HeaderSize + Entries.Count * EntrySize; }
        }

        public static GeosRelocationTable Build(LinkedImage image, ushort entryAddress)
        {
            if (image == null)
                throw new ArgumentNullException("image");

            // Refused before anything is written, and by name. A high byte cannot be
            // moved by adding a bias to the byte in memory: it is a function of the
            // whole address plus the carry out of the low byte, and the table cannot
            // know that carry. An entry that claimed to describe one would relocate
            // the program to an address one page off, with no sign anywhere.
            //
            // The check lives here rather than in the linker because the linker does
            // not know whether the image will ever move. A program that stays where
            // it was assembled is correct with a high byte in it, so refusing at link
            // time would reject good code on behalf of a target nobody chose yet.
            for (int r = 0; r < image.References.Count; r++)
            {
                if (image.References[r].Selector == ByteSelector.High)
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                        UNREPRESENTABLE_REFERENCE, image.References[r].Address));
            }

            GeosRelocationTable table = new GeosRelocationTable();
            table.BaseAddress = image.OriginAddress;
            table.EntryAddress = entryAddress;
            table.Entries = image.References;

            byte[] data = new byte[table.Size];
            for (int i = 0; i < Magic.Length; i++)
                data[i] = Magic[i];
            WriteWord(data, 4, table.BaseAddress);
            WriteWord(data, 6, (ushort)table.Entries.Count);
            WriteWord(data, 8, table.EntryAddress);
            for (int e = 0; e < table.Entries.Count; e++)
            {
                int at = HeaderSize + e * EntrySize;
                data[at] = (byte)(table.Entries[e].Address & 0xFF);
                data[at + 1] = (byte)((table.Entries[e].Address >> 8) & 0xFF);
                data[at + 2] = (byte)table.Entries[e].Width;
            }
            table.Data = data;
            return table;
        }

        /// <summary>
        /// Reads a table back. This is the other half of <see cref="Build"/>: an
        /// application that relocates itself has to read this, so a writer that
        /// cannot be read back is a writer whose output is only useful to the
        /// machine that produced it.
        /// </summary>
        public static bool TryRead(byte[] data, out GeosRelocationTable table)
        {
            table = null;
            if (data == null || data.Length < HeaderSize)
                return false;
            for (int i = 0; i < Magic.Length; i++)
            {
                if (data[i] != Magic[i])
                    return false;
            }

            int count = data[6] | (data[7] << 8);
            if (data.Length < HeaderSize + count * EntrySize)
                return false;

            GeosRelocationTable result = new GeosRelocationTable();
            result.BaseAddress = ReadWord(data, 4);
            result.EntryAddress = ReadWord(data, 8);
            List<LinkedReference> entries = new List<LinkedReference>();
            for (int e = 0; e < count; e++)
            {
                int at = HeaderSize + e * EntrySize;
                int width = data[at + 2];
                if (width != 1 && width != 2)
                    return false;
                entries.Add(new LinkedReference
                {
                    Address = ReadWord(data, at),
                    Width = width
                });
            }
            result.Entries = entries;
            result.Data = data;
            table = result;
            return true;
        }

        /// <summary>
        /// The pass: every entry gains the difference between where the image was
        /// assembled and where it has been loaded.
        /// <para>
        /// This is a word add, which is the same operation the kernal would have
        /// to do, and it is deliberately the only thing it does. Wrapping is
        /// left to the sixteen bits the addresses are made of, because a table
        /// that refused to describe an address past $FFFF would be a table that
        /// could not describe a program that ends at $FFFF.
        /// </para>
        /// <para>
        /// A low byte is the same operation truncated to one octet, and that is
        /// exact: <c>(v + bias) &amp; $FF == (v &amp; $FF) + bias</c> modulo 256,
        /// because addition commutes with reduction. A high byte is not -- see
        /// <see cref="UnrepresentableReference"/>.
        /// </para>
        /// </summary>
        public void Apply(byte[] image, int offsetInImage, ushort loadAddress)
        {
            if (image == null)
                throw new ArgumentNullException("image");

            int bias = (int)loadAddress - BaseAddress;
            for (int e = 0; e < Entries.Count; e++)
            {
                // Refused here as well as in Build, because a table can be read back
                // from a file and handed to Apply without ever going through Build.
                // Silently biasing a high byte would produce an address one page off,
                // which is the exact failure the refusal exists to prevent.
                if (Entries[e].Selector == ByteSelector.High)
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                        UNREPRESENTABLE_REFERENCE, Entries[e].Address));

                int site = Entries[e].Address - BaseAddress + offsetInImage;
                if (site < 0 || site + Entries[e].Width > image.Length)
                {
                    throw new InvalidOperationException(
                        "Relocation site $" + Entries[e].Address.ToString("X4")
                        + " is outside the image it belongs to.");
                }

                int value = image[site];
                if (Entries[e].Width == 2)
                    value |= image[site + 1] << 8;
                value = (value + bias) & 0xFFFF;
                image[site] = (byte)(value & 0xFF);
                if (Entries[e].Width == 2)
                    image[site + 1] = (byte)((value >> 8) & 0xFF);
            }
        }

        private static void WriteWord(byte[] data, int at, ushort value)
        {
            data[at] = (byte)(value & 0xFF);
            data[at + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static ushort ReadWord(byte[] data, int at)
        {
            return (ushort)(data[at] | (data[at + 1] << 8));
        }
    }
}
