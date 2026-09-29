using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;

namespace WinASM65.Tests
{
    [TestClass]
    public class OperandRangeTests
    {
        [TestMethod]
        public void InByteRange_Accepte255EtMoins128()
        {
            Assert.IsTrue(Value.InByteRange(255));
            Assert.IsTrue(Value.InByteRange(-128));
            Assert.IsTrue(Value.InByteRange(0));
        }

        [TestMethod]
        public void InByteRange_Rejette256EtMoins129()
        {
            Assert.IsFalse(Value.InByteRange(256));
            Assert.IsFalse(Value.InByteRange(-129));
            Assert.IsFalse(Value.InByteRange(300));
        }

        [TestMethod]
        public void InWordRange_Accepte65535EtMoins32768()
        {
            Assert.IsTrue(Value.InWordRange(65535));
            Assert.IsTrue(Value.InWordRange(-32768));
            Assert.IsTrue(Value.InWordRange(0));
        }

        [TestMethod]
        public void InWordRange_Rejette65536EtMoins32769()
        {
            Assert.IsFalse(Value.InWordRange(65536));
            Assert.IsFalse(Value.InWordRange(-32769));
        }

        [TestMethod]
        public void Immediate_HorsPlage_EmetUneErreur()
        {
            AssemblyResult result = Assemble(".org $8000\nlda #300\n");
            Assert.IsFalse(result.Success);
            AssertOutOfRange(result, "300");
            CollectionAssert.AreEqual(new byte[] { 0xA9, 0x00 }, result.OutputBytes);
        }

        [TestMethod]
        public void Immediate_Negatif_EmetFF()
        {
            AssemblyResult result = Assemble(".org $8000\nlda #-1\n");
            Assert.IsTrue(result.Success, Describe(result));
            CollectionAssert.AreEqual(new byte[] { 0xA9, 0xFF }, result.OutputBytes);
        }

        [TestMethod]
        public void Immediat_256_EstValide()
        {
            AssemblyResult result = Assemble(".org $8000\nlda #255\n");
            Assert.IsTrue(result.Success, Describe(result));
            CollectionAssert.AreEqual(new byte[] { 0xA9, 0xFF }, result.OutputBytes);
        }

        [TestMethod]
        public void Absolu_HorsPlage_EmetUneErreur()
        {
            AssemblyResult result = Assemble(".org $8000\njmp $12345\n");
            Assert.IsFalse(result.Success);
            AssertOutOfRange(result, "74565");
            CollectionAssert.AreEqual(new byte[] { 0x4C, 0x00, 0x00 }, result.OutputBytes);
        }

        [TestMethod]
        public void SymboleEnAvant_HorsPlage_EmetUneErreur()
        {
            AssemblyResult result = Assemble(".org $8000\nlda #later\nlater = 300\n");
            Assert.IsFalse(result.Success, Describe(result) + " bytes=" + BitConverter.ToString(result.OutputBytes));
            AssertOutOfRange(result, "300");
            CollectionAssert.AreEqual(new byte[] { 0xA9, 0x00 }, result.OutputBytes);
        }

        [TestMethod]
        public void ZeroPage_Optimise_NEstPasAffecte()
        {
            AssemblyResult result = Assemble(".org $8000\nlda $10\n");
            Assert.IsTrue(result.Success, Describe(result));
            CollectionAssert.AreEqual(new byte[] { 0xA5, 0x10 }, result.OutputBytes);
        }

        [TestMethod]
        public void OperandeDansLaPlage_NEmetPasDeDiagnostic()
        {
            AssemblyResult result = Assemble(".org $8000\nlda #$01\nsta $c000\njmp target\ntarget:\n");
            Assert.IsTrue(result.Success, Describe(result));
            Assert.AreEqual(0, result.Diagnostics.Count);
        }

        private static void AssertOutOfRange(AssemblyResult result, string expectedValue)
        {
            foreach (Diagnostic diag in result.Diagnostics)
            {
                if (diag.Message.Contains("out of range") && diag.Message.Contains(expectedValue))
                    return;
            }
            Assert.Fail("No out-of-range diagnostic containing '" + expectedValue + "'. Diagnostics: " + Describe(result));
        }

        private static string Describe(AssemblyResult result)
        {
            if (result.Diagnostics.Count == 0)
                return "no diagnostics";
            string text = string.Empty;
            foreach (Diagnostic diag in result.Diagnostics)
                text += diag.ToString() + " | ";
            return text;
        }

        private static AssemblyResult Assemble(string content)
        {
            string dir = Path.Combine(Path.GetTempPath(), "WinASM65Range_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string source = Path.Combine(dir, "range.asm");
                File.WriteAllText(source, content);
                return new AssemblerEngine().Assemble(source, Path.Combine(dir, "range.o"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
