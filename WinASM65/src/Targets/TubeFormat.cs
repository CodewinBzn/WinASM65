using System.Collections.Generic;
using WinASM65.Core;

namespace WinASM65.Targets
{
    /// <summary>
    /// Code for a second processor, the way a loader wants it: the payload and
    /// nothing else.
    /// <para>
    /// A loader that copies a block knows the length because it was told, and
    /// knows the address because it decided. A header would be read as
    /// instructions, and an address inside the file would be an address the file
    /// does not get to choose.
    /// </para>
    /// <para>
    /// This is why the second processor is a target and not a format: what makes
    /// it different is not how the file is written but where the bytes go and
    /// who puts them there.
    /// </para>
    /// </summary>
    public class TubeFormat : IExecutableFormat
    {
        /// <summary>
        /// A 6502 second processor has 64 KB of RAM across its whole address
        /// space, so nothing in the file can be too large. The size check that
        /// would matter is not "does it fit" but "does it overwrite the system",
        /// which is a question about the program, not the file.
        /// </summary>
        public const int AddressSpaceSize = 0x10000;

        public string Name { get { return "tube"; } }

        public OperationResult Write(string path, byte[] payload, ResolvedTarget target)
        {
            payload = payload ?? new byte[0];

            if (payload.Length > AddressSpaceSize)
            {
                List<Diagnostic> diagnostics = new List<Diagnostic>();
                diagnostics.Add(new Diagnostic(new SourceLocation(path, 0),
                    "A second processor addresses 64 KB, so a " + payload.Length
                    + " byte block cannot be placed at all. The loader has to split it."));
                return new OperationResult(false, diagnostics);
            }

            return ExecutableFile.WriteBytes(path, payload);
        }
    }
}
