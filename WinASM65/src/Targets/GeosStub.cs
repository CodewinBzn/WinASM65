// WinASM65 - the stub a GEOS application runs before its own code
//
// The kernal does not relocate, so an application loaded away from the address
// it was assembled for has to correct itself. The table says where; this is the
// code that walks it.
//
// The stub is placed at the head of the first record, the table immediately
// after it, and the program's bytes after that:
//
//   record 0:   [ stub ][ table ][ code ... ]
//   load addr:  ^
//
// so the kernal, which jumps to the load address, runs the stub first. The stub
// corrects the code and jumps to the real entry. Everything the application
// needs is therefore in the one record the kernal loads: nothing has to be read
// back off the disk, and a relocated application needs no installer.
//
// The stub's source is 6502 and lives beside the writer that lays it out, and
// the repository's own assembler builds it -- one table, one loop, one source
// of truth for both ends of the walk.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using WinASM65.Core;

namespace WinASM65.Targets
{
    /// <summary>
    /// Assembles the relocation stub that runs ahead of a GEOS application.
    /// </summary>
    public static class GeosStub
    {
        /// <summary>The resource the stub's source is embedded under.</summary>
        public const string StubResource = "WinASM65.Targets.geos-stub.asm";

        /// <summary>
        /// The stub, for a program whose code was assembled at
        /// <paramref name="codeBase"/> and whose table is
        /// <paramref name="tableLength"/> bytes long.
        /// <para>
        /// The stub needs to know two distances -- from the load address to the
        /// table, and from the load address to the code -- and the first of them
        /// is the stub's own length, which is not known until it has been
        /// assembled. So it is assembled twice: once to be measured, once with
        /// the real distances in it. Both passes are the same length, because a
        /// distance only ever appears as a one byte immediate, and the check
        /// below says so rather than trusting it.
        /// </para>
        /// </summary>
        public static byte[] Assemble(ushort codeBase, int tableLength, List<Diagnostic> problems)
        {
            byte[] probe = AssembleOnce(codeBase, 0, 0, problems);
            if (probe == null)
                return null;

            int stubLength = probe.Length;
            byte[] stub = AssembleOnce(codeBase, stubLength, stubLength + tableLength, problems);
            if (stub == null)
                return null;

            if (stub.Length != stubLength)
            {
                problems.Add(new Diagnostic(new SourceLocation("geos-stub", 0),
                    "The GEOS stub changed length between its two passes ("
                    + stubLength + " then " + stub.Length
                    + "), so the distances it was built with are already wrong."));
                return null;
            }

            return stub;
        }

        private static byte[] AssembleOnce(ushort origin, int tableOffset, int codeOffset,
            List<Diagnostic> problems)
        {
            string source = Source();
            if (source == null)
            {
                problems.Add(new Diagnostic(new SourceLocation("geos-stub", 0),
                    "The GEOS stub's source is not in the assembly: " + StubResource
                    + " is missing."));
                return null;
            }

            string file = Path.Combine(Path.GetTempPath(),
                "WinASM65-geos-stub-" + Guid.NewGuid().ToString("N") + ".asm");
            string output = Path.ChangeExtension(file, ".o");
            try
            {
                File.WriteAllText(file, source);

                Dictionary<string, long> symbols = new Dictionary<string, long>
                {
                    { "TABLE_OFFSET", tableOffset },
                    { "CODE_OFFSET", codeOffset }
                };
                AssemblyResult result = new AssemblerEngine(
                    predefinedSymbols: symbols,
                    defaultOrigin: origin).Assemble(file, output);
                if (!result.Success)
                {
                    for (int i = 0; i < result.Diagnostics.Count; i++)
                        problems.Add(result.Diagnostics[i]);
                    return null;
                }
                return result.OutputBytes;
            }
            finally
            {
                TryDelete(file);
                TryDelete(output);
                TryDelete(Path.ChangeExtension(file, ".symb"));
            }
        }

        private static string Source()
        {
            Assembly self = typeof(GeosStub).GetTypeInfo().Assembly;
            using (Stream stream = self.GetManifestResourceStream(StubResource))
            {
                if (stream == null)
                    return null;

                using (StreamReader reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
                // A file left in the temporary directory is not worth failing a
                // build over, and the next build uses another name anyway.
            }
        }
    }
}
