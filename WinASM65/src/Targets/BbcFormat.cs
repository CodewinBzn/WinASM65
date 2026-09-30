using System.Collections.Generic;
using WinASM65.Core;

namespace WinASM65.Targets
{
    /// <summary>
    /// A BBC Micro file with its header.
    /// <para>
    /// The machine reads three kinds of file, told apart by the first byte:
    /// a binary file, an executable file, and a text file. Text is a bare
    /// $FF and nothing else; the two binary kinds carry a length, and the high
    /// bits of the type byte say what the length means and where the file
    /// starts. Bit 6 of the type byte is dropped on the way in, which is why
    /// the largest executable is 16 KB and not 32 KB.
    /// </para>
    /// <para>
    /// The $4F $4F pair is the marker that says "the two bytes after me are an
    /// address". It sits in the header of a binary file, and at the start of
    /// the data of an executable.
    /// </para>
    /// </summary>
    public class BbcFormat : IExecutableFormat
    {
        public const byte TextType = 0xFF;
        public const byte AddressMarker = 0x4F;

        /// <summary>A binary file: length in the type byte, data after a 4 byte header.</summary>
        public const string BinaryKind = "binary";

        /// <summary>An executable file: length in the type byte, data after a 2 byte header.</summary>
        public const string ExecKind = "exec";

        /// <summary>A text file: a bare $FF, no length, no address.</summary>
        public const string TextKind = "text";

        /// <summary>Payload plus the 6 byte header cannot be expressed by a type byte.</summary>
        public const int MaxBinaryPayload = 0x7FFF - 6;

        /// <summary>The exec length is masked to 14 bits, so the ceiling is 16 KB.</summary>
        public const int MaxExecPayload = 0x3FFF - 2 - 4;

        public string Name { get { return "bbc"; } }

        public OperationResult Write(string path, byte[] payload, ResolvedTarget target)
        {
            payload = payload ?? new byte[0];
            target = target ?? new ResolvedTarget();

            string kind = NormalizeKind(target.BbcFileType);
            if (kind == TextKind)
                return WriteText(path, payload);

            if (kind == BinaryKind)
                return WriteBinary(path, payload, ResolveAddress(target, (ushort)0x0E00));

            return WriteExec(path, payload, ResolveAddress(target, (ushort)0x0E00));
        }

        /// <summary>
        /// A text file is the payload as it stands. No header, no length: the
        /// machine reads to end of file, so anything else would appear to the
        /// program as data.
        /// </summary>
        private static OperationResult WriteText(string path, byte[] payload)
        {
            byte[] file = new byte[payload.Length + 1];
            file[0] = TextType;
            System.Array.Copy(payload, 0, file, 1, payload.Length);
            return ExecutableFile.WriteBytes(path, file);
        }

        private static OperationResult WriteBinary(string path, byte[] payload, ushort address)
        {
            // type, length, $4F $4F, load address: six bytes, all counted in the
            // length the type byte encodes.
            int total = payload.Length + 6;
            if (total > 0x7FFF)
            {
                return Refuse(path, "a BBC binary file holds at most 32767 bytes; this one would be "
                    + total + ". Split it, or use OSLOAD, which has no such header.");
            }

            byte[] file = new byte[total];
            file[0] = (byte)(total >> 8);
            file[1] = (byte)(total & 0xFF);
            file[2] = AddressMarker;
            file[3] = AddressMarker;
            file[4] = (byte)(address & 0xFF);
            file[5] = (byte)((address >> 8) & 0xFF);
            System.Array.Copy(payload, 0, file, 6, payload.Length);
            return ExecutableFile.WriteBytes(path, file);
        }

        private static OperationResult WriteExec(string path, byte[] payload, ushort address)
        {
            byte[] body;
            if (StartsWithMarker(payload))
            {
                // The code already carries the marker and the address, which is
                // what a source that writes its own entry point produces. Writing
                // a second one would shift every instruction by four bytes and
                // still look like a valid file.
                body = payload;
                address = ReadMarkedAddress(payload, address);
            }
            else
            {
                body = new byte[payload.Length + 4];
                body[0] = AddressMarker;
                body[1] = AddressMarker;
                body[2] = (byte)(address & 0xFF);
                body[3] = (byte)((address >> 8) & 0xFF);
                System.Array.Copy(payload, 0, body, 4, payload.Length);
            }

            int total = body.Length + 2;
            if (total > 0x3FFF)
            {
                return Refuse(path, "a BBC executable holds at most 16383 bytes; this one would be "
                    + total + ". Bit 6 of the type byte carries no length, so a bigger file "
                    + "cannot be described.");
            }

            byte[] file = new byte[total];
            file[0] = (byte)(0x80 | ((total >> 8) & 0x3F));
            file[1] = (byte)(total & 0xFF);
            System.Array.Copy(body, 0, file, 2, body.Length);
            return ExecutableFile.WriteBytes(path, file);
        }

        /// <summary>True when the payload opens with the $4F $4F address marker.</summary>
        public static bool StartsWithMarker(byte[] payload)
        {
            return payload != null && payload.Length >= 2
                && payload[0] == AddressMarker && payload[1] == AddressMarker;
        }

        /// <summary>The address a marked payload declares, or the fallback when it is too short.</summary>
        public static ushort ReadMarkedAddress(byte[] payload, ushort fallback)
        {
            if (payload == null || payload.Length < 4)
                return fallback;
            return (ushort)(payload[2] | (payload[3] << 8));
        }

        public static string NormalizeKind(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind))
                return ExecKind;
            string key = kind.Trim().ToLowerInvariant();
            if (key == BinaryKind)
                return BinaryKind;
            if (key == TextKind)
                return TextKind;
            return ExecKind;
        }

        private static ushort ResolveAddress(ResolvedTarget target, ushort fallback)
        {
            if (target == null)
                return fallback;
            if (target.OriginAddress.HasValue)
                return target.OriginAddress.Value;
            if (target.RunAddress.HasValue)
                return target.RunAddress.Value;
            if (target.LoadAddress.HasValue)
                return target.LoadAddress.Value;
            return fallback;
        }

        private static OperationResult Refuse(string path, string message)
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            diagnostics.Add(new Diagnostic(new SourceLocation(path, 0), message));
            return new OperationResult(false, diagnostics);
        }
    }
}
