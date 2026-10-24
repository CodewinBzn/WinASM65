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
            Match group = Regex.Match(match, @"\((?:\?i:)?([a-z|]+)\)");
            Assert.IsTrue(group.Success, "no alternation in " + match);
            return new SortedSet<string>(group.Groups[1].Value.Split('|'), StringComparer.OrdinalIgnoreCase);
        }

        [TestMethod]
        public void EveryMnemonicOfTheOpcodeTablesIsColouredAndNoOther()
        {
            SortedSet<string> grammar = Alternatives(Repository().GetProperty("mnemonic").GetProperty("match").GetString());

            SortedSet<string> assembler = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            assembler.UnionWith(new Cpu6502().Mnemonics);
            assembler.UnionWith(new Cpu65C02().Mnemonics);

            CollectionAssert.AreEquivalent(assembler.Select(m => m.ToLowerInvariant()).ToList(),
                grammar.Select(m => m.ToLowerInvariant()).ToList());
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
