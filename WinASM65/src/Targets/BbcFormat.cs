using System.Collections.Generic;
using System.Text;
using WinASM65.Core;

namespace WinASM65.Targets
{
    /// <summary>
    /// A BBC Micro file, in the one self-describing format the machine accepts:
    /// the code header.
    /// <para>
    /// A file on a filing system carries its load and execution addresses in a
    /// catalogue, not in itself. The code header exists for the case where it
    /// cannot: a file carried on a filesystem that has no such fields. It is a
    /// stub of code, a type byte saying which processor the code is for, a
    /// title, a copyright marker, and the load address.
    /// </para>
    /// <para>
    /// There is no length anywhere in it and no type byte that encodes one. A
    /// client that wants the length asks the filesystem. That is worth saying
    /// because a scheme where the first byte carries the kind and half the
    /// length is a natural thing to invent, and it is not this machine's.
    /// </para>
    /// </summary>
    public class BbcFormat : IExecutableFormat
    {
        /// <summary>The byte a text file starts with. Nothing else follows it.</summary>
        public const byte TextType = 0xFF;

        /// <summary>CPU ids of the type byte's low nibble.</summary>
        public const byte CpuBasic6502 = 0;
        public const byte CpuTurbo6502 = 1;
        public const byte Cpu6502 = 2;

        /// <summary>A file, not a ROM image, so no service entry.</summary>
        public const byte RomTypeFile = 0x60;

        /// <summary>Set with the load address present; clear means "load at $8000".</summary>
        public const byte RomTypeHasLoadAddress = 0x20;

        /// <summary>A file carrying a code header. The default.</summary>
        public const string CodeKind = "code";

        /// <summary>A file of characters, marked by a lone $FF.</summary>
        public const string TextKind = "text";

        /// <summary>Code with no header at all; the addresses travel beside it.</summary>
        public const string FlatKind = "flat";

        /// <summary>Where the code starts once the header is in place.</summary>
        public const int EntryStubLength = 6;

        public string Name { get { return "bbc"; } }

        public OperationResult Write(string path, byte[] payload, ResolvedTarget target)
        {
            payload = payload ?? new byte[0];
            target = target ?? new ResolvedTarget();

            string kind = NormalizeKind(target.BbcFileType);
            if (kind == TextKind)
                return WriteText(path, payload);
            if (kind == FlatKind)
                return ExecutableFile.WriteBytes(path, payload);

            ushort address = ResolveAddress(target);
            ushort declared;
            byte[] code = StripStubIfPresent(payload, out declared);
            if (declared != 0)
                address = declared;

            return ExecutableFile.WriteBytes(path, BuildHeader(address, code, target));
        }

        /// <summary>
        /// Builds the header, followed by the code. The entry stub is a branch
        /// to the first byte of the code, which is where a client enters after
        /// working out the load address.
        /// </summary>
        public static byte[] BuildHeader(ushort loadAddress, byte[] code, ResolvedTarget target)
        {
            code = code ?? new byte[0];

            StringBuilder title = new StringBuilder();
            if (target != null && !string.IsNullOrWhiteSpace(target.Title))
                title.Append(target.Title.Trim());
            if (title.Length == 0)
                title.Append("1.00");
            title.Append('\0');

            string copyright = "(C) WinASM65";
            if (target != null && !string.IsNullOrWhiteSpace(target.Author))
                copyright = "(C) " + target.Author.Trim();

            // offset 7 must land on a zero byte whose followers are "(C)": that
            // is how a client tells a headered file from raw code, so the title
            // is padded to a known place instead of searched for.
            int copyrightOffset = 8 + title.Length + 1;
            int loadOffset = copyrightOffset + 1 + copyright.Length + 1;
            int entry = loadOffset + 4;

            byte[] file = new byte[entry + code.Length];

            file[0] = 0x4C;                                   // JMP entry
            file[1] = (byte)(entry & 0xFF);
            file[2] = (byte)(entry >> 8);
            file[3] = 0xEA;                                   // NOPs, never run:
            file[4] = 0xEA;                                   // a client may only
            file[5] = 0xEA;                                   // trust three bytes

            byte type = RomTypeFile | RomTypeHasLoadAddress;
            type |= target == null ? Cpu6502 : target.BbcCpuType;
            file[6] = type;
            file[7] = (byte)copyrightOffset;

            for (int i = 0; i < title.Length; i++)
                file[8 + i] = (byte)title[i];

            // file[copyrightOffset] stays zero: it is the marker.
            int at = copyrightOffset + 1;
            for (int i = 0; i < copyright.Length; i++)
                file[at + i] = (byte)copyright[i];
            at += copyright.Length;
            file[at] = 0;

            at = loadOffset;
            file[at] = (byte)(loadAddress & 0xFF);
            file[at + 1] = (byte)(loadAddress >> 8);
            file[at + 2] = 0;
            file[at + 3] = 0;

            System.Array.Copy(code, 0, file, entry, code.Length);
            return file;
        }

        /// <summary>
        /// A payload that already opens with its own entry stub is kept as it
        /// is. Writing a second stub would shift the code by the header size
        /// while still looking like a valid file, and the entry point the header
        /// publishes would not be the one the source wrote.
        /// </summary>
        public static byte[] StripStubIfPresent(byte[] payload, out ushort address)
        {
            address = 0;
            if (payload == null || payload.Length < 3 || payload[0] != 0x4C)
                return payload ?? new byte[0];

            address = (ushort)(payload[1] | (payload[2] << 8));

            // Only the branch is removed. The six bytes reserved before the type
            // byte belong to the header this class writes, not to a stub a
            // source might have written, and guessing wider would cut into the
            // first instructions of perfectly ordinary code that happens to
            // start with a JMP.
            byte[] code = new byte[payload.Length - 3];
            System.Array.Copy(payload, 3, code, 0, code.Length);
            return code;
        }

        private static OperationResult WriteText(string path, byte[] payload)
        {
            // The machine reads a text file to end of file. A length or an
            // address in front of it would be shown to the program as text.
            byte[] file = new byte[payload.Length + 1];
            file[0] = TextType;
            System.Array.Copy(payload, 0, file, 1, payload.Length);
            return ExecutableFile.WriteBytes(path, file);
        }

        public static string NormalizeKind(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind))
                return CodeKind;
            string key = kind.Trim().ToLowerInvariant();
            if (key == TextKind)
                return TextKind;
            if (key == FlatKind || key == "raw" || key == "bin")
                return FlatKind;
            return CodeKind;
        }

        private static ushort ResolveAddress(ResolvedTarget target)
        {
            if (target.OriginAddress.HasValue)
                return target.OriginAddress.Value;
            if (target.RunAddress.HasValue)
                return target.RunAddress.Value;
            if (target.LoadAddress.HasValue)
                return target.LoadAddress.Value;
            return 0x0E00;
        }
    }
}
