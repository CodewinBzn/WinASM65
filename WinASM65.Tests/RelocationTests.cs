using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.Expressions;
using WinASM65.Output;
using WinASM65.Segments;

namespace WinASM65.Tests
{
    /// <summary>
    /// T1: the type of the expression reaches the emitter.
    /// T2: BinaryEmitter records a relocation per site whose value depends on a symbol.
    ///
    /// The tests that matter here are the negative ones. A relocation table built by
    /// guessing — "two bytes means absolute, one byte means zero page" — passes every
    /// test that only checks that a relocation exists. So each case below pins the
    /// type, the width and the offsets, and several would fail under the guess.
    ///
    /// Operands are spelled in upper case throughout, which keeps the cases where a
    /// label could be mistaken for a mnemonic out of these tests. InstructionRegex
    /// does take the first three letters as a candidate label, so a lower-case
    /// three-letter mnemonic followed by a lower-case symbol has to be recovered by
    /// hand; the operand keeps the case it was written in, so it still matches the
    /// symbol table. See ThreeLetterOperand_KeepsItsCase.
    /// </summary>
    [TestClass]
    public class RelocationTests
    {
        #region T1 — the expression type reaches the emitter

        [TestMethod]
        public void Evaluator_ConstantExpressionUsesNoSymbol()
        {
            ExpressionResult result = new ExpressionEvaluator(new Tokenizer()).Evaluate("$05 + 1");
            Assert.IsTrue(result.IsResolved);
            Assert.IsTrue(result.IsConstant, "a literal expression reads no symbol");
            Assert.AreEqual(0, result.UsedSymbols.Count);
            Assert.IsNull(result.TargetSymbol);
        }

        [TestMethod]
        public void Evaluator_RecordsTheSymbolsItActuallyUsed()
        {
            Dictionary<string, long> symbols = new Dictionary<string, long> { { "BASE", 0x2000 }, { "DELTA", 3 } };
            ExpressionResult result = Evaluate("BASE + DELTA", new StaticResolver(symbols));

            Assert.IsTrue(result.IsResolved);
            Assert.IsFalse(result.IsConstant, "the expression read two symbols");
            Assert.AreEqual(2, result.UsedSymbols.Count);
            Assert.AreEqual("BASE", result.UsedSymbols[0].Name);
            Assert.AreEqual("DELTA", result.UsedSymbols[1].Name);
            Assert.AreEqual("BASE", result.TargetSymbol);
        }

        [TestMethod]
        public void Evaluator_AttachesTheSourceLocationToEverySymbol()
        {
            Dictionary<string, long> symbols = new Dictionary<string, long> { { "BASE", 0x2000 } };
            SourceLocation location = new SourceLocation("game.asm", 42);
            ExpressionResult result = new ExpressionEvaluator(new Tokenizer())
                .Evaluate("BASE", new StaticResolver(symbols), location);

            Assert.AreEqual(1, result.UsedSymbols.Count);
            Assert.AreEqual("game.asm", result.UsedSymbols[0].Location.FilePath);
            Assert.AreEqual(42, result.UsedSymbols[0].Location.LineNumber);
        }

        [TestMethod]
        public void Evaluator_KeepsReportingUndefinedSymbolsWithoutEvaluating()
        {
            ExpressionResult result = new ExpressionEvaluator(new Tokenizer())
                .Evaluate("MISSING + 1", new StaticResolver(new Dictionary<string, long>()));

            Assert.IsFalse(result.IsResolved);
            CollectionAssert.AreEqual(new[] { "MISSING" }, ToArray(result.UndefinedSymbols));
            // The early return is the point: no partially evaluated value is handed back.
            Assert.AreEqual(0L, result.Value.AsInteger);
            // The symbol read is still reported, so the site can be described.
            Assert.AreEqual(1, result.UsedSymbols.Count);
            Assert.AreEqual("MISSING", result.UsedSymbols[0].Name);
        }

        [TestMethod]
        public void RoleFor_MapsTheDecodedAddressingMode()
        {
            Assert.AreEqual(ExpressionRole.Immediate, AssemblerEngine.RoleFor(AddressingMode.Immediate));
            Assert.AreEqual(ExpressionRole.Address, AssemblerEngine.RoleFor(AddressingMode.Absolute));
            Assert.AreEqual(ExpressionRole.Address, AssemblerEngine.RoleFor(AddressingMode.AbsoluteX));
            Assert.AreEqual(ExpressionRole.Address, AssemblerEngine.RoleFor(AddressingMode.ZeroPageY));
            Assert.AreEqual(ExpressionRole.Address, AssemblerEngine.RoleFor(AddressingMode.IndirectY));
            Assert.AreEqual(ExpressionRole.RelativeBranch, AssemblerEngine.RoleFor(AddressingMode.Relative));
            Assert.AreEqual(ExpressionRole.None, AssemblerEngine.RoleFor(AddressingMode.Implicit));
            Assert.AreEqual(ExpressionRole.None, AssemblerEngine.RoleFor(AddressingMode.None));
            // A .if condition is a boolean, never an operand.
            Assert.AreEqual(ExpressionRole.None, AssemblerEngine.RoleFor(AddressingMode.Absolute, true));
        }

        [TestMethod]
        public void TypeFor_DependsOnRoleAndOnEmittedWidth()
        {
            Assert.AreEqual(RelocationType.Rel8, RelocationRecord.TypeFor(ExpressionRole.RelativeBranch, 1));
            Assert.AreEqual(RelocationType.Imm8, RelocationRecord.TypeFor(ExpressionRole.Immediate, 1));
            Assert.AreEqual(RelocationType.Zp8, RelocationRecord.TypeFor(ExpressionRole.Address, 1));
            Assert.AreEqual(RelocationType.Abs16, RelocationRecord.TypeFor(ExpressionRole.Address, 2));
            Assert.AreEqual(RelocationType.Data8, RelocationRecord.TypeFor(ExpressionRole.Data, 1));
            Assert.AreEqual(RelocationType.Data16, RelocationRecord.TypeFor(ExpressionRole.Data, 2));
            Assert.AreEqual(RelocationType.None, RelocationRecord.TypeFor(ExpressionRole.None, 2));
        }

        #endregion

        #region T2 — the relocation table

        [TestMethod]
        public void ImmediateConstant_RecordsNoRelocation()
        {
            AssemblyResult result = Assemble(".org $8000\nLDA #$05\n");
            Assert.IsTrue(result.Success, Describe(result));
            Assert.AreEqual(0, result.Relocations.Count, "LDA #$05 reads no symbol: there is no site to relocalise");
        }

        [TestMethod]
        public void ImmediateConstant_InAnExpression_RecordsNoRelocation()
        {
            AssemblyResult result = Assemble(".org $8000\nLDA #$05+$01\n");
            Assert.AreEqual(0, result.Relocations.Count);
        }

        [TestMethod]
        public void ImmediateSymbol_IsDistinguishedFromImmediateConstant()
        {
            // The case the plan calls out: LDA #$05 and LDA #MASK are the same shape,
            // and only the symbol tells them apart. #MASK is a value, not an address.
            AssemblyResult result = Assemble(".org $8000\nMASK = $3F\nLDA #MASK\n");
            Assert.IsTrue(result.Success, Describe(result));

            RelocationRecord record = Only(result);
            Assert.AreEqual(RelocationType.Imm8, record.Type);
            Assert.AreEqual(1, record.Width);
            Assert.AreEqual("MASK", record.TargetSymbol);
            Assert.AreEqual(0x3F, record.Value);
        }

        [TestMethod]
        public void AbsoluteAddress_IsTwoBytesWide()
        {
            AssemblyResult result = Assemble(".org $8000\nTARGET = $C000\nLDA TARGET\n");
            Assert.IsTrue(result.Success, Describe(result));
            CollectionAssert.AreEqual(new byte[] { 0xAD, 0x00, 0xC0 }, result.OutputBytes);

            RelocationRecord record = Only(result);
            Assert.AreEqual(RelocationType.Abs16, record.Type);
            Assert.AreEqual(2, record.Width);
            Assert.AreEqual(1, record.Offset, "the field follows the opcode at buffer offset 0");
            Assert.AreEqual(0x8001, record.Address);
            Assert.AreEqual("TARGET", record.TargetSymbol);
        }

        [TestMethod]
        public void AbsoluteIndexed_StaysTwoBytesWide()
        {
            AssemblyResult result = Assemble(".org $8000\nTARGET = $2000\nSTA TARGET,X\n");
            Assert.IsTrue(result.Success, Describe(result));

            RelocationRecord record = Only(result);
            Assert.AreEqual(RelocationType.Abs16, record.Type);
            Assert.AreEqual(2, record.Width, "STA $2000,X is three bytes: opcode plus a word");
        }

        /// <summary>
        /// The width decision the brief singles out. <c>LDA ZP</c> is parsed as
        /// Absolute, length 3, and Cpu6502.TryOptimizeZeroPage then shrinks it to
        /// ZeroPage, length 2. A relocation that assumed the pre-optimization length
        /// would claim two bytes at a site that holds one.
        /// </summary>
        [TestMethod]
        public void ZeroPageOptimizedAbsolute_IsOneByteWide()
        {
            AssemblyResult result = Assemble(".org $8000\nZP = $10\nLDA ZP\n");
            Assert.IsTrue(result.Success, Describe(result));
            CollectionAssert.AreEqual(new byte[] { 0xA5, 0x10 }, result.OutputBytes, "zero-page form");

            RelocationRecord record = Only(result);
            Assert.AreEqual(1, record.Width, "TryOptimizeZeroPage shrank the operand to one byte");
            Assert.AreEqual(RelocationType.Zp8, record.Type);
            Assert.AreEqual(1, record.Offset);
            Assert.AreEqual(0x8001, record.Address);
            Assert.AreEqual(0x10, record.Value);
        }

        [TestMethod]
        public void ZeroPageOptimizedIndexed_StaysOneByteWide()
        {
            // LDX has a ZeroPageY opcode ($B6), so AbsoluteY really does shrink here.
            AssemblyResult result = Assemble(".org $8000\nZP = $10\nLDX ZP,Y\n");
            Assert.IsTrue(result.Success, Describe(result));
            CollectionAssert.AreEqual(new byte[] { 0xB6, 0x10 }, result.OutputBytes, "AbsoluteY optimized to ZeroPageY");

            RelocationRecord record = Only(result);
            Assert.AreEqual(1, record.Width);
            Assert.AreEqual(RelocationType.Zp8, record.Type);
        }

        [TestMethod]
        public void AbsoluteIndexed_WithoutAZeroPageOpcode_StaysTwoBytes()
        {
            // LDA has no ZeroPageY opcode, so nothing shrinks: three bytes, width 2.
            AssemblyResult result = Assemble(".org $8000\nZP = $10\nLDA ZP,Y\n");
            Assert.IsTrue(result.Success, Describe(result));
            CollectionAssert.AreEqual(new byte[] { 0xB9, 0x10, 0x00 }, result.OutputBytes);

            RelocationRecord record = Only(result);
            Assert.AreEqual(2, record.Width);
            Assert.AreEqual(RelocationType.Abs16, record.Type);
        }

        /// <summary>
        /// The same source line, the same mnemonic, two different relocation types.
        /// Nothing but the value and the zero-page decision tells them apart, which is
        /// exactly why the type cannot be guessed from the operand size alone.
        /// </summary>
        [TestMethod]
        public void SameMnemonic_YieldsDifferentTypesDependingOnZeroPage()
        {
            AssemblyResult low = Assemble(".org $8000\nT = $10\nLDA T\n");
            AssemblyResult high = Assemble(".org $8000\nT = $1234\nLDA T\n");

            Assert.AreEqual(RelocationType.Zp8, Only(low).Type);
            Assert.AreEqual(1, Only(low).Width);
            Assert.AreEqual(RelocationType.Abs16, Only(high).Type);
            Assert.AreEqual(2, Only(high).Width);
        }

        [TestMethod]
        public void RelativeBranch_IsOneByteAndIsNotAnAddress()
        {
            AssemblyResult result = Assemble(".org $8000\nSTART:\n  NOP\n  BNE START\n");
            Assert.IsTrue(result.Success, Describe(result));
            CollectionAssert.AreEqual(new byte[] { 0xEA, 0xD0, 0xFD }, result.OutputBytes);

            RelocationRecord record = Only(result);
            Assert.AreEqual(RelocationType.Rel8, record.Type);
            Assert.AreEqual(1, record.Width);
            Assert.AreEqual("START", record.TargetSymbol);
            Assert.AreEqual(2, record.Offset, "NOP is at offset 0, the branch opcode at 1, the displacement at 2");
            Assert.AreEqual(0x8002, record.Address);
            Assert.AreEqual(-3, record.Value, "the value written is the displacement, not the target address");
        }

        [TestMethod]
        public void RelativeBranch_ForwardReference_KeepsTheRelativeType()
        {
            AssemblyResult result = Assemble(".org $8000\n  BNE LATER\n  NOP\nLATER:\n  RTS\n");
            Assert.IsTrue(result.Success, Describe(result));

            RelocationRecord record = Only(result);
            Assert.AreEqual(RelocationType.Rel8, record.Type);
            Assert.AreEqual(1, record.Width);
            Assert.AreEqual("LATER", record.TargetSymbol);
            Assert.AreEqual(1, record.Value, "one byte of displacement forward");
            CollectionAssert.AreEqual(new byte[] { 0xD0, 0x01, 0xEA, 0x60 }, result.OutputBytes);
        }

        [TestMethod]
        public void RelativeBranch_TooFar_LeavesTheSiteUnresolvedAndReportsTheError()
        {
            // The forward reference is unresolved on the first pass, so a placeholder
            // and a relocation exist. The second pass then refuses the displacement:
            // the record stays unresolved and the error is reported.
            AssemblyResult result = Assemble(".org $8000\n  BNE FAR\n" + Nops(200) + "FAR:\n  RTS\n");
            Assert.IsFalse(result.Success);

            RelocationRecord record = Only(result);
            Assert.AreEqual(RelocationType.Rel8, record.Type);
            Assert.AreEqual(1, record.Width);
            Assert.IsFalse(record.IsResolved, "the displacement was never accepted");
            Assert.AreEqual(0, result.OutputBytes[1], "the placeholder is still zero");
        }

        [TestMethod]
        public void RelativeBranch_JustInsideRange_Patches()
        {
            // 120 NOPs: the branch sits at $8000, the target at $807A, so the
            // displacement from the next instruction at $8002 is 0x78.
            AssemblyResult result = Assemble(".org $8000\n  BNE FAR\n" + Nops(120) + "FAR:\n  RTS\n");
            Assert.IsTrue(result.Success, Describe(result));
            Assert.AreEqual(0x78, Only(result).Value);
            Assert.AreEqual(0x78, result.OutputBytes[1]);
        }

        [TestMethod]
        public void AddressExpression_RecordsEverySymbolItUsed()
        {
            AssemblyResult result = Assemble(".org $8000\nBASE = $2000\nDELTA = 4\nLDA BASE+DELTA\n");
            Assert.IsTrue(result.Success, Describe(result));

            RelocationRecord record = Only(result);
            CollectionAssert.AreEqual(new[] { "BASE", "DELTA" }, ToArray(record.Symbols));
            Assert.AreEqual("BASE", record.TargetSymbol, "the first symbol read is the target");
            Assert.AreEqual(0x2004, record.Value);
        }

        [TestMethod]
        public void MultipleSites_AreRecordedInEmissionOrderWithTheirOwnOffsets()
        {
            AssemblyResult result = Assemble(
                ".org $8000\n" +
                "ONE = $10\n" +
                "TWO = $2000\n" +
                "  LDA ONE\n" +
                "  JSR TWO\n" +
                "  JSR TWO\n");

            Assert.IsTrue(result.Success, Describe(result));
            Assert.AreEqual(3, result.Relocations.Count);

            Assert.AreEqual(1, result.Relocations[0].Offset);
            Assert.AreEqual(0x8001, result.Relocations[0].Address);
            Assert.AreEqual(1, result.Relocations[0].Width);
            Assert.AreEqual(RelocationType.Zp8, result.Relocations[0].Type);

            Assert.AreEqual(3, result.Relocations[1].Offset);
            Assert.AreEqual(0x8003, result.Relocations[1].Address);
            Assert.AreEqual(2, result.Relocations[1].Width);
            Assert.AreEqual(RelocationType.Abs16, result.Relocations[1].Type);

            Assert.AreEqual(6, result.Relocations[2].Offset);
            Assert.AreEqual(0x8006, result.Relocations[2].Address);
            Assert.AreEqual(2, result.Relocations[2].Width);
            Assert.AreEqual(RelocationType.Abs16, result.Relocations[2].Type);
        }

        [TestMethod]
        public void Relocation_CarriesTheProvenanceOfItsSite()
        {
            AssemblyResult result = Assemble(".org $8000\nTARGET = $C000\n\n  LDA TARGET\n");
            Assert.IsTrue(result.Success, Describe(result));

            RelocationRecord record = Only(result);
            StringAssert.EndsWith(record.SourceFile, "reloc.asm");
            Assert.AreEqual(4, record.SourceLine, "the site is on line 4 of the fixture");
            Assert.AreEqual("TARGET", record.Expression);
        }

        [TestMethod]
        public void Relocation_CarriesTheSegmentItBelongsTo()
        {
            AssemblyResult result = Assemble(".org $8000\nTARGET = $C000\nLDA TARGET\n");
            Assert.AreEqual(0, Only(result).SegmentIndex, "direct-burn mode has one implicit segment");
        }

        [TestMethod]
        public void OffsetIsIndependentOfOriginAddress()
        {
            // The offset is a buffer offset, not an address made relative to whatever
            // OriginAddress happens to be when the record is written.
            AssemblyResult result = Assemble(
                ".org $8000\nTARGET = $C000\n  LDA TARGET\n  NOP\n");
            Assert.AreEqual(1, Only(result).Offset);
            Assert.AreEqual(0x8001, Only(result).Address);
        }

        [TestMethod]
        public void WordDirective_RecordsADataRelocation()
        {
            AssemblyResult result = Assemble(".org $8000\nTARGET = $C000\n.WORD TARGET\n");
            Assert.IsTrue(result.Success, Describe(result));

            RelocationRecord record = Only(result);
            Assert.AreEqual(RelocationType.Data16, record.Type);
            Assert.AreEqual(2, record.Width);
            Assert.AreEqual(0, record.Offset);
        }

        [TestMethod]
        public void ByteDirective_RecordsADataRelocation()
        {
            AssemblyResult result = Assemble(".org $8000\nCOUNT = $05\n.BYTE COUNT\n");
            Assert.IsTrue(result.Success, Describe(result));

            RelocationRecord record = Only(result);
            Assert.AreEqual(RelocationType.Data8, record.Type);
            Assert.AreEqual(1, record.Width);
        }

        [TestMethod]
        public void DataDirectives_RecordNothingForLiterals()
        {
            AssemblyResult result = Assemble(".org $8000\n.BYTE $01,$02\n.WORD $1234\n");
            Assert.IsTrue(result.Success, Describe(result));
            Assert.AreEqual(0, result.Relocations.Count);
        }

        [TestMethod]
        public void ImplicitInstruction_RecordsNoRelocation()
        {
            AssemblyResult result = Assemble(".org $8000\n  CLC\n  RTS\n  ASL\n");
            Assert.IsTrue(result.Success, Describe(result));
            Assert.AreEqual(0, result.Relocations.Count);
        }

        [TestMethod]
        public void OperandOutOfRange_RecordsNoRelocation()
        {
            AssemblyResult result = Assemble(".org $8000\nFAR = $12345\nJMP FAR\n");
            Assert.IsFalse(result.Success, "the operand does not fit");
            Assert.AreEqual(0, result.Relocations.Count, "a rejected operand is a diagnostic, not a site");
        }

        [TestMethod]
        public void PredefinedHardwareSymbol_IsRecordedLikeAnyOther()
        {
            Dictionary<string, long> symbols = new Dictionary<string, long> { { "PPUCTRL", 0x2000 } };
            AssemblyResult result = Assemble(".org $8000\nLDA PPUCTRL\n", null, symbols);

            Assert.IsTrue(result.Success, Describe(result));
            RelocationRecord record = Only(result);
            Assert.AreEqual("PPUCTRL", record.TargetSymbol);
            Assert.AreEqual(RelocationType.Abs16, record.Type, "PPUCTRL is $2000, so no zero-page shrink");
        }

        #endregion

        #region T2 — the cross-file JSR, which goes through the placeholder path

        [TestMethod]
        public void ForwardReference_RecordsARelocationOnThePlaceholderSite()
        {
            AssemblyResult result = Assemble(".org $8000\n  JSR LATER\nLATER:\n  RTS\n");
            Assert.IsTrue(result.Success, Describe(result));

            RelocationRecord record = Only(result);
            Assert.AreEqual(RelocationType.Abs16, record.Type);
            Assert.AreEqual(2, record.Width, "the placeholder is a word, so the site is two bytes wide");
            Assert.AreEqual(1, record.Offset);
            Assert.AreEqual(0x8001, record.Address);
            Assert.AreEqual("LATER", record.TargetSymbol);
            Assert.IsTrue(record.IsResolved, "the second pass patched it");
            Assert.AreEqual(0x8003, record.Value);
            CollectionAssert.AreEqual(new byte[] { 0x20, 0x03, 0x80, 0x60 }, result.OutputBytes);
        }

        [TestMethod]
        public void ForwardReference_InZeroPage_KeepsTheWordWidthOfThePlaceholder()
        {
            // The placeholder is emitted before the value is known, so the zero-page
            // optimization cannot run. The recorded width describes the bytes that
            // really exist, which is the whole point: the table must not claim one.
            // This is a pre-existing asymmetry with the same-file case, verified on the
            // base commit; the relocation table is required to describe reality.
            AssemblyResult result = Assemble(".org $8000\n  JSR LATER\nLATER = $10\n");
            Assert.IsTrue(result.Success, Describe(result));

            RelocationRecord record = Only(result);
            Assert.AreEqual(2, record.Width);
            Assert.AreEqual(3, result.OutputBytes.Length);
            CollectionAssert.AreEqual(new byte[] { 0x20, 0x10, 0x00 }, result.OutputBytes);
        }

        [TestMethod]
        public void UndefinedSymbol_LeavesTheSiteUnresolved()
        {
            // Permissive by default, on purpose: this engine also runs as one
            // phase of a multi-file build, where the name may come from a file
            // read later. AssembleStrict below is the standalone case.
            AssemblyResult result = Assemble(".org $8000\n  JSR NOWHERE\n");
            Assert.IsTrue(result.Success, Describe(result), "the assembler does not fail on an unresolved cross-file JSR");

            RelocationRecord record = Only(result);
            Assert.AreEqual(RelocationType.Abs16, record.Type);
            Assert.IsFalse(record.IsResolved, "nothing patched it");
            Assert.AreEqual("NOWHERE", record.TargetSymbol);
        }

        /// <summary>
        /// Assembled on its own, a name nobody defines has nowhere to come from.
        /// The placeholder left in the buffer is a zero, so the file builds and
        /// the machine is wrong: this is the case the plan calls a deferred
        /// resolution error, and the only way to see it is here.
        /// </summary>
        [TestMethod]
        public void UneAssemblageSeulRefuseUnSymboleQuePersonneNeDefinit()
        {
            AssemblyResult result = AssembleStrict(".org $8000\n  JSR NOWHERE\n");

            Assert.IsFalse(result.Success, "un nom indefini ne peut pas disparaitre en silence");
            StringAssert.Contains(Describe(result), "NOWHERE", "le nom est cite");
        }

        /// <summary>
        /// The line matters as much as the name. An error that only says which
        /// symbol is wrong sends the reader looking through the whole file.
        /// </summary>
        [TestMethod]
        public void LeSymboleIndefiniEstSignaleALaLigneQuiLEcrit()
        {
            AssemblyResult result = AssembleStrict(".org $8000\n  NOP\n  NOP\n  JSR NOWHERE\n");

            Assert.IsFalse(result.Success);
            StringAssert.Contains(Describe(result), "Line 4", "la ligne du source est reprise");
        }

        /// <summary>
        /// A name another module provides is undefined on purpose here: the
        /// linker resolves it. Refusing it would make a module impossible to
        /// assemble on its own, which is the point of a module.
        /// </summary>
        [TestMethod]
        public void UnSymboleDeclareParImportNEstPasRefuse()
        {
            AssemblyResult result = AssembleStrict(
                ".org $8000\n  .import DrawTile gfx\n  JSR DrawTile\n");

            Assert.IsTrue(result.Success, Describe(result));
        }

        /// <summary>
        /// The real cross-file case, through MultiSegmentOrchestrator: the symbol is
        /// defined in a file assembled later, so the site only ever exists as a
        /// placeholder until the orchestrator reloads the .o and resolves it.
        /// </summary>
        [TestMethod]
        public void CrossFileReference_PatchesThroughTheSegmentOrchestrator()
        {
            string dir = Path.Combine(Path.GetTempPath(), "WinASM65XFile_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string aPath = Path.Combine(dir, "a.asm");
                string bPath = Path.Combine(dir, "b.asm");
                File.WriteAllText(aPath, ".org $8000\n  JSR TGT\n  RTS\n");
                File.WriteAllText(bPath, "TGT = $C123\n");

                // The orchestrator takes absolute paths; the command line resolves the
                // names in config.json against the config's own directory.
                Segment[] segments = new[]
                {
                    new Segment { FileName = bPath, OutputFile = Path.Combine(dir, "b.o"), Dependencies = new string[0] },
                    new Segment { FileName = aPath, OutputFile = Path.Combine(dir, "a.o"), Dependencies = new[] { bPath } }
                };

                MultiSegmentResult result = new MultiSegmentOrchestrator(() => new AssemblerEngine()).AssembleSegments(segments);
                Assert.IsTrue(result.Success, "orchestrated build: " + Describe(result.Diagnostics));
                CollectionAssert.AreEqual(new byte[] { 0x20, 0x23, 0xC1, 0x60 },
                    File.ReadAllBytes(Path.Combine(dir, "a.o")), "JSR $C123 then RTS");
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void Emitter_ResetClearsTheRelocationTable()
        {
            BinaryEmitter emitter = new BinaryEmitter();
            emitter.RecordRelocation(new RelocationRecord(0, "", 0, 0x8001, 2, RelocationType.Abs16,
                new[] { "X" }, new SourceLocation("a.asm", 1), "X"));
            Assert.AreEqual(1, emitter.Relocations.Count);

            emitter.Reset();
            Assert.AreEqual(0, emitter.Relocations.Count);
        }

        [TestMethod]
        public void Emitter_ResolvesARecordedSiteBySegmentAndOffset()
        {
            BinaryEmitter emitter = new BinaryEmitter();
            emitter.RecordRelocation(new RelocationRecord(0, "", 4, 0x8005, 2, RelocationType.Abs16,
                new[] { "X" }, new SourceLocation("a.asm", 1), "X"));

            RelocationRecord found;
            Assert.IsTrue(emitter.TryGetRelocation(0, 4, out found));
            Assert.IsFalse(found.IsResolved);

            RelocationRecord other;
            Assert.IsFalse(emitter.TryGetRelocation(1, 4, out other), "a different segment is a different site");
            Assert.IsNull(other);

            emitter.ResolveRelocation(0, 4, 0x1234);
            Assert.IsTrue(found.IsResolved);
            Assert.AreEqual(0x1234, found.Value);
        }

        [TestMethod]
        public void Relocation_ExposesEverySymbolInTheOrderRead()
        {
            AssemblyResult result = Assemble(".org $8000\nA = $10\nB = $20\nC = $30\nLDA A+B+C\n");
            RelocationRecord record = Only(result);
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, ToArray(record.Symbols));
            Assert.AreEqual(0x60, record.Value);
        }

        #endregion

        #region Pre-existing behaviour, pinned so a later fix is visible

        /// <summary>
        /// Pins the three-letter operand case, which used to be broken.
        ///
        /// ParseLine tries LabelDeclareRegex and ConstantRegex before
        /// InstructionRegex, and ConstantRegex is not anchored: its value group is
        /// (.+), so on <c>tgt = $1234</c> it matches with the "label" being the whole
        /// line up to the "=" and the "value" being $1234. ParseLine then calls
        /// HandleConstant("tgt ", "$1234"), which trims to "tgt" and calls
        /// AddSymbol — fine. The break was in HandleInstruction: InstructionRegex has
        /// an optional leading label group, so on <c>lda tgt</c> it takes "lda" as
        /// the label and "tgt" as the opcode. HandleInstruction then finds that
        /// "LDA" is an instruction, so it swaps the two back — but it swapped the
        /// upper-cased copy, turning the operand into "TGT". The symbol table is
        /// case-sensitive, so the lookup missed and the operand was silently left as
        /// a placeholder.
        ///
        /// The trigger is any three-letter mnemonic followed by a three-letter
        /// operand, because only then can the label group swallow the mnemonic. It
        /// was invisible at assembly time and only showed up at link time as an
        /// unresolvable symbol, which is what makes it worth a test of its own.
        /// </summary>
        [TestMethod]
        public void ThreeLetterOperand_KeepsItsCase()
        {
            AssemblyResult upper = Assemble(".org $8000\nTGT = $1234\nlda tgt\n");
            AssemblyResult lower = Assemble(".org $8000\ntgt = $1234\nLDA tgt\n");

            Assert.AreEqual(0x1234, Only(upper).Value,
                "a lower-case operand matches a symbol defined in upper case");
            Assert.AreEqual(0x1234, Only(lower).Value,
                "a lower-case operand must match a symbol defined in lower case too");
            Assert.IsTrue(Only(lower).IsResolved,
                "l operande garde la casse ecrite, donc il rejoint le symbole");
        }

        #endregion

        private static string Nops(int count)
        {
            string text = string.Empty;
            for (int i = 0; i < count; i++)
                text += "  NOP\n";
            return text;
        }

        private static RelocationRecord Only(AssemblyResult result)
        {
            Assert.AreEqual(1, result.Relocations.Count,
                "expected exactly one relocation, got " + Describe(result.Relocations));
            return result.Relocations[0];
        }

        private static string Describe(IReadOnlyList<RelocationRecord> records)
        {
            if (records == null || records.Count == 0)
                return "none";
            string text = string.Empty;
            foreach (RelocationRecord record in records)
                text += record.ToString() + " | ";
            return text;
        }

        private static string Describe(IReadOnlyList<Diagnostic> diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0)
                return "no diagnostics";
            string text = string.Empty;
            foreach (Diagnostic diag in diagnostics)
                text += diag.ToString() + " | ";
            return text;
        }

        private static string Describe(AssemblyResult result)
        {
            return Describe(result.Diagnostics);
        }

        private static ExpressionResult Evaluate(string expression, ISymbolResolver resolver)
        {
            return new ExpressionEvaluator(new Tokenizer()).Evaluate(expression, resolver);
        }

        private static string[] ToArray(IReadOnlyList<string> values)
        {
            string[] copy = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
                copy[i] = values[i];
            return copy;
        }

        private static AssemblyResult Assemble(string content, string fileName = null,
            IDictionary<string, long> predefined = null)
        {
            return AssembleWith(content, fileName, predefined, false);
        }

        private static AssemblyResult AssembleStrict(string content, string fileName = null,
            IDictionary<string, long> predefined = null)
        {
            return AssembleWith(content, fileName, predefined, true);
        }

        private static AssemblyResult AssembleWith(string content, string fileName,
            IDictionary<string, long> predefined, bool strict)
        {
            string dir = Path.Combine(Path.GetTempPath(), "WinASM65Reloc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string source = Path.Combine(dir, fileName ?? "reloc.asm");
                File.WriteAllText(source, content);
                return new AssemblerEngine(predefinedSymbols: predefined, reportUndefinedSymbols: strict)
                    .Assemble(source, Path.Combine(dir, "reloc.o"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private sealed class StaticResolver : ISymbolResolver
        {
            private readonly Dictionary<string, long> _symbols;

            public StaticResolver(Dictionary<string, long> symbols)
            {
                _symbols = symbols;
            }

            public bool TryResolveSymbol(string name, out Value value)
            {
                long found;
                if (_symbols.TryGetValue(name, out found))
                {
                    value = new Value(found);
                    return true;
                }
                value = default(Value);
                return false;
            }
        }
    }
}
