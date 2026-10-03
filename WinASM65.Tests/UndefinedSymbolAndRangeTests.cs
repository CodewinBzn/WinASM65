using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Output;

namespace WinASM65.Tests
{
    /// <summary>
    /// The two answers the assembler used to give without saying anything.
    /// <para>
    /// A name nobody defines became zero, so <c>JMP NEVER_DEFINED</c> assembled to
    /// <c>4C 00 00</c> and a typo in a label produced a ROM that branched to
    /// $0000 with no diagnostic and no reported problem. A value too wide for
    /// its field kept its low octet, so <c>.byte 300</c> assembled to <c>$2C</c>
    /// and the image built clean while behaving in a way the source never
    /// described -- which is worse in a <c>.byte</c> than anywhere else, because
    /// that is the instruction byte of every LDA/STA operand the NES sources write.
    /// </para>
    /// <para>
    /// The tests that keep the assembler usable are here too, and they matter more
    /// than the two that close the hole. A label used on line 10 and defined on
    /// line 200 is not an error, and a symbol defined in a file included later is
    /// not an error; refusing either would break correct programs, which is a
    /// worse failure than the one being fixed here.
    /// </para>
    /// </summary>
    [TestClass]
    public class UndefinedSymbolAndRangeTests
    {
        #region An undefined symbol is reported

        [TestMethod]
        public void UndefinedSymbolInOperandPositionIsReported()
        {
            AssemblyResult result = Assemble(".org $C000\nJMP NEVER_DEFINED_ANYWHERE\n");

            Assert.IsFalse(result.Success, "a name nobody defines must not assemble clean");
            AssertUndefinedSymbol(result, "NEVER_DEFINED_ANYWHERE");
        }

        [TestMethod]
        public void TheUndefinedSymbolIsReportedAtTheLineThatWritesIt()
        {
            AssemblyResult result = Assemble(".org $C000\n        nop\n        nop\nJMP NEVER_DEFINED_ANYWHERE\n");

            Assert.IsFalse(result.Success);
            AssertUndefinedSymbolAt(result, "NEVER_DEFINED_ANYWHERE", 4);
        }

        [TestMethod]
        public void TheUndefinedSymbolIsReportedInTheFileThatWritesIt()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "typo.asm");
                File.WriteAllText(source, ".org $C000\nJMP NEVER_DEFINED_ANYWHERE\n");

                AssemblyResult result = new AssemblerEngine().Assemble(source, Path.Combine(temp.Path, "typo.o"));

                Assert.IsFalse(result.Success);
                string where = FileNameOf(WhereOf(result, "NEVER_DEFINED_ANYWHERE"));
                Assert.AreEqual("typo.asm", where,
                    "the diagnostic must name the file the user was looking at");
            }
        }

        [TestMethod]
        public void UndefinedSymbolInImmediatePositionIsReported()
        {
            AssemblyResult result = Assemble(".org $C000\nLDA #NEVER_DEFINED_ANYWHERE\n");

            Assert.IsFalse(result.Success);
            AssertUndefinedSymbol(result, "NEVER_DEFINED_ANYWHERE");
        }

        [TestMethod]
        public void UndefinedSymbolInAByteFieldIsReported()
        {
            AssemblyResult result = Assemble(".org $C000\n.byte NEVER_DEFINED_ANYWHERE\n");

            Assert.IsFalse(result.Success);
            AssertUndefinedSymbol(result, "NEVER_DEFINED_ANYWHERE");
        }

        [TestMethod]
        public void UndefinedSymbolInsideAnExpressionIsReportedByName()
        {
            AssemblyResult result = Assemble(".org $C000\nLDA #NEVER_DEFINED_ANYWHERE + 1\n");

            Assert.IsFalse(result.Success);
            AssertUndefinedSymbol(result, "NEVER_DEFINED_ANYWHERE");
        }

        [TestMethod]
        public void TheListingApiReportsAnUndefinedSymbolRatherThanSucceeding()
        {
            // The path a user interface goes through. It built its own options
            // object and never asked for this, so the pane showed the assembled
            // bytes of a JMP to $0000 and reported no problem at all.
            SourceListing listing = new SourceListingService()
                .ListSourceText(".org $C000\nJMP NEVER_DEFINED_ANYWHERE\n", "typo.asm");

            Assert.IsFalse(listing.Success, "the listing said the source assembled");
            AssertUndefinedSymbol(listing.Assembly, "NEVER_DEFINED_ANYWHERE");
        }

        #endregion

        #region A forward reference is still accepted

        [TestMethod]
        public void AForwardReferenceResolvesToItsDefinition()
        {
            AssemblyResult result = Assemble(".org $C000\n        jmp Later\nLater:\n        nop\n");

            Assert.IsTrue(result.Success, Describe(result));
            Assert.AreEqual(0, result.Diagnostics.Count, Describe(result));
            CollectionAssert.AreEqual(new byte[] { 0x4C, 0x03, 0xC0, 0xEA }, result.OutputBytes);
        }

        [TestMethod]
        public void AForwardReferenceInImmediatePositionResolves()
        {
            AssemblyResult result = Assemble(".org $C000\n        lda #Value\nValue   = $5A\n");

            Assert.IsTrue(result.Success, Describe(result));
            CollectionAssert.AreEqual(new byte[] { 0xA9, 0x5A }, result.OutputBytes);
        }

        [TestMethod]
        public void AForwardReferenceInAByteFieldResolves()
        {
            // "<Later" is how a .byte gets half an address, so the field holds the
            // low octet of a label defined two bytes further down.
            AssemblyResult result = Assemble(".org $C000\n        .byte <Later\nLater:\n");

            Assert.IsTrue(result.Success, Describe(result));
            CollectionAssert.AreEqual(new byte[] { 0x01 }, result.OutputBytes);
        }

        [TestMethod]
        public void AForwardReferenceInAWordFieldResolves()
        {
            // The .word occupies $C000 and $C001, so Later is $C002.
            AssemblyResult result = Assemble(".org $C000\n        .word Later\nLater:\n");

            Assert.IsTrue(result.Success, Describe(result));
            CollectionAssert.AreEqual(new byte[] { 0x02, 0xC0 }, result.OutputBytes);
        }

        [TestMethod]
        public void AForwardReferenceInTheSecondByteFieldOfALineResolves()
        {
            AssemblyResult result = Assemble(".org $C000\n        .byte 0, <Later\nLater:\n");

            Assert.IsTrue(result.Success, Describe(result));
            CollectionAssert.AreEqual(new byte[] { 0x00, 0x02 }, result.OutputBytes);
        }

        [TestMethod]
        public void AForwardReferenceTwoHundredLinesDownResolves()
        {
            string source = ".org $C000\n        jmp Destination\n";
            source += new string('\n', 198);
            source += "Destination:\n";

            AssemblyResult result = Assemble(source);

            Assert.IsTrue(result.Success, Describe(result));
            CollectionAssert.AreEqual(new byte[] { 0x4C, 0x03, 0xC0 }, result.OutputBytes);
        }

        [TestMethod]
        public void ASymbolDefinedInALaterIncludeIsAccepted()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                File.WriteAllText(Path.Combine(temp.Path, "late.asm"), "Table:\n        nop\n");
                File.WriteAllText(Path.Combine(temp.Path, "main.asm"),
                    ".org $C000\n        jmp Table\n        .include \"late.asm\"\n");

                AssemblyResult result = new AssemblerEngine()
                    .Assemble(Path.Combine(temp.Path, "main.asm"), Path.Combine(temp.Path, "main.o"));

                Assert.IsTrue(result.Success, Describe(result));
                CollectionAssert.AreEqual(new byte[] { 0x4C, 0x03, 0xC0, 0xEA }, result.OutputBytes);
            }
        }

        [TestMethod]
        public void ASymbolDefinedInAnEarlierIncludeIsAccepted()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                File.WriteAllText(Path.Combine(temp.Path, "late.asm"), "Table:\n        nop\n");
                File.WriteAllText(Path.Combine(temp.Path, "main.asm"),
                    ".org $C000\n        .include \"late.asm\"\n        jmp Table\n");

                AssemblyResult result = new AssemblerEngine()
                    .Assemble(Path.Combine(temp.Path, "main.asm"), Path.Combine(temp.Path, "main.o"));

                Assert.IsTrue(result.Success, Describe(result));
                CollectionAssert.AreEqual(new byte[] { 0xEA, 0x4C, 0x00, 0xC0 }, result.OutputBytes);
            }
        }

        [TestMethod]
        public void ANameDeclaredWithImportIsNotUndefined()
        {
            // A module is meant to be assembled on its own, and the linker is what
            // resolves its imports. Refusing them would make a module impossible to
            // build, which is the whole point of a module.
            AssemblyResult result = Assemble(".org $C000\n        .import DrawTile gfx\n        jsr DrawTile\n");

            Assert.IsTrue(result.Success, Describe(result));
        }

        #endregion

        #region A value too wide is reported

        [TestMethod]
        public void AByteFieldTooWideIsReportedRatherThanTruncated()
        {
            AssemblyResult result = Assemble(".org $C000\n.byte 300\n");

            Assert.IsFalse(result.Success, "300 does not fit a byte and must not become $2C");
            AssertOutOfRange(result, "300");
        }

        [TestMethod]
        public void AByteFieldTooWideEmitsAZeroAndKeepsTheAddressesAfterIt()
        {
            AssemblyResult result = Assemble(".org $C000\n.byte 300\n        nop\n");

            Assert.IsFalse(result.Success);
            CollectionAssert.AreEqual(new byte[] { 0x00, 0xEA }, result.OutputBytes,
                "the field is still one byte wide, so the instruction after it is still at $C001");
        }

        [TestMethod]
        public void ANegativeByteFieldTooSmallIsReported()
        {
            AssemblyResult result = Assemble(".org $C000\n.byte -129\n");

            Assert.IsFalse(result.Success);
            AssertOutOfRange(result, "-129");
        }

        [TestMethod]
        public void AWordFieldTooWideIsReportedRatherThanTruncated()
        {
            AssemblyResult result = Assemble(".org $C000\n.word 65536\n");

            Assert.IsFalse(result.Success, "65536 does not fit a word and must not become $0000");
            AssertOutOfRange(result, "65536");
        }

        [TestMethod]
        public void AnImmediateTooWideIsReported()
        {
            AssemblyResult result = Assemble(".org $C000\nLDA #300\n");

            Assert.IsFalse(result.Success);
            AssertOutOfRange(result, "300");
        }

        [TestMethod]
        public void AnAbsoluteAddressTooWideIsReported()
        {
            AssemblyResult result = Assemble(".org $C000\nJMP $12345\n");

            Assert.IsFalse(result.Success);
            AssertOutOfRange(result, "74565");
        }

        [TestMethod]
        public void AForwardReferenceResolvingToAValueTooWideIsReported()
        {
            AssemblyResult result = Assemble(".org $C000\n        lda #Later\nLater   = 300\n");

            Assert.IsFalse(result.Success);
            AssertOutOfRange(result, "300");
        }

        [TestMethod]
        public void AForwardReferenceInAByteFieldResolvingToAValueTooWideIsReported()
        {
            AssemblyResult result = Assemble(".org $C000\n        .byte Later\nLater   = 300\n");

            Assert.IsFalse(result.Success, "the second pass must refuse it too, not narrow it");
            AssertOutOfRange(result, "300");
        }

        #endregion

        #region The boundary values keep working

        [TestMethod]
        public void AByteFieldAcceptsMinus128And255()
        {
            AssemblyResult result = Assemble(".org $C000\n.byte -128, 255\n");

            Assert.IsTrue(result.Success, Describe(result));
            Assert.AreEqual(0, result.Diagnostics.Count, Describe(result));
            CollectionAssert.AreEqual(new byte[] { 0x80, 0xFF }, result.OutputBytes);
        }

        [TestMethod]
        public void AWordFieldAccepts0And65535()
        {
            AssemblyResult result = Assemble(".org $C000\n.word 0, 65535\n");

            Assert.IsTrue(result.Success, Describe(result));
            Assert.AreEqual(0, result.Diagnostics.Count, Describe(result));
            CollectionAssert.AreEqual(new byte[] { 0x00, 0x00, 0xFF, 0xFF }, result.OutputBytes);
        }

        [TestMethod]
        public void AnImmediateAcceptsMinus128And255()
        {
            AssemblyResult result = Assemble(".org $C000\nLDA #-128\nLDA #255\n");

            Assert.IsTrue(result.Success, Describe(result));
            Assert.AreEqual(0, result.Diagnostics.Count, Describe(result));
            CollectionAssert.AreEqual(new byte[] { 0xA9, 0x80, 0xA9, 0xFF }, result.OutputBytes);
        }

        [TestMethod]
        public void AnAbsoluteAddressAccepts0And65535()
        {
            AssemblyResult result = Assemble(".org $C000\nJMP $0000\nJMP $FFFF\n");

            Assert.IsTrue(result.Success, Describe(result));
            Assert.AreEqual(0, result.Diagnostics.Count, Describe(result));
            CollectionAssert.AreEqual(new byte[] { 0x4C, 0x00, 0x00, 0x4C, 0xFF, 0xFF }, result.OutputBytes);
        }

        [TestMethod]
        public void AByteFieldRejectsOnlyWhatIsOutsideMinus128To255()
        {
            foreach (string value in new[] { "-129", "256" })
            {
                AssemblyResult result = Assemble(".org $C000\n.byte " + value + "\n");
                Assert.IsFalse(result.Success, value + " does not fit a byte");
            }

            foreach (string value in new[] { "-128", "0", "255" })
            {
                AssemblyResult result = Assemble(".org $C000\n.byte " + value + "\n");
                Assert.IsTrue(result.Success, value + " fits a byte: " + Describe(result));
            }
        }

        [TestMethod]
        public void AWordFieldRejectsOnlyWhatIsOutside0To65535()
        {
            // The signed low half is in range on purpose, the same rule the
            // instruction path uses: .word -1 is FFFF, and refusing it would make
            // the two widths disagree about the same value.
            foreach (string value in new[] { "65536", "-32769" })
            {
                AssemblyResult result = Assemble(".org $C000\n.word " + value + "\n");
                Assert.IsFalse(result.Success, value + " does not fit a word");
            }

            foreach (string value in new[] { "0", "-1", "65535" })
            {
                AssemblyResult result = Assemble(".org $C000\n.word " + value + "\n");
                Assert.IsTrue(result.Success, value + " fits a word: " + Describe(result));
            }
        }

        #endregion

        #region helpers

        private static void AssertUndefinedSymbol(AssemblyResult result, string name)
        {
            Assert.IsFalse(string.IsNullOrEmpty(WhereOf(result, name)),
                "no diagnostic names " + name + ". Diagnostics: " + Describe(result));
        }

        private static void AssertUndefinedSymbolAt(AssemblyResult result, string name, int line)
        {
            Diagnostic diagnostic = DiagnosticNaming(result, name);
            Assert.IsNotNull(diagnostic, "no diagnostic names " + name + ". Diagnostics: " + Describe(result));
            Assert.AreEqual(line, diagnostic.Location.LineNumber,
                "the line is the one that writes the name, not the last line of the file");
        }

        private static Diagnostic DiagnosticNaming(AssemblyResult result, string name)
        {
            if (result == null || result.Diagnostics == null)
                return null;

            foreach (Diagnostic diagnostic in result.Diagnostics)
            {
                if (diagnostic.Message != null && diagnostic.Message.Contains(name))
                    return diagnostic;
            }
            return null;
        }

        private static string WhereOf(AssemblyResult result, string name)
        {
            Diagnostic diagnostic = DiagnosticNaming(result, name);
            return diagnostic == null ? string.Empty : diagnostic.Location.ToString();
        }

        private static void AssertOutOfRange(AssemblyResult result, string value)
        {
            foreach (Diagnostic diagnostic in result.Diagnostics)
            {
                if (diagnostic.Message.Contains("out of range") && diagnostic.Message.Contains(value))
                    return;
            }
            Assert.Fail("No out-of-range diagnostic for '" + value + "'. Diagnostics: " + Describe(result));
        }

        private static string FileNameOf(string location)
        {
            int colon = location.LastIndexOf(':');
            return colon < 0 ? location : Path.GetFileName(location.Substring(0, colon));
        }

        private static string Describe(AssemblyResult result)
        {
            if (result == null || result.Diagnostics == null || result.Diagnostics.Count == 0)
                return "no diagnostics";

            string text = string.Empty;
            foreach (Diagnostic diagnostic in result.Diagnostics)
                text += diagnostic.ToString() + " | ";
            return text;
        }

        private static AssemblyResult Assemble(string content)
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "unit.asm");
                File.WriteAllText(source, content);
                return new AssemblerEngine().Assemble(source, Path.Combine(temp.Path, "unit.o"));
            }
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }

            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "WinASM65Silent_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public void Dispose()
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, true);
            }
        }

        #endregion
    }
}