using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Linking;
using WinASM65.Modules;
using WinASM65.Output;
using WinASM65.Projects;
using WinASM65.Targets;
using WinASM65.TextFormat;

namespace WinASM65.Tests
{
    /// <summary>
    /// The bytes real sources assemble to, pinned. A change to the parser that
    /// alters one byte of these is a regression until proven otherwise; the
    /// Bomberman image is pinned the same way in ProjectSessionTests.
    /// </summary>
    [TestClass]
    public class GoldenOutputTests
    {
        private static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
        }

        private static string RepositoryRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WinASM65.sln")))
                dir = dir.Parent;
            Assert.IsNotNull(dir, "the repository root was not found");
            return dir.FullName;
        }

        private static void CopyTree(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(source))
                CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)));
        }

        [TestMethod]
        public void TheNesGameImageIsUnchanged()
        {
            string temp = Path.Combine(Path.GetTempPath(), "winasm65-golden-" + Guid.NewGuid().ToString("N"));
            try
            {
                CopyTree(Path.Combine(RepositoryRoot(), "example_nes_game"), temp);
                ProjectBuildResult build = ProjectSession.Open(Path.Combine(temp, "config.json")).Build();
                Assert.IsTrue(build.Success, string.Join("\n", build.Diagnostics));
                Assert.AreEqual(NesGameHash, Hash(build.Image), "the NES game image has changed");
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch (IOException) { }
            }
        }

        [TestMethod]
        public void TheStarCatcherImageIsUnchanged()
        {
            // The example game of the documentation: three units, a CHR file,
            // and most of the syntax a reader is shown.
            string temp = Path.Combine(Path.GetTempPath(), "winasm65-golden-" + Guid.NewGuid().ToString("N"));
            try
            {
                CopyTree(Path.Combine(RepositoryRoot(), "example_star_catcher"), temp);
                ProjectBuildResult build = ProjectSession.Open(Path.Combine(temp, "config.json")).Build();
                Assert.IsTrue(build.Success, string.Join("\n", build.Diagnostics));
                Assert.AreEqual(24592, build.Image.Length, "16-byte header, 16 KB of program, 8 KB of tiles");
                Assert.AreEqual(StarCatcherHash, Hash(build.Image), "the Star Catcher image has changed");
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch (IOException) { }
            }
        }

        [TestMethod]
        public void TheGeosStubBytesAreUnchanged()
        {
            List<Diagnostic> problems = new List<Diagnostic>();
            byte[] stub = GeosStub.Assemble(0x4000, 32, problems);
            Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
            Assert.AreEqual(GeosStubHash, Hash(stub), "the GEOS relocation stub has changed");
        }

        [TestMethod]
        public void TheRuntimeStubBytesAreUnchanged()
        {
            List<Diagnostic> problems = new List<Diagnostic>();
            ModuleImage module = new ModuleImage();
            module.ModuleName = "golden";
            ModuleSegment segment = new ModuleSegment("CODE", new byte[] { 0x60 }, SegmentKind.Ro, 1, 0);
            segment.OriginAddress = 0x8000;
            module.AddSegment(segment);
            LinkedImage image;
            OperationResult linked = new Linker().Link(new List<ModuleImage> { module }, out image);
            Assert.IsTrue(linked.Success);

            RuntimeBlock block = RuntimeBlock.Build(image,
                new RuntimeBlockOptions { StubAddress = 0x2000, BlockAddress = 0x4000 }, problems);
            Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
            Assert.AreEqual(RuntimeStubHash, Hash(block.Data), "the runtime stub has changed");
        }

        private const string NesGameHash = "A88D8890CAEDEFFC497E1C931C14D5F4D651492D99CE312AEAB08ED6CE9FE06F";
        private const string StarCatcherHash = "3AB4B07FEABD0EF68C6AEA041CC170024C1F17E856D33FE1748DACA243C15E7B";
        private const string GeosStubHash = "D2E816CE9767FDB1487566C9911FBCC630A584CE2AABFF41E3455AE15E583973";
        private const string RuntimeStubHash = "6F0FE8D68E03A844C8E22FE525F0E665CA7BDBA3C3C696320B967BBFBA7DCA7D";
    }
}
