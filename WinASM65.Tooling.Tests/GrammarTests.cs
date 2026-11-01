using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Cpu;

namespace WinASM65.Tooling.Tests
{
    /// <summary>
    /// The VS Code grammar colours exactly what the assembler accepts: every
    /// mnemonic of its opcode tables and every directive its engine dispatches,
    /// and nothing else. A table that grows without the grammar fails here.
    /// </summary>
    [TestClass]
    public class GrammarTests
    {
        private static JsonElement Repository()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WinASM65.sln")))
                dir = dir.Parent;
            Assert.IsNotNull(dir);
            string grammar = Path.Combine(dir.FullName, "vscode-extension", "syntaxes", "winasm65.tmLanguage.json");
            return JsonDocument.Parse(File.ReadAllText(grammar)).RootElement.GetProperty("repository");
        }

        private static SortedSet<string> Alternatives(string match)
        {
            // The longest alternation in the pattern: a mnemonic list has dozens of
            // names, digits included (bbr0), where other groups have a handful.
            Match longest = null;
            foreach (Match group in Regex.Matches(match, @"\((?:\?i:)?([a-z0-9|]+)\)"))
            {
                if (longest == null || group.Groups[1].Length > longest.Groups[1].Length)
                    longest = group;
            }
            Assert.IsNotNull(longest, "no alternation in " + match);
            return new SortedSet<string>(longest.Groups[1].Value.Split('|'), StringComparer.OrdinalIgnoreCase);
        }

        private static List<string> AllMnemonics()
        {
            SortedSet<string> assembler = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ICpuInstructionSet cpu in CpuFactory.All.Values)
                assembler.UnionWith(cpu.Mnemonics);
            return assembler.Select(m => m.ToLowerInvariant()).ToList();
        }

        [TestMethod]
        public void BlockCommentsAreColouredBeforeAnythingElse()
        {
            // /* ... */ spans lines, as the assembler strips it; it comes first, so that
            // a "/" is not taken for an operator and a "*" for the program counter.
            JsonElement block = Repository().GetProperty("block-comment");
            Assert.IsTrue(Regex.IsMatch("/*", block.GetProperty("begin").GetString()));
            Assert.IsTrue(Regex.IsMatch("*/", block.GetProperty("end").GetString()));
            Assert.AreEqual("comment.block.winasm65", block.GetProperty("name").GetString());
            JsonElement root = JsonDocument.Parse(File.ReadAllText(GrammarPath())).RootElement;
            Assert.AreEqual("#block-comment", root.GetProperty("patterns")[0].GetProperty("include").GetString());
        }

        private static string GrammarPath()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WinASM65.sln")))
                dir = dir.Parent;
            return Path.Combine(dir.FullName, "vscode-extension", "syntaxes", "winasm65.tmLanguage.json");
        }

        [TestMethod]
        public void EveryMnemonicOfTheOpcodeTablesIsColouredAndNoOther()
        {
            SortedSet<string> grammar = Alternatives(Repository().GetProperty("mnemonic").GetProperty("match").GetString());
            CollectionAssert.AreEquivalent(AllMnemonics(), grammar.Select(m => m.ToLowerInvariant()).ToList());
        }

        [TestMethod]
        public void TheLabelBeforeAnInstructionKnowsEveryMnemonicToo()
        {
            // "Name lda #1": the label is recognised by the mnemonic that follows it.
            JsonElement rules = Repository().GetProperty("label-definition").GetProperty("patterns");
            SortedSet<string> lookahead = Alternatives(rules[1].GetProperty("match").GetString());
            CollectionAssert.AreEquivalent(AllMnemonics(), lookahead.Select(m => m.ToLowerInvariant()).ToList());
        }

        [TestMethod]
        public void EveryDirectiveTheEngineDispatchesIsColouredAndNoOther()
        {
            SortedSet<string> grammar = Alternatives(Repository().GetProperty("directive").GetProperty("match").GetString());

            SortedSet<string> engine = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in AssemblerEngine.DefaultDirectiveNames)
                engine.Add(name.TrimStart('.'));
            engine.Add("res"); // matched by the line dispatch, not a handler

            CollectionAssert.AreEquivalent(engine.Select(d => d.ToLowerInvariant()).ToList(),
                grammar.Select(d => d.ToLowerInvariant()).ToList());
        }
    }
}
