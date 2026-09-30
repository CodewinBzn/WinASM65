// WinASM65 - GEOS application files and D64 disk images
//
// Layouts, normative in docs/geos-d64.md and taken from the GEOS Programmer's
// Reference Guide rather than guessed:
//
//   Directory entry, 32 bytes:
//     $00-$01  t/s of the next entry block, $00 for the last
//     $02      C64 file type, bottom three bits must be 0, 1 or 2
//     $03-$04  t/s of the file, or of the single RECORD sector when GEOS
//              file type is not $00
//     $05-$14  name, 16 bytes, padded with $A0
//     $15-$16  t/s of the info block
//     $17      GEOS file structure: $00 sequential, $01 VLIR
//     $18      GEOS file type, $06 for an application
//     $19-$1D  timestamp: year as 1900+, month, day, hour, minute
//     $1E-$1F  size in sectors, low byte first
//
//   INFO block, one sector:
//     $00-$01  $00/$FF, the block is one sector long so nothing follows it
//     $02-$04  icon width $03, height $15, and $BF
//     $05-$43  icon bitmap, 63 bytes, sprite format
//     $44-$46  C64 type, GEOS type, structure: the same as the directory entry
//     $47-$48  load address
//     $49-$4A  end address
//     $4B-$4C  start address
//     $4D-$60  class text, $00 terminated
//     $61-$74  author, $00 terminated
//     $75-$88  name of the application that created a document
//     $89-$9F  free for applications
//     $A0-$FF  description, $00 terminated
//
//   VLIR RECORD sector, one sector:
//     $00-$01  $00/$FF
//     $02-     track/sector pairs, one per record. $00/$00 ends the list,
//              $00/$FF marks a record that is not present.
//
//   BAM sector 18/0:
//     $00-$01  t/s of the first directory sector
//     $02      $41, the 1541
//     $03      $2A, the DOS version
//     $04-$8F  35 tracks of 4 bytes, starting at track 1
//     $90-$9F  disk name
//     $A0-$A1  $A0 $A0
//     $A2-$A3  disk ID
//     $A4      $A0
//     $A5-$A6  "2A"
//     $A7-$AA  $A0 x4
//     $AB-$AC  border sector t/s
//     $AD-$BC  GEOS signature, "GEOS format V1.0"
//     $BD-$FF  unused
//
// A four byte BAM group is a free count in its low seven bits, then the free
// bitmaps for sectors 0-7, 8-15 and 16-23. Bits 5 and 6 of the fourth byte
// carry bits 7 and 8 of the count, so a track can count past 127.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WinASM65.Core;
using WinASM65.Output;
using WinASM65.Targets;

namespace WinASM65.Targets
{
    /// <summary>GEOS file types, as stored in the directory entry at $18.</summary>
    public enum GeosFileType
    {
        NonGeos = 0x00,
        Basic = 0x01,
        Assembler = 0x02,
        Data = 0x03,
        System = 0x04,
        DeskAccessory = 0x05,
        Application = 0x06,
        ApplicationData = 0x07,
        Font = 0x08,
        PrinterDriver = 0x09,
        InputDriver = 0x0A,
        DiskDriver = 0x0B,
        SystemBoot = 0x0C,
        Temporary = 0x0D,
        AutoExecute = 0x0E
    }

    /// <summary>One loadable chain of a VLIR file.</summary>
    public sealed class GeosRecord
    {
        public byte[] Data { get; set; }

        public GeosRecord()
        {
            Data = new byte[0];
        }

        public GeosRecord(byte[] data)
        {
            Data = data ?? new byte[0];
        }
    }

    /// <summary>A GEOS application, as it will appear on the disk.</summary>
    public sealed class GeosApplication
    {
        public string Name { get; set; }
        public GeosFileType FileType { get; set; }

        /// <summary>
        /// The bottom three bits of the C64 file type. A value of 3 or more is
        /// REL and up, which GEOS does not allow, so only 0, 1 and 2 are legal
        /// here and the writer refuses the rest.
        /// </summary>
        public byte C64FileType { get; set; }

        public bool Vlir { get; set; }
        public ushort LoadAddress { get; set; }
        public ushort EndAddress { get; set; }
        public ushort StartAddress { get; set; }
        public byte[] Icon { get; set; }
        public string ClassText { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public DateTime Timestamp { get; set; }
        public List<GeosRecord> Records { get; private set; }

        public GeosApplication()
        {
            Name = "UNTITLED";
            FileType = GeosFileType.Application;
            C64FileType = 0x02;
            Vlir = true;
            Icon = new byte[0];
            ClassText = string.Empty;
            Author = string.Empty;
            Description = string.Empty;
            Timestamp = new DateTime(1900, 1, 1);
            Records = new List<GeosRecord>();
        }
    }

    /// <summary>
    /// Builds a 1541 disk image holding GEOS applications.
    /// </summary>
    public sealed class D64Builder
    {
        /// <summary>Sectors per track on a 1541, tracks 1 to 35.</summary>
        private static readonly int[] SectorsPerTrack =
        {
            0,
            21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21,
            19, 19, 19, 19, 19, 19, 19,
            18, 18, 18, 18, 18, 18,
            17, 17, 17, 17, 17
        };

        public const int TrackCount = 35;
        public const int SectorSize = 256;
        public const int FirstDirectoryTrack = 18;

        /// <summary>
        /// How far ahead to look for the next free sector on a track. Ten is the
        /// interleave most tools use; it only affects read speed, never whether
        /// the image is valid, since the drive follows the chain as written.
        /// </summary>
        public int Interleave { get; set; }

        public string DiskName { get; set; }
        public string DiskId { get; set; }
        public List<GeosApplication> Applications { get; private set; }

        /// <summary>When true the stamp is today's date rather than the fixed one.</summary>
        public bool UseCurrentDate { get; set; }

        public D64Builder()
        {
            Interleave = 10;
            DiskName = "GEOS DISK";
            DiskId = "01";
            Applications = new List<GeosApplication>();
        }

        // ------------------------------------------------------------ allocation

        /// <summary>A track and sector on the disk.</summary>
        private struct Location
        {
            public byte Track;
            public byte Sector;

            public Location(byte track, byte sector)
            {
                Track = track;
                Sector = sector;
            }

            public bool IsNull
            {
                get { return Track == 0 && Sector == 0; }
            }

            public override string ToString()
            {
                return Track + "/" + Sector;
            }
        }

        private sealed class Allocation
        {
            public bool[,] Free = new bool[TrackCount + 1, 32];
            public List<byte[]> Sectors = new List<byte[]>();

            /// <summary>
            /// Copied in from the builder: a nested class cannot reach the
            /// instance that owns it, and the allocation strategy belongs to the
            /// disk being built, not to the bookkeeping.
            /// </summary>
            private readonly int _interleave;

            public Allocation(int interleave)
            {
                _interleave = interleave;
                for (int track = 1; track <= TrackCount; track++)
                {
                    for (int sector = 0; sector < SectorsPerTrack[track]; sector++)
                        Free[track, sector] = true;
                }
                // Only the BAM sector starts life allocated. The rest of track 18
                // is left free on purpose: the directory goes there, and
                // reserving the whole track up front would leave nowhere for it
                // to go and it would land on a data track, where DOS would not
                // find it.
                Free[FirstDirectoryTrack, 0] = false;

                // One slot per sector on the disk, in track then sector order, so
                // that a sector can be written before it is read and so the final
                // image is a straight copy of this list.
                int total = 0;
                for (int track = 1; track <= TrackCount; track++)
                    total += SectorsPerTrack[track];
                for (int i = 0; i < total; i++)
                    Sectors.Add(null);
            }

            public byte[] Read(Location location)
            {
                return Sectors[SectorOffset(location)];
            }

            public void Write(Location location, byte[] data)
            {
                Sectors[SectorOffset(location)] = data;
            }

            public bool IsFree(Location location)
            {
                if (location.Track < 1 || location.Track > TrackCount)
                    return false;
                if (location.Sector >= SectorsPerTrack[location.Track])
                    return false;
                return Free[location.Track, location.Sector];
            }

            /// <summary>The 256 bytes of one sector, or null if nothing was written.</summary>
            public byte[] Content(int track, int sector)
            {
                return Sectors[SectorOffset(new Location((byte)track, (byte)sector))];
            }

            private int SectorOffset(Location location)
            {
                int offset = 0;
                for (int track = 1; track < location.Track; track++)
                    offset += SectorsPerTrack[track];
                return offset + location.Sector;
            }

            /// <summary>
            /// Reserves a sector of one specific track. The directory has to live
            /// on track 18, so it cannot be left to the interleaved search, which
            /// would put it wherever the first free sector happened to be.
            /// </summary>
            public Location AllocateOnTrack(int track, int fromSector)
            {
                int count = SectorsPerTrack[track];
                for (int sector = fromSector; sector < count; sector++)
                {
                    if (Free[track, sector])
                    {
                        Free[track, sector] = false;
                        return new Location((byte)track, (byte)sector);
                    }
                }

                throw new InvalidOperationException(
                    "Track " + track + " is full: the directory does not fit.");
            }

            /// <summary>
            /// Reserves a single sector, stepping forward by the interleave and
            /// then to the next track when the current one runs out. Starting
            /// after the given location is what spreads consecutive sectors of a
            /// file across the surface instead of stacking them.
            /// </summary>
            public Location Allocate(Location after)
            {
                int startTrack = after.Track < 1 || after.Track > TrackCount ? 1 : after.Track;
                int startSector = after.Sector;

                for (int pass = 0; pass < 2; pass++)
                {
                    for (int step = 0; step < TrackCount; step++)
                    {
                        int track = startTrack + step;
                        if (track > TrackCount)
                            track -= TrackCount;
                        if (pass == 1 && track <= startTrack)
                            break;

                        int count = SectorsPerTrack[track];
                        for (int hop = 1; hop <= count; hop++)
                        {
                            int sector = (startSector + hop * _interleave) % count;
                            if (sector < 0)
                                sector += count;
                            if (Free[track, sector])
                            {
                                Free[track, sector] = false;
                                return new Location((byte)track, (byte)sector);
                            }
                        }
                    }
                }

                throw new InvalidOperationException(
                    "The disk is full: no free sector left for another file.");
            }

            /// <summary>
            /// Writes a payload as a chain of 254 byte sectors. The first two
            /// bytes of every sector are the link to the next one, and the last
            /// sector of a chain holds $00 then how much of it is used, which is
            /// how the drive knows where the file ends.
            /// </summary>
            public Location WriteChain(byte[] payload, Location after)
            {
                if (payload == null || payload.Length == 0)
                {
                    Location only = Allocate(after);
                    byte[] empty = new byte[SectorSize];
                    empty[0] = 0x00;
                    empty[1] = (byte)(SectorSize - 2);
                    Write(only, empty);
                    return only;
                }

                int used = (payload.Length + 253) / 254;
                if (used < 1)
                    used = 1;

                Location[] chain = new Location[used];
                Location cursor = after;
                for (int i = 0; i < used; i++)
                {
                    cursor = Allocate(cursor);
                    chain[i] = cursor;
                }

                for (int i = 0; i < used; i++)
                {
                    byte[] sector = new byte[SectorSize];
                    int from = i * 254;
                    int count = Math.Min(254, payload.Length - from);
                    if (count < 0)
                        count = 0;
                    if (count > 0)
                        Array.Copy(payload, from, sector, 2, count);

                    if (i + 1 < used)
                    {
                        sector[0] = chain[i + 1].Track;
                        sector[1] = chain[i + 1].Sector;
                    }
                    else
                    {
                        sector[0] = 0x00;
                        sector[1] = (byte)(count == 0 ? 0 : count);
                    }

                    Write(chain[i], sector);
                }

                return chain[0];
            }

            public int ChainLength(Location first)
            {
                int count = 0;
                Location cursor = first;
                while (!cursor.IsNull && count < 4096)
                {
                    byte[] sector = Read(cursor);
                    count++;
                    if (sector[0] == 0)
                        break;
                    cursor = new Location(sector[0], sector[1]);
                }
                return count;
            }
        }

        // ---------------------------------------------------------------- output

        public OperationResult Write(string path)
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            try
            {
                byte[] image = Build(diagnostics);
                if (diagnostics.Count > 0)
                    return new OperationResult(false, diagnostics);
                return ExecutableFile.WriteBytes(path, image);
            }
            catch (InvalidOperationException ex)
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(path, 0), ex.Message));
                return new OperationResult(false, diagnostics);
            }
        }

        public byte[] Build(List<Diagnostic> diagnostics)
        {
            Allocation disk = new Allocation(Interleave);

            // The border sector is one sector with a directory sector's layout,
            // where a dragged file waits while it is being copied. GEOS looks for
            // it from track 19 upward, then from track 1.
            Location border = disk.Allocate(new Location(0, 0));
            byte[] borderSector = new byte[SectorSize];
            borderSector[0] = 0x00;
            borderSector[1] = (byte)(SectorSize - 1);
            disk.Write(border, borderSector);

            List<byte[]> entries = new List<byte[]>();
            foreach (GeosApplication application in Applications)
            {
                byte[] entry = BuildEntry(application, disk, diagnostics);
                if (entry != null)
                    entries.Add(entry);
            }

            // 32 bytes per entry, 8 entries per sector.
            Location[] directory = WriteDirectory(entries, disk);
            WriteBam(disk, border, directory);

            // A D64 is a 683 byte header followed by every sector of every track,
            // in track then sector order. The header holds one byte per sector
            // giving the track that sector is on, which is how a tool can seek
            // without a track map. Without it the file is 683 bytes short and
            // every offset is wrong.
            int total = 0;
            for (int track = 1; track <= TrackCount; track++)
                total += SectorsPerTrack[track];

            byte[] image = new byte[total + total * SectorSize];
            int header = 0;
            int body = total;
            for (int track = 1; track <= TrackCount; track++)
            {
                for (int sector = 0; sector < SectorsPerTrack[track]; sector++)
                {
                    image[header++] = (byte)track;
                    byte[] content = disk.Content(track, sector);
                    if (content != null)
                        Array.Copy(content, 0, image, body, SectorSize);
                    body += SectorSize;
                }
            }

            return image;
        }

        private byte[] BuildEntry(GeosApplication application, Allocation disk,
            List<Diagnostic> diagnostics)
        {
            if (application.C64FileType > 0x02)
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(application.Name, 0),
                    "GEOS file '" + application.Name + "' has C64 type $"
                    + application.C64FileType.ToString("X2")
                    + ". The bottom three bits must be 0, 1 or 2: 3 and above are "
                    + "REL files, which GEOS does not allow."));
                return null;
            }

            if (application.Name == null || application.Name.Length == 0)
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0),
                    "A GEOS file on the disk has no name."));
                return null;
            }

            // The info block comes first, then the record sector for a VLIR, then
            // the data chains. A file is written whole or not at all: leaving a
            // half written chain on the disk would make VALIDATE deallocate it.
            Location info = disk.Allocate(new Location(0, 0));
            disk.Write(info, BuildInfoBlock(application));

            int sectorCount = 1;
            Location data = new Location(0, 0);

            if (application.Vlir)
            {
                if (application.Records.Count == 0)
                {
                    diagnostics.Add(new Diagnostic(new SourceLocation(application.Name, 0),
                        "GEOS file '" + application.Name + "' is a VLIR with no record. "
                        + "A VLIR has to have at least one chain."));
                    return null;
                }
                if (application.Records.Count > 127)
                {
                    diagnostics.Add(new Diagnostic(new SourceLocation(application.Name, 0),
                        "GEOS file '" + application.Name + "' has "
                        + application.Records.Count + " records; the RECORD sector holds 127."));
                    return null;
                }

                Location[] chains = new Location[application.Records.Count];
                for (int i = 0; i < application.Records.Count; i++)
                {
                    data = disk.WriteChain(application.Records[i].Data, data);
                    chains[i] = data;
                    sectorCount += disk.ChainLength(data);
                }

                Location records = disk.Allocate(data);
                disk.Write(records, BuildRecordSector(chains));

                return AssembleEntry(application, info, records, sectorCount);
            }

            // Sequential: one chain for the whole file.
            byte[] payload = new byte[0];
            foreach (GeosRecord record in application.Records)
            {
                byte[] grown = new byte[payload.Length + record.Data.Length];
                Array.Copy(payload, grown, payload.Length);
                Array.Copy(record.Data, 0, grown, payload.Length, record.Data.Length);
                payload = grown;
            }

            data = disk.WriteChain(payload, data);
            sectorCount += disk.ChainLength(data);
            return AssembleEntry(application, info, data, sectorCount);
        }

        private static byte[] AssembleEntry(GeosApplication application, Location info,
            Location data, int sectorCount)
        {
            byte[] entry = new byte[32];
            // $00-$01 stay $00: the directory writer fills in the chain.
            entry[0x02] = application.C64FileType;
            entry[0x03] = data.Track;
            entry[0x04] = data.Sector;

            WritePaddedText(entry, 0x05, 16, application.Name);
            entry[0x15] = info.Track;
            entry[0x16] = info.Sector;
            entry[0x17] = application.Vlir ? (byte)0x01 : (byte)0x00;
            entry[0x18] = (byte)application.FileType;

            DateTime stamp = application.Timestamp;
            int year = stamp.Year - 1900;
            if (year < 0 || year > 255)
                year = 0;
            entry[0x19] = (byte)year;
            entry[0x1A] = (byte)stamp.Month;
            entry[0x1B] = (byte)stamp.Day;
            entry[0x1C] = (byte)stamp.Hour;
            entry[0x1D] = (byte)stamp.Minute;

            entry[0x1E] = (byte)(sectorCount & 0xFF);
            entry[0x1F] = (byte)((sectorCount >> 8) & 0xFF);
            return entry;
        }

        private byte[] BuildInfoBlock(GeosApplication application)
        {
            byte[] block = new byte[SectorSize];

            // $00-$01: one sector long, so the link is $00 then how much is used.
            block[0] = 0x00;
            block[1] = (byte)(SectorSize - 1);

            // $02-$04: icon width 3, height 21, and $BF. Height 21 is what makes
            // the bitmap 63 bytes, which is the only size that fits here.
            block[2] = 0x03;
            block[3] = 0x15;
            block[4] = 0xBF;

            byte[] icon = application.Icon ?? new byte[0];
            int iconLength = Math.Min(63, icon.Length);
            if (iconLength > 0)
                Array.Copy(icon, 0, block, 0x05, iconLength);

            block[0x44] = application.C64FileType;
            block[0x45] = (byte)application.FileType;
            block[0x46] = application.Vlir ? (byte)0x01 : (byte)0x00;

            WriteWord(block, 0x47, application.LoadAddress);
            WriteWord(block, 0x49, application.EndAddress);
            WriteWord(block, 0x4B, application.StartAddress);

            WriteTerminatedText(block, 0x4D, 0x60, application.ClassText);
            WriteTerminatedText(block, 0x61, 0x74, application.Author);
            WriteTerminatedText(block, 0xA0, 0xFF, application.Description);

            return block;
        }

        private static byte[] BuildRecordSector(Location[] chains)
        {
            byte[] block = new byte[SectorSize];
            block[0] = 0x00;
            block[1] = (byte)(SectorSize - 1);

            for (int i = 0; i < chains.Length && i < 127; i++)
            {
                block[2 + i * 2] = chains[i].Track;
                block[3 + i * 2] = chains[i].Sector;
            }

            // $00/$00 from here on is the end of the list. A chain that is not
            // present would be $00/$FF instead.
            return block;
        }

        private Location[] WriteDirectory(List<byte[]> entries, Allocation disk)
        {
            int needed = Math.Max(1, (entries.Count + 7) / 8);
            if (needed > SectorsPerTrack[FirstDirectoryTrack])
                throw new InvalidOperationException(
                    "The directory needs " + needed + " sectors but track "
                    + FirstDirectoryTrack + " only has "
                    + SectorsPerTrack[FirstDirectoryTrack]
                    + ". There is nowhere to put the entries.");

            // From sector 1: sector 0 of track 18 is the BAM.
            Location[] sectors = new Location[needed];
            for (int i = 0; i < needed; i++)
                sectors[i] = disk.AllocateOnTrack(FirstDirectoryTrack, i + 1);

            for (int s = 0; s < needed; s++)
            {
                byte[] block = new byte[SectorSize];
                if (s + 1 < needed)
                {
                    block[0] = sectors[s + 1].Track;
                    block[1] = sectors[s + 1].Sector;
                }
                else
                {
                    block[0] = 0x00;
                    block[1] = (byte)(SectorSize - 1);
                }

                for (int slot = 0; slot < 8; slot++)
                {
                    int index = s * 8 + slot;
                    if (index < entries.Count)
                        Array.Copy(entries[index], 0, block, slot * 32, 32);
                }

                disk.Write(sectors[s], block);
            }

            return sectors;
        }

        private void WriteBam(Allocation disk, Location border, Location[] directory)
        {
            Location bam = new Location(FirstDirectoryTrack, 0);
            byte[] block = new byte[SectorSize];

            block[0] = directory[0].Track;
            block[1] = directory[0].Sector;
            block[2] = 0x41;                 // the 1541
            block[3] = 0x2A;                 // DOS version

            for (int track = 1; track <= TrackCount; track++)
            {
                int count = SectorsPerTrack[track];
                int free = 0;
                byte low = 0, mid = 0, high = 0;
                for (int sector = 0; sector < count; sector++)
                {
                    if (disk.Free[track, sector])
                    {
                        free++;
                        if (sector < 8)
                            low |= (byte)(1 << sector);
                        else if (sector < 16)
                            mid |= (byte)(1 << (sector - 8));
                        else
                            high |= (byte)(1 << (sector - 16));
                    }
                }

                int at = 0x04 + (track - 1) * 4;
                block[at] = (byte)(free & 0x7F);
                block[at + 1] = low;
                block[at + 2] = mid;
                block[at + 3] = (byte)(high | (((free >> 7) & 1) << 5) | (((free >> 8) & 1) << 6));
            }

            WritePaddedText(block, 0x90, 16, DiskName);
            block[0xA0] = 0xA0;
            block[0xA1] = 0xA0;
            WritePaddedText(block, 0xA2, 2, DiskId);
            block[0xA4] = 0xA0;
            block[0xA5] = (byte)'2';
            block[0xA6] = (byte)'A';
            for (int i = 0; i < 4; i++)
                block[0xA7 + i] = 0xA0;

            block[0xAB] = border.Track;
            block[0xAC] = border.Sector;

            // The GEOS signature. GEOS itself decides whether a disk is one of
            // its own by looking for "GEOS format" at $AD, so this has to be
            // exactly that string and not merely a version marker.
            byte[] signature = Encoding.ASCII.GetBytes("GEOS format V1.0");
            for (int i = 0; i < 16; i++)
                block[0xAD + i] = i < signature.Length ? signature[i] : (byte)0xA0;

            disk.Write(bam, block);
        }

        // ------------------------------------------------------------- primitives

        private static void WriteWord(byte[] target, int at, ushort value)
        {
            target[at] = (byte)(value & 0xFF);
            target[at + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static void WritePaddedText(byte[] target, int at, int length, string value)
        {
            for (int i = 0; i < length; i++)
                target[at + i] = 0xA0;
            if (string.IsNullOrEmpty(value))
                return;
            for (int i = 0; i < length && i < value.Length; i++)
            {
                char c = value[i];
                target[at + i] = c >= 0x20 && c < 0x7F ? (byte)c : (byte)0x3F;
            }
        }

        private static void WriteTerminatedText(byte[] target, int from, int to, string value)
        {
            for (int i = from; i <= to; i++)
                target[i] = 0x00;
            if (string.IsNullOrEmpty(value))
                return;
            int at = from;
            for (int i = 0; i < value.Length && at <= to; i++)
            {
                char c = value[i];
                target[at++] = c >= 0x20 && c < 0x7F ? (byte)c : (byte)0x3F;
            }
        }
    }
}
