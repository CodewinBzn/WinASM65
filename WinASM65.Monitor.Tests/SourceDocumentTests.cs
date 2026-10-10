using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Monitor.Shell;

namespace WinASM65.Monitor.Tests
{
    [TestClass]
    public class SourceDocumentTests
    {
        [TestMethod]
        public void ReadsALineForEachLineInTheFile()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = "prog.asm";
                File.WriteAllText(Path.Combine(temp.Path, name),
                    ".org $8000\n  lda #$5A\n  rts\n");

                SourceDocument doc = new SourceDocument(temp.Path);
                string problem;
                IReadOnlyList<string> lines = doc.Read(name, out problem);

                Assert.AreEqual(null, problem, "a readable file has no problem");
                Assert.AreEqual(3, lines.Count);
                Assert.AreEqual(".org $8000", lines[0]);
                Assert.AreEqual("  lda #$5A", lines[1]);
                Assert.AreEqual("  rts", lines[2]);
            }
        }

        [TestMethod]
        public void MissingFileIsAProblemNotAnException()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                SourceDocument doc = new SourceDocument(temp.Path);
                string problem;
                IReadOnlyList<string> lines = doc.Read("no-such-file.asm", out problem);

                Assert.AreEqual(0, lines.Count);
                Assert.IsNotNull(problem);
                Assert.IsTrue(problem.Contains("no such source"));
            }
        }

        [TestMethod]
        public void APathEscapingTheDirectoryIsRefused()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                SourceDocument doc = new SourceDocument(temp.Path);
                string problem;
                IReadOnlyList<string> lines = doc.Read(@"..\..\..\..\etc\passwd", out problem);

                Assert.AreEqual(0, lines.Count);
                Assert.IsNotNull(problem);
                Assert.IsTrue(problem.Contains("outside"));
            }
        }

        [TestMethod]
        public void WritePreservesCRLFLineEndings()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = "prog.asm";
                string path = Path.Combine(temp.Path, name);
                File.WriteAllText(path, ".org $8000\r\n  lda #$5A\r\n  rts\r\n", Encoding.UTF8);

                SourceDocument doc = new SourceDocument(temp.Path);
                string problem;
                doc.Read(name, out problem);

                string newText = ".org $8000\n  lda #$01\n  rts\n";
                bool written = doc.Write(name, newText, out problem);

                Assert.IsTrue(written, "the write was refused: " + problem ?? "(null)");
                Assert.IsNull(problem);

                byte[] bytes = File.ReadAllBytes(path);
                string written2 = Encoding.UTF8.GetString(bytes);
                Assert.IsTrue(written2.Contains("\r\n"), "CRLF was not preserved: [" + written2 + "]");
                Assert.IsFalse(written2.Contains(" lda #$01\r\n") == false, "content did not round-trip");
            }
        }

        [TestMethod]
        public void WriteRefusesMixedLineEndings()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = "prog.asm";
                string path = Path.Combine(temp.Path, name);
                File.WriteAllText(path, ".org $8000\r\n  lda #$5A\n  rts\r\n", Encoding.UTF8);

                SourceDocument doc = new SourceDocument(temp.Path);
                string readProblem;
                IReadOnlyList<string> lines = doc.Read(name, out readProblem);

                Assert.IsNotNull(readProblem, "mixed endings on read should be refused");
                Assert.AreEqual(0, lines.Count);
                Assert.IsFalse(doc.Write(name, ".org $8000\n", out string writeProblem));
                StringAssert.Contains(writeProblem, "refused when read");
            }
        }

        [TestMethod]
        public void WriteRefusesAPathEscapingTheDirectory()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = "escaped.asm";
                File.WriteAllText(Path.Combine(temp.Path, name), ".org $8000\n  rts\n");

                SourceDocument doc = new SourceDocument(temp.Path);
                string problem;
                bool written = doc.Write(@"..\..\..\..\tmp\" + name, "test\n", out problem);

                Assert.IsFalse(written);
                Assert.IsNotNull(problem);
                Assert.IsTrue(problem.Contains("outside"));
            }
        }

        [TestMethod]
        public void AnUnchangedBufferWritesNothing()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string name = "prog.asm";
                string content = ".org $8000\n  rts\n";
                string path = Path.Combine(temp.Path, name);
                File.WriteAllText(path, content, Encoding.UTF8);

                SourceDocument doc = new SourceDocument(temp.Path);
                string problem;
                doc.Read(name, out problem);

                string problem2;
                bool written = doc.Write(name, content, out problem2);

                Assert.IsTrue(written);
            }

        }

        [TestMethod]
        public void ProjectConfigurationWritePreservesUtf16BomAndCrLf()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, "config.json");
                Encoding encoding = new UnicodeEncoding(false, true);
                File.WriteAllBytes(path, encoding.GetPreamble().Concat(
                    encoding.GetBytes("{\r\n  \"name\": \"game\"\r\n}\r\n")).ToArray());

                SourceDocument doc = new SourceDocument(temp.Path);
                string problem;
                string text = doc.ReadText("config.json", out problem);
                Assert.IsNull(problem);
                Assert.AreEqual("{\n  \"name\": \"game\"\n}\n", text);
                Assert.IsTrue(doc.Write("config.json", text.Replace("game", "new game"), out problem), problem);

                byte[] actual = File.ReadAllBytes(path);
                CollectionAssert.AreEqual(encoding.GetPreamble(), actual.Take(2).ToArray());
                Assert.AreEqual("{\r\n  \"name\": \"new game\"\r\n}\r\n",
                    encoding.GetString(actual, 2, actual.Length - 2));
            }
        }

        [TestMethod]
        public void BinaryFilesAndUnsupportedExtensionsCannotBeWritten()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                File.WriteAllBytes(Path.Combine(temp.Path, "image.nes"), new byte[] { 1, 2, 3 });
                File.WriteAllBytes(Path.Combine(temp.Path, "notes.txt"), new byte[] { 65, 0, 66 });
                SourceDocument doc = new SourceDocument(temp.Path);
                string problem;

                Assert.IsNull(doc.ReadText("image.nes", out problem));
                StringAssert.Contains(problem, "not a supported project text file");
                Assert.IsFalse(doc.Write("image.nes", "bad", out problem));
                Assert.IsNull(doc.ReadText("notes.txt", out problem));
                StringAssert.Contains(problem, "binary");
                Assert.IsFalse(doc.Write("notes.txt", "bad", out problem));
            }
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }

            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "WinASM65Doc_" + Guid.NewGuid().ToString("N"));
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
