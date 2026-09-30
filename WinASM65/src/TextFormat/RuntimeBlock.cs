// WinASM65 - the payload a shared routine travels in
//
// A linked image is flat and it holds absolute addresses: it is correct at the
// one address the linker put it at, and nowhere else. A program in a tokenised
// file has no such address to give it — the interpreter loads the file wherever
// it finds room, and the same file has to work on any machine — so a routine
// that is shared between two programs cannot be linked into either of them.
//
// The way out is the one this repository has used for every other machine: the
// file carries no absolute address at all, and the code that moves is code that
// runs. `runtime-stub.asm` is that code; this is the other half, the payload it
// reads, and the two are one thing written twice: the header offsets below are
// the offsets the stub assembles, and the stub's own comment lists them.
//
// The payload is:
//
//   the stub, assembled here, at an address the caller chose
//   the header, ten bytes, which the stub finds immediately after the code
//   the block, coded: runs of equal bytes and everything else, as below
//   the site table, three bytes a site
//
// The coding is not compression in any interesting sense. A run of three equal
// bytes becomes three bytes that say so, and everything else is copied, so the
// result is never larger than the block and usually a good deal smaller. It is
// the smallest thing that is genuinely a codec and that a 6502 can be trusted
// with: the alternative, a dictionary or a back reference, needs either more
// state to get right or a second pass, and neither buys anything here, where
// the block is code.
//
// The shift is the whole point, and it is why a one byte absolute reference is
// refused rather than written out: a byte cannot hold an address that has moved,
// and a stub that left it alone would be a stub that quietly produced a program
// reading the wrong place.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using WinASM65.Core;
using WinASM65.Linking;

namespace WinASM65.TextFormat
{
    public sealed class RuntimeBlockOptions
    {
        /// <summary>Where the stub is placed. The caller JSRs to this.</summary>
        public ushort StubAddress { get; set; }

        /// <summary>Where the block is to be written at run time.</summary>
        public ushort BlockAddress { get; set; }

        /// <summary>The entry point, as an offset inside the block. Zero is the origin.</summary>
        public int EntryOffset { get; set; }

        public RuntimeBlockOptions()
        {
            StubAddress = 0x2000;
            BlockAddress = 0x4000;
            EntryOffset = 0;
        }
    }

    /// <summary>A stub, its header, its block and its site table, ready to be written.</summary>
    public sealed class RuntimeBlock
    {
        /// <summary>The bytes between the stub's code and its block.</summary>
        public const int HeaderLength = 10;

        /// <summary>What one site costs in the table: offset low, offset high, width.</summary>
        public const int SiteLength = 3;

        /// <summary>How many equal bytes are worth coding as a run.</summary>
        public const int RunThreshold = 3;

        /// <summary>The resource the stub's source is embedded under.</summary>
        public const string StubResource = "WinASM65.TextFormat.runtime-stub.asm";

        public byte[] Data { get; private set; }
        public ushort StubAddress { get; private set; }
        public int StubLength { get; private set; }
        public ushort BlockAddress { get; private set; }
        public ushort EntryAddress { get; private set; }
        public int CodedLength { get; private set; }

        private RuntimeBlock()
        {
            Data = new byte[0];
        }

        /// <summary>
        /// Assembles the stub and lays the payload after it. The stub is
        /// assembled twice: the first time only to learn how long it is, because
        /// the header sits immediately after the code and the stub has to know
        /// where that is. The second pass is told, and the two lengths have to
        /// agree — if they did not, the header would be written where the stub
        /// does not think it is, and the failure would be a machine that runs
        /// nothing rather than a build that stops.
        ///
        /// The first pass is therefore given an address that is not in the zero
        /// page, and not by accident. Read with a value of zero, <c>LDA
        /// STUB_HEADER</c> assembles to the two byte zero page form, and with the
        /// real address to the three byte absolute one; the stub would measure
        /// fourteen bytes short and the header would land in the middle of it.
        /// A measurement pass has to ask the question the second pass will.
        /// </summary>
        public static RuntimeBlock Build(LinkedImage image, RuntimeBlockOptions options,
            List<Diagnostic> diagnostics)
        {
            List<Diagnostic> problems = diagnostics ?? new List<Diagnostic>();
            RuntimeBlock block = new RuntimeBlock();
            if (image == null)
                throw new ArgumentNullException("image");
            if (options == null)
                throw new ArgumentNullException("options");

            int placeholder = 0x0100 | (options.StubAddress & 0xFF);
            AssemblyResult measured = Assemble(options.StubAddress, placeholder, problems);
            if (measured == null)
                return block;

            int header = options.StubAddress + measured.OutputBytes.Length;
            if (header > 0xFFFF)
            {
                problems.Add(new Diagnostic(new SourceLocation("stub", 0),
                    "The stub at $" + options.StubAddress.ToString("X4")
                    + " leaves no room for its header before the top of memory."));
                return block;
            }

            AssemblyResult stub = Assemble(options.StubAddress, header, problems);
            if (stub == null)
                return block;
            if (stub.OutputBytes.Length != measured.OutputBytes.Length)
            {
                problems.Add(new Diagnostic(new SourceLocation("stub", 0),
                    "The stub assembled to " + measured.OutputBytes.Length + " bytes and then to "
                    + stub.OutputBytes.Length + ". The header would be written where the code is not."));
                return block;
            }

            int beforeSites = problems.Count;
            byte[] coded = Code(image.Data);
            List<byte> siteTable = Sites(image, problems);
            if (problems.Count != beforeSites)
            {
                // A site the stub cannot move is not a warning: the block
                // carries an address that would survive the move unchanged, and
                // a machine given that would read the wrong place in silence.
                return block;
            }

            if (options.EntryOffset < 0 || options.EntryOffset >= image.Data.Length)
            {
                problems.Add(new Diagnostic(new SourceLocation("block", 0),
                    "The entry point is " + options.EntryOffset + " bytes into a block of "
                    + image.Data.Length + ", so it is not inside it."));
                return block;
            }

            List<byte> data = new List<byte>(stub.OutputBytes.Length + HeaderLength
                + coded.Length + siteTable.Count);
            data.AddRange(stub.OutputBytes);

            int shift = options.BlockAddress - image.OriginAddress;
            data.Add((byte)(options.BlockAddress & 0xFF));
            data.Add((byte)((options.BlockAddress >> 8) & 0xFF));
            data.Add((byte)(shift & 0xFF));
            data.Add((byte)((shift >> 8) & 0xFF));
            data.Add((byte)(options.EntryOffset & 0xFF));
            data.Add((byte)((options.EntryOffset >> 8) & 0xFF));
            data.Add((byte)(image.Data.Length & 0xFF));
            data.Add((byte)((image.Data.Length >> 8) & 0xFF));
            data.Add((byte)(siteTable.Count / SiteLength & 0xFF));
            data.Add((byte)((siteTable.Count / SiteLength >> 8) & 0xFF));
            data.AddRange(coded);
            data.AddRange(siteTable);

            block.Data = data.ToArray();
            block.StubAddress = options.StubAddress;
            block.StubLength = stub.OutputBytes.Length;
            block.BlockAddress = options.BlockAddress;
            block.EntryAddress = (ushort)(options.BlockAddress + options.EntryOffset);
            block.CodedLength = coded.Length;
            return block;
        }

        /// <summary>
        /// Assembles the stub once, with the address of its header as a symbol.
        /// The source lives in the repository rather than in a string here, and
        /// the repository's own assembler is what builds it.
        /// </summary>
        private static AssemblyResult Assemble(ushort stubAddress, int header, List<Diagnostic> problems)
        {
            string source = Source();
            if (source == null)
            {
                problems.Add(new Diagnostic(new SourceLocation("stub", 0),
                    "The stub's source is not in the assembly: " + StubResource + " is missing."));
                return null;
            }

            string file = Path.Combine(Path.GetTempPath(),
                "WinASM65-stub-" + Guid.NewGuid().ToString("N") + ".asm");
            string output = Path.ChangeExtension(file, ".o");
            try
            {
                File.WriteAllText(file, source);

                Dictionary<string, long> symbols = new Dictionary<string, long>
                {
                    { "STUB_HEADER", header }
                };
                AssemblyResult result = new AssemblerEngine(
                    predefinedSymbols: symbols,
                    defaultOrigin: stubAddress).Assemble(file, output);
                if (!result.Success)
                {
                    for (int i = 0; i < result.Diagnostics.Count; i++)
                        problems.Add(result.Diagnostics[i]);
                    return null;
                }
                return result;
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
            Assembly self = typeof(RuntimeBlock).GetTypeInfo().Assembly;
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

        /// <summary>
        /// The site's table: where each absolute reference is, and how wide it
        /// is. A width of one cannot be moved — a byte has no room for an
        /// address that has changed — so it is refused here rather than written
        /// out and left wrong.
        /// </summary>
        private static List<byte> Sites(LinkedImage image, List<Diagnostic> problems)
        {
            List<byte> table = new List<byte>();
            for (int i = 0; i < image.References.Count; i++)
            {
                LinkedReference reference = image.References[i];
                if (reference.Width != 2)
                {
                    problems.Add(new Diagnostic(new SourceLocation("block", reference.Address),
                        "A one byte absolute reference at $" + reference.Address.ToString("X4")
                        + " cannot be moved to another address. A block can only be shared if it"
                        + " holds none."));
                    continue;
                }

                int offset = reference.Address - image.OriginAddress;
                table.Add((byte)(offset & 0xFF));
                table.Add((byte)((offset >> 8) & 0xFF));
                table.Add(0x02);
            }
            return table;
        }

        /// <summary>
        /// A run of equal bytes becomes three bytes that say so, and everything
        /// else is copied. Nothing here can make the result longer than what it
        /// was given, and a group never crosses the 127 that a length byte
        /// cannot reach.
        /// </summary>
        public static byte[] Code(byte[] plain)
        {
            List<byte> coded = new List<byte>();
            List<byte> literal = new List<byte>();

            int at = 0;
            while (at < plain.Length)
            {
                int run = 1;
                while (at + run < plain.Length && plain[at + run] == plain[at] && run < 0xFF)
                    run++;

                if (run >= RunThreshold)
                {
                    Flush(coded, literal);
                    coded.Add(0x00);
                    coded.Add((byte)run);
                    coded.Add(plain[at]);
                    at += run;
                }
                else
                {
                    literal.Add(plain[at]);
                    at++;
                    if (literal.Count == 0x7F)
                        Flush(coded, literal);
                }
            }

            Flush(coded, literal);
            return coded.ToArray();
        }

        private static void Flush(List<byte> coded, List<byte> literal)
        {
            if (literal.Count == 0)
                return;

            coded.Add((byte)literal.Count);
            coded.AddRange(literal);
            literal.Clear();
        }
    }
}
