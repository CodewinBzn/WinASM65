using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Execution;
using WinASM65.Projects;

namespace WinASM65.Tests
{
    /// <summary>
    /// The NES knowledge base (knowledge/nes) that small models copy code from:
    /// every snippet must assemble as its header says, the pure ones must compute
    /// what they claim, the project template must build to a NROM-128 ROM, and
    /// every page the index links to must exist.
    ///
    /// <para>
    /// A snippet's header is machine-readable: <c>; requires: nes.inc, other_snippet</c>
    /// (files it needs), <c>; vars: name size, ...</c> (variables declared with
    /// .res), <c>; wrap: org=$C000</c> (where its code is placed). The harness
    /// writes a unit from those lines and includes the snippet, as a user would
    /// paste it into game.asm.
    /// </para>
    /// </summary>
    [TestClass]
    public class KnowledgeBaseTests
    {
        private const ushort Halt = 0x9000;

        private static string KnowledgeRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WinASM65.sln")))
                dir = dir.Parent;
            Assert.IsNotNull(dir, "the repository root was not found");
            return Path.Combine(dir.FullName, "knowledge", "nes");
        }

        private static string SnippetPath(string name)
        {
            return Path.Combine(KnowledgeRoot(), "snippets", name + ".asm");
        }

        private static Dictionary<string, string> Header(string path)
        {
            Dictionary<string, string> header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadLines(path))
            {
                if (!line.StartsWith(";", StringComparison.Ordinal))
                    break;
                Match m = Regex.Match(line, "^;\\s*([a-z][a-z0-9-]*):\\s*(.*)$", RegexOptions.IgnoreCase);
                if (m.Success && !header.ContainsKey(m.Groups[1].Value))
                    header[m.Groups[1].Value] = m.Groups[2].Value.Trim();
            }
            return header;
        }

        private static List<string> HeaderList(Dictionary<string, string> header, string key)
        {
            string value;
            if (!header.TryGetValue(key, out value))
                return new List<string>();
            return value.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0 && v != "none").ToList();
        }

        private static IEnumerable<string> SnippetNames()
        {
            return Directory.GetFiles(Path.Combine(KnowledgeRoot(), "snippets"), "*.asm")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.Ordinal);
        }

        /// <summary>A built snippet: the bytes from its origin, and the unit's symbols.</summary>
        private sealed class BuiltSnippet
        {
            public ProjectBuildResult Build;
            public int Origin;
            public byte[] Code;

            public ushort Symbol(string name)
            {
                KeyValuePair<string, long> found = Build.Units[0].Symbols.FirstOrDefault(s => string.Equals(s.Key, name, StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(found.Key, "no symbol " + name);
                return (ushort)found.Value;
            }
        }

        /// <summary>Assembles a snippet in the harness its header describes.</summary>
        private static BuiltSnippet BuildSnippet(string name, string temp)
        {
            string path = SnippetPath(name);
            Dictionary<string, string> header = Header(path);
            foreach (string key in new[] { "snippet", "purpose", "inputs", "outputs", "clobbers", "requires", "vars", "wrap", "usage" })
                Assert.IsTrue(header.ContainsKey(key), name + ": the header has no \"" + key + ":\" line");
            Assert.AreEqual(name, header["snippet"], name + ": the header names another snippet");

            Match org = Regex.Match(header["wrap"], "org=\\$([0-9A-Fa-f]+)");
            Assert.IsTrue(org.Success, name + ": wrap: has no org=$hex");
            int origin = Convert.ToInt32(org.Groups[1].Value, 16);

            List<string> requires = HeaderList(header, "requires");
            List<string> units = requires.Where(r => r != "nes.inc").Concat(new[] { name }).ToList();
            StringBuilder wrapper = new StringBuilder();
            if (requires.Contains("nes.inc"))
            {
                File.Copy(Path.Combine(KnowledgeRoot(), "templates", "nrom-game", "src", "nes.inc"), Path.Combine(temp, "nes.inc"));
                wrapper.AppendLine(".include \"nes.inc\"");
            }
            wrapper.AppendLine(".memarea $0000");
            HashSet<string> declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string unit in units)
            {
                Assert.IsTrue(File.Exists(SnippetPath(unit)), name + " requires " + unit + ", which is not a snippet");
                foreach (string variable in HeaderList(Header(SnippetPath(unit)), "vars"))
                {
                    string[] parts = variable.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    Assert.AreEqual(2, parts.Length, unit + ": vars: entries are \"name size\"");
                    if (declared.Add(parts[0]))
                        wrapper.AppendLine(parts[0] + " .res " + parts[1]);
                }
            }
            wrapper.AppendLine(".org $" + origin.ToString("X4"));
            foreach (string unit in units)
            {
                File.Copy(SnippetPath(unit), Path.Combine(temp, unit + ".asm"));
                wrapper.AppendLine(".include \"" + unit + ".asm\"");
            }
            File.WriteAllText(Path.Combine(temp, "wrap.asm"), wrapper.ToString());
            File.WriteAllText(Path.Combine(temp, "config.json"),
                "{ \"Input\": [ { \"FileName\": \"wrap.asm\" } ], \"Output\": { \"ObjectFile\": \"wrap.bin\", \"Files\": [ { \"FileName\": \"wrap.o\" } ] } }");

            ProjectBuildResult build = ProjectSession.Open(Path.Combine(temp, "config.json")).Build();
            Assert.IsTrue(build.Success, name + " does not assemble:\n" + string.Join("\n", build.Diagnostics));
            return new BuiltSnippet { Build = build, Origin = origin, Code = build.Image };
        }

        private static T WithTemp<T>(Func<string, T> body)
        {
            string temp = Path.Combine(Path.GetTempPath(), "winasm65-kb-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                return body(temp);
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch (IOException) { }
            }
        }

        /// <summary>A machine with the snippet's code at its origin, ready to call routines.</summary>
        private static Cpu6502Core Machine(BuiltSnippet snippet)
        {
            Cpu6502Core cpu = new Cpu6502Core();
            cpu.Load((ushort)snippet.Origin, snippet.Code);
            return cpu;
        }

        private static void Call(Cpu6502Core cpu, ushort routine)
        {
            cpu.SP = 0xFD;
            cpu.Push16((ushort)(Halt - 1)); // RTS adds one
            cpu.PC = routine;
            cpu.Run(Halt, 100000);
        }

        // ------------------------------------------------------------------ assembly

        [TestMethod]
        public void EverySnippetAssemblesInItsHarness()
        {
            List<string> names = SnippetNames().ToList();
            Assert.IsTrue(names.Count >= 15, "the knowledge base lists its snippets");
            foreach (string name in names)
                WithTemp(temp => BuildSnippet(name, temp));
        }

        [TestMethod]
        public void TheTemplateBuildsANromRom()
        {
            WithTemp(temp =>
            {
                CopyTree(Path.Combine(KnowledgeRoot(), "templates", "nrom-game"), temp);
                ProjectBuildResult build = ProjectSession.Open(Path.Combine(temp, "config.json")).Build();
                Assert.IsTrue(build.Success, string.Join("\n", build.Diagnostics));
                Assert.AreEqual(24592, build.Image.Length, "16-byte header, 16 KB of program, 8 KB of tiles");
                CollectionAssert.AreEqual(new byte[] { (byte)'N', (byte)'E', (byte)'S', 0x1A, 1, 1, 1, 0 }, build.Image.Take(8).ToArray());
                int reset = build.Image[16 + 0x3FFC] | (build.Image[16 + 0x3FFD] << 8);
                int nmi = build.Image[16 + 0x3FFA] | (build.Image[16 + 0x3FFB] << 8);
                Assert.AreEqual(0xC000, reset, "RESET is the first byte of game.asm");
                Assert.IsTrue(nmi > 0xC000 && nmi < 0xFFFA, "NMI is in the program");
                return 0;
            });
        }

        // ------------------------------------------------------------------ behaviour

        [TestMethod]
        public void Add16AndCompare16Compute()
        {
            WithTemp(temp =>
            {
                BuiltSnippet add = BuildSnippet("add16", temp);
                Cpu6502Core cpu = Machine(add);
                ushort value = add.Symbol("value16"), other = add.Symbol("other16");
                cpu[value] = 0xF0; cpu[(ushort)(value + 1)] = 0x12;
                cpu.A = 0x20;
                Call(cpu, add.Symbol("add16_8"));
                Assert.AreEqual(0x1310, cpu[value] | (cpu[(ushort)(value + 1)] << 8), "$12F0 + $20");

                cpu[other] = 0x34; cpu[(ushort)(other + 1)] = 0x01;
                Call(cpu, add.Symbol("add16_16"));
                Assert.AreEqual(0x1444, cpu[value] | (cpu[(ushort)(value + 1)] << 8), "$1310 + $0134");
                Call(cpu, add.Symbol("sub16_16"));
                Assert.AreEqual(0x1310, cpu[value] | (cpu[(ushort)(value + 1)] << 8), "$1444 - $0134");
                return 0;
            });
            WithTemp(temp =>
            {
                BuiltSnippet compare = BuildSnippet("compare16", temp);
                Cpu6502Core cpu = Machine(compare);
                ushort value = compare.Symbol("value16"), other = compare.Symbol("other16");
                foreach (int[] pair in new[] { new[] { 0x1200, 0x11FF }, new[] { 0x11FF, 0x1200 }, new[] { 0x0505, 0x0505 }, new[] { 0x00FF, 0x0100 } })
                {
                    cpu[value] = (byte)pair[0]; cpu[(ushort)(value + 1)] = (byte)(pair[0] >> 8);
                    cpu[other] = (byte)pair[1]; cpu[(ushort)(other + 1)] = (byte)(pair[1] >> 8);
                    Call(cpu, compare.Symbol("compare16"));
                    Assert.AreEqual(pair[0] >= pair[1], cpu.Carry, "carry for $" + pair[0].ToString("X4") + " >= $" + pair[1].ToString("X4"));
                    Assert.AreEqual(pair[0] == pair[1], cpu.Zero, "Z for equality");
                }
                return 0;
            });
        }

        [TestMethod]
        public void TheLfsrVisitsEveryNonZeroByte()
        {
            WithTemp(temp =>
            {
                BuiltSnippet random = BuildSnippet("random_lfsr", temp);
                Cpu6502Core cpu = Machine(random);
                ushort rng = random.Symbol("rng");
                cpu[rng] = 0x5A;
                HashSet<byte> seen = new HashSet<byte>();
                for (int i = 0; i < 255; i++)
                {
                    Call(cpu, random.Symbol("random"));
                    Assert.AreEqual(cpu[rng], cpu.A, "A returns the new value");
                    seen.Add(cpu.A);
                }
                Assert.AreEqual(255, seen.Count, "period 255");
                Assert.IsFalse(seen.Contains(0));
                return 0;
            });
        }

        [TestMethod]
        public void TheScoreCarriesFromDigitToDigit()
        {
            WithTemp(temp =>
            {
                BuiltSnippet bcd = BuildSnippet("bcd_score", temp);
                Cpu6502Core cpu = Machine(bcd);
                ushort score = bcd.Symbol("score");
                byte[] start = { 0, 1, 9, 9 };
                for (int i = 0; i < 4; i++)
                    cpu[(ushort)(score + i)] = start[i];
                Call(cpu, bcd.Symbol("add_point"));
                CollectionAssert.AreEqual(new byte[] { 0, 2, 0, 0 }, Enumerable.Range(0, 4).Select(i => cpu[(ushort)(score + i)]).ToArray(), "0199 + 1");
                for (int i = 0; i < 4; i++)
                    cpu[(ushort)(score + i)] = 9;
                Call(cpu, bcd.Symbol("add_point"));
                CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 0 }, Enumerable.Range(0, 4).Select(i => cpu[(ushort)(score + i)]).ToArray(), "9999 wraps");
                return 0;
            });
        }

        [TestMethod]
        public void BoxesCollideOnlyWhenTheyOverlap()
        {
            WithTemp(temp =>
            {
                BuiltSnippet aabb = BuildSnippet("collision_aabb", temp);
                Cpu6502Core cpu = Machine(aabb);
                ushort ax = aabb.Symbol("a_x"), ay = aabb.Symbol("a_y"), bx = aabb.Symbol("b_x"), by = aabb.Symbol("b_y");
                int[][] cases =
                {
                    new[] { 100, 100, 100, 100, 1 }, new[] { 100, 100, 107, 107, 1 }, new[] { 100, 100, 108, 100, 0 },
                    new[] { 100, 100, 93, 100, 1 }, new[] { 100, 100, 92, 100, 0 }, new[] { 100, 100, 100, 92, 0 },
                    new[] { 100, 93, 100, 100, 1 }, new[] { 10, 10, 200, 10, 0 }
                };
                foreach (int[] c in cases)
                {
                    cpu[ax] = (byte)c[0]; cpu[ay] = (byte)c[1]; cpu[bx] = (byte)c[2]; cpu[by] = (byte)c[3];
                    Call(cpu, aabb.Symbol("collide_boxes"));
                    Assert.AreEqual(c[4] == 1, cpu.Carry, string.Format("({0},{1}) vs ({2},{3})", c[0], c[1], c[2], c[3]));
                }
                return 0;
            });
        }

        [TestMethod]
        public void TheTileAddressIsRowTimes32PlusColumn()
        {
            WithTemp(temp =>
            {
                BuiltSnippet tile = BuildSnippet("tile_address", temp);
                Cpu6502Core cpu = Machine(tile);
                ushort row = tile.Symbol("tile_row"), col = tile.Symbol("tile_col"), addr = tile.Symbol("tile_addr");
                foreach (int[] rc in new[] { new[] { 0, 0 }, new[] { 10, 5 }, new[] { 29, 31 }, new[] { 7, 16 } })
                {
                    cpu[row] = (byte)rc[0]; cpu[col] = (byte)rc[1];
                    Call(cpu, tile.Symbol("tile_address"));
                    Assert.AreEqual(0x2000 + rc[0] * 32 + rc[1], cpu[addr] | (cpu[(ushort)(addr + 1)] << 8), "row " + rc[0] + " col " + rc[1]);
                }
                return 0;
            });
        }

        [TestMethod]
        public void TheMetaspriteLoopWritesOamEntries()
        {
            WithTemp(temp =>
            {
                BuiltSnippet ms = BuildSnippet("draw_metasprite", temp);
                Cpu6502Core cpu = Machine(ms);
                ushort ship = ms.Symbol("ship");
                cpu[ms.Symbol("ms_ptr")] = (byte)ship;
                cpu[(ushort)(ms.Symbol("ms_ptr") + 1)] = (byte)(ship >> 8);
                cpu[ms.Symbol("ms_x")] = 100;
                cpu[ms.Symbol("ms_y")] = 50;
                cpu[ms.Symbol("oam_index")] = 8;
                Call(cpu, ms.Symbol("draw_metasprite"));
                CollectionAssert.AreEqual(new byte[] { 46, 1, 0, 92, 46, 2, 0, 100 },
                    Enumerable.Range(0x0208, 8).Select(a => cpu[(ushort)a]).ToArray(), "two sprites at (x-8, y-4) and (x, y-4)");
                Assert.AreEqual(16, cpu[ms.Symbol("oam_index")], "oam_index moves past them");
                return 0;
            });
        }

        [TestMethod]
        public void TheJoypadBitOrderPutsAInBit7()
        {
            WithTemp(temp =>
            {
                // A fake joypad: $4016 returns the buttons one by one, A first.
                BuiltSnippet joy = BuildSnippet("read_joypad", temp);
                JoypadBus bus = new JoypadBus(0x81); // A and Right held
                Cpu6502Core cpu = new Cpu6502Core(bus);
                cpu.Load((ushort)joy.Origin, joy.Code);
                Call(cpu, joy.Symbol("read_joypad"));
                Assert.AreEqual(0x81, cpu[joy.Symbol("buttons")]);
                Assert.AreEqual(0x81, cpu[joy.Symbol("pressed")], "both went down this frame");
                bus.Buttons = 0x80;
                Call(cpu, joy.Symbol("read_joypad"));
                Assert.AreEqual(0x80, cpu[joy.Symbol("buttons")]);
                Assert.AreEqual(0x00, cpu[joy.Symbol("pressed")], "A was already held");
                return 0;
            });
        }

        private sealed class JoypadBus : ICpuBus
        {
            private readonly byte[] _ram = new byte[0x10000];
            private int _shift;

            public JoypadBus(byte buttons)
            {
                Buttons = buttons;
            }

            public byte Buttons { get; set; }

            public byte Read(ushort address)
            {
                if (address == 0x4016)
                {
                    int bit = _shift < 8 ? (Buttons >> (7 - _shift)) & 1 : 1;
                    _shift++;
                    return (byte)(0x40 | bit);
                }
                return _ram[address];
            }

            public void Write(ushort address, byte value)
            {
                if (address == 0x4016)
                {
                    if ((value & 1) != 0)
                        _shift = 0;
                    return;
                }
                _ram[address] = value;
            }
        }

        // ------------------------------------------------------------------ pages

        [TestMethod]
        public void EveryPageTheIndexLinksToExists()
        {
            string root = KnowledgeRoot();
            string index = File.ReadAllText(Path.Combine(root, "index.md"));
            MatchCollection links = Regex.Matches(index, "\\]\\(([^)#]+)\\)");
            Assert.IsTrue(links.Count >= 12, "the index links its pages");
            foreach (Match link in links)
                Assert.IsTrue(File.Exists(Path.Combine(root, link.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar))),
                    "index.md links to a missing file: " + link.Groups[1].Value);

            foreach (string page in Directory.GetFiles(root, "*.md").Select(Path.GetFileName).Where(p => p != "index.md"))
                StringAssert.Contains(index, "(" + page + ")", "index.md does not list " + page);
            foreach (string snippet in SnippetNames())
                StringAssert.Contains(index, "| " + snippet + " |", "index.md does not list the snippet " + snippet);
        }

        [TestMethod]
        public void PagesAreSmallEnoughForASmallModel()
        {
            foreach (string page in Directory.GetFiles(KnowledgeRoot(), "*.md"))
            {
                int lines = File.ReadAllLines(page).Length;
                Assert.IsTrue(lines >= 30 && lines <= 150, Path.GetFileName(page) + " has " + lines + " lines (30-150 expected)");
            }
        }

        private static void CopyTree(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            foreach (string dir in Directory.GetDirectories(source))
                CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }
}
