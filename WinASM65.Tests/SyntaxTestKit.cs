using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Cpu;

namespace WinASM65.Tests
{
    /// <summary>Assembles a source text in memory for the syntax tests.</summary>
    internal static class SyntaxTestKit
    {
        public static AssemblyResult Assemble(string source, string cpu = "6502")
        {
            return new AssemblerEngine(CpuFactory.Create(cpu)).AssembleSource(source, null, "test.asm");
        }

        /// <summary>The bytes of a source that must assemble without any diagnostic.</summary>
        public static byte[] Bytes(string source, string cpu = "6502")
        {
            AssemblyResult result = Assemble(source, cpu);
            Assert.AreEqual(0, result.Diagnostics.Count, Describe(result));
            Assert.IsTrue(result.Success, Describe(result));
            return result.OutputBytes;
        }

        /// <summary>The errors of a source that must fail, as text.</summary>
        public static string Errors(string source, string cpu = "6502")
        {
            AssemblyResult result = Assemble(source, cpu);
            Assert.IsFalse(result.Success, "the source was expected to fail: " + Hex(result.OutputBytes));
            return Describe(result);
        }

        public static string Describe(AssemblyResult result)
        {
            return string.Join(" | ", result.Diagnostics.Select(d => d.ToString()));
        }

        public static string Hex(byte[] bytes)
        {
            return bytes == null ? "(none)" : BitConverter.ToString(bytes).Replace("-", " ");
        }

        public static void AreBytes(string expected, byte[] actual)
        {
            Assert.AreEqual(expected, Hex(actual));
        }
    }
}
