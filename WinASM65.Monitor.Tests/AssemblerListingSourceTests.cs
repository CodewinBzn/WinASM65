using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Cpu;
using WinASM65.Monitor.Shell;
using WinASM65.TextFormat;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// The listing seam wired to the library: real rows for a real source, a ceiling
    /// that holds, and a failure that says what went wrong instead of producing rows.
    ///
    /// Every test writes a real file and assembles it for real. A stubbed listing
    /// service would let the adapter agree with itself, and the things this has to get
    /// right — that a missing file is named, that a data directive is not mistaken for
    /// an instruction, that the token roles are the palette's names — are all
    /// agreements between the adapter and the assembler, and a stub removes the second
    /// party.
    /// </summary>
    [TestClass]
    public class AssemblerListingSourceTests
    {
        private const string Programme =
            ".org $8000\n" +
            "; a comment, which has no address at all\n" +
            "Start:\n" +
            "  lda #$5A      ; load a constant\n" +
            "  ldx #$01\n" +
            "  jmp Start\n" +
            "  .byte $01, $02\n";

        // ---------------------------------------------------------------- real rows

        [TestMethod]
        public void ARealSourceProducesRealRows()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, Programme);

                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);
                IReadOnlyList<ListingRow> rows =
                    source.Rows(new ListingRequest(name, 0x8000, 100));

                Assert.IsTrue(source.IsAvailable);
                Assert.IsNull(source.LastProblem, "nothing failed: " + source.LastProblem);
                Assert.IsTrue(rows.Count > 0, "a source with instructions lists something");

                ListingRow instruction = RowAtLine(rows, 4);
                Assert.AreEqual(0x8000, instruction.Address, "the first byte lands at the .org");
                Assert.AreEqual(2, instruction.Bytes.Count);
                Assert.AreEqual(0xA9, instruction.Bytes[0], "lda # is A9");
                Assert.AreEqual(0x5A, instruction.Bytes[1], "and the immediate is the value");
                StringAssert.Contains(instruction.Source, "lda #$5A");
            }
        }

        [TestMethod]
        public void ARowWithNoAddressHasNoneRatherThanZero()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, Programme);

                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);
                IReadOnlyList<ListingRow> rows = source.Rows(new ListingRequest(name, 0x8000, 100));

                ListingRow comment = RowAtLine(rows, 2);
                Assert.AreEqual(0, comment.Bytes.Count, "a comment emits nothing");
                Assert.AreEqual(-1, comment.Address, "and it has no address, not $0000");
                Assert.IsFalse(comment.EmitsBytes);

                // And nothing anywhere in the listing invents an address of zero.
                foreach (ListingRow row in rows)
                {
                    if (row.Bytes.Count == 0 && row.Address >= 0)
                        Assert.AreNotEqual(0, row.Address,
                            "line " + row.LineNumber + " emits nothing and was given the real address 0");
                }
            }
        }

        [TestMethod]
        public void TheTokensCarryThePalettesRoleNames()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, Programme);

                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);
                IReadOnlyList<ListingRow> rows = source.Rows(new ListingRequest(name, 0x8000, 100));

                ListingRow instruction = RowAtLine(rows, 4);
                Assert.AreEqual(ThemeRole.Mnemonic, RoleOf(instruction, "lda"));

                // The radix marker belongs to the literal: the assembler reads #$5A as
                // the number $5A, and a token that stopped at the '5' would paint the
                // marker as punctuation.
                Assert.AreEqual(ThemeRole.Number, RoleOf(instruction, "$5A"));
                Assert.AreEqual(ThemeRole.Comment, RoleOf(instruction, "; load a constant"));

                ListingRow directive = RowAtLine(rows, 7);
                Assert.AreEqual(ThemeRole.Directive, RoleOf(directive, ".byte"));
                Assert.AreEqual(ThemeRole.Number, RoleOf(directive, "$01"));

                ListingRow label = RowAtLine(rows, 3);
                Assert.AreEqual(ThemeRole.Label, RoleOf(label, "Start"));
            }
        }

        [TestMethod]
        public void ThereIsNoSixtyFiveCTwoListingTestBecauseTheListingServiceDoesNotAssembleForOne()
        {
            // Stated rather than left out: SourceListingService builds its assembler with
            // no CPU of its own, so a source written for the 65C02 does not assemble
            // through it. A test asserting that stz lists would be asserting something
            // the API does not do.
            //
            // What the shell does control is that the lexer and the listing are built
            // for the same CPU, so a source the assembler accepts classifies the same
            // way whichever of the two was handed to it.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, ".org $8000\n  lda #$5A\n  rts\n");

                AssemblerListingSource nmos = new AssemblerListingSource(new Cpu6502(), temp.Path);
                AssemblerListingSource cmos = new AssemblerListingSource(new Cpu65C02(), temp.Path);

                IReadOnlyList<ListingRow> fromNmos = nmos.Rows(new ListingRequest(name, 0x8000, 100));
                IReadOnlyList<ListingRow> fromCmos = cmos.Rows(new ListingRequest(name, 0x8000, 100));

                Assert.AreEqual(fromNmos.Count, fromCmos.Count);

                for (int i = 0; i < fromNmos.Count; i++)
                {
                    Assert.AreEqual(fromNmos[i].Tokens.Count, fromCmos[i].Tokens.Count, "row " + i);

                    for (int t = 0; t < fromNmos[i].Tokens.Count; t++)
                    {
                        Assert.AreEqual(fromNmos[i].Tokens[t].Text, fromCmos[i].Tokens[t].Text, "row " + i);
                        Assert.AreEqual(fromNmos[i].Tokens[t].Role, fromCmos[i].Tokens[t].Role, "row " + i);
                    }
                }
            }
        }

        [TestMethod]
        public void EveryRoleTheAdapterEmitsIsARoleThePaletteHas()
        {
            // The point of mapping onto ThemeRole rather than inventing names: a token
            // carrying a name the palette has never heard of is painted with whatever
            // the theme does for a missing role, which is the default. That fallback is
            // safe, and it is also silent — so the mapping is checked here instead.
            foreach (SourceTokenKind kind in Enum.GetValues(typeof(SourceTokenKind)))
            {
                string role = AssemblerListingSource.RoleFor(kind);

                Assert.IsTrue(ThemeRole.IsKnown(role),
                    "SourceTokenKind." + kind + " maps to '" + role + "', which no palette defines");
            }
        }


        // ---------------------------------------------------------------- cycles

        [TestMethod]
        public void ACycleCountTheProjectDoesNotHaveIsPassedThroughAsNothing()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, Programme);

                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);
                IReadOnlyList<ListingRow> rows = source.Rows(new ListingRequest(name, 0x8000, 100));

                ListingRow instruction = RowAtLine(rows, 4);
                Assert.IsFalse(instruction.Cycles.HasValue,
                    "no cycle table exists in this project, so a count here would be invented");

                // The blank column is the honest rendering of that, and the bytes are
                // still drawn beside it.
                Assert.AreEqual(2, instruction.Bytes.Count);
                StringAssert.Contains(ListingFormatter.Row(instruction), "A9 5A");
            }
        }

        [TestMethod]
        public void ADataDirectiveThatStartsWithAnOpcodeByteGetsNoCycleCount()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                // $A9 is the documented lda-immediate opcode, so a row that merely
                // starts with $A9 is not an lda. The adapter checks the documented
                // length against the row's own length, which is how it tells them apart.
                string name = Write(temp, ".org $8000\n  .byte $A9\n  .byte $A9, $5A\n");

                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);
                IReadOnlyList<ListingRow> rows = source.Rows(new ListingRequest(name, 0x8000, 100));

                foreach (ListingRow row in rows)
                {
                    if (row.Bytes.Count > 0)
                        Assert.IsFalse(row.Cycles.HasValue,
                            "line " + row.LineNumber + " is data, so it has no cycles");
                }
            }
        }

        // ---------------------------------------------------------------- MaxRows

        [TestMethod]
        public void TheRowCeilingIsHonoured()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                StringBuilder source = new StringBuilder(".org $8000\n");
                for (int i = 0; i < 200; i++)
                    source.Append("  lda #$01\n");

                string name = Write(temp, source.ToString());

                AssemblerListingSource adapter = new AssemblerListingSource(new Cpu6502(), temp.Path);

                int all = adapter.Rows(new ListingRequest(name, 0x8000, 0)).Count;
                Assert.IsTrue(all > 200, "the source really does list more rows than the ceiling");

                Assert.AreEqual(5, adapter.Rows(new ListingRequest(name, 0x8000, 5)).Count);
                Assert.AreEqual(1, adapter.Rows(new ListingRequest(name, 0x8000, 1)).Count);

                // A non-positive ceiling means no ceiling. Anything else would punish a
                // caller that simply does not care by showing it nothing at all.
                Assert.AreEqual(all, adapter.Rows(new ListingRequest(name, 0x8000, 0)).Count);
            }
        }

        [TestMethod]
        public void TheRowCeilingTruncatesRatherThanSkips()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, Programme);

                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);

                IReadOnlyList<ListingRow> all = source.Rows(new ListingRequest(name, 0x8000, 0));
                IReadOnlyList<ListingRow> two = source.Rows(new ListingRequest(name, 0x8000, 2));

                for (int i = 0; i < 2; i++)
                {
                    Assert.AreEqual(all[i].LineNumber, two[i].LineNumber, "row " + i);
                    Assert.AreEqual(all[i].Source, two[i].Source, "row " + i);
                }
            }
        }

        // ---------------------------------------------------------------- failures

        [TestMethod]
        public void ASourceThatDoesNotAssembleYieldsTheReasonAndNoRows()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, ".org $8000\n  lda #$5A\n  this is not an instruction\n");

                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);
                IReadOnlyList<ListingRow> rows = source.Rows(new ListingRequest(name, 0x8000, 100));

                Assert.AreEqual(0, rows.Count,
                    "rows drawn beside the reason they could not be produced are the fiction"
                    + " this project refuses");
                Assert.IsNotNull(source.LastProblem);
                StringAssert.Contains(source.LastProblem, name);
                Assert.AreEqual(source.LastProblem, source.UnavailableReason);
            }
        }

        [TestMethod]
        public void AMissingFileYieldsTheReasonAndNoRows()
        {
            AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), AppContext.BaseDirectory);
            IReadOnlyList<ListingRow> rows = source.Rows(new ListingRequest("no-such-source.asm", 0x8000, 100));

            Assert.AreEqual(0, rows.Count);
            Assert.IsNotNull(source.LastProblem, "a missing file has a name and it is 'missing'");
            StringAssert.Contains(source.LastProblem, "no-such-source.asm");
        }

        [TestMethod]
        public void ASuccessfulListingAfterAFailureClearsTheReason()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string good = Write(temp, ".org $8000\n  rts\n");
                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);

                source.Rows(new ListingRequest("no-such-source.asm", 0x8000, 100));
                Assert.IsNotNull(source.LastProblem);

                source.Rows(new ListingRequest(good, 0x8000, 100));
                Assert.IsNull(source.LastProblem,
                    "a reason left over from a previous failure would be shown next to rows");
                Assert.AreEqual(string.Empty, source.UnavailableReason);
            }
        }

        [TestMethod]
        public void ANullOrEmptyRequestIsAReasonNotACrash()
        {
            AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), AppContext.BaseDirectory);

            Assert.AreEqual(0, source.Rows(null).Count);
            Assert.AreEqual(0, source.Rows(new ListingRequest(string.Empty, 0x8000, 10)).Count);
            StringAssert.Contains(source.LastProblem, "no source file");
        }

        [TestMethod]
        public void ASourceWithNoCpuIsUnavailableAndSaysSo()
        {
            AssemblerListingSource source = new AssemblerListingSource(null, AppContext.BaseDirectory);

            Assert.IsFalse(source.IsAvailable,
                "what a listing reports comes from the opcode table, and there is none");
            Assert.AreEqual(AssemblerListingSource.NoCpuReason, source.UnavailableReason);
            StringAssert.Contains(AssemblerListingSource.NoCpuReason, "CPU");

            Assert.AreEqual(0, source.Rows(new ListingRequest("whatever.asm", 0x8000, 10)).Count);
        }

        [TestMethod]
        public void ARelativeSourceIsResolvedAgainstTheDirectoryTheShellWasGiven()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                Write(temp, ".org $8000\n  rts\n");

                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);

                Assert.AreEqual(0, source.Rows(new ListingRequest("in-this-directory.asm", 0x8000, 10)).Count);
                StringAssert.Contains(source.LastProblem, "in-this-directory.asm");

                Assert.AreEqual(0, source.Rows(new ListingRequest("elsewhere.asm", 0x8000, 10)).Count);
                StringAssert.Contains(source.LastProblem, "elsewhere.asm");
            }
        }

        // ---------------------------------------------------------------- nothing cached

        [TestMethod]
        public void EveryRequestAssemblesAgainSoAListingCannotBeStale()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, ".org $8000\n  rts\n");

                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);

                Assert.AreEqual(1, RowWithBytes(source.Rows(new ListingRequest(name, 0x8000, 100))),
                    "the source really does list a row that emits bytes");

                File.WriteAllText(Path.Combine(temp.Path, name), ".org $8000\n  lda #$5A\n  rts\n");

                IReadOnlyList<ListingRow> after = source.Rows(new ListingRequest(name, 0x8000, 100));

                Assert.AreEqual(2, RowWithBytes(after),
                    "the source changed on disk and the second request still shows the old listing");

                foreach (ListingRow row in after)
                {
                    if (row.LineNumber == 2)
                        Assert.AreEqual(2, row.Bytes.Count, "line 2 is lda #$5A, which is two bytes");
                }
            }
        }

        [TestMethod]
        public void NothingIsWrittenBesideTheSource()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = Write(temp, Programme);
                string before = string.Join("|", Directory.GetFiles(temp.Path));

                new AssemblerListingSource(new Cpu6502(), temp.Path)
                    .Rows(new ListingRequest(name, 0x8000, 100));

                string after = string.Join("|", Directory.GetFiles(temp.Path));
                Assert.AreEqual(before, after, "a listing asked for in the shell writes nothing");
            }
        }

        // ---------------------------------------------------------------- helpers

        private static int RowWithBytes(IReadOnlyList<ListingRow> rows)
        {
            int count = 0;
            foreach (ListingRow row in rows)
            {
                if (row.Bytes.Count > 0)
                    count++;
            }

            return count;
        }

        private static string RoleOf(ListingRow row, string text)
        {
            foreach (ListingToken token in row.Tokens)
            {
                if (token.Text.Trim() == text)
                    return token.Role;
            }

            Assert.Fail("no token '" + text + "' in [" + row.Source + "]");
            return null;
        }

        private static ListingRow RowAtLine(IReadOnlyList<ListingRow> rows, int lineNumber)
        {
            foreach (ListingRow row in rows)
            {
                if (row.LineNumber == lineNumber && row.Tokens.Count > 0)
                    return row;
            }

            // A label and an instruction can share a line; fall back to any row for it.
            foreach (ListingRow row in rows)
            {
                if (row.LineNumber == lineNumber)
                    return row;
            }

            Assert.Fail("no row for source line " + lineNumber);
            return null;
        }

        private static string Write(TemporaryDirectory temp, string text)
        {
            const string Name = "prog.asm";
            File.WriteAllText(Path.Combine(temp.Path, Name), text);
            return Name;
        }

        // ---------------------------------------------------------------- editor rows

        [TestMethod]
        public void EditorRowsProducesOneRowPerBufferLine()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);
                IReadOnlyList<EditorRow> rows = source.EditorRows(Programme, "prog.asm", 0x8000);

                string[] lines = Programme.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                Assert.AreEqual(lines.Length, rows.Count,
                    "one editor row per source line, no phantom extra at the end");

                Assert.AreEqual(1, rows[0].LineNumber, "line numbers start at 1");
                Assert.AreEqual(7, rows[6].LineNumber);
            }
        }

        [TestMethod]
        public void EditorRowsCarriesAddressAndBytesFromAssembly()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);
                IReadOnlyList<EditorRow> rows = source.EditorRows(Programme, "prog.asm", 0x8000);

                // Line 4 is "  lda #$5A      ; load a constant"
                EditorRow lda = rows[3];
                Assert.AreEqual("$8000", "$" + lda.Address.ToString("X4"), "the first instruction lands at the .org");
                Assert.AreEqual(2, lda.Bytes.Count, "lda #imm is two bytes");
                Assert.AreEqual(0xA9, lda.Bytes[0]);
                Assert.AreEqual(0x5A, lda.Bytes[1]);
                Assert.IsTrue(lda.EmitsBytes);

                // Line 2 is a comment — no address, no bytes
                EditorRow comment = rows[1];
                Assert.AreEqual(-1, comment.Address, "a comment has no address, not $0000");
                Assert.AreEqual(0, comment.Bytes.Count);
                Assert.IsFalse(comment.EmitsBytes);
            }
        }

        [TestMethod]
        public void EditorRowsAttachesDiagnosticsToAssemblyFailure()
        {
            const string BadSource =
                ".org $8000\n" +
                "  lda #$5A\n" +
                "  not_an_instruction\n" +
                "  rts\n";

            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);
                IReadOnlyList<EditorRow> rows = source.EditorRows(BadSource, "bad.asm", 0x8000);

                Assert.AreEqual(4, rows.Count, "all lines show even when assembly fails");

                // Every row carries no address/bytes on failure
                foreach (EditorRow row in rows)
                    Assert.AreEqual(-1, row.Address, "a failed assembly gives no addresses");

                // Line 3 has the error
                Assert.IsFalse(string.IsNullOrEmpty(rows[2].Problem),
                    "the failing line carries the assembler's diagnostic");
            }
        }

        [TestMethod]
        public void EditorRowsHandlesEmptySource()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);
                IReadOnlyList<EditorRow> rows = source.EditorRows(string.Empty, "empty.asm", 0x8000);

                Assert.AreEqual(0, rows.Count, "empty source yields no rows");
                Assert.IsTrue(source.IsAvailable, "the source is still available");
            }
        }

        [TestMethod]
        public void EditorRowsPreservesLineNumbersAcrossBlanksAndComments()
        {
            const string WithBlanks =
                ".org $8000\n" +
                "\n" +
                "; comment\n" +
                "\n" +
                "  lda #$5A\n";

            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);
                IReadOnlyList<EditorRow> rows = source.EditorRows(WithBlanks, "blanks.asm", 0x8000);

                Assert.AreEqual(5, rows.Count, "five lines including blanks");
                Assert.AreEqual(1, rows[0].LineNumber);
                Assert.AreEqual(2, rows[1].LineNumber);
                Assert.AreEqual(5, rows[4].LineNumber);

                // The lda on line 5 should have the address
                Assert.AreEqual(0x8000, rows[4].Address, "the instruction carries the .org address");
            }
        }

        [TestMethod]
        public void TextRequestProducesRowsViaListingRequest()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);
                ListingRequest request = new ListingRequest(null, 0x8000, 0);
                request.SourceText = Programme;

                IReadOnlyList<ListingRow> rows = source.Rows(request);
                Assert.IsTrue(rows.Count > 0, "a text request assembles in memory");

                ListingRow lda = RowAtLine(rows, 4);
                Assert.AreEqual(0x8000, lda.Address);
                Assert.AreEqual(0xA9, lda.Bytes[0]);
            }
        }

        [TestMethod]
        public void EditorRowsExcludesIncludedFileRowsBySourceName()
        {
            // A multi-line include whose line numbers overlap with the main file.
            // Without filtering by SourceName, the included file's lines (numbered
            // 1, 2, 3, ...) steal the main file's line numbers in the byLine
            // dictionary, and the wrong address/bytes are shown for main file lines.
            const string IncludeText =
                ".org $C000\n" +
                "  inc $02\n" +      // included line 2 — collides with main file line 2
                "  dec $02\n" +      // included line 3 — collides with main file line 3
                "  nop\n";           // included line 4 — collides with main file line 4

            const string ProgramText =
                ".include \"inc.asm\"\n" +  // main line 1
                ".org $8000\n" +            // main line 2 — .org, no bytes
                "  lda #$5A\n" +            // main line 3 — lda immediate
                "  rts\n";                  // main line 4 — rts

            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string incFile = System.IO.Path.Combine(temp.Path, "inc.asm");
                string mainFile = System.IO.Path.Combine(temp.Path, "main.asm");
                File.WriteAllText(incFile, IncludeText);

                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);
                IReadOnlyList<EditorRow> rows = source.EditorRows(ProgramText, mainFile, 0x8000);

                Assert.AreEqual(4, rows.Count);

                // Main line 2 — .org $8000 sets the origin (shown in gutter)
                Assert.AreEqual(0x8000, rows[1].Address, "line 2 is .org, origin shown");
                Assert.AreEqual(0, rows[1].Bytes.Count, "line 2 emits no bytes");

                // Main line 3 — lda #$5A at $8000
                Assert.AreEqual(0x8000, rows[2].Address, "line 3 lda assembled at $8000");
                Assert.AreEqual(2, rows[2].Bytes.Count, "lda #imm is two bytes");
                Assert.AreEqual(0xA9, rows[2].Bytes[0], "opcode is LDA #$xx");
                Assert.AreEqual(0x5A, rows[2].Bytes[1], "operand is 0x5A");

                // Main line 4 — rts at $8002
                Assert.AreEqual(0x8002, rows[3].Address, "line 4 rts assembled at $8002");
                Assert.AreEqual(1, rows[3].Bytes.Count, "rts is one byte");
                Assert.AreEqual(0x60, rows[3].Bytes[0], "opcode is RTS");
            }
        }

        [TestMethod]
        public void EditorRowsResolvesIncludesViaSourceName()
        {
            const string Equates = "foo = $5A\n";
            const string ProgramText = ".org $8000\n  lda #foo\n  rts\n";

            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string equFile = System.IO.Path.Combine(temp.Path, "inc.asm");
                string mainFile = System.IO.Path.Combine(temp.Path, "main.asm");
                File.WriteAllText(equFile, Equates);
                // Source text includes "inc.asm" — without the sourceName carrying
                // the directory path, the include cannot be resolved and the row
                // shows no bytes.
                string sourceText = ".include \"inc.asm\"\n" + ProgramText;

                AssemblerListingSource source = new AssemblerListingSource(new Cpu6502(), temp.Path);
                IReadOnlyList<EditorRow> rows = source.EditorRows(sourceText, mainFile, 0x8000);

                EditorRow lda = rows[2]; // ".org $8000\n  lda #foo\n  rts\n"
                Assert.AreEqual(0x8000, lda.Address, "the include resolved and the lda assembled");
                Assert.AreEqual(2, lda.Bytes.Count, "lda #imm is two bytes");
                Assert.AreEqual(0xA9, lda.Bytes[0]);
                Assert.AreEqual(0x5A, lda.Bytes[1]);
                Assert.IsTrue(lda.EmitsBytes);
            }
        }

        /// <summary>
        /// A directory of its own, so a test that writes a source cannot see — or be
        /// seen by — anything the rest of the suite is doing.
        /// </summary>
        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }

            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "WinASM65Listing_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(Path))
                        Directory.Delete(Path, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
