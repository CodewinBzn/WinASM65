// WinASM65 - Reader and writer for the .w65 module object format
//
// Layout, normative in docs/format-module.md:
//
//   [ header    16 bytes ]
//   [ segment table   ]
//   [ export table    ]
//   [ import table    ]
//   [ relocation table]
//   [ segment data    ]
//
// The magic is W65, never o65: this is not a cc65 O65 object, and confusing
// the two would be worse than having no format at all.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WinASM65.Core;
using WinASM65.Output;
using WinASM65.Targets;

namespace WinASM65.Modules
{
    /// <summary>Writes and reads the .w65 container.</summary>
    public static class W65Format
    {
        /// <summary>"W65" as three bytes. Little-endian readers see 0x353657.</summary>
        public static readonly byte[] Magic = { (byte)'W', (byte)'6', (byte)'5' };

        public const int HeaderSize = 16;
        public const ushort VersionMajor = 1;
        public const ushort VersionMinor = 0;

        public const byte RelocAbs16 = 2;
        public const byte RelocImm8 = 3;
        public const byte RelocRel8 = 4;
        public const byte RelocData8 = 5;
        public const byte RelocData16 = 6;
        public const byte RelocSeg = 7;
        public const byte RelocZp8 = 1;

        private static readonly byte[] BssBank = { 0xFF };

        /// <summary>True when the buffer starts with the .w65 magic.</summary>
        public static bool HasMagic(byte[] data)
        {
            if (data == null || data.Length < 3)
                return false;
            return data[0] == Magic[0] && data[1] == Magic[1] && data[2] == Magic[2];
        }

        // ---------------------------------------------------------------- write

        public static OperationResult Write(string path, ModuleImage image)
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            if (image == null)
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0),
                    "Cannot write a .w65 module: no module was produced."));
                return new OperationResult(false, diagnostics);
            }

            try
            {
                byte[] file = Serialize(image);
                return ExecutableFile.WriteBytes(path, file);
            }
            catch (IOException ex)
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0),
                    "Cannot write module '" + path + "': " + ex.Message));
                return new OperationResult(false, diagnostics);
            }
        }

        public static byte[] Serialize(ModuleImage image)
        {
            // The segment payloads go last, so the tables can carry where each one
            // landed. Those offsets are absolute, measured from the start of the
            // file, because that is how the reader addresses them: a payload
            // relative offset silently points into the header instead.
            //
            // The tables are therefore written twice. The first pass carries zero
            // offsets and only establishes the table length, since the length does
            // not depend on the offset values; the second pass carries the real
            // ones. Laying the payload out first, as this did before, cannot work:
            // the payload base is only known once the tables are sized.
            List<ModuleSegment> segments = new List<ModuleSegment>(image.Segments);

            MemoryStream measure = new MemoryStream();
            WriteSegmentTable(measure, segments);
            WriteExportTable(measure, image.Exports);
            WriteImportTable(measure, image.Imports);
            WriteRelocationTable(measure, image.Relocations);
            uint tablesLength = (uint)measure.Length;
            uint payloadBase = (uint)(HeaderSize + tablesLength);

            MemoryStream payload = new MemoryStream();
            foreach (ModuleSegment segment in segments)
            {
                if (segment.Kind == SegmentKind.Bss || segment.Data.Length == 0)
                {
                    segment.FileOffset = 0;
                    continue;
                }
                segment.FileOffset = payloadBase + (uint)payload.Position;
                payload.Write(segment.Data, 0, segment.Data.Length);
            }

            MemoryStream body = new MemoryStream();
            WriteSegmentTable(body, segments);
            WriteExportTable(body, image.Exports);
            WriteImportTable(body, image.Imports);
            WriteRelocationTable(body, image.Relocations);
            byte[] tables = body.ToArray();
            byte[] bytes = payload.ToArray();

            MemoryStream file = new MemoryStream();
            file.Write(Magic, 0, 3);
            file.WriteByte(0x00);
            WriteU16(file, VersionMajor);
            WriteU16(file, VersionMinor);
            WriteU32(file, (uint)tables.Length);
            WriteU32(file, (uint)(HeaderSize + tables.Length));
            file.Write(tables, 0, tables.Length);
            file.Write(bytes, 0, bytes.Length);
            return file.ToArray();
        }

        private static void WriteSegmentTable(Stream stream, List<ModuleSegment> segments)
        {
            WriteU32(stream, (uint)segments.Count);
            for (int i = 0; i < segments.Count; i++)
            {
                ModuleSegment segment = segments[i];
                WriteString(stream, segment.Name);
                WriteU32(stream, segment.OccupiedSize);
                WriteU32(stream, segment.Alignment);
                stream.WriteByte((byte)segment.Kind);
                stream.WriteByte(segment.Kind == SegmentKind.Bss ? BssBank[0] : segment.Bank);
                WriteU32(stream, segment.FileOffset);
                // The origin the unit was written against. It is not a placement:
                // it is the address the linker falls back to when nothing else
                // constrains the segment. Without it the linker has no idea where
                // a segment meant to live and silently places it at a default.
                WriteU16(stream, segment.OriginAddress);
            }
        }

        private static void WriteExportTable(Stream stream, IReadOnlyList<ModuleExport> exports)
        {
            WriteU32(stream, (uint)exports.Count);
            for (int i = 0; i < exports.Count; i++)
            {
                WriteString(stream, exports[i].Name);
                WriteU32(stream, (uint)exports[i].SegmentIndex);
                WriteU32(stream, exports[i].Offset);
            }
        }

        private static void WriteImportTable(Stream stream, IReadOnlyList<ModuleImport> imports)
        {
            WriteU32(stream, (uint)imports.Count);
            for (int i = 0; i < imports.Count; i++)
            {
                WriteString(stream, imports[i].Name);
                WriteString(stream, imports[i].ModuleName);
            }
        }

        private static void WriteRelocationTable(Stream stream, IReadOnlyList<RelocationRecord> relocations)
        {
            WriteU32(stream, (uint)relocations.Count);
            for (int i = 0; i < relocations.Count; i++)
            {
                RelocationRecord record = relocations[i];
                WriteU32(stream, (uint)record.SegmentIndex);
                WriteU32(stream, (uint)record.Offset);
                stream.WriteByte(record.Width);
                stream.WriteByte((byte)record.Type);
                WriteU16(stream, record.Address);
                WriteString(stream, record.TargetSymbol ?? string.Empty);
                WriteString(stream, record.SourceFile);
                WriteU32(stream, (uint)Math.Max(0, record.SourceLine));
            }
        }

        // ----------------------------------------------------------------- read

        /// <summary>Reads a .w65 file. Returns null and a diagnostic if the file is not one.</summary>
        public static OperationResult TryRead(string path, out ModuleImage image, out string moduleName)
        {
            image = null;
            moduleName = string.Empty;
            List<Diagnostic> diagnostics = new List<Diagnostic>();

            byte[] data;
            try
            {
                data = File.ReadAllBytes(path);
            }
            catch (IOException ex)
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(path, 0),
                    "Cannot read module '" + path + "': " + ex.Message));
                return new OperationResult(false, diagnostics);
            }

            if (!HasMagic(data))
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(path, 0),
                    "'" + path + "' is not a .w65 module (bad magic)."));
                return new OperationResult(false, diagnostics);
            }

            if (data.Length < HeaderSize)
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(path, 0),
                    "'" + path + "' is a truncated .w65 module."));
                return new OperationResult(false, diagnostics);
            }

            try
            {
                int cursor = HeaderSize;
                ModuleImage result = new ModuleImage();

                uint segmentCount = ReadU32(data, ref cursor);
                List<ModuleSegment> segments = new List<ModuleSegment>();
                for (uint i = 0; i < segmentCount; i++)
                {
                    string name = ReadString(data, ref cursor);
                    uint size = ReadU32(data, ref cursor);
                    uint alignment = ReadU32(data, ref cursor);
                    SegmentKind kind = (SegmentKind)data[cursor++];
                    byte bank = data[cursor++];
                    uint fileOffset = ReadU32(data, ref cursor);
                    ushort origin = ReadU16(data, ref cursor);

                    byte[] content = new byte[size];
                    if (kind != SegmentKind.Bss && size > 0)
                        Array.Copy(data, fileOffset, content, 0, (int)size);
                    ModuleSegment segment = new ModuleSegment(name, content, kind, alignment, bank);
                    segment.OriginAddress = origin;
                    // The data has been copied out already, so this offset no longer
                    // addresses anything live. It is kept because it says where the
                    // bytes came from, which is what a diagnostic about a corrupt
                    // module needs to point at.
                    segment.FileOffset = fileOffset;
                    segments.Add(segment);
                }
                for (int i = 0; i < segments.Count; i++)
                    result.AddSegment(segments[i]);

                uint exportCount = ReadU32(data, ref cursor);
                for (uint i = 0; i < exportCount; i++)
                {
                    string name = ReadString(data, ref cursor);
                    uint segment = ReadU32(data, ref cursor);
                    uint offset = ReadU32(data, ref cursor);
                    result.AddExport(new ModuleExport(name, (int)segment, offset));
                }

                uint importCount = ReadU32(data, ref cursor);
                for (uint i = 0; i < importCount; i++)
                {
                    string name = ReadString(data, ref cursor);
                    string module = ReadString(data, ref cursor);
                    result.AddImport(new ModuleImport(name, module));
                }

                uint relocCount = ReadU32(data, ref cursor);
                for (uint i = 0; i < relocCount; i++)
                {
                    uint segment = ReadU32(data, ref cursor);
                    uint offset = ReadU32(data, ref cursor);
                    byte width = data[cursor++];
                    byte type = data[cursor++];
                    ushort address = ReadU16(data, ref cursor);
                    string target = ReadString(data, ref cursor);
                    string sourceFile = ReadString(data, ref cursor);
                    uint sourceLine = ReadU32(data, ref cursor);

                    result.AddRelocation(new RelocationRecord(
                        (int)segment, string.Empty, (int)offset, address, width,
                        (RelocationType)type,
                        string.IsNullOrEmpty(target) ? new List<string>() : new List<string> { target },
                        new SourceLocation(sourceFile, (int)sourceLine), string.Empty));
                }

                image = result;
                moduleName = Path.GetFileNameWithoutExtension(path);
                return new OperationResult(true, diagnostics);
            }
            catch (ArgumentOutOfRangeException)
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(path, 0),
                    "'" + path + "' is a corrupt .w65 module: a table runs past the end of the file."));
                image = null;
                return new OperationResult(false, diagnostics);
            }
            catch (IndexOutOfRangeException)
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(path, 0),
                    "'" + path + "' is a corrupt .w65 module."));
                image = null;
                return new OperationResult(false, diagnostics);
            }
        }

        // ------------------------------------------------------------- primitives

        private static void WriteU16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
        }

        private static void WriteU32(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
            stream.WriteByte((byte)((value >> 16) & 0xFF));
            stream.WriteByte((byte)((value >> 24) & 0xFF));
        }

        private static void WriteString(Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            WriteU32(stream, (uint)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static ushort ReadU16(byte[] data, ref int cursor)
        {
            ushort value = (ushort)(data[cursor] | (data[cursor + 1] << 8));
            cursor += 2;
            return value;
        }

        private static uint ReadU32(byte[] data, ref int cursor)
        {
            uint value = (uint)(data[cursor]
                | (data[cursor + 1] << 8)
                | (data[cursor + 2] << 16)
                | (data[cursor + 3] << 24));
            cursor += 4;
            return value;
        }

        private static string ReadString(byte[] data, ref int cursor)
        {
            uint length = ReadU32(data, ref cursor);
            if (length == 0)
                return string.Empty;
            string value = Encoding.UTF8.GetString(data, cursor, (int)length);
            cursor += (int)length;
            return value;
        }
    }
}
