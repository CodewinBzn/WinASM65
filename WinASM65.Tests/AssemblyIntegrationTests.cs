using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Segments;

namespace WinASM65.Tests
{
    [TestClass]
    public class AssemblyIntegrationTests
    {
        [TestMethod]
        public void Assembler_ProcessesNestedConditionalsAndForwardBranches()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "sample.asm");
                string output = Path.Combine(temp.Path, "sample.o");
                File.WriteAllText(source, ".org $8000\n.if 0\n.if 1\nlda #$ff\n.endif\n.else\nlda #$01\n.endif\nbne target\nnop\ntarget:\nrts\n");
                AssemblyResult result = new AssemblerEngine().Assemble(source, output);
                CollectionAssert.AreEqual(new byte[] { 0xA9, 0x01, 0xD0, 0x01, 0xEA, 0x60 }, result.OutputBytes);
                Assert.IsTrue(result.Success);
                Assert.IsTrue(File.Exists(Path.ChangeExtension(source, ".symb")));
            }
        }

        [TestMethod]
        public void BinaryCombiner_PadsInputsAndRejectsMissingFiles()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string input = Path.Combine(temp.Path, "part.o");
                string output = Path.Combine(temp.Path, "combined.o");
                File.WriteAllBytes(input, new byte[] { 1, 2 });
                OperationResult ok = new BinaryCombiner().Combine(new CombineConf { ObjectFile = output, Files = new[] { new FileConf { FileName = input, Size = "$4" } } });
                Assert.IsTrue(ok.Success);
                CollectionAssert.AreEqual(new byte[] { 1, 2, 0, 0 }, File.ReadAllBytes(output));
                Assert.IsFalse(new BinaryCombiner().Combine(new CombineConf { ObjectFile = output, Files = new[] { new FileConf { FileName = "missing.o" } } }).Success);
            }
        }

        [TestMethod]
        public void Assembler_ReportsUnpairedConditionalDirective()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "invalid.asm");
                File.WriteAllText(source, ".else\n");
                AssemblyResult result = new AssemblerEngine().Assemble(source, Path.Combine(temp.Path, "invalid.o"));
                Assert.IsFalse(result.Success);
                Assert.AreEqual(ErrorCodes.NO_CONDITIONAL_ASSEMBLY, result.Diagnostics[0].Message);
            }
        }

        [TestMethod]
        public void MultiSegment_FailureIsReturned()
        {
            MultiSegmentResult result = new MultiSegmentOrchestrator(() => new AssemblerEngine()).AssembleSegments(
                new[] { new Segment { FileName = "does-not-exist.asm" } });
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Diagnostics.Count > 0);
        }

        [TestMethod]
        public void MultiSegment_ResolvesDeclaredDependency()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string provider = Path.Combine(temp.Path, "provider.asm");
                string consumer = Path.Combine(temp.Path, "consumer.asm");
                string consumerOutput = Path.Combine(temp.Path, "consumer.o");
                File.WriteAllText(provider, ".org $8000\nshared:\nnop\n");
                File.WriteAllText(consumer, ".org $8000\n.word shared\n");
                MultiSegmentResult result = new MultiSegmentOrchestrator(() => new AssemblerEngine()).AssembleSegments(new[]
                {
                    new Segment { FileName = provider },
                    new Segment { FileName = consumer, OutputFile = consumerOutput, Dependencies = new[] { provider } }
                });
                Assert.IsTrue(result.Success);
                CollectionAssert.AreEqual(new byte[] { 0x00, 0x80 }, File.ReadAllBytes(consumerOutput));
            }
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }
            public TemporaryDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinASM65Tests_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
            public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
        }
    }
}
