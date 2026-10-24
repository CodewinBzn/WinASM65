using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WinASM65.Tooling.Tests
{
    /// <summary>
    /// --build prints what the extension's problem matcher reads. The pattern is
    /// taken from package.json itself, so the two cannot drift apart unnoticed.
    /// </summary>
    [TestClass]
    public class BuildModeTests
    {
        private static string RepositoryRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WinASM65.sln")))
                dir = dir.Parent;
            return dir.FullName;
        }

        private static Regex ProblemPattern()
        {
            using (JsonDocument package = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "vscode-extension", "package.json"))))
            {
                string pattern = package.RootElement.GetProperty("contributes").GetProperty("problemPatterns")[0]
                    .GetProperty("regexp").GetString();
                return new Regex(pattern);
            }
        }

        private static (int Exit, string[] Lines) Build(string source)
        {
            string dir = Path.Combine(Path.GetTempPath(), "winasm65-build-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "prog.asm"), source);
            File.WriteAllText(Path.Combine(dir, "config.json"),
                "{ \"Input\": [ { \"FileName\": \"prog.asm\", \"Dependencies\": [] } ],"
                + " \"Output\": { \"ObjectFile\": \"out.bin\", \"Files\": [ { \"FileName\": \"prog.o\" } ] } }");

            TextWriter original = Console.Out;
            StringWriter captured = new StringWriter();
            try
            {
                Console.SetOut(captured);
                int exit = Program.Main(new[] { "--build", Path.Combine(dir, "config.json") });
                return (exit, captured.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            }
            finally
            {
                Console.SetOut(original);
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        [TestMethod]
        public void AnErrorIsPrintedInTheFormTheProblemMatcherReads()
        {
            var (exit, lines) = Build(".org $C000\n  lda #$01\n  bogus $10\n");
            Assert.AreEqual(1, exit);

            Match match = lines.Select(l => ProblemPattern().Match(l)).FirstOrDefault(m => m.Success);
            Assert.IsNotNull(match, "no line matched: " + string.Join(" | ", lines));
            StringAssert.EndsWith(match.Groups[1].Value, "prog.asm");
            Assert.AreEqual("3", match.Groups[2].Value);
            Assert.AreEqual("error", match.Groups[3].Value);
            Assert.IsFalse(string.IsNullOrWhiteSpace(match.Groups[4].Value));
        }

        [TestMethod]
        public void ACleanBuildSucceedsAndMatchesNothing()
        {
            var (exit, lines) = Build(".org $C000\n  lda #$01\n  rts\n");
            Assert.AreEqual(0, exit);
            Assert.IsFalse(lines.Any(l => ProblemPattern().IsMatch(l)), string.Join(" | ", lines));
        }
    }
}
