using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static WinASM65.Tooling.Mcp.McpTool;

namespace WinASM65.Tooling.Mcp
{
    /// <summary>
    /// The NES knowledge base (knowledge/nes: Markdown pages, tested snippets and a
    /// project template) as files on disk, with the lookups the tools need.
    ///
    /// <para>
    /// It is written for small models, which must look facts up instead of
    /// remembering them: pages are short, and a search returns a section, not a
    /// whole page. The folder ships next to the executable (the csproj copies it);
    /// in a source tree it is found by walking up to the repository.
    /// </para>
    /// </summary>
    public sealed class KnowledgeBase
    {
        /// <summary>Overrides where the knowledge folder (the one holding index.md) is.</summary>
        public const string EnvironmentVariable = "WINASM65_KNOWLEDGE";

        private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "the", "of", "to", "in", "on", "for", "and", "or", "is", "it", "how", "what", "do", "i", "with",
            "my", "by", "be", "at", "as", "from", "this", "that", "can", "does", "which", "nes"
        };

        /// <summary>Words a user may type for the ones the pages use.</summary>
        private static readonly Dictionary<string, string> Synonyms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "color", "colour" }, { "colors", "colour" }, { "colours", "colour" },
            { "gamepad", "joypad" }, { "joystick", "joypad" }, { "controller", "joypad" }, { "input", "joypad" },
            { "beep", "pulse" }, { "audio", "sound" }, { "bg", "background" }, { "letters", "text" }
        };

        private KnowledgeBase(string root)
        {
            Root = root;
            Pages = Directory.GetFiles(root, "*.md")
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => new KnowledgePage(Path.GetFileNameWithoutExtension(p), p, File.ReadAllText(p)))
                .ToList();
            string snippets = Path.Combine(root, "snippets");
            Snippets = Directory.Exists(snippets)
                ? Directory.GetFiles(snippets, "*.asm")
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .Select(p => new KnowledgeSnippet(Path.GetFileNameWithoutExtension(p), p, File.ReadAllText(p)))
                    .ToList()
                : new List<KnowledgeSnippet>();
        }

        /// <summary>The folder holding index.md, the pages, snippets/ and templates/.</summary>
        public string Root { get; private set; }

        public IReadOnlyList<KnowledgePage> Pages { get; private set; }

        public IReadOnlyList<KnowledgeSnippet> Snippets { get; private set; }

        public string TemplateFolder { get { return Path.Combine(Root, "templates", "nrom-game"); } }

        /// <summary>Loads the knowledge base in a folder (the one holding index.md).</summary>
        public static KnowledgeBase Load(string root)
        {
            if (!File.Exists(Path.Combine(root, "index.md")))
                throw new DirectoryNotFoundException("no knowledge base (index.md) in " + root);
            return new KnowledgeBase(Path.GetFullPath(root));
        }

        /// <summary>
        /// Finds the knowledge folder: the environment variable, then knowledge/nes
        /// next to the executable, then up from the executable and the current
        /// directory (a source tree). Null when there is none.
        /// </summary>
        public static string Find()
        {
            string fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(Path.Combine(fromEnvironment, "index.md")))
                return Path.GetFullPath(fromEnvironment);
            foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                for (DirectoryInfo dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, "knowledge", "nes");
                    if (File.Exists(Path.Combine(candidate, "index.md")))
                        return candidate;
                }
            }
            return null;
        }

        // ---------------------------------------------------------------- lookups

        /// <summary>Lower case, no extension, words joined by '-': "PPU Registers.md" -> "ppu-registers".</summary>
        public static string Normalize(string name)
        {
            string text = (name ?? string.Empty).Trim().ToLowerInvariant();
            if (text.EndsWith(".md", StringComparison.Ordinal) || text.EndsWith(".asm", StringComparison.Ordinal))
                text = text.Substring(0, text.LastIndexOf('.'));
            return Regex.Replace(text, "[\\s_]+", "-");
        }

        /// <summary>The words of a query worth matching: lower case, without stop words.</summary>
        public static List<string> Terms(string query)
        {
            return Regex.Matches((query ?? string.Empty).ToLowerInvariant(), "[a-z0-9$%#_]+")
                .Select(m => m.Value.Trim('_'))
                .Select(t => Synonyms.TryGetValue(t, out string same) ? same : t)
                .Where(t => t.Length > 1 && !StopWords.Contains(t))
                .Distinct()
                .ToList();
        }

        public KnowledgePage Page(string name)
        {
            string key = Normalize(name);
            return Pages.FirstOrDefault(p => p.Name == key);
        }

        /// <summary>
        /// The pages that best answer a topic: an exact name, else names containing
        /// it (or it containing them), else keyword scores over titles, headings and text.
        /// </summary>
        public List<KnowledgePage> FindPages(string topic)
        {
            string key = Normalize(topic);
            KnowledgePage exact = Page(key);
            if (exact != null)
                return new List<KnowledgePage> { exact };

            List<KnowledgePage> byName = Pages
                .Where(p => p.Name != "index" && key.Length >= 3 && (p.Name.Contains(key) || key.Contains(p.Name)))
                .ToList();
            if (byName.Count > 0)
                return byName;

            List<string> terms = Terms(topic);
            return Pages
                .Where(p => p.Name != "index")
                .Select(p => new { Page = p, Score = PageScore(p, terms) })
                .Where(s => s.Score > 0)
                .OrderByDescending(s => s.Score)
                .ThenBy(s => s.Page.Name, StringComparer.Ordinal)
                .Select(s => s.Page)
                .ToList();
        }

        private static int PageScore(KnowledgePage page, List<string> terms)
        {
            int score = 0;
            foreach (string term in terms)
            {
                if (page.Name.Contains(term))
                    score += 8;
                if (Contains(page.Title, term))
                    score += 5;
                score += 3 * page.Sections.Count(s => Contains(s.Heading, term));
                score += Math.Min(5, Count(page.Text, term));
            }
            return score;
        }

        /// <summary>The sections (and snippets) matching a query, best first.</summary>
        public List<KnowledgeHit> Search(string query, int max)
        {
            List<string> terms = Terms(query);
            if (terms.Count == 0)
                return new List<KnowledgeHit>();
            string phrase = (query ?? string.Empty).Trim().ToLowerInvariant();

            List<KnowledgeHit> hits = new List<KnowledgeHit>();
            foreach (KnowledgePage page in Pages)
            {
                foreach (KnowledgeSection section in page.Sections)
                {
                    int score = Score(page.Title + " " + section.Heading, section.Lines, terms, phrase);
                    if (score > 0)
                        hits.Add(new KnowledgeHit(page.Name + ".md", section.Heading, section.Lines, score, terms));
                }
            }
            foreach (KnowledgeSnippet snippet in Snippets)
            {
                string[] lines = snippet.Text.Replace("\r\n", "\n").Split('\n');
                int score = Score(snippet.Name.Replace('_', ' ') + " " + snippet.Purpose, lines, terms, phrase);
                if (score > 0)
                    hits.Add(new KnowledgeHit("snippets/" + snippet.Name + ".asm", "snippet " + snippet.Name, lines, score, terms));
            }
            return hits.OrderByDescending(h => h.Score).ThenBy(h => h.File, StringComparer.Ordinal).Take(max).ToList();
        }

        private static int Score(string heading, IReadOnlyList<string> lines, List<string> terms, string phrase)
        {
            int score = 0;
            int matched = 0;
            foreach (string term in terms)
            {
                int inHeading = Contains(heading, term) ? 6 : 0;
                int inLines = Math.Min(6, lines.Sum(l => Count(l, term)));
                if (inHeading + inLines > 0)
                    matched++;
                score += inHeading + inLines;
            }
            if (matched == 0)
                return 0;
            score += matched * matched * 4; // sections with every word beat sections with one word many times
            if (phrase.Length > 3 && lines.Any(l => l.ToLowerInvariant().Contains(phrase)))
                score += 10;
            return score;
        }

        private static bool Contains(string text, string term)
        {
            return text != null && text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int Count(string text, string term)
        {
            int count = 0;
            for (int at = text.IndexOf(term, StringComparison.OrdinalIgnoreCase); at >= 0;
                at = text.IndexOf(term, at + term.Length, StringComparison.OrdinalIgnoreCase))
                count++;
            return count;
        }

        /// <summary>Snippets whose name matches exactly, else contains the name, else whose purpose has the words.</summary>
        public List<KnowledgeSnippet> FindSnippets(string name)
        {
            string key = Normalize(name).Replace('-', '_');
            KnowledgeSnippet exact = Snippets.FirstOrDefault(s => s.Name == key);
            if (exact != null)
                return new List<KnowledgeSnippet> { exact };
            List<KnowledgeSnippet> partial = Snippets
                .Where(s => key.Length >= 3 && (s.Name.Contains(key) || key.Contains(s.Name)))
                .ToList();
            if (partial.Count > 0)
                return partial;
            List<string> terms = Terms(name);
            return Snippets
                .Select(s => new { Snippet = s, Score = terms.Count(t => Contains(s.Name + " " + s.Purpose, t)) })
                .Where(s => s.Score > 0)
                .OrderByDescending(s => s.Score)
                .Select(s => s.Snippet)
                .ToList();
        }
    }

    /// <summary>A Markdown page: its name (file name without .md), title and sections.</summary>
    public sealed class KnowledgePage
    {
        public KnowledgePage(string name, string path, string text)
        {
            Name = name;
            Path = path;
            Text = text.Replace("\r\n", "\n");
            string[] lines = Text.Split('\n');
            Title = lines.FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal))?.Substring(2).Trim() ?? name;

            List<KnowledgeSection> sections = new List<KnowledgeSection>();
            string heading = "(top)";
            List<string> current = new List<string>();
            bool inCode = false;
            foreach (string line in lines)
            {
                if (line.StartsWith("```", StringComparison.Ordinal))
                    inCode = !inCode;
                if (!inCode && line.StartsWith("## ", StringComparison.Ordinal))
                {
                    if (current.Any(l => l.Trim().Length > 0))
                        sections.Add(new KnowledgeSection(heading, current));
                    heading = line.Substring(3).Trim();
                    current = new List<string> { line };
                    continue;
                }
                current.Add(line);
            }
            if (current.Any(l => l.Trim().Length > 0))
                sections.Add(new KnowledgeSection(heading, current));
            Sections = sections;
        }

        public string Name { get; private set; }
        public string Path { get; private set; }
        public string Title { get; private set; }
        public string Text { get; private set; }
        public IReadOnlyList<KnowledgeSection> Sections { get; private set; }
    }

    public sealed class KnowledgeSection
    {
        public KnowledgeSection(string heading, List<string> lines)
        {
            Heading = heading;
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0)
                lines.RemoveAt(lines.Count - 1);
            Lines = lines;
        }

        public string Heading { get; private set; }
        public IReadOnlyList<string> Lines { get; private set; }
    }

    /// <summary>A snippet file and its header fields (; key: value lines at the top).</summary>
    public sealed class KnowledgeSnippet
    {
        public KnowledgeSnippet(string name, string path, string text)
        {
            Name = name;
            Path = path;
            Text = text.Replace("\r\n", "\n");
            Header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in Text.Split('\n'))
            {
                if (!line.StartsWith(";", StringComparison.Ordinal))
                    break;
                Match m = Regex.Match(line, "^;\\s*([a-z][a-z0-9-]*):\\s*(.*)$", RegexOptions.IgnoreCase);
                if (m.Success && !Header.ContainsKey(m.Groups[1].Value))
                    Header[m.Groups[1].Value] = m.Groups[2].Value.Trim();
            }
        }

        public string Name { get; private set; }
        public string Path { get; private set; }
        public string Text { get; private set; }

        /// <summary>snippet, purpose, inputs, outputs, clobbers, cycles, requires, vars, wrap, usage.</summary>
        public IDictionary<string, string> Header { get; private set; }

        public string Purpose { get { string p; return Header.TryGetValue("purpose", out p) ? p : string.Empty; } }

        /// <summary>The <c>requires:</c> entries: "nes.inc" and names of other snippets.</summary>
        public IReadOnlyList<string> Requires { get { return List("requires"); } }

        /// <summary>The <c>vars:</c> entries as (name, size).</summary>
        public IReadOnlyList<KeyValuePair<string, int>> Vars
        {
            get
            {
                return List("vars")
                    .Select(v => v.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    .Select(p => new KeyValuePair<string, int>(p[0], p.Length > 1 ? int.Parse(p[1]) : 1))
                    .ToList();
            }
        }

        private IReadOnlyList<string> List(string key)
        {
            string value;
            if (!Header.TryGetValue(key, out value))
                return new List<string>();
            return value.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0 && v != "none").ToList();
        }
    }

    public sealed class KnowledgeHit
    {
        public KnowledgeHit(string file, string section, IReadOnlyList<string> lines, int score, List<string> terms)
        {
            File = file;
            Section = section;
            Lines = lines;
            Score = score;
            Terms = terms;
        }

        public string File { get; private set; }
        public string Section { get; private set; }
        public IReadOnlyList<string> Lines { get; private set; }
        public int Score { get; private set; }
        public List<string> Terms { get; private set; }

        /// <summary>At most <paramref name="max"/> lines, around the first line with a query word.</summary>
        public IEnumerable<string> Excerpt(int max)
        {
            if (Lines.Count <= max)
                return Lines;
            int first = 0;
            for (int i = 0; i < Lines.Count; i++)
            {
                if (Terms.Any(t => Lines[i].IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    first = i;
                    break;
                }
            }
            int start = Math.Max(0, Math.Min(first - 5, Lines.Count - max));
            return Lines.Skip(start).Take(max);
        }
    }

    /// <summary>
    /// The knowledge base as MCP tools: nes_reference, nes_search, nes_snippet and
    /// new_nes_project.
    /// </summary>
    public static class KnowledgeTools
    {
        private const int SearchResults = 3;
        private const int ExcerptLines = 40;

        /// <summary>The tools over the knowledge base found by <see cref="KnowledgeBase.Find"/>.</summary>
        public static IEnumerable<McpTool> All()
        {
            KnowledgeBase loaded = null;
            return All(() =>
            {
                if (loaded == null)
                {
                    string root = KnowledgeBase.Find();
                    if (root == null)
                        throw new InvalidOperationException("the NES knowledge base was not found: it ships as knowledge/nes next to "
                            + "WinASM65.Tooling; set " + KnowledgeBase.EnvironmentVariable + " to its folder otherwise.");
                    loaded = KnowledgeBase.Load(root);
                }
                return loaded;
            });
        }

        public static IEnumerable<McpTool> All(Func<KnowledgeBase> knowledge)
        {
            yield return new McpTool("nes_reference", "NES reference page",
                "Returns a page of the NES knowledge base: hardware facts (PPU registers, sprites, palettes, controllers, "
                + "sound, timing, memory map, iNES header) and WinASM65 syntax/project help. topic = a page name "
                + "(e.g. ppu-registers, oam-sprites, controllers, apu-sound, winasm65-syntax) or a keyword. Call it "
                + "BEFORE writing NES code; an unknown topic lists the pages.",
                McpEffect.ReadOnly,
                Schema(Arg.Required_("topic", "string", "Page name or keyword, e.g. \"ppu-registers\", \"sprites\", \"index\"")),
                arguments => Reference(knowledge(), Text(arguments, "topic")));

            yield return new McpTool("nes_search", "Search the NES knowledge base",
                "Finds the sections of the NES knowledge base and snippets that match words (e.g. \"attribute table\", "
                + "\"PPUSCROLL\", \"sprite flip\"). Returns the best " + SearchResults + " with file, section and up to "
                + ExcerptLines + " lines each.",
                McpEffect.ReadOnly,
                Schema(Arg.Required_("query", "string", "Words to look for")),
                arguments => Search(knowledge(), Text(arguments, "query")));

            yield return new McpTool("nes_snippet", "NES code snippet",
                "Returns a tested WinASM65 routine to copy into a game (e.g. read_joypad, move_sprite, load_palettes, "
                + "draw_text, nmi_oam_dma, pulse_blip, bcd_score, random_lfsr). Its header says the variables to declare "
                + "(vars:), what it needs (requires:) and how to call it (usage:). An unknown name lists them all.",
                McpEffect.ReadOnly,
                Schema(Arg.Required_("name", "string", "Snippet name, e.g. \"read_joypad\"")),
                arguments => Snippet(knowledge(), Text(arguments, "name", false) ?? string.Empty));

            yield return new McpTool("new_nes_project", "New NES project",
                "Creates a NES game project (NROM, mapper 0) from the template in an empty folder: config.json, "
                + "src/header.asm, src/nes.inc, src/game.asm (RESET, NMI, main loop, joypad, a text and a sprite), "
                + "src/vectors.asm, chr/tiles.chr. Refuses a folder that is not empty. Build it next with build_project.",
                McpEffect.Writes,
                Schema(Arg.Required_("folder", "string", "Folder to create the project in (absolute path; created if missing)"),
                    Arg.Optional("name", "string", "Name of the game: the ROM is <name>.nes (default: the folder name)")),
                arguments => NewProject(knowledge(), Text(arguments, "folder"), Text(arguments, "name", false)));
        }

        // ---------------------------------------------------------------- nes_reference

        private static McpResult Reference(KnowledgeBase knowledge, string topic)
        {
            List<KnowledgePage> pages = knowledge.FindPages(topic);
            if (pages.Count == 0)
            {
                List<KnowledgeSnippet> snippets = knowledge.FindSnippets(topic);
                if (snippets.Count == 1)
                    return McpResult.Ok("\"" + topic + "\" is a snippet (nes_snippet):\n\n" + snippets[0].Text);
                return McpResult.Fail("No page about \"" + topic + "\". " + Topics(knowledge));
            }
            KnowledgePage page = pages[0];
            StringBuilder text = new StringBuilder();
            text.AppendLine("[" + page.Name + ".md]");
            text.AppendLine(page.Text.TrimEnd());
            if (pages.Count > 1)
            {
                text.AppendLine();
                text.AppendLine("Other pages about \"" + topic + "\": " + string.Join(", ", pages.Skip(1).Take(4).Select(p => p.Name)));
            }
            return McpResult.Ok(text.ToString());
        }

        private static string Topics(KnowledgeBase knowledge)
        {
            StringBuilder text = new StringBuilder("The pages (topic = name):\n");
            foreach (KnowledgePage page in knowledge.Pages)
                text.AppendLine("  " + page.Name + " - " + page.Title);
            text.Append("Snippets: nes_snippet with one of " + string.Join(", ", knowledge.Snippets.Select(s => s.Name)) + ".");
            return text.ToString();
        }

        // ---------------------------------------------------------------- nes_search

        private static McpResult Search(KnowledgeBase knowledge, string query)
        {
            List<KnowledgeHit> hits = knowledge.Search(query, SearchResults);
            if (hits.Count == 0)
                return McpResult.Ok("Nothing matches \"" + query + "\". Try other words, or nes_reference with a page name. "
                    + Topics(knowledge));
            StringBuilder text = new StringBuilder();
            foreach (KnowledgeHit hit in hits)
            {
                text.AppendLine("=== " + hit.File + " > " + hit.Section);
                foreach (string line in hit.Excerpt(ExcerptLines))
                    text.AppendLine(line);
                if (hit.Lines.Count > ExcerptLines)
                    text.AppendLine("[... section continues: nes_reference " + Path.GetFileNameWithoutExtension(hit.File) + "]");
                text.AppendLine();
            }
            return McpResult.Ok(text.ToString().TrimEnd() + "\n");
        }

        // ---------------------------------------------------------------- nes_snippet

        private static McpResult Snippet(KnowledgeBase knowledge, string name)
        {
            List<KnowledgeSnippet> found = name.Trim().Length == 0 ? new List<KnowledgeSnippet>() : knowledge.FindSnippets(name);
            if (found.Count == 1)
                return McpResult.Ok("[snippets/" + found[0].Name + ".asm]\n" + found[0].Text.TrimEnd() + "\n");

            StringBuilder text = new StringBuilder();
            IEnumerable<KnowledgeSnippet> listed = found.Count > 1 ? found : knowledge.Snippets;
            text.AppendLine(found.Count > 1
                ? "Several snippets match \"" + name + "\"; call nes_snippet with one name:"
                : (name.Trim().Length == 0 ? "The snippets" : "No snippet \"" + name + "\". The snippets") + " (name - purpose):");
            foreach (KnowledgeSnippet snippet in listed)
                text.AppendLine("  " + snippet.Name + " - " + snippet.Purpose);
            return found.Count > 1 || name.Trim().Length == 0 ? McpResult.Ok(text.ToString()) : McpResult.Fail(text.ToString());
        }

        // ---------------------------------------------------------------- new_nes_project

        private static readonly string[] BuildOutputs = { ".o", ".symb", ".lst", ".nes", ".Unsolved", ".UnsolvedExpr" };

        private static McpResult NewProject(KnowledgeBase knowledge, string folder, string name)
        {
            string target = Path.GetFullPath(folder);
            if (File.Exists(target))
                throw new McpArgumentException(target + " is a file, not a folder");
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                return McpResult.Fail(target + " is not empty: give an empty or new folder (nothing was written).");
            if (!Directory.Exists(knowledge.TemplateFolder))
                throw new InvalidOperationException("the project template is missing: " + knowledge.TemplateFolder);

            string game = Regex.Replace(string.IsNullOrWhiteSpace(name) ? Path.GetFileName(target.TrimEnd('\\', '/')) : name.Trim(),
                "[^A-Za-z0-9_-]+", "_").Trim('_');
            if (game.Length == 0)
                game = "game";

            List<string> written = new List<string>();
            Copy(knowledge.TemplateFolder, target, written);
            string config = Path.Combine(target, "config.json");
            File.WriteAllText(config, File.ReadAllText(config).Replace("\"game.nes\"", "\"" + game + ".nes\""));

            StringBuilder text = new StringBuilder();
            text.AppendLine("Created the NES project " + game + " in " + target + ":");
            foreach (string file in written)
                text.AppendLine("  " + file);
            text.AppendLine();
            text.AppendLine("Next:");
            text.AppendLine("  1. build_project {\"config\": \"" + config.Replace("\\", "\\\\") + "\"} -> " + game + ".nes, 24592 bytes.");
            text.AppendLine("  2. Edit src/game.asm: variables after .memarea $0000, logic in MainLoop (\"game logic goes here\"), "
                + "video memory writes in NMI. Build after every change.");
            text.AppendLine("  3. Before writing NES code, read nes_reference (index lists the pages) and copy routines from nes_snippet.");
            text.AppendLine("The template shows \"HELLO NES\" at row 4, column 11 and the ship sprite (tile $01) at x=120, y=100.");
            return McpResult.Ok(text.ToString());
        }

        private static void Copy(string source, string target, List<string> written)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source).OrderBy(f => f, StringComparer.Ordinal))
            {
                if (BuildOutputs.Any(e => file.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                    continue;
                string to = Path.Combine(target, Path.GetFileName(file));
                File.Copy(file, to);
                written.Add(to);
            }
            foreach (string dir in Directory.GetDirectories(source).OrderBy(d => d, StringComparer.Ordinal))
                Copy(dir, Path.Combine(target, Path.GetFileName(dir)), written);
        }
    }
}
