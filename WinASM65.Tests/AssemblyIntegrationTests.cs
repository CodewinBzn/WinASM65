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
        public void Assembler_GardeLesEtiquettesLocalesDansLeModule()
        {
            // Le module doit pouvoir relire ses propres etiquettes, exportees ou non.
            // Sans cette table, une reference a une etiquette definie trois lignes
            // plus haut devenait une relocation que personne ne pouvait resoudre, et
            // le linker annoncait qu'aucun module n'exportait le nom.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "labels.asm");
                File.WriteAllText(source,
                    ".org $8000\n" +
                    "Start:  jmp Boucle\n" +
                    "Boucle: jmp Boucle\n" +
                    "Pointeur = $1234\n" +
                    "        .export Start\n");
                AssemblyResult result = new AssemblerEngine().Assemble(source, Path.Combine(temp.Path, "labels.o"));

                Assert.IsTrue(result.Success);
                Assert.IsNotNull(result.Module, "un module qui declare un export est produit");

                WinASM65.Modules.ModuleSymbol found = null;
                for (int i = 0; i < result.Module.Symbols.Count; i++)
                {
                    if (result.Module.Symbols[i].Name == "Boucle")
                        found = result.Module.Symbols[i];
                }

                Assert.IsNotNull(found, "Boucle est une etiquette du source, donc une etiquette du module");
                Assert.AreEqual(3u, found.Offset, "Boucle est apres le jmp de trois octets");
            }
        }

        [TestMethod]
        public void Assembler_NEMetPasLesConstantesDansLaTableDesEtiquettes()
        {
            // Une constante est une valeur, pas une adresse. La mettre dans la table
            // ferait qu'une relocation nommant un calcul aurait l'air de viser une
            // etiquette, et gonflerait chaque module de ce qu'il compte de constantes.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "const.asm");
                File.WriteAllText(source, ".org $8000\nStart: rts\nPointeur = $1234\n        .export Start\n");
                AssemblyResult result = new AssemblerEngine().Assemble(source, Path.Combine(temp.Path, "const.o"));

                Assert.IsTrue(result.Success);
                for (int i = 0; i < result.Module.Symbols.Count; i++)
                    Assert.AreNotEqual("Pointeur", result.Module.Symbols[i].Name,
                        "une constante n'est pas une etiquette");
            }
        }

        [TestMethod]
        public void Assembler_LEtiquetteExporteeNestPasRepeteeDansLaTableDesLocales()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "exp.asm");
                File.WriteAllText(source, ".org $8000\nStart: rts\nAutre: nop\n        .export Start\n");
                AssemblyResult result = new AssemblerEngine().Assemble(source, Path.Combine(temp.Path, "exp.o"));

                Assert.IsTrue(result.Success);
                Assert.AreEqual(1, result.Module.Exports.Count);
                Assert.AreEqual(1, result.Module.Symbols.Count, "seule l'etiquette non exportee est locale");
                Assert.AreEqual("Autre", result.Module.Symbols[0].Name);
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
