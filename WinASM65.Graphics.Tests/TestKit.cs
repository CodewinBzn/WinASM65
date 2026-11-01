using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Graphics.Assets;
using WinASM65.Graphics.Systems;

namespace WinASM65.Graphics.Tests
{
    /// <summary>A temporary folder per test, and the real assembler to check what the exporters write.</summary>
    internal sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winasm65-graphics-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; private set; }

        public string File(string name)
        {
            return System.IO.Path.Combine(Path, name);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
            }
        }
    }

    internal static class TestKit
    {
        public static IGraphicsSystem Nes
        {
            get { return GraphicsSystems.For("nes"); }
        }

        /// <summary>
        /// Assembles <c>.org $8000</c>, an <c>.include</c> of the generated file, then
        /// any extra lines, with WinASM65 itself; the bytes, with no diagnostic allowed.
        /// </summary>
        public static byte[] Assemble(string folder, string include, string extra = "")
        {
            string main = Path.Combine(folder, "main_test.asm");
            File.WriteAllText(main, ".org $8000\n.include \"" + include + "\"\n" + extra + "\n");
            AssemblyResult result = new AssemblerEngine().Assemble(main, Path.Combine(folder, "main_test.o"));
            string described = string.Join(" | ", result.Diagnostics.Select(d => d.ToString()));
            Assert.IsTrue(result.Success, "the generated source must assemble: " + described);
            Assert.AreEqual(0, result.Diagnostics.Count, described);
            return result.OutputBytes;
        }

        public static string Hex(IEnumerable<byte> bytes)
        {
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }

        public static string Describe(IEnumerable<AssetDiagnostic> diagnostics)
        {
            return string.Join(" | ", diagnostics.Select(d => d.ToString()));
        }

        /// <summary>A tile of one colour.</summary>
        public static byte[] Solid(int colour)
        {
            byte[] pixels = new byte[64];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = (byte)colour;
            return pixels;
        }

        /// <summary>The standard palettes of the tests: a background of $21, then distinct colours.</summary>
        public static PaletteAsset Palettes()
        {
            PaletteAsset palette = PaletteAsset.Blank(Nes, "game", 0x0F);
            int[][] colours =
            {
                new[] { 0x21, 0x30, 0x16, 0x28 },
                new[] { 0x30, 0x16, 0x28, 0x30 },
                new[] { 0x0F, 0x11, 0x21, 0x31 },
                new[] { 0x0F, 0x07, 0x17, 0x27 },
                new[] { 0x0F, 0x30, 0x11, 0x16 },
                new[] { 0x0F, 0x28, 0x38, 0x30 },
                new[] { 0x0F, 0x07, 0x17, 0x27 },
                new[] { 0x0F, 0x27, 0x37, 0x30 },
            };
            for (int p = 0; p < 8; p++)
                palette.Palettes[p].Colors = colours[p].ToList();
            return palette;
        }
    }
}
