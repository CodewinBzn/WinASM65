using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Output;

namespace WinASM65.Tests
{
    [TestClass]
    public class SourceListingTests
    {
        // A source with one of every row kind the listing can produce: an
        // origin, a constant, a reservation, instructions of one, two, three and
        // more than four bytes, a label on its own line and a bare line.
        private const string Programme =
            ".org $8000\n" +
            "Count = $10\n" +
            "Start:\n" +
            "  lda #$2A      ; one comment\n" +
            "  lda $10\n" +
            "  jmp Start\n" +
            "Table:\n" +
            "  .byte $01, $02, $03, $04, $05\n" +
            "  .word $1234, $5678\n" +
            "Buffer .res 4\n" +
            "\n";

        [TestMethod]
        public void ListingFromSourceTextMatchesTheFileBasedListing()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "prog.asm");
                string objectFile = Path.Combine(temp.Path, "prog.o");
                File.WriteAllText(source, Programme);

                AssemblyResult fromFile = new AssemblerEngine(listingService: new ListingService { IsEnabled = true })
                    .Assemble(source, objectFile);
                string listingFile = Path.ChangeExtension(source, ".lst");
                Assert.IsTrue(File.Exists(listingFile), "the file listing is written");

                SourceListing inMemory = new SourceListingService().ListSourceFile(source);

                Assert.IsTrue(inMemory.Success);
                Assert.AreEqual(fromFile.Success, inMemory.Success);
                CollectionAssert.AreEqual(fromFile.OutputBytes, inMemory.Assembly.OutputBytes);
                Assert.AreEqual(fromFile.OriginAddress, inMemory.Assembly.OriginAddress);

                string[] fileRows = File.ReadAllLines(listingFile);
                Assert.AreEqual(fileRows.Length, inMemory.Rows.Count,
                    "the in-memory rows are the rows the .lst holds");

                for (int i = 0; i < fileRows.Length; i++)
                    Assert.AreEqual(fileRows[i], inMemory.Rows[i].Render(), "row " + i);
            }
        }

        [TestMethod]
        public void ListingFromSourceTextEqualsListingFromTheSameTextInAFile()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "same.asm");
                File.WriteAllText(source, Programme);

                SourceListing fromText = new SourceListingService().ListSourceText(Programme, "same.asm");
                SourceListing fromFile = new SourceListingService().ListSourceFile(source);

                Assert.AreEqual(fromFile.Rows.Count, fromText.Rows.Count);
                for (int i = 0; i < fromFile.Rows.Count; i++)
                {
                    Assert.AreEqual(fromFile.Rows[i].Render(), fromText.Rows[i].Render(), "row " + i);
                    Assert.AreEqual(fromFile.Rows[i].LineNumber, fromText.Rows[i].LineNumber, "line of row " + i);
                    CollectionAssert.AreEqual(new List<byte>(fromFile.Rows[i].Bytes), new List<byte>(fromText.Rows[i].Bytes), "bytes of row " + i);
                }
            }
        }

        [TestMethod]
        public void RowsCarryTheirSourceLineAddressAndBytes()
        {
            SourceListing listing = new SourceListingService().ListSourceText(Programme, "prog.asm");
            IReadOnlyList<ListingRow> rows = listing.Rows;

            ListingRow origin = RowAtLine(rows, 1);
            Assert.AreEqual(ListingRowKind.Origin, origin.Kind);
            Assert.AreEqual(0x8000, origin.Address.GetValueOrDefault());

            ListingRow constant = RowAtLine(rows, 2);
            Assert.AreEqual(ListingRowKind.Constant, constant.Kind);
            Assert.AreEqual(0x10, constant.Value.GetValueOrDefault());

            ListingRow label = RowAtLine(rows, 3);
            Assert.AreEqual(ListingRowKind.Label, label.Kind);
            Assert.AreEqual(0x8000, label.Address.GetValueOrDefault());

            ListingRow immediate = RowAtLine(rows, 4);
            Assert.AreEqual(ListingRowKind.Instruction, immediate.Kind);
            Assert.AreEqual(2, immediate.Bytes.Count);
            Assert.AreEqual(0xA9, immediate.Bytes[0]);
            Assert.AreEqual(0x2A, immediate.Bytes[1]);

            ListingRow reservation = RowAtLine(rows, 10);
            Assert.AreEqual(ListingRowKind.Reserve, reservation.Kind);
        }

        [TestMethod]
        public void AnInstructionLongerThanFourBytesSpansSeveralRows()
        {
            // Five bytes: the first row carries the text, the second carries the
            // fifth byte and nothing else. That is why a row is not a source line.
            SourceListing listing = new SourceListingService().ListSourceText(".org $8000\n.byte $01, $02, $03, $04, $05\n");

            int rowsOfLineTwo = 0;
            foreach (ListingRow row in listing.Rows)
            {
                if (row.LineNumber != 2)
                    continue;

                rowsOfLineTwo++;
                if (rowsOfLineTwo == 1)
                {
                    Assert.AreEqual(4, row.Bytes.Count);
                    Assert.IsNotNull(row.Text, "the first row of a long instruction carries the source");
                }
                else
                {
                    Assert.AreEqual(1, row.Bytes.Count);
                    Assert.IsNull(row.Text, "a continuation row has bytes and no source of its own");
                    Assert.AreEqual(0x8004, row.Address.GetValueOrDefault());
                }
            }

            Assert.AreEqual(2, rowsOfLineTwo);
        }

        [TestMethod]
        public void TheTextColumnStartsAtTheSameCellWhateverTheMnemonicLength()
        {
            SourceListing listing = new SourceListingService().ListSourceText(
                ".org $8000\n  clc\n  ldx #$01\n  jmp $1234\n");

            int instructions = 0;
            int textColumn = -1;

            foreach (ListingRow row in listing.Rows)
            {
                if (row.Kind != ListingRowKind.Instruction)
                    continue;

                string rendered = row.Render();
                Assert.IsTrue(rendered.StartsWith(string.Format("{0:X4} ", row.Address.GetValueOrDefault())),
                    "every instruction row starts at the address cell");

                int column = rendered.IndexOf(row.Text.Trim(), StringComparison.Ordinal);
                if (textColumn < 0)
                    textColumn = column;

                Assert.AreEqual(textColumn, column,
                    "the text starts at the same cell for a one, a two and a three byte instruction");
                instructions++;
            }

            Assert.AreEqual(3, instructions, "the source has three instruction rows");
        }

        [TestMethod]
        public void TheListingFileKeepsTheFormatItHasAlwaysWritten()
        {
            // Golden: the .lst is an artefact users already read, so its columns
            // are pinned here rather than recomputed from the model that now
            // writes it. The third column sits one cell further right on a full
            // group of four bytes than on a short one; that spacing predates the
            // in-memory rows and is preserved deliberately.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "golden.asm");
                File.WriteAllText(source, ".org $8000\nLabel:\n  clc\n  jmp Label\n  .byte $01, $02, $03, $04, $05\n");

                new AssemblerEngine(listingService: new ListingService { IsEnabled = true })
                    .Assemble(source, Path.Combine(temp.Path, "golden.o"));

                string[] expected =
                {
                    "8000" + new string(' ', 14) + ".org $8000",
                    "8000" + new string(' ', 14) + "Label:",
                    "8000 18" + new string(' ', 12) + "clc",
                    "8001 4C 00 80" + new string(' ', 6) + "jmp Label",
                    "8004 01 02 03 04" + new string(' ', 4) + ".byte $01, $02, $03, $04, $05",
                    "8008 05 "
                };

                CollectionAssert.AreEqual(expected, File.ReadAllLines(Path.ChangeExtension(source, ".lst")));
            }
        }

        [TestMethod]
        public void AssemblingTextWritesNoFileAtAll()
        {
            SourceListing listing = new SourceListingService().ListSourceText(Programme, "no-files.asm");

            Assert.IsTrue(listing.Success);
            Assert.AreEqual("no-files.asm", listing.SourceName);
            Assert.IsTrue(listing.Rows.Count > 0);
            Assert.IsFalse(File.Exists("no-files.asm.symb"),
                "a source assembled from memory has no path to export symbols next to");
            Assert.IsFalse(File.Exists("no-files.asm.lst"),
                "a listing asked for in memory never writes a .lst");
        }

        [TestMethod]
        public void AMissingFileIsReportedTheWayTheAssemblerReportsIt()
        {
            SourceListing listing = new SourceListingService().ListSourceFile("no-such-file.asm");

            Assert.IsFalse(listing.Success);
            Assert.AreEqual(1, listing.Assembly.Diagnostics.Count);
            Assert.AreEqual(ErrorCodes.FILE_NOT_EXISTS, listing.Assembly.Diagnostics[0].Message);
        }

        [TestMethod]
        public void TheSourceStringEntryPointAcceptsANullOutputFile()
        {
            AssemblyResult result = new AssemblerEngine().AssembleSource(".org $C000\nlda #$42\nrts\n", null);

            Assert.IsTrue(result.Success);
            CollectionAssert.AreEqual(new byte[] { 0xA9, 0x42, 0x60 }, result.OutputBytes);
            Assert.AreEqual(0xC000, result.OriginAddress);
        }

        [TestMethod]
        public void TheSourceStringEntryPointNamesItsSourceInDiagnostics()
        {
            AssemblyResult result = new AssemblerEngine().AssembleSource(".include \"absent.asm\"\n", null, "buffer.asm");

            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, result.Diagnostics.Count);
            Assert.AreEqual(ErrorCodes.FILE_NOT_EXISTS, result.Diagnostics[0].Message);
            Assert.AreEqual("buffer.asm", result.Diagnostics[0].Location.FilePath);
            Assert.AreEqual(1, result.Diagnostics[0].Location.LineNumber);
        }

        [TestMethod]
        public void TheSourceStringEntryPointUsesTheDefaultNameWhenGivenNone()
        {
            AssemblyResult result = new AssemblerEngine().AssembleSource(".include \"absent.asm\"\n", null);

            Assert.AreEqual(AssemblerEngine.DefaultSourceName, result.Diagnostics[0].Location.FilePath);
        }

        [TestMethod]
        public void TheSourceStringEntryPointHandlesEveryLineTerminator()
        {
            SourceListing lf = new SourceListingService().ListSourceText(".org $8000\nclc\nrts\n");
            SourceListing crlf = new SourceListingService().ListSourceText(".org $8000\r\nclc\r\nrts\r\n");

            Assert.AreEqual(lf.Rows.Count, crlf.Rows.Count);
            for (int i = 0; i < lf.Rows.Count; i++)
                Assert.AreEqual(lf.Rows[i].Render(), crlf.Rows[i].Render(), "row " + i);
        }

        private static ListingRow RowAtLine(IReadOnlyList<ListingRow> rows, int lineNumber)
        {
            foreach (ListingRow row in rows)
            {
                if (row.LineNumber == lineNumber)
                    return row;
            }

            Assert.Fail("no row for source line " + lineNumber);
            return null;
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }
            public TemporaryDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinASM65Tests_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
            public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
        }
    }
}