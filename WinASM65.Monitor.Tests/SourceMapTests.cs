using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Monitor.Debugging;
using WinASM65.Output;
using WinASM65.Projects;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// The map a debugger stops by: a program counter back to the line that
    /// produced it, and a line forward to the address a breakpoint goes on.
    /// The image is the referee: a line mapped to a PRG offset must find its own
    /// opcode at that offset in the ROM the build produced.
    /// </summary>
    [TestClass]
    public class SourceMapTests
    {
        private static string RepositoryRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WinASM65.sln")))
                dir = dir.Parent;
            Assert.IsNotNull(dir, "the repository root was not found");
            return dir.FullName;
        }

        private static ProjectBuildResult Build(string configuration)
        {
            ProjectBuildResult result = ProjectSession.Open(configuration)
                .Build(new ProjectBuildOptions { CaptureListing = true });
            Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
            return result;
        }

        [TestMethod]
        public void EveryMappedLineFindsItsOwnOpcodeInTheImage()
        {
            using (TemporaryDirectory temp = TemporaryDirectory.CopyOf(Path.Combine(RepositoryRoot(), "example_bomberman-nes")))
            {
                ProjectBuildResult result = Build(Path.Combine(temp.Path, "config.json"));
                SourceMap map = SourceMapBuilder.Build(result);
                int header = SourceMapBuilder.PrgStart(result.Image);
                Assert.AreEqual(16, header, "the bomberman image is iNES");

                int checkedRows = 0;
                foreach (ProjectUnitResult unit in result.Units)
                {
                    foreach (ListingRow row in unit.Listing)
                    {
                        if (row.Kind != ListingRowKind.Instruction || !row.Address.HasValue || row.Bytes.Count == 0)
                            continue;
                        MappedLine line = map.AtAddress(row.Address.Value)
                            .FirstOrDefault(l => l.Line == row.LineNumber);
                        Assert.IsNotNull(line, "row at $" + row.Address.Value.ToString("X4") + " is not mapped");
                        Assert.IsTrue(line.PrgOffset.HasValue, line + " has no PRG offset");

                        // A name another unit defines is a zero placeholder in this
                        // unit's listing and is patched afterwards (.word NMI in the
                        // vectors), so only a byte the listing already knew is compared.
                        if (row.Bytes[0] == 0)
                            continue;
                        Assert.AreEqual(row.Bytes[0], result.Image[header + line.PrgOffset.Value],
                            line + ": the image holds another byte at its PRG offset");
                        checkedRows++;
                    }
                }
                Assert.IsTrue(checkedRows > 1000, "only " + checkedRows + " rows were checked");
            }
        }

        [TestMethod]
        public void TheNmiHandlerMapsBothWays()
        {
            using (TemporaryDirectory temp = TemporaryDirectory.CopyOf(Path.Combine(RepositoryRoot(), "example_bomberman-nes")))
            {
                SourceMap map = SourceMapBuilder.Build(Build(Path.Combine(temp.Path, "config.json")));
                string bank1 = Path.Combine(temp.Path, "BMAN_BANK1.NAS");
                int labelLine = File.ReadAllLines(bank1).ToList().FindIndex(l => l.TrimEnd() == "NMI:") + 1;
                Assert.IsTrue(labelLine > 0, "the NMI label was not found");

                // The label emits nothing; a breakpoint on it lands on its first instruction.
                int emitting;
                Assert.IsTrue(map.TryNearestEmittingLine(bank1, labelLine, out emitting));
                Assert.AreEqual(labelLine + 1, emitting);

                MappedLine pha = map.AtLine(bank1, emitting).Single();
                Assert.AreEqual(0xC01A, pha.Address);
                Assert.AreEqual(0x1A, pha.PrgOffset);

                bool ambiguous;
                MappedLine resolved = map.Resolve(0xC01A, 0x1A, out ambiguous);
                Assert.AreSame(pha, resolved);
                Assert.IsFalse(ambiguous);
            }
        }

        [TestMethod]
        public void TwoBanksAtOneAddressAreToldApartByTheirOffset()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                File.WriteAllText(Path.Combine(temp.Path, "bank0.asm"), ".org $C000\n  lda #$01\n  rts\n");
                File.WriteAllText(Path.Combine(temp.Path, "bank1.asm"), ".org $C000\n  ldx #$02\n  rts\n");
                File.WriteAllText(Path.Combine(temp.Path, "config.json"),
                    "{ \"Input\": [ { \"FileName\": \"bank0.asm\", \"Dependencies\": [] },"
                    + " { \"FileName\": \"bank1.asm\", \"Dependencies\": [] } ],"
                    + " \"Output\": { \"ObjectFile\": \"out.bin\", \"Files\": ["
                    + " { \"FileName\": \"bank0.o\", \"Size\": \"$10\" }, { \"FileName\": \"bank1.o\" } ] } }");

                ProjectBuildResult result = Build(Path.Combine(temp.Path, "config.json"));
                SourceMap map = SourceMapBuilder.Build(result);

                IReadOnlyList<MappedLine> both = map.AtAddress(0xC000);
                Assert.AreEqual(2, both.Count);

                bool ambiguous;
                MappedLine second = map.Resolve(0xC000, 0x10, out ambiguous);
                Assert.IsFalse(ambiguous);
                StringAssert.EndsWith(second.Path, "bank1.asm");
                Assert.AreEqual(2, second.Line);

                map.Resolve(0xC000, null, out ambiguous);
                Assert.IsTrue(ambiguous, "without an offset the two banks cannot be told apart");
            }
        }

        [TestMethod]
        public void ALineThatEmitsNothingNearbyIsNotMapped()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "prog.asm");
                File.WriteAllText(source, ".org $8000\n  lda #$01\n  rts\n; one\n; two\n; three\n; four\n; five\n; six\n; seven\n");
                File.WriteAllText(Path.Combine(temp.Path, "config.json"),
                    "{ \"Input\": [ { \"FileName\": \"prog.asm\", \"Dependencies\": [] } ],"
                    + " \"Output\": { \"ObjectFile\": \"out.bin\", \"Files\": [ { \"FileName\": \"prog.o\" } ] } }");

                SourceMap map = SourceMapBuilder.Build(Build(Path.Combine(temp.Path, "config.json")));

                int line;
                Assert.IsTrue(map.TryNearestEmittingLine(source, 1, out line));
                Assert.AreEqual(2, line);
                Assert.IsFalse(map.TryNearestEmittingLine(source, 4, out line), "nothing emits after the rts");
            }
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winasm65-map-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public static TemporaryDirectory CopyOf(string source)
            {
                TemporaryDirectory copy = new TemporaryDirectory();
                foreach (string file in Directory.GetFiles(source))
                    File.Copy(file, System.IO.Path.Combine(copy.Path, System.IO.Path.GetFileName(file)));
                return copy;
            }

            public void Dispose()
            {
                try { Directory.Delete(Path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
