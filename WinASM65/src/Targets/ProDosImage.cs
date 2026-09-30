// WinASM65 - the ProDOS container
//
// ProDOS is the one container of this family that has a filesystem in it. A load
// file is not the file on the disk: it is a directory entry, a data fork built
// out of 512 byte blocks, a volume bitmap that has to agree with both, and a
// volume directory whose first block describes the volume rather than a file.
// Every one of those can be wrong in a way that still looks like a disk image,
// and none of them is checked by anything on this machine, so the layout below
// follows the ProDOS 8 Technical Reference field by field.
//
// The pieces, and why each is here:
//
//   B.1  the volume is blocks of 512 bytes: two reserved, four of directory,
//        thirty two of bitmap, and the rest for data. The bitmap is allocated
//        whether or not anything reads it, so omitting it would leave a volume
//        that writes over itself.
//   B.2  a directory block starts with the block before and the block after,
//        then thirteen entries of $27 bytes. The volume directory header is the
//        first entry of block 2, not a block of its own.
//   B.3  a file of 256 bytes or less is a seedling and is stored in as many
//        blocks as it needs, contiguously. A larger one is a sapling, which
//        needs an index block in front of it. A tree, past 128K, needs a master
//        index in front of that and is refused here rather than half written.
//
// The interleave is the one the plan asks for, applied to the blocks as they
// are laid on the disk: within every group of sixteen, the order is
// 0, 8, 1, 9, 2, 10, 3, 11, 4, 12, 5, 13, 6, 14, 7, 15. Reading that sequence
// left to right as "which logical block sits here", logical block 0 is the
// first block written and logical block 8 the second. ProDOS itself numbers
// logical blocks, and an unadorned .po image stores them in logical order, so
// the interleave is a property of this writer and not of the filesystem; a
// reader takes the order the volume says it has.

using System;
using System.Collections.Generic;
using System.Text;
using WinASM65.Core;

namespace WinASM65.Targets
{
    /// <summary>One segment of a ProDOS load file.</summary>
    public sealed class ProDosSegment
    {
        public int Number { get; set; }
        public int LoadAddress { get; set; }
        public string Name { get; set; }
        public byte[] Data { get; set; }

        public ProDosSegment()
        {
            Number = 0;
            LoadAddress = 0;
            Name = string.Empty;
            Data = new byte[0];
        }
    }

    /// <summary>The load file format, which is a container inside the volume.</summary>
    public static class ProDosLoadFile
    {
        /// <summary>The most bytes one data record can carry.</summary>
        public const int RecordSize = 512;

        /// <summary>The most bytes one segment can declare.</summary>
        public const int MaxSegmentSize = 0xFFFF;

        /// <summary>
        /// Builds a load file. The entry point is the offset of the byte
        /// execution starts at, counted from the first byte of the file, which
        /// is what ProDOS stores: it is not an address in the loaded segments.
        /// </summary>
        public static byte[] Build(IReadOnlyList<ProDosSegment> segments, int entryPoint)
        {
            List<byte> file = new List<byte>();
            file.Add(0x00);              // record kind: header
            file.Add(0x00);              // format version
            file.Add(0x00);              // minimum ProDOS version that can read it
            file.Add((byte)(entryPoint & 0xFF));
            file.Add((byte)((entryPoint >> 8) & 0xFF));

            for (int i = 0; i < segments.Count; i++)
            {
                ProDosSegment segment = segments[i];
                byte[] data = segment.Data ?? new byte[0];
                string name = segment.Name ?? string.Empty;
                if (data.Length > MaxSegmentSize)
                    throw new ArgumentOutOfRangeException("segments",
                        "A ProDOS segment cannot be longer than " + MaxSegmentSize + " bytes.");

                file.Add(0x00);          // record kind: segment
                file.Add((byte)segment.Number);
                file.Add((byte)(data.Length & 0xFF));
                file.Add((byte)((data.Length >> 8) & 0xFF));
                file.Add((byte)(segment.LoadAddress & 0xFF));
                file.Add((byte)((segment.LoadAddress >> 8) & 0xFF));
                file.Add((byte)name.Length);
                foreach (char c in name)
                    file.Add((byte)c);

                // Data records carry a count of at most $FF, and a count of zero
                // is how a full block says so. Segments whose length is an exact
                // multiple of the block size would otherwise end on a record
                // that says nothing at all.
                for (int at = 0; at < data.Length; at += RecordSize)
                {
                    int count = Math.Min(RecordSize, data.Length - at);
                    file.Add(0x00);
                    file.Add((byte)(count == RecordSize ? 0 : count));
                    for (int b = 0; b < count; b++)
                        file.Add(data[at + b]);
                }
            }

            file.Add(0x00);              // record kind: end of file
            file.Add(0x00);

            return file.ToArray();
        }

        /// <summary>
        /// Where a segment's bytes begin in the file. That is after the header,
        /// after every record that precedes it, and after the record header that
        /// introduces its first block of data. For a file with one segment it is
        /// the value the entry point field holds: ProDOS counts the offset from
        /// the start of the load file and lands on the load address of the
        /// segment that contains it.
        /// </summary>
        public static int EntryPointOf(IReadOnlyList<ProDosSegment> segments, int segmentIndex)
        {
            int at = 5;
            for (int i = 0; i < segments.Count; i++)
            {
                ProDosSegment segment = segments[i];
                byte[] data = segment.Data ?? new byte[0];
                string name = segment.Name ?? string.Empty;

                // Segment record: kind, number, length, address, name length, name.
                at += 7 + name.Length;
                if (i == segmentIndex)
                    return data.Length > 0 ? at + 2 : at;

                // Data records: a kind and a count for every block of the segment.
                at += ((((data.Length + RecordSize - 1) / RecordSize) * 2) + data.Length);
            }
            return at;
        }
    }

    /// <summary>The volume a ProDOS file lives in.</summary>
    public sealed class ProDosVolumeOptions
    {
        public ProDosVolumeOptions()
        {
            VolumeName = "W65";
            FileName = "PROGRAM";
            FileType = 0x06;               // binary, which is what a load file is
            VolumeBlocks = 280;            // 143K, the size ProDOS formats by default
            BitmapBlocks = 32;
            Interleaved = true;
            TimeStamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        public string VolumeName { get; set; }
        public string FileName { get; set; }
        public int FileType { get; set; }
        public int VolumeBlocks { get; set; }
        public int BitmapBlocks { get; set; }
        public bool Interleaved { get; set; }
        public DateTime TimeStamp { get; set; }
    }

    public static class ProDosVolume
    {
        public const int BlockSize = 512;
        public const int EntryLength = 0x27;
        public const int EntriesPerBlock = 13;
        public const int DirectoryKeyBlock = 2;
        public const int DirectoryBlocks = 4;
        public const int ReservedBlocks = 2;
        public const int FirstBitmapBlock = ReservedBlocks + DirectoryBlocks;
        public const int MaxVolumeName = 15;
        public const int MaxFileName = 15;
        public const int AccessReadWrite = 0xE3;

        // Storage types, from Technical Reference B.2.4.
        public const byte StorageSeedling = 0x01;
        public const byte StorageSapling = 0x02;
        public const byte StorageSubdirectory = 0x0D;
        public const byte StorageVolumeHeader = 0x0F;

        public const int FileTypeDirectory = 0x0F;
        public const int FileTypeText = 0x04;

        /// <summary>
        /// Lays a file out on a fresh volume. The file is one data fork, stored
        /// as a seedling when it fits in a block and a sapling when it does not.
        /// </summary>
        public static byte[] Build(byte[] fileData, ProDosVolumeOptions options)
        {
            options = options ?? new ProDosVolumeOptions();
            byte[] data = fileData ?? new byte[0];

            int firstData = FirstBitmapBlock + options.BitmapBlocks;
            int blocks = (data.Length + BlockSize - 1) / BlockSize;
            bool sapling = data.Length > BlockSize;

            int needed = firstData + blocks + (sapling ? 1 : 0);
            if (needed > options.VolumeBlocks)
                throw new ArgumentOutOfRangeException("fileData",
                    "A file of " + data.Length + " bytes does not fit in a volume of "
                    + options.VolumeBlocks + " blocks.");

            // The image is kept in logical order and scattered at the very end,
            // so everything above is about blocks the filesystem numbered and
            // nothing below has to know where a block physically lands.
            byte[] logical = new byte[options.VolumeBlocks * BlockSize];
            bool[] used = new bool[options.VolumeBlocks];
            for (int i = 0; i < used.Length; i++)
                used[i] = i < firstData;

            int cursor = firstData;
            int dataKeyBlock = blocks > 0 ? cursor : 0;
            for (int i = 0; i < blocks; i++)
            {
                Array.Copy(data, i * BlockSize, logical, (cursor + i) * BlockSize,
                    Math.Min(BlockSize, data.Length - i * BlockSize));
                used[cursor + i] = true;
            }
            cursor += blocks;

            // A sapling's index block sits after the data it indexes, and it is
            // the block the directory entry points at, not the first data block.
            int indexBlock = 0;
            if (sapling)
            {
                indexBlock = cursor;
                WriteIndexBlock(logical, indexBlock, dataKeyBlock, blocks);
                used[indexBlock] = true;
                cursor++;
            }

            int entryKeyBlock = sapling ? indexBlock : dataKeyBlock;
            WriteBitmap(logical, options.BitmapBlocks, used);
            WriteDirectory(logical, options, entryKeyBlock, blocks, sapling, data.Length);

            return options.Interleaved ? Interleave(logical, options.VolumeBlocks) : logical;
        }

        private static void WriteIndexBlock(byte[] logical, int block, int keyBlock, int blocks)
        {
            int at = block * BlockSize;
            for (int i = 0; i < 256; i++)
            {
                // The last pointer of an index block is the next index block, so
                // a single index can name 255 data blocks and not 256.
                if (i < 255 && i < blocks)
                {
                    logical[at + i * 2] = (byte)((keyBlock + i) & 0xFF);
                    logical[at + i * 2 + 1] = (byte)(((keyBlock + i) >> 8) & 0xFF);
                }
                else
                {
                    logical[at + i * 2] = 0x00;
                    logical[at + i * 2 + 1] = 0x00;
                }
            }
        }

        private static void WriteBitmap(byte[] logical, int bitmapBlocks, bool[] used)
        {
            // One bit per block, eight blocks to a byte, low bit first, starting
            // at the first byte of the first bitmap block. Blocks below the first
            // free one are the reserved blocks, the directory and the bitmap
            // itself: all of them are taken, which is what stops ProDOS from
            // handing out the filesystem.
            int at = FirstBitmapBlock * BlockSize;
            if (used.Length > bitmapBlocks * 8 * BlockSize)
                throw new ArgumentOutOfRangeException("options",
                    "A bitmap of " + bitmapBlocks + " blocks cannot describe a volume of "
                    + used.Length + " blocks.");

            for (int bit = 0; bit < used.Length; bit++)
            {
                if (used[bit])
                    logical[at + (bit / 8)] |= (byte)(1 << (bit % 8));
            }
        }

        private static void WriteDirectory(byte[] logical, ProDosVolumeOptions options,
            int keyBlock, int blocks, bool sapling, int length)
        {
            string volumeName = Name(options.VolumeName, MaxVolumeName);
            string fileName = Name(options.FileName, MaxFileName);
            int entryAt = DirectoryKeyBlock * BlockSize + 4;

            // The header entry describes the volume, and its name is the volume's
            // name, not a file's.
            byte volumeHeader = (byte)((StorageVolumeHeader << 4) | (volumeName.Length & 0x0F));
            logical[entryAt] = volumeHeader;
            WriteName(logical, entryAt + 1, volumeName, MaxVolumeName);
            Stamp(logical, entryAt + 0x18, options.TimeStamp);
            logical[entryAt + 0x1C] = 0x00;             // version
            logical[entryAt + 0x1D] = 0x00;             // minimum version
            logical[entryAt + 0x1E] = 0x27;             // access: everything but rename
            logical[entryAt + 0x1F] = (byte)EntryLength;
            logical[entryAt + 0x20] = (byte)EntriesPerBlock;
            WriteWord(logical, entryAt + 0x21, 1);      // one file in the volume directory
            WriteWord(logical, entryAt + 0x23, FirstBitmapBlock);
            WriteWord(logical, entryAt + 0x25, options.VolumeBlocks);

            entryAt += EntryLength;

            byte storage = sapling ? StorageSapling : StorageSeedling;
            logical[entryAt] = (byte)((storage << 4) | fileName.Length);
            WriteName(logical, entryAt + 1, fileName, MaxFileName);
            logical[entryAt + 0x10] = (byte)options.FileType;
            WriteWord(logical, entryAt + 0x11, keyBlock);
            WriteWord(logical, entryAt + 0x13, blocks + (sapling ? 1 : 0));
            WriteEof(logical, entryAt + 0x15, length);
            Stamp(logical, entryAt + 0x18, options.TimeStamp);
            logical[entryAt + 0x1C] = 0x00;
            logical[entryAt + 0x1D] = 0x00;
            logical[entryAt + 0x1E] = AccessReadWrite;
            WriteWord(logical, entryAt + 0x1F, 0x0000); // auxiliary type
            Stamp(logical, entryAt + 0x21, options.TimeStamp);
            WriteWord(logical, entryAt + 0x25, DirectoryKeyBlock);

            // The directory's own blocks are a linked list, and every block of
            // it says which two it sits between.
            for (int i = 0; i < DirectoryBlocks; i++)
            {
                int block = DirectoryKeyBlock + i;
                int at = block * BlockSize;
                WriteWord(logical, at, i == 0 ? 0 : block - 1);
                WriteWord(logical, at + 2, i + 1 < DirectoryBlocks ? block + 1 : 0);
            }
        }

        /// <summary>
        /// Scatters the logical blocks into the order the plan asks for. The
        /// sequence repeats every sixteen blocks, and it is read as "which
        /// logical block belongs here": zero, eight, one, nine, and so on.
        /// </summary>
        public static int PhysicalBlock(int logical)
        {
            int group = logical / 16;
            int within = logical % 16;
            return group * 16 + (within < 8 ? within * 2 : (within - 8) * 2 + 1);
        }

        public static byte[] Interleave(byte[] logical, int blocks)
        {
            byte[] physical = new byte[logical.Length];
            for (int i = 0; i < blocks; i++)
            {
                int target = PhysicalBlock(i);
                if (target >= blocks)
                    continue;
                Array.Copy(logical, i * BlockSize, physical, target * BlockSize, BlockSize);
            }
            return physical;
        }

        private static void WriteName(byte[] data, int at, string name, int max)
        {
            for (int i = 0; i < name.Length && i < max; i++)
                data[at + i] = (byte)name[i];
        }

        private static string Name(string name, int max)
        {
            if (string.IsNullOrEmpty(name))
                return "UNTITLED";
            string trimmed = name.ToUpperInvariant();
            return trimmed.Length <= max ? trimmed : trimmed.Substring(0, max);
        }

        private static void WriteWord(byte[] data, int at, int value)
        {
            data[at] = (byte)(value & 0xFF);
            data[at + 1] = (byte)((value >> 8) & 0xFF);
        }

        /// <summary>EOF is three bytes: the last two of them are the byte count.</summary>
        private static void WriteEof(byte[] data, int at, int length)
        {
            data[at] = (byte)(length & 0xFF);
            data[at + 1] = (byte)((length >> 8) & 0xFF);
            data[at + 2] = (byte)((length >> 16) & 0xFF);
        }

        /// <summary>
        /// The ProDOS date and time, which is not a Unix one: seconds are
        /// counted from midnight, the year starts at 1980, and the day field is
        /// $00 for Sunday rather than 1.
        /// </summary>
        private static void Stamp(byte[] data, int at, DateTime time)
        {
            DateTime local = time.Kind == DateTimeKind.Utc ? time.ToLocalTime() : time;
            int seconds = (local.Hour * 3600) + (local.Minute * 60) + local.Second;
            int days = (int)(local.Date - new DateTime(1980, 1, 1)).TotalDays;
            if (days < 0)
            {
                seconds = 0;
                days = 0;
            }

            WriteWord(data, at, seconds);
            WriteWord(data, at + 2, days);
        }
    }

    /// <summary>
    /// The ProDOS container as a target format: the payload goes into one
    /// segment of a load file, and the load file goes into one file of a fresh
    /// volume.
    /// </summary>
    public class ProDosFormat : IExecutableFormat
    {
        public string Name { get { return "prodos"; } }

        public Core.OperationResult Write(string path, byte[] payload, ResolvedTarget target)
        {
            byte[] data = payload ?? new byte[0];
            ProDosVolumeOptions options = new ProDosVolumeOptions();
            int load = 0x2003;

            if (target != null)
            {
                if (target.OriginAddress.HasValue)
                    load = target.OriginAddress.Value;
                else if (target.LoadAddress.HasValue)
                    load = target.LoadAddress.Value;
            }

            ProDosSegment segment = new ProDosSegment
            {
                Number = 1,
                LoadAddress = load,
                Name = "PROGRAM",
                Data = data
            };

            // The entry point of a load file is an offset in the file, not an
            // address: for one segment it is where that segment's first byte
            // lands, which is where ProDOS jumps once the segments are in place.
            List<ProDosSegment> segments = new List<ProDosSegment> { segment };
            byte[] loadFile = ProDosLoadFile.Build(segments,
                ProDosLoadFile.EntryPointOf(segments, 0));
            byte[] image = ProDosVolume.Build(loadFile, options);

            return ExecutableFile.WriteBytes(path, image);
        }
    }
}