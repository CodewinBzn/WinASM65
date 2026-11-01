using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.Monitor.Shell;
using ListingRow = WinASM65.Output.ListingRow;
using WinASM65.Output;
using WinASM65.Projects;
using WinASM65.Segments;

namespace WinASM65.Tooling.Lsp
{
    /// <summary>
    /// A language server for WinASM65 sources: diagnostics as you type and a hover
    /// that shows what a line assembled to.
    ///
    /// Two sources of truth, each used where it is right:
    /// <list type="bullet">
    /// <item>On open and save, the whole project is built. Its diagnostics are
    /// exact for every unit and every included file, because the build is the one
    /// that resolves names across units.</item>
    /// <item>While typing, the unsaved text of one unit is listed against the names
    /// the last build saw, so an error appears without saving.</item>
    /// </list>
    /// A file no <c>config.json</c> governs is assembled on its own.
    ///
    /// Everything runs on one worker thread; the reader only queues.
    /// </summary>
    public sealed class LanguageServer : IDisposable
    {
        private const int DebounceMs = 250;

        private readonly Action<JsonObject> _send;
        private readonly BlockingCollection<Action> _work = new BlockingCollection<Action>();
        private readonly Dictionary<string, Document> _documents = new Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Project> _projects = new Dictionary<string, Project>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Timer> _pending = new ConcurrentDictionary<string, Timer>(StringComparer.OrdinalIgnoreCase);
        private readonly string _scratch = Path.Combine(Path.GetTempPath(), "winasm65-lsp-" + Guid.NewGuid().ToString("N"));
        private string _root;

        public LanguageServer(Action<JsonObject> send)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
        }

        public bool Finished { get; private set; }
        public int ExitCode { get; private set; }

        private sealed class Document
        {
            public string Uri;
            public string Path;
            public string Text;
        }

        private sealed class Project
        {
            public string Config;
            public ProjectSession Session;
            public bool Built;
            public ProjectBuildResult LastBuild;
            public Dictionary<string, List<Diagnostic>> ByFile = new Dictionary<string, List<Diagnostic>>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Published = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public void Dispatch(JsonObject message)
        {
            if (!_work.IsAddingCompleted)
                _work.Add(() => Handle(message));
        }

        public void Run()
        {
            foreach (Action action in _work.GetConsumingEnumerable())
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    LogMessage("winasm65: " + ex.Message);
                }
                if (Finished)
                    break;
            }
        }

        public void InputClosed()
        {
            if (!_work.IsAddingCompleted)
                _work.Add(() => Finished = true);
        }

        public void Dispose()
        {
            foreach (Timer timer in _pending.Values)
                timer.Dispose();
            _work.CompleteAdding();
            try { Directory.Delete(_scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        // ------------------------------------------------------------ dispatch

        private void Handle(JsonObject message)
        {
            string method = (string)message["method"];
            JsonNode id = message["id"];
            JsonObject parameters = message["params"] as JsonObject ?? new JsonObject();

            switch (method)
            {
                case "initialize":
                    _root = RootOf(parameters);
                    Reply(id, new JsonObject
                    {
                        ["capabilities"] = new JsonObject
                        {
                            ["textDocumentSync"] = new JsonObject
                            {
                                ["openClose"] = true,
                                ["change"] = 1,
                                ["save"] = new JsonObject { ["includeText"] = false }
                            },
                            ["hoverProvider"] = true
                        },
                        ["serverInfo"] = new JsonObject { ["name"] = "winasm65" }
                    });
                    return;
                case "initialized":
                    return;
                case "shutdown":
                    Reply(id, null);
                    return;
                case "exit":
                    Finished = true;
                    return;
                case "textDocument/didOpen":
                    {
                        JsonObject item = (JsonObject)parameters["textDocument"];
                        Document document = Track((string)item["uri"], (string)item["text"]);
                        BuildAndPublish(document);
                        return;
                    }
                case "textDocument/didChange":
                    {
                        string uri = (string)parameters["textDocument"]["uri"];
                        JsonArray changes = parameters["contentChanges"] as JsonArray;
                        if (changes == null || changes.Count == 0)
                            return;
                        Track(uri, (string)changes[changes.Count - 1]["text"]);
                        Debounce(uri);
                        return;
                    }
                case "textDocument/didSave":
                    {
                        Document document;
                        if (_documents.TryGetValue((string)parameters["textDocument"]["uri"], out document))
                            BuildAndPublish(document);
                        return;
                    }
                case "textDocument/didClose":
                    {
                        string uri = (string)parameters["textDocument"]["uri"];
                        Document document;
                        if (_documents.TryGetValue(uri, out document) && ProjectOf(document.Path) == null)
                            Publish(uri, new List<Diagnostic>(), null);
                        _documents.Remove(uri);
                        return;
                    }
                case "textDocument/hover":
                    Reply(id, Hover(parameters));
                    return;
                case "winasm65/listing":
                    Reply(id, Listing(parameters));
                    return;
            }

            if (id != null)
            {
                _send(new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id.DeepClone(),
                    ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "method not found: " + method }
                });
            }
        }

        private Document Track(string uri, string text)
        {
            Document document;
            if (!_documents.TryGetValue(uri, out document))
            {
                document = new Document { Uri = uri, Path = PathOf(uri) };
                _documents[uri] = document;
            }
            document.Text = text ?? string.Empty;
            return document;
        }

        private void Debounce(string uri)
        {
            Timer timer = new Timer(_ =>
            {
                Timer done;
                _pending.TryRemove(uri, out done);
                done?.Dispose();
                if (!_work.IsAddingCompleted)
                    _work.Add(() => AnalyseUnsaved(uri));
            }, null, DebounceMs, Timeout.Infinite);

            Timer previous = null;
            _pending.AddOrUpdate(uri, timer, (key, old) => { previous = old; return timer; });
            previous?.Dispose();
        }

        // --------------------------------------------------------- diagnostics

        /// <summary>
        /// The config.json that builds <paramref name="path"/>: the one listing it as
        /// an input, else the nearest one above it (the file is then an include).
        /// </summary>
        private Project ProjectOf(string path)
        {
            string config = null;
            string root = _root ?? Path.GetPathRoot(path);
            ProjectGovernance governance = ProjectConfigLocator.Locate(path, root);
            if (governance.Governed)
            {
                // The locator answers relative to the root it was given.
                config = Path.IsPathRooted(governance.Configuration)
                    ? governance.Configuration
                    : Path.Combine(root, governance.Configuration);
            }
            else
            {
                DirectoryInfo dir = new FileInfo(path).Directory;
                for (int depth = 0; dir != null && depth < 8; depth++, dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, "config.json");
                    if (File.Exists(candidate))
                    {
                        config = candidate;
                        break;
                    }
                }
            }
            if (config == null)
                return null;

            config = Path.GetFullPath(config);
            Project project;
            if (!_projects.TryGetValue(config, out project))
            {
                try
                {
                    project = new Project { Config = config, Session = ProjectSession.Open(config) };
                }
                catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is Newtonsoft.Json.JsonException)
                {
                    LogMessage("winasm65: cannot open " + config + ": " + ex.Message);
                    return null;
                }
                _projects[config] = project;
            }
            return project;
        }

        private void BuildAndPublish(Document document)
        {
            Project project = ProjectOf(document.Path);
            if (project == null)
            {
                AnalyseUnsaved(document.Uri);
                return;
            }

            Directory.CreateDirectory(_scratch);
            ProjectBuildResult result = project.Session.Build(new ProjectBuildOptions
            {
                CaptureListing = true,
                SideFileDirectory = Path.Combine(_scratch, Math.Abs(project.Config.GetHashCode()).ToString(CultureInfo.InvariantCulture))
            });
            project.Built = result.Success;
            if (result.Success)
                project.LastBuild = result;

            project.ByFile.Clear();
            foreach (Diagnostic diagnostic in result.Diagnostics)
            {
                string file = string.IsNullOrEmpty(diagnostic.Location.FilePath) ? project.Config : Path.GetFullPath(diagnostic.Location.FilePath);
                List<Diagnostic> list;
                if (!project.ByFile.TryGetValue(file, out list))
                    project.ByFile[file] = list = new List<Diagnostic>();
                list.Add(diagnostic);
            }

            // Every file the build spoke about, and every file it spoke about last
            // time: a fixed error must disappear from a file nobody has open.
            HashSet<string> files = new HashSet<string>(project.ByFile.Keys, StringComparer.OrdinalIgnoreCase);
            files.UnionWith(project.Published);
            files.Add(Path.GetFullPath(document.Path));
            foreach (string file in files)
            {
                List<Diagnostic> list;
                project.ByFile.TryGetValue(file, out list);
                Publish(UriFor(file), list ?? new List<Diagnostic>(), TextOf(file));
            }
            project.Published = new HashSet<string>(project.ByFile.Keys, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Lists the unsaved text of one file. In a project it is listed against the
        /// names the last successful build saw; until one succeeds, an undefined
        /// name is not reported, because it may well be defined in another unit.
        /// </summary>
        private void AnalyseUnsaved(string uri)
        {
            Document document;
            if (!_documents.TryGetValue(uri, out document))
                return;

            Project project = ProjectOf(document.Path);
            ProjectSession session = project != null
                ? project.Session
                : ProjectSession.Open(new ConfigFile(), Path.GetDirectoryName(document.Path));

            if (project != null && !IsUnit(project, document.Path))
                return; // an include: only the build knows the context it is read in

            SourceListing listing = session.ListSourceText(document.Text, document.Path);
            bool namesKnown = project == null || project.Built;
            List<Diagnostic> mine = listing.Assembly.Diagnostics
                .Where(d => namesKnown || !d.Message.StartsWith(ErrorCodes.UNDEFINED_SYMBOL, StringComparison.OrdinalIgnoreCase))
                .Where(d => string.IsNullOrEmpty(d.Location.FilePath) || SamePath(d.Location.FilePath, document.Path))
                .ToList();
            Publish(uri, mine, document.Text);
        }

        private static bool IsUnit(Project project, string path)
        {
            return project.Session.SourceFiles.Any(f => SamePath(f, path));
        }

        private void Publish(string uri, List<Diagnostic> diagnostics, string text)
        {
            string[] lines = text == null ? new string[0] : text.Replace("\r\n", "\n").Split('\n');
            JsonArray items = new JsonArray();
            foreach (Diagnostic diagnostic in diagnostics)
            {
                int line = Math.Max(0, diagnostic.Location.LineNumber - 1);
                int end = line < lines.Length ? Math.Max(1, lines[line].Length) : 1;
                items.Add(new JsonObject
                {
                    ["range"] = Range(line, 0, line, end),
                    ["severity"] = diagnostic.Severity == DiagnosticSeverity.Error ? 1 : 2,
                    ["source"] = "winasm65",
                    ["message"] = diagnostic.Message
                });
            }
            Notify("textDocument/publishDiagnostics", new JsonObject { ["uri"] = uri, ["diagnostics"] = items });
        }

        // ---------------------------------------------------------------- hover

        private JsonObject Hover(JsonObject parameters)
        {
            Document document;
            if (!_documents.TryGetValue((string)parameters["textDocument"]["uri"], out document))
                return null;
            int line = (int)parameters["position"]["line"];
            int character = (int)parameters["position"]["character"];
            string[] lines = document.Text.Replace("\r\n", "\n").Split('\n');
            if (line < 0 || line >= lines.Length)
                return null;

            Project project = ProjectOf(document.Path);
            ProjectSession session = project != null ? project.Session : ProjectSession.Open(new ConfigFile(), Path.GetDirectoryName(document.Path));
            SourceListing listing = session.ListSourceText(document.Text, document.Path);

            string word = WordAt(lines[line], character);
            ICpuInstructionSet cpu = CpuOf(session);

            ListingRow row = listing.Rows.FirstOrDefault(r => r.Kind == ListingRowKind.Instruction && r.Address.HasValue
                && r.LineNumber == line + 1 && (string.IsNullOrEmpty(r.SourceName) || SamePath(r.SourceName, document.Path)));
            InstructionDocumentation assembled = row == null ? null : FormOf(cpu, row, lines[line]);

            // On a mnemonic, or anywhere on an instruction line but a name: the
            // instruction's card. On a name: its value, then what the line assembled to.
            string markdown = null;
            long value;
            bool onMnemonic = word != null && InstructionDocs.IsMnemonic(word, cpu);
            if (!onMnemonic && word != null
                && (session.ProjectSymbols.TryGetValue(word, out value) || TrySymbolOf(listing, word, out value)))
            {
                markdown = HoverCard.Symbol(word, value, lines[line], assembled, row);
            }
            else if (onMnemonic || assembled != null)
            {
                string mnemonic = onMnemonic ? word : assembled.Mnemonic;
                markdown = HoverCard.Instruction(mnemonic, lines[line], assembled,
                    InstructionDocs.ForMnemonic(mnemonic, cpu), row);
            }

            if (markdown == null)
                return null;
            return new JsonObject
            {
                ["contents"] = new JsonObject { ["kind"] = "markdown", ["value"] = markdown.ToString() }
            };
        }

        /// <summary>
        /// The listing column the editor draws left of the source: for each line that
        /// emitted something, its address and bytes. A unit is listed from its unsaved
        /// text; an included file only exists inside a unit, so its rows come from the
        /// last successful build.
        /// </summary>
        private JsonObject Listing(JsonObject parameters)
        {
            string uri = (string)parameters["textDocument"]?["uri"] ?? (string)parameters["uri"];
            if (uri == null)
                return new JsonObject { ["rows"] = new JsonArray() };

            Document document;
            string path = _documents.TryGetValue(uri, out document) ? document.Path : PathOf(uri);
            string text = document != null ? document.Text : TextOf(path) ?? string.Empty;

            Project project = ProjectOf(path);
            IEnumerable<ListingRow> rows;
            if (project != null && !IsUnit(project, path))
            {
                string directory = project.Session.BaseDirectory;
                rows = project.LastBuild == null
                    ? Enumerable.Empty<ListingRow>()
                    : project.LastBuild.Units.SelectMany(u => u.Listing)
                        .Where(r => !string.IsNullOrEmpty(r.SourceName) && SamePath(Rooted(r.SourceName, directory), path));
            }
            else
            {
                ProjectSession session = project != null
                    ? project.Session
                    : ProjectSession.Open(new ConfigFile(), Path.GetDirectoryName(path));
                string directory = Path.GetDirectoryName(path);
                rows = session.ListSourceText(text, path).Rows
                    .Where(r => string.IsNullOrEmpty(r.SourceName) || SamePath(Rooted(r.SourceName, directory), path));
            }

            JsonArray result = new JsonArray();
            foreach (IGrouping<int, ListingRow> line in rows
                .Where(r => r.Kind == ListingRowKind.Instruction && r.Address.HasValue && r.Bytes.Count > 0)
                .GroupBy(r => r.LineNumber)
                .OrderBy(g => g.Key))
            {
                ListingRow first = line.First();
                result.Add(new JsonObject
                {
                    ["line"] = first.LineNumber - 1,
                    ["address"] = first.Address.Value,
                    ["bytes"] = string.Join(" ", first.Bytes.Select(b => b.ToString("X2"))),
                    ["more"] = line.Count() > 1
                });
            }
            return new JsonObject { ["rows"] = result };
        }

        private static string Rooted(string name, string directory)
        {
            return Path.IsPathRooted(name) || string.IsNullOrEmpty(directory) ? name : Path.Combine(directory, name);
        }

        /// <summary>The CPU the project assembles for (its Target), the NMOS 6502 otherwise.</summary>
        private static ICpuInstructionSet CpuOf(ProjectSession session)
        {
            try
            {
                return CpuFactory.Create(WinASM65.Targets.TargetResolver.Resolve(session.Configuration.Target, null, null, null).CpuName);
            }
            catch (ArgumentException)
            {
                return new Cpu6502();
            }
        }

        /// <summary>
        /// The instruction form a listing row holds: the documented form with its first
        /// byte as opcode, the same length, and its mnemonic written on the line. A data
        /// row (".byte $A9, $01") has no mnemonic on its line and gets none.
        /// </summary>
        private static InstructionDocumentation FormOf(ICpuInstructionSet cpu, ListingRow row, string text)
        {
            if (row.Bytes.Count == 0)
                return null;
            foreach (InstructionDocumentation doc in InstructionDocs.ForCpu(cpu))
            {
                if (doc.Opcode == row.Bytes[0] && doc.Length == row.Bytes.Count
                    && System.Text.RegularExpressions.Regex.IsMatch(text, @"\b" + doc.Mnemonic + @"\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return doc;
            }
            return null;
        }

        private static bool TrySymbolOf(SourceListing listing, string word, out long value)
        {
            value = 0;
            foreach (ListingRow row in listing.Rows)
            {
                // A label has its address; a constant (and a named .res) its value.
                int? rowValue = row.Kind == ListingRowKind.Label ? row.Address
                    : row.Kind == ListingRowKind.Constant || row.Kind == ListingRowKind.Reserve ? row.Value ?? row.Address
                    : null;
                if (rowValue.HasValue && row.Text != null
                    && row.Text.TrimStart().StartsWith(word, StringComparison.OrdinalIgnoreCase))
                {
                    string rest = row.Text.TrimStart().Substring(word.Length);
                    if (rest.Length == 0 || rest[0] == ':' || rest[0] == '=' || char.IsWhiteSpace(rest[0]))
                    {
                        value = rowValue.Value;
                        return true;
                    }
                }
            }
            return false;
        }

        internal static string WordAt(string line, int character)
        {
            if (line == null || character < 0 || character > line.Length)
                return null;
            Func<char, bool> part = c => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '@';
            int start = character, end = character;
            while (start > 0 && part(line[start - 1]))
                start--;
            while (end < line.Length && part(line[end]))
                end++;
            if (end <= start)
                return null;
            string word = line.Substring(start, end - start).TrimEnd('.');
            return word.Length == 0 || char.IsDigit(word[0]) ? null : word;
        }

        // -------------------------------------------------------------- helpers

        /// <summary>The URI VS Code used for an open file, so both sides name it alike.</summary>
        private string UriFor(string path)
        {
            Document open = _documents.Values.FirstOrDefault(d => SamePath(d.Path, path));
            return open != null ? open.Uri : UriOf(path);
        }

        private string TextOf(string path)
        {
            Document open = _documents.Values.FirstOrDefault(d => SamePath(d.Path, path));
            if (open != null)
                return open.Text;
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (IOException) { return null; }
        }

        private static JsonObject Range(int startLine, int startCharacter, int endLine, int endCharacter)
        {
            return new JsonObject
            {
                ["start"] = new JsonObject { ["line"] = startLine, ["character"] = startCharacter },
                ["end"] = new JsonObject { ["line"] = endLine, ["character"] = endCharacter }
            };
        }

        private void Reply(JsonNode id, JsonNode result)
        {
            _send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result });
        }

        private void Notify(string method, JsonObject parameters)
        {
            _send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });
        }

        private void LogMessage(string text)
        {
            Notify("window/logMessage", new JsonObject { ["type"] = 3, ["message"] = text });
        }

        private static string RootOf(JsonObject parameters)
        {
            JsonArray folders = parameters["workspaceFolders"] as JsonArray;
            string uri = folders != null && folders.Count > 0 ? (string)folders[0]["uri"] : (string)parameters["rootUri"];
            return uri == null ? null : PathOf(uri);
        }

        /// <summary>
        /// VS Code escapes the drive colon (<c>file:///c%3A/x</c>), which
        /// <see cref="Uri.LocalPath"/> leaves as a rooted <c>/c:/x</c>; the path is
        /// therefore unescaped and its leading slash dropped by hand.
        /// </summary>
        internal static string PathOf(string uri)
        {
            Uri parsed = new Uri(uri);
            if (!string.IsNullOrEmpty(parsed.Host))
                return parsed.LocalPath; // UNC
            string path = Uri.UnescapeDataString(parsed.AbsolutePath);
            if (path.Length >= 3 && path[0] == '/' && char.IsLetter(path[1]) && path[2] == ':')
                path = path.Substring(1);
            return Path.GetFullPath(path);
        }

        internal static string UriOf(string path)
        {
            return new Uri(Path.GetFullPath(path)).AbsoluteUri;
        }

        private static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
