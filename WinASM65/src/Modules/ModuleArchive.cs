// WinASM65 - Module archives (.w65a)
//
// Layout, normative in docs/format-module.md:
//
//   [ header            20 bytes ]
//   [ member table              ]   name + absolute offset + length
//   [ reference table           ]   paths of the archives this one pulls in
//   [ member blobs              ]   each one a complete .w65 module
//
// The symbol index is not stored. It is the union of the members' export
// tables, computed when the archive is opened. A stored index would be a
// second copy of facts that already live in the members, and the two could
// disagree; nothing here can hold a stale duplicate.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WinASM65.Core;
using WinASM65.Output;
using WinASM65.Targets;

namespace WinASM65.Modules
{
    /// <summary>One .w65 module held by an archive.</summary>
    public sealed class ArchiveMember
    {
        public string Name { get; internal set; }
        public ModuleImage Module { get; internal set; }

        public ArchiveMember()
        {
            Name = string.Empty;
        }

        public ArchiveMember(string name, ModuleImage module)
        {
            Name = name ?? string.Empty;
            Module = module;
        }
    }

    /// <summary>One exported name, and where it was found.</summary>
    public sealed class ArchiveSymbol
    {
        public string Name { get; internal set; }
        public string ArchivePath { get; internal set; }
        public string MemberName { get; internal set; }
        public ModuleExport Export { get; internal set; }
    }

    public sealed class ModuleArchive
    {
        /// <summary>Where this archive was read from, or the path it will be written to.</summary>
        public string Path { get; set; }

        public List<ArchiveMember> Members { get; private set; }
        public List<string> References { get; private set; }

        public ModuleArchive()
        {
            Path = string.Empty;
            Members = new List<ArchiveMember>();
            References = new List<string>();
        }

        public string Name
        {
            get
            {
                return string.IsNullOrEmpty(Path)
                    ? string.Empty
                    : System.IO.Path.GetFileNameWithoutExtension(Path);
            }
        }
    }

    /// <summary>
    /// The whole set of modules an archive needs, once its references have been
    /// followed. Archives come before the ones that need them.
    /// </summary>
    public sealed class ArchiveResolution
    {
        public List<ModuleArchive> Archives { get; private set; }
        public List<ArchiveSymbol> Symbols { get; private set; }

        public ArchiveResolution()
        {
            Archives = new List<ModuleArchive>();
            Symbols = new List<ArchiveSymbol>();
        }
    }

    public static class ArchiveFormat
    {
        /// <summary>"W65A" as four bytes. Distinct from the "W65" module magic.</summary>
        public static readonly byte[] Magic = { (byte)'W', (byte)'6', (byte)'5', (byte)'A' };

        public const int HeaderSize = 20;

        /// <summary>Magic plus the two version fields. The three length and count
        /// fields follow, so a reader starts its header parsing here.</summary>
        public const int VersionSize = 8;
        public const ushort VersionMajor = 1;
        public const ushort VersionMinor = 0;

        public static bool HasMagic(byte[] data)
        {
            if (data == null || data.Length < 4)
                return false;
            return data[0] == Magic[0] && data[1] == Magic[1] && data[2] == Magic[2] && data[3] == Magic[3];
        }

        // ---------------------------------------------------------------- write

        /// <summary>
        /// Writes the archive. Member names default to the module's own name when
        /// it has one, so an archive built from modules needs no naming of its own.
        /// </summary>
        public static OperationResult Write(string path, ModuleArchive archive)
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            if (archive == null)
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0),
                    "Cannot write an archive: none was given."));
                return new OperationResult(false, diagnostics);
            }

            try
            {
                byte[] file = Serialize(archive);
                return ExecutableFile.WriteBytes(path, file);
            }
            catch (IOException ex)
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0),
                    "Cannot write archive '" + path + "': " + ex.Message));
                return new OperationResult(false, diagnostics);
            }
        }

        public static byte[] Serialize(ModuleArchive archive)
        {
            List<byte[]> blobs = new List<byte[]>();
            List<string> names = new List<string>();
            for (int i = 0; i < archive.Members.Count; i++)
            {
                ArchiveMember member = archive.Members[i];
                string name = member.Name;
                if (string.IsNullOrEmpty(name) && member.Module != null)
                    name = member.Module.ModuleName;
                names.Add(string.IsNullOrEmpty(name) ? "module" + i : name);
                blobs.Add(W65Format.Serialize(member.Module));
            }

            // The blobs go last, so the member table can carry absolute offsets.
            // Their base is only known once the tables are sized, so the tables are
            // measured first and written a second time with the real offsets. The
            // table length does not depend on the offset values, so the second pass
            // lands exactly where the first one predicted.
            MemoryStream measure = new MemoryStream();
            WriteMemberTable(measure, names, null, LengthsOf(blobs));
            WriteReferenceTable(measure, archive.References);
            uint tablesLength = (uint)measure.Length;
            uint payloadBase = (uint)(HeaderSize + tablesLength);

            uint[] offsets = new uint[blobs.Count];
            MemoryStream payload = new MemoryStream();
            for (int i = 0; i < blobs.Count; i++)
            {
                offsets[i] = payloadBase + (uint)payload.Position;
                payload.Write(blobs[i], 0, blobs[i].Length);
            }

            MemoryStream tables = new MemoryStream();
            WriteMemberTable(tables, names, offsets, LengthsOf(blobs));
            WriteReferenceTable(tables, archive.References);
            byte[] tableBytes = tables.ToArray();
            byte[] payloadBytes = payload.ToArray();

            MemoryStream file = new MemoryStream();
            file.Write(Magic, 0, 4);
            WriteU16(file, VersionMajor);
            WriteU16(file, VersionMinor);
            WriteU32(file, (uint)blobs.Count);
            WriteU32(file, (uint)tableBytes.Length);
            WriteU32(file, (uint)(HeaderSize + tableBytes.Length));
            file.Write(tableBytes, 0, tableBytes.Length);
            file.Write(payloadBytes, 0, payloadBytes.Length);
            return file.ToArray();
        }

        private static uint[] LengthsOf(List<byte[]> blobs)
        {
            uint[] lengths = new uint[blobs.Count];
            for (int i = 0; i < blobs.Count; i++)
                lengths[i] = (uint)blobs[i].Length;
            return lengths;
        }

        private static void WriteMemberTable(Stream stream, List<string> names, uint[] offsets, uint[] lengths)
        {
            // No count here: the member count lives in the header and governs the
            // walk. Writing it in both places would be a second copy of a fact the
            // reader could disagree with.
            for (int i = 0; i < names.Count; i++)
            {
                WriteString(stream, names[i]);
                // The length is a fixed width, so the measuring pass already knows
                // it and can carry the real value. The offset is the only field
                // that is not yet known, and it is zero in that pass.
                WriteU32(stream, offsets == null ? 0u : offsets[i]);
                WriteU32(stream, lengths[i]);
            }
        }

        private static void WriteReferenceTable(Stream stream, IReadOnlyList<string> references)
        {
            WriteU32(stream, (uint)references.Count);
            for (int i = 0; i < references.Count; i++)
                WriteString(stream, references[i]);
        }

        // ----------------------------------------------------------------- read

        public static OperationResult TryRead(string path, out ModuleArchive archive)
        {
            archive = null;
            List<Diagnostic> diagnostics = new List<Diagnostic>();

            byte[] data;
            try
            {
                data = File.ReadAllBytes(path);
            }
            catch (IOException ex)
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(path, 0),
                    "Cannot read archive '" + path + "': " + ex.Message));
                return new OperationResult(false, diagnostics);
            }

            if (!HasMagic(data))
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(path, 0),
                    "'" + path + "' is not a .w65a archive (bad magic)."));
                return new OperationResult(false, diagnostics);
            }

            if (data.Length < HeaderSize)
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(path, 0),
                    "'" + path + "' is a truncated .w65a archive."));
                return new OperationResult(false, diagnostics);
            }

            ModuleArchive result = new ModuleArchive();
            result.Path = path;

            try
            {
                // The member count is a header field, so the walk starts just after
                // the magic and the two version fields, not at the end of the
                // header: reading it from HeaderSize would read the first member's
                // name length as if it were the count.
                int cursor = VersionSize;
                uint memberCount = ReadU32(data, ref cursor);
                ReadU32(data, ref cursor);            // table length, the cursor already tracks it
                ReadU32(data, ref cursor);            // payload offset, likewise

                List<string> names = new List<string>();
                List<uint> offsets = new List<uint>();
                List<uint> lengths = new List<uint>();
                for (uint i = 0; i < memberCount; i++)
                {
                    names.Add(ReadString(data, ref cursor));
                    offsets.Add(ReadU32(data, ref cursor));
                    lengths.Add(ReadU32(data, ref cursor));
                }

                uint referenceCount = ReadU32(data, ref cursor);
                for (uint i = 0; i < referenceCount; i++)
                    result.References.Add(ReadString(data, ref cursor));

                for (int i = 0; i < names.Count; i++)
                {
                    if ((ulong)offsets[i] + lengths[i] > (ulong)data.Length)
                    {
                        diagnostics.Add(new Diagnostic(new SourceLocation(path, 0),
                            "'" + path + "' is a corrupt .w65a archive: member '" + names[i]
                            + "' runs past the end of the file."));
                        archive = null;
                        return new OperationResult(false, diagnostics);
                    }

                    byte[] blob = new byte[lengths[i]];
                    Array.Copy(data, (int)offsets[i], blob, 0, (int)lengths[i]);

                    ModuleImage module;
                    string moduleName;
                    OperationResult read = W65Format.TryRead(blob, names[i], out module, out moduleName);
                    if (!read.Success)
                    {
                        diagnostics.AddRange(read.Diagnostics);
                        archive = null;
                        return new OperationResult(false, diagnostics);
                    }

                    ArchiveMember member = new ArchiveMember();
                    member.Name = names[i];
                    member.Module = module;
                    result.Members.Add(member);
                }

                archive = result;
                return new OperationResult(true, diagnostics);
            }
            catch (ArgumentOutOfRangeException)
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(path, 0),
                    "'" + path + "' is a corrupt .w65a archive: a table runs past the end of the file."));
                archive = null;
                return new OperationResult(false, diagnostics);
            }
            catch (IndexOutOfRangeException)
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(path, 0),
                    "'" + path + "' is a corrupt .w65a archive."));
                archive = null;
                return new OperationResult(false, diagnostics);
            }
        }

        // ------------------------------------------------------------- resolving

        /// <summary>
        /// Loads an archive and every archive it references, transitively.
        /// <para>
        /// A reference is a path relative to the archive that names it, so a
        /// library can be moved with its dependents as long as their relative
        /// positions hold.
        /// </para>
        /// <para>
        /// A reference cycle is reported as an error. It does not make resolution
        /// impossible, because each archive is only expanded once, but it is
        /// always a mistake in the build setup and saying nothing about it hides
        /// it. The message names the whole cycle, because with three or more
        /// archives the pair that closes it is not obvious.
        /// </para>
        /// <para>
        /// Archives are returned in the order they must be linked: a referenced
        /// archive comes before the archive that needs it.
        /// </para>
        /// </summary>
        public static OperationResult Resolve(string path, out ArchiveResolution resolution)
        {
            resolution = new ArchiveResolution();
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            List<string> stack = new List<string>();
            List<string> done = new List<string>();

            if (!Expand(path, stack, done, resolution, diagnostics))
            {
                resolution = new ArchiveResolution();
                return new OperationResult(false, diagnostics);
            }

            OperationResult index = BuildSymbolIndex(resolution);
            diagnostics.AddRange(index.Diagnostics);
            return new OperationResult(index.Success, diagnostics);
        }

        private static bool Expand(string path, List<string> stack, List<string> done,
            ArchiveResolution resolution, List<Diagnostic> diagnostics)
        {
            string full = SafeFullPath(path);

            int onStack = stack.IndexOf(full);
            if (onStack >= 0)
            {
                // Close the loop explicitly: reporting only the pair would hide
                // which archive in a longer chain is the one that repeats.
                string chain = string.Empty;
                for (int i = onStack; i < stack.Count; i++)
                    chain += (chain.Length == 0 ? string.Empty : " -> ") + Display(stack[i]);
                chain += " -> " + Display(full);

                diagnostics.Add(new Diagnostic(new SourceLocation(path, 0),
                    "Archive reference cycle: " + chain + "."));
                return false;
            }

            if (done.Contains(full))
                return true;

            ModuleArchive archive;
            OperationResult read = TryRead(path, out archive);
            if (!read.Success)
            {
                diagnostics.AddRange(read.Diagnostics);
                return false;
            }

            string directory = string.IsNullOrEmpty(Path.GetDirectoryName(SafeFullPath(path)))
                ? string.Empty
                : Path.GetDirectoryName(SafeFullPath(path));

            stack.Add(full);
            try
            {
                for (int i = 0; i < archive.References.Count; i++)
                {
                    string reference = archive.References[i];
                    string resolved = string.IsNullOrEmpty(reference) || Path.IsPathRooted(reference)
                        ? reference
                        : Path.Combine(directory, reference);

                    if (!Expand(resolved, stack, done, resolution, diagnostics))
                        return false;
                }
            }
            finally
            {
                stack.RemoveAt(stack.Count - 1);
            }

            done.Add(full);
            resolution.Archives.Add(archive);
            return true;
        }

        /// <summary>
        /// The symbol index over every module in the resolution.
        /// <para>
        /// A name exported twice is an error, not a warning: nothing in the
        /// resolution says which of the two an importing module should have meant,
        /// and picking one silently would link a call to the wrong routine. Both
        /// providers are named, since "duplicate symbol X" alone leaves the
        /// reader to guess which archive to open.
        /// </para>
        /// </summary>
        public static OperationResult BuildSymbolIndex(ArchiveResolution resolution)
        {
            return BuildSymbolIndexInto(resolution, null, null);
        }

        /// <summary>
        /// Builds the symbol index of one archive into a resolution, without the
        /// caller having to fill in <see cref="ArchiveResolution.Archives"/>
        /// first. The viewer shows a single archive on its own, and it wants the
        /// same duplicate detection the resolver does, not a second looser pass.
        /// </summary>
        public static OperationResult BuildSymbolIndexInto(ArchiveResolution resolution,
            ModuleArchive only, List<Diagnostic> diagnostics)
        {
            if (resolution == null)
                return new OperationResult(false, new List<Diagnostic>());

            if (diagnostics == null)
                diagnostics = new List<Diagnostic>();
            else
                diagnostics.Clear();

            if (only != null && resolution.Archives.Count == 0)
                resolution.Archives.Add(only);

            Dictionary<string, ArchiveSymbol> seen = new Dictionary<string, ArchiveSymbol>(StringComparer.Ordinal);

            for (int a = 0; a < resolution.Archives.Count; a++)
            {
                ModuleArchive archive = resolution.Archives[a];
                for (int m = 0; m < archive.Members.Count; m++)
                {
                    ArchiveMember member = archive.Members[m];
                    if (member.Module == null)
                        continue;

                    for (int e = 0; e < member.Module.Exports.Count; e++)
                    {
                        ModuleExport export = member.Module.Exports[e];
                        ArchiveSymbol symbol = new ArchiveSymbol();
                        symbol.Name = export.Name;
                        symbol.ArchivePath = archive.Path;
                        symbol.MemberName = member.Name;
                        symbol.Export = export;

                        ArchiveSymbol previous;
                        if (seen.TryGetValue(export.Name, out previous))
                        {
                            diagnostics.Add(new Diagnostic(new SourceLocation(archive.Path, 0),
                                "Symbol '" + export.Name + "' is exported by both "
                                + previous.ArchivePath + " (" + previous.MemberName + ") and "
                                + archive.Path + " (" + member.Name + ")."));
                            continue;
                        }

                        seen.Add(export.Name, symbol);
                        resolution.Symbols.Add(symbol);
                    }
                }
            }

            return new OperationResult(diagnostics.Count == 0, diagnostics);
        }

        private static string SafeFullPath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (ArgumentException)
            {
                return path;
            }
            catch (NotSupportedException)
            {
                return path;
            }
        }

        private static string Display(string fullPath)
        {
            string name = Path.GetFileName(fullPath);
            return string.IsNullOrEmpty(name) ? fullPath : name;
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
