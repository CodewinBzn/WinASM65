using System.Collections.Generic;
using WinASM65.Core;

namespace WinASM65.Targets
{
    /// <summary>
    /// A Tube file: what OSLOAD expects, which is the code and nothing else.
    /// <para>
    /// The main machine's files carry a header because the machine has to find
    /// the length and the address. A second processor is given a file of a
    /// known length by a service that already knows where to put it, so the
    /// header would be read as instructions.
    /// </para>
    /// <para>
    /// The load address is not in the file. It is the machine's rule, not the
    /// program's, which is why it is a property of the target rather than a
    /// field the writer has to guess.
    /// </para>
    /// </summary>
    public class TubeFormat : IExecutableFormat
    {
        /// <summary>
        /// The address the OSLOAD service uses on a 6502 second processor. Code
        /// for that processor is usually written for $0200, which is where the
        /// RAM is, while the service loads at $2000. Both are real, and which
        /// one a program wants is a property of the program, not of this
        /// writer, so the address is configurable and the default is the one
        /// the service uses.
        /// </summary>
        public const ushort DefaultLoadAddress = 0x2000;

        /// <summary>RAM of a 6502 second processor, $0200 to $7FFF.</summary>
        public const int SecondProcessorRamSize = 0x7E00;

        public string Name { get { return "tube"; } }

        public OperationResult Write(string path, byte[] payload, ResolvedTarget target)
        {
            payload = payload ?? new byte[0];

            if (payload.Length > SecondProcessorRamSize)
            {
                List<Diagnostic> diagnostics = new List<Diagnostic>();
                diagnostics.Add(new Diagnostic(new SourceLocation(path, 0),
                    "A second processor holds " + SecondProcessorRamSize + " bytes of RAM ($0200-$7FFF); "
                    + "this file is " + payload.Length + " bytes. OSLOAD has no way to split it."));
                return new OperationResult(false, diagnostics);
            }

            return ExecutableFile.WriteBytes(path, payload);
        }
    }
}
