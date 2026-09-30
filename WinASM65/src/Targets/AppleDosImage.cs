// WinASM65 - the Apple II DOS 3.2 and DOS 3.3 containers
//
// ProDOS and BBC BASIC both have a container this repository can write, and the
// two are not alike: ProDOS has a filesystem with a volume directory and a
// bitmap, BBC BASIC has nothing at all. DOS order sits between them, and it is
// the container of the machine the plan calls the most widespread of all.
//
// What a DOS 3.3 disk has, and where each piece comes from:
//
//   B.1  the volume table of contents is track 17 sector 0, and it holds the
//        geometry, the volume number, and a free sector bitmap with four bytes
//        per track and one bit per sector. A bit set means free.
//   B.2  the catalog is a chain of sectors on track 17, and each sector holds
//        seven file descriptive entries of 35 bytes. An entry names the file,
//        its type, how many sectors it takes, and the first sector of its track
//        and sector list.
//   B.3  a track and sector list is itself a chain of sectors, and it is the
//        index that turns a file into sectors. The list holds 122 pairs, and
//        each pair is a track and a sector.
//
// The two versions are the same shape and a different set of numbers, and the
// differences are real rather than cosmetic:
//
//   C.1  DOS 3.2 is a 13 sector volume and DOS 3.3 a 16 sector one. A 3.2 disk
//        holds 35 * 13 * 256 = 116 480 bytes, a 3.3 disk 143 360.
//   C.2  the version byte at $03 says 2 or 3, and the byte at $00 says 2 or 4.
//   C.3  DOS 3.2 allocates a file's data in pairs of sectors, because a drive
//        of the day could only step over a sector by writing the one after it.
//        So a file of an odd number of sectors takes a pair it does not use,
//        the sector count in the catalog is even, and the first sector of every
//        pair is even. DOS 3.3 allocates one free sector at a time.
//   C.4  the catalog of DOS 3.2 is three sectors, so 21 files. DOS 3.3 has the
//        whole track, 112 files.
//
// One decision belongs here rather than to the format, and it is the same one
// the ProDOS writer makes: the image is written in DOS sector order, and the
// interleave DOS applies when it talks to a drive is not stored anywhere in
// the volume. A physical image needs the skew applied to the sectors as they
// are laid down, so the skew is an option here and it is the identity by
// default, which is what makes the bytes of this image comparable with what
// DOS itself reads and writes on an emulator that does not re-skew.

using System;
using System.Collections.Generic;
using WinASM65.Core;

namespace WinASM65.Targets
{
    /// <summary>Which DOS wrote the volume. The two differ in geometry and in allocation.</summary>
    public enum AppleDosVersion
    {
        Dos32 = 2,
        Dos33 = 3
    }

    /// <summary>What a volume holds and how it is shaped.</summary>
    public sealed class AppleDosVolumeOptions
    {
        public AppleDosVolumeOptions(AppleDosVersion version)
        {
            Version = version;
            Tracks = 35;
            SectorsPerTrack = version == AppleDosVersion.Dos32 ? 13 : 16;
            VolumeNumber = 254;
            FileName = "PROGRAM";
            FileType = AppleDosProgram.Binary;

            // DOS 3.3 keeps the catalog at the top of track 17 and works down it.
            // A 13 sector track has no sector 15, so DOS 3.2 starts at the last
            // sector of the track. The field that says so is written either way:
            // it is the volume that says where its catalog is, not this writer.
            FirstCatalogSector = SectorsPerTrack - 1;
            CatalogSectors = version == AppleDosVersion.Dos32 ? 3 : 16;
            Skew = Identity(SectorsPerTrack);
        }

        public AppleDosVersion Version { get; private set; }
        public int Tracks { get; set; }
        public int SectorsPerTrack { get; set; }
        public int VolumeNumber { get; set; }
        public string FileName { get; set; }
        public int FileType { get; set; }
        public int FirstCatalogSector { get; set; }
        public int CatalogSectors { get; set; }

        /// <summary>Where a sector of a track sits on the disk, or the identity.</summary>
        public byte[] Skew { get; set; }

        public int SectorsPerVolume
        {
            get { return Tracks * SectorsPerTrack; }
        }

        private static byte[] Identity(int sectors)
        {
            byte[] skew = new byte[sectors];
            for (int i = 0; i < sectors; i++)
                skew[i] = (byte)i;
            return skew;
        }
    }

    public static class AppleDosVolume
    {
        public const int SectorSize = 256;
        public const int VtocTrack = 17;
        public const int VtocSector = 0;
        public const int CatalogEntryLength = 35;
        public const int EntriesPerCatalogSector = 7;
        public const int FirstEntryAt = 0x0B;
        public const int TsPairs = 122;
        public const int FreeBitmapAt = 0x38;

        /// <summary>The byte at $00 of the VTOC, which says 2 on a 3.2 and 4 on a 3.3.</summary>
        public const byte Marker32 = 0x02;
        public const byte Marker33 = 0x04;

        /// <summary>
        /// Lays a file out on a fresh volume: its sectors, the track and sector
        /// list that indexes them, one catalog entry, one catalog sector, and
        /// the VTOC that says all of it.
        /// </summary>
        public static byte[] Build(byte[] fileData, AppleDosVolumeOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            byte[] data = fileData ?? new byte[0];
            int sectors = (data.Length + SectorSize - 1) / SectorSize;
            if (sectors == 0)
                sectors = 1;                      // a file takes a sector even when it is empty

            bool pairs = options.Version == AppleDosVersion.Dos32;
            int allocated = pairs ? ((sectors + 1) / 2) * 2 : sectors;
            if (allocated > TsPairs)
                throw new ArgumentOutOfRangeException("fileData",
                    "A file of " + data.Length + " bytes needs " + allocated
                    + " sectors, and a track and sector list holds " + TsPairs + ".");

            byte[] image = new byte[options.SectorsPerVolume * SectorSize];

            // Every sector is free until something takes it, and a bit set is
            // free. Track 0 is never handed out: a catalog entry whose track is
            // zero is an entry that was never used.
            bool[] used = new bool[options.SectorsPerVolume];
            for (int i = 0; i < used.Length; i++)
                used[i] = TrackOf(i, options) == 0;

            Allocation cursor = new Allocation(options, used);
            int catalogSector = cursor.Take(VtocTrack, options.FirstCatalogSector);
            if (catalogSector < 0)
                throw new InvalidOperationException(
                    "Track 17 has no free sector left for the catalog.");

            int tsSector = cursor.Take(VtocTrack, options.FirstCatalogSector - 1);
            if (tsSector < 0)
                throw new InvalidOperationException(
                    "Track 17 has no free sector left for the track and sector list.");

            int[] fileSectors = new int[allocated];
            for (int i = 0; i < allocated; i += pairs ? 2 : 1)
            {
                int sector = cursor.TakeNext(pairs && (i % 2) == 0);
                if (sector < 0)
                    throw new InvalidOperationException(
                        "The volume has no room left for the file.");
                fileSectors[i] = sector;
                if (pairs)
                {
                    int other = sector + 1;
                    if (other % options.SectorsPerTrack == 0)
                        throw new InvalidOperationException(
                            "A DOS 3.2 pair runs off the end of track "
                            + TrackOf(sector, options) + ".");
                    used[other] = true;
                    fileSectors[i + 1] = other;
                }
            }

            for (int i = 0; i < sectors; i++)
            {
                int at = fileSectors[i] * SectorSize;
                int count = Math.Min(SectorSize, data.Length - i * SectorSize);
                Array.Copy(data, i * SectorSize, image, at, count);
            }

            WriteSectorList(image, options, tsSector, fileSectors);
            WriteCatalog(image, options, catalogSector, tsSector, allocated);
            WriteVtoc(image, options, used, cursor.LastTrack);

            return ApplySkew(image, options);
        }

        private static int TrackOf(int sector, AppleDosVolumeOptions options)
        {
            return sector / options.SectorsPerTrack;
        }

        private static int SectorAddress(AppleDosVolumeOptions options, int track, int sector)
        {
            return (track * options.SectorsPerTrack + sector) * SectorSize;
        }

        /// <summary>
        /// Hands out sectors. DOS allocates upward from the first track that has
        /// room, and it keeps track of where it stopped, which the VTOC records
        /// as the last track it allocated. Every number here is a sector, counted
        /// from the first sector of the volume.
        /// </summary>
        private sealed class Allocation
        {
            private readonly AppleDosVolumeOptions _options;
            private readonly bool[] _used;
            private int _next;

            public Allocation(AppleDosVolumeOptions options, bool[] used)
            {
                _options = options;
                _used = used;
                _next = 0;
                LastTrack = 0;
            }

            public int LastTrack { get; private set; }

            /// <summary>Takes one named sector of a track, and gives back its number.</summary>
            public int Take(int track, int sector)
            {
                int at = track * _options.SectorsPerTrack + sector;
                if (at < 0 || at >= _used.Length || _used[at])
                    return -1;
                _used[at] = true;
                LastTrack = Math.Max(LastTrack, track);
                return at;
            }

            /// <summary>
            /// The next free sector. On DOS 3.2 the caller is holding a pair, so
            /// the even sector of the pair is the one that has to be found, and
            /// the odd one is taken with it.
            /// </summary>
            public int TakeNext(bool even)
            {
                for (int at = _next; at < _used.Length; at++)
                {
                    if (_used[at])
                        continue;

                    int sector = at % _options.SectorsPerTrack;
                    if (even && (sector % 2) != 0)
                        continue;

                    _used[at] = true;
                    LastTrack = Math.Max(LastTrack, at / _options.SectorsPerTrack);
                    _next = at + 1;
                    return at;
                }
                return -1;
            }
        }

        private static void WriteSectorList(byte[] image, AppleDosVolumeOptions options,
            int sectorAddress, int[] fileSectors)
        {
            int at = sectorAddress * SectorSize;
            image[at] = 0x00;                     // not used
            image[at + 1] = 0x00;                 // no second list
            image[at + 2] = 0x00;
            image[at + 3] = 0x00;
            image[at + 4] = 0x00;
            image[at + 5] = 0x00;                 // the first sector of this list is at 0
            image[at + 6] = 0x00;

            for (int i = 0; i < TsPairs; i++)
            {
                int pair = at + 0x0C + i * 2;
                if (i < fileSectors.Length)
                {
                    int address = fileSectors[i];
                    image[pair] = (byte)((address / options.SectorsPerTrack) & 0xFF);
                    image[pair + 1] = (byte)(address % options.SectorsPerTrack);
                }
                else
                {
                    image[pair] = 0x00;           // a track of zero ends the list
                    image[pair + 1] = 0x00;
                }
            }
        }

        private static void WriteCatalog(byte[] image, AppleDosVolumeOptions options,
            int sectorAddress, int tsAddress, int sectors)
        {
            int at = sectorAddress * SectorSize;
            image[at] = 0x00;                     // not used
            image[at + 1] = 0x00;                 // no second catalog sector
            image[at + 2] = 0x00;

            int entry = at + FirstEntryAt;
            image[entry] = (byte)((tsAddress / options.SectorsPerTrack) & 0xFF);
            image[entry + 1] = (byte)(tsAddress % options.SectorsPerTrack);
            image[entry + 2] = (byte)(options.FileType & 0x7F);

            // A DOS name is high ASCII, thirty characters, padded with blanks.
            string name = Name(options.FileName);
            for (int i = 0; i < 30; i++)
            {
                char c = i < name.Length ? name[i] : ' ';
                image[entry + 3 + i] = (byte)(c | 0x80);
            }

            image[entry + 0x21] = (byte)(sectors & 0xFF);
            image[entry + 0x22] = (byte)((sectors >> 8) & 0xFF);
        }

        private static void WriteVtoc(byte[] image, AppleDosVolumeOptions options,
            bool[] used, int lastTrack)
        {
            int at = VtocTrack * options.SectorsPerTrack * SectorSize;
            int vtoc = at + VtocSector * SectorSize;

            image[vtoc] = options.Version == AppleDosVersion.Dos32 ? Marker32 : Marker33;
            image[vtoc + 1] = VtocTrack;         // the catalog is on its own track
            image[vtoc + 2] = (byte)options.FirstCatalogSector;
            image[vtoc + 3] = (byte)options.Version;
            image[vtoc + 6] = (byte)options.VolumeNumber;
            image[vtoc + 0x27] = TsPairs;
            image[vtoc + 0x30] = (byte)lastTrack;
            image[vtoc + 0x31] = 0x01;            // allocation walks up the tracks
            image[vtoc + 0x34] = (byte)options.Tracks;
            image[vtoc + 0x35] = (byte)options.SectorsPerTrack;
            image[vtoc + 0x36] = (byte)(SectorSize & 0xFF);
            image[vtoc + 0x37] = (byte)((SectorSize >> 8) & 0xFF);

            // One bit per sector, four bytes per track, a bit set is free. A
            // track of sixteen sectors uses two of the four bytes, so the bits of
            // the sectors a volume does not have stay clear without any test.
            for (int i = FreeBitmapAt; i < SectorSize; i++)
                image[vtoc + i] = 0x00;

            for (int sector = 0; sector < options.SectorsPerVolume; sector++)
            {
                if (used[sector])
                    continue;

                // The map is four bytes per track, and the bit that says whether
                // a sector of that track is free is counted from the start of
                // the track, not from the start of the volume.
                int track = sector / options.SectorsPerTrack;
                int bit = sector % options.SectorsPerTrack;
                image[vtoc + FreeBitmapAt + track * 4 + bit / 8] |= (byte)(1 << (bit % 8));
            }
        }

        private static string Name(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "UNTITLED";
            string upper = name.ToUpperInvariant();
            return upper.Length <= 30 ? upper : upper.Substring(0, 30);
        }

        /// <summary>
        /// Scatters the sectors of the image through the skew. The default skew
        /// is the identity, so this copies and a caller can compare the result
        /// with what DOS reads on an emulator that does not re-skew.
        /// </summary>
        private static byte[] ApplySkew(byte[] image, AppleDosVolumeOptions options)
        {
            byte[] skew = options.Skew;
            if (skew == null || skew.Length != options.SectorsPerTrack)
                return image;

            bool identity = true;
            for (int i = 0; i < skew.Length; i++)
            {
                if (skew[i] != i)
                {
                    identity = false;
                    break;
                }
            }
            if (identity)
                return image;

            byte[] scattered = new byte[image.Length];
            for (int track = 0; track < options.Tracks; track++)
            {
                for (int sector = 0; sector < options.SectorsPerTrack; sector++)
                {
                    int from = (track * options.SectorsPerTrack + sector) * SectorSize;
                    int to = (track * options.SectorsPerTrack + skew[sector]) * SectorSize;
                    Array.Copy(image, from, scattered, to, SectorSize);
                }
            }
            return scattered;
        }
    }

    /// <summary>
    /// A BASIC program as a DOS file. A type A file, Applesoft, begins with its
    /// own length in two bytes, because DOS hands the interpreter a block and
    /// not a program; a type B file begins with the address it loads at.
    /// </summary>
    public static class AppleDosProgram
    {
        public const int Text = 0x00;
        public const int Integer = 0x01;
        public const int Applesoft = 0x02;
        public const int Binary = 0x04;

        public static byte[] ApplesoftFile(byte[] program)
        {
            byte[] data = program ?? new byte[0];
            List<byte> file = new List<byte>(data.Length + 2);
            file.Add((byte)(data.Length & 0xFF));
            file.Add((byte)((data.Length >> 8) & 0xFF));
            file.AddRange(data);
            return file.ToArray();
        }

        public static byte[] BinaryFile(int loadAddress, byte[] payload)
        {
            byte[] data = payload ?? new byte[0];
            List<byte> file = new List<byte>(data.Length + 2);
            file.Add((byte)(loadAddress & 0xFF));
            file.Add((byte)((loadAddress >> 8) & 0xFF));
            file.AddRange(data);
            return file.ToArray();
        }
    }

    /// <summary>
    /// DOS order as a target format: the payload is a file, and the file is the
    /// only one on a fresh volume of the version asked for.
    /// </summary>
    public sealed class AppleDosFormat : IExecutableFormat
    {
        private readonly AppleDosVersion _version;

        public AppleDosFormat(AppleDosVersion version)
        {
            _version = version;
        }

        public string Name
        {
            get { return _version == AppleDosVersion.Dos32 ? "dos32" : "dos33"; }
        }

        public Core.OperationResult Write(string path, byte[] payload, ResolvedTarget target)
        {
            byte[] data = payload ?? new byte[0];
            AppleDosVolumeOptions options = new AppleDosVolumeOptions(_version);
            options.FileType = AppleDosProgram.Binary;

            int load = 0x0803;
            if (target != null)
            {
                if (target.OriginAddress.HasValue)
                    load = target.OriginAddress.Value;
                else if (target.LoadAddress.HasValue)
                    load = target.LoadAddress.Value;
            }

            byte[] image = AppleDosVolume.Build(AppleDosProgram.BinaryFile(load, data), options);
            return ExecutableFile.WriteBytes(path, image);
        }
    }
}
