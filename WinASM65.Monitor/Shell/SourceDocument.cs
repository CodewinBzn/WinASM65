using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// Reads and writes a source file without changing its encoding or line
    /// endings, and refuses to write one that mixes them.
    ///
    /// A source editor that calls <c>File.WriteAllText</c> turns a one-character
    /// edit into a whole-file diff: every line ending changes, and the encoding
    /// is re-decided from scratch. This type remembers the encoding and the line
    /// ending when the document is loaded, and writes back with both, so an edit
    /// writes only the line that changed.
    ///
    /// It confines paths the way <see cref="ShellRunner.ResolveInitialSource"/>
    /// does: relative names are taken against the session's directory, and
    /// <c>..\..\</c> that escapes it is refused rather than followed. A source
    /// outside the directory being edited is a different file, not a different
    /// path to the same one, and the shell must not write to one by accident.
    /// </summary>
    public sealed class SourceDocument
    {
        private readonly string _directory;
        private Encoding _encoding;
        private LineEnding _lineEnding;
        private bool _trailingNewline;
        private bool _refuseWrite;
        private byte[] _preamble = new byte[0];

        private static readonly HashSet<string> EditableExtensions = new HashSet<string>(
            new[] { ".asm", ".s", ".inc", ".65", ".nas", ".json", ".jsonc", ".txt", ".md",
                ".mdx", ".rst", ".cfg", ".ini", ".yaml", ".yml", ".xml", ".lua", ".bat",
                ".cmd", ".ps1", ".sh", ".py", ".c", ".h", ".cc", ".cpp", ".hpp", ".js",
                ".jsx", ".mjs", ".ts", ".tsx", ".html", ".css", ".scss", ".csv", ".toml",
                ".sln", ".csproj", ".props", ".targets", ".editorconfig", ".gitignore",
                ".gitattributes", ".dockerignore", ".config", ".resx", ".xaml", ".sql",
                ".go", ".rs", ".java", ".kt", ".rb", ".php", ".cs", ".fs", ".fsproj",
                ".gradle", ".properties", ".lock", ".env", ".example", ".log", ".mk",
                ".make", ".vue", ".svelte", ".pl", ".manifest" },
            StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> AssemblerExtensions = new HashSet<string>(
            new[] { ".asm", ".s", ".inc", ".65", ".nas" }, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> EditableDotFiles = new HashSet<string>(
            new[] { ".gitignore", ".gitattributes", ".dockerignore", ".editorconfig" },
            StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> EditableExtensionlessFiles = new HashSet<string>(
            new[] { "makefile", "dockerfile", "readme", "license", "changelog", "authors",
                "contributors", "copying", "notice", "justfile", "procfile" },
            StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// How the loaded file ends its lines. Stored so a write can reproduce it,
        /// and checked so a write can refuse a buffer that mixes them.
        /// </summary>
        private enum LineEnding
        {
            /// <summary>None detected: the file has no line breaks.</summary>
            None,

            /// <summary><c>\r\n</c> throughout.</summary>
            CRLF,

            /// <summary><c>\n</c> throughout.</summary>
            LF,

            /// <summary><c>\r</c> throughout.</summary>
            CR,

            /// <summary>Both seen: the file mixes endings, and a write refuses.</summary>
            Mixed
        }

        /// <summary>
        /// Creates a document rooted at <paramref name="directory"/>, the same
        /// directory every other path in the session resolves against.
        /// </summary>
        public SourceDocument(string directory)
        {
            _directory = string.IsNullOrEmpty(directory)
                ? Directory.GetCurrentDirectory()
                : directory;
            _encoding = Encoding.UTF8;
            _lineEnding = LineEnding.None;
        }

        /// <summary>
        /// Reads the file, or returns a reason and an empty list when it cannot.
        ///
        /// The lines returned have no trailing line ending: the editor splits on
        /// one and joins on one, so carrying the ending would add it back twice.
        /// The file's encoding and line ending are remembered so a later
        /// <see cref="Write"/> reproduces them rather than re-deciding them.
        /// </summary>
        public IReadOnlyList<string> Read(string sourceFile, out string problem)
        {
            string text = ReadText(sourceFile, out problem);
            return text == null ? new string[0] : SplitLines(text);
        }

        /// <summary>Reads a supported project text file, normalizing its line endings
        /// for the editor while remembering the original encoding and newline style.</summary>
        public string ReadText(string sourceFile, out string problem)
        {
            problem = null;

            string path = Resolve(sourceFile);
            if (path == null)
            {
                problem = sourceFile + " is outside the directory being edited; refusing to read it.";
                return null;
            }

            if (!IsEditableTextFile(path))
            {
                problem = sourceFile + " is not a supported project text file.";
                return null;
            }

            if (!File.Exists(path))
            {
                problem = "no such source: " + sourceFile;
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (IOException ex)
            {
                problem = "cannot read " + sourceFile + ": " + ex.Message;
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                problem = "cannot read " + sourceFile + ": " + ex.Message;
                return null;
            }

            _encoding = DetectEncoding(bytes);
            _preamble = DetectPreamble(bytes);
            string content;
            try
            {
                content = _encoding.GetString(bytes, _preamble.Length, bytes.Length - _preamble.Length);
            }
            catch (DecoderFallbackException)
            {
                _encoding = Encoding.Latin1;
                content = _encoding.GetString(bytes, _preamble.Length, bytes.Length - _preamble.Length);
            }
            if (content.Any(character => character == '\0'
                || (char.IsControl(character) && character != '\r' && character != '\n' && character != '\t')))
            {
                _refuseWrite = true;
                problem = sourceFile + " appears to be binary; refusing to edit it.";
                return null;
            }
            _lineEnding = DetectLineEnding(content);
            _trailingNewline = content.EndsWith("\n", StringComparison.Ordinal)
                || content.EndsWith("\r", StringComparison.Ordinal);

            if (_lineEnding == LineEnding.Mixed)
            {
                _refuseWrite = true;
                problem = sourceFile + " mixes line endings; refusing to remember a style to write back.";
                return null;
            }

            _refuseWrite = false;
            return content.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        /// <summary>
        /// Writes <paramref name="text"/> as the source file, preserving the
        /// encoding and line ending detected at load.
        ///
        /// Returns false and a problem when the file cannot be written: the path
        /// escapes the directory, the text mixes line endings, or the OS refuses
        /// the write. Each lands on the message line rather than taking the shell
        /// down, which is the rule a pane that touches the file system must keep.
        /// </summary>
        public bool Write(string sourceFile, string text, out string problem)
        {
            problem = null;

            string path = Resolve(sourceFile);
            if (path == null)
            {
                problem = sourceFile + " is outside the directory being edited; refusing to write it.";
                return false;
            }
            if (!IsEditableTextFile(path))
            {
                problem = sourceFile + " is not a supported project text file.";
                return false;
            }
            if (_refuseWrite)
            {
                problem = sourceFile + " was refused when read; refusing to overwrite it.";
                return false;
            }

            LineEnding detected = DetectLineEnding(text ?? string.Empty);
            if (detected == LineEnding.Mixed)
            {
                problem = sourceFile + " mixes line endings; save after picking one style, or fix the whole file.";
                return false;
            }

            // The text from the editor has the editor's endings (\n on this
            // platform), not the file's. Write back with the ending the file
            // came in as so a one-character edit does not reformat the whole
            // file. Fall back to the detected ending when the file had none.
            LineEnding ending = _lineEnding != LineEnding.None ? _lineEnding : detected;

            string content = text ?? string.Empty;
            if (_trailingNewline && content.Length > 0
                && !content.EndsWith("\n", StringComparison.Ordinal)
                && !content.EndsWith("\r", StringComparison.Ordinal))
            {
                content += "\n";
            }
            byte[] encoded = _encoding.GetBytes(ApplyEnding(content, ending));
            byte[] bytes = new byte[_preamble.Length + encoded.Length];
            Buffer.BlockCopy(_preamble, 0, bytes, 0, _preamble.Length);
            Buffer.BlockCopy(encoded, 0, bytes, _preamble.Length, encoded.Length);

            try
            {
                File.WriteAllBytes(path, bytes);
            }
            catch (IOException ex)
            {
                problem = "cannot write " + sourceFile + ": " + ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                problem = "cannot write " + sourceFile + ": " + ex.Message;
                return false;
            }

            _lineEnding = ending;
            return true;
        }

        public static bool IsEditableTextFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            string name = Path.GetFileName(path);
            return EditableDotFiles.Contains(name) || EditableExtensionlessFiles.Contains(name)
                || EditableExtensions.Contains(Path.GetExtension(path));
        }

        public static bool IsAssemblerSourceFile(string path)
        {
            return !string.IsNullOrEmpty(path)
                && AssemblerExtensions.Contains(Path.GetExtension(path));
        }

        /// <summary>
        /// Resolves <paramref name="sourceFile"/> against the session directory,
        /// refusing paths that escape it. Returns null when the path is outside
        /// the directory or otherwise unresolved.
        /// </summary>
        private string Resolve(string sourceFile)
        {
            if (string.IsNullOrEmpty(sourceFile))
                return null;

            string fullPath;
            try
            {
                if (Path.IsPathRooted(sourceFile))
                    fullPath = Path.GetFullPath(sourceFile);
                else
                    fullPath = Path.GetFullPath(Path.Combine(_directory, sourceFile));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException
                || ex is PathTooLongException)
            {
                return null;
            }

            string dirRoot = Path.GetFullPath(_directory);
            string rootWithSeparator = dirRoot.TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            // Path.GetFullPath normalises casing and separators on Windows, so a
            // StartsWith check at the end of a directory separator is what keeps
            // "C:\proj\evil" from matching "C:\project".
            if (!string.Equals(fullPath, dirRoot, StringComparison.OrdinalIgnoreCase)
                && !fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                return null;

            // Do not let a project-local symlink redirect the editor outside its root.
            string relative = Path.GetRelativePath(dirRoot, fullPath);
            string current = dirRoot;
            string[] segments = relative.Split(new[] { Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length; i++)
            {
                current = Path.Combine(current, segments[i]);
                FileSystemInfo info = i == segments.Length - 1
                    ? (FileSystemInfo)new FileInfo(current)
                    : new DirectoryInfo(current);
                try
                {
                    if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0)
                        return null;
                }
                catch (IOException)
                {
                    return null;
                }
                catch (UnauthorizedAccessException)
                {
                    return null;
                }
            }

            return fullPath;
        }

        private static Encoding DetectEncoding(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return new UTF8Encoding(false, true);

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

            if (bytes.Length >= 2)
            {
                if (bytes[0] == 0xFF && bytes[1] == 0xFE)
                    return new UnicodeEncoding(false, true, true);
                if (bytes[0] == 0xFE && bytes[1] == 0xFF)
                    return new UnicodeEncoding(true, true, true);
            }

            return new UTF8Encoding(false, true);
        }

        private static byte[] DetectPreamble(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new byte[] { 0xEF, 0xBB, 0xBF };
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return new byte[] { 0xFF, 0xFE };
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return new byte[] { 0xFE, 0xFF };
            return new byte[0];
        }

        private static LineEnding DetectLineEnding(string text)
        {
            if (text == null || text.Length == 0)
                return LineEnding.None;

            // Count \r\n, lone \n, and lone \r. A \r immediately followed by \n
            // is a CRLF pair, not two separate endings, so the index advances
            // past both and only the pair is counted.
            int crlf = 0, lf = 0, cr = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        crlf++;
                        i++;
                    }
                    else
                    {
                        cr++;
                    }
                }
                else if (text[i] == '\n')
                {
                    lf++;
                }
            }

            int kinds = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);
            if (kinds > 1)
                return LineEnding.Mixed;

            if (crlf > 0)
                return LineEnding.CRLF;
            if (lf > 0)
                return LineEnding.LF;
            if (cr > 0)
                return LineEnding.CR;

            return LineEnding.None;
        }

        /// <summary>
        /// Splits content into lines, dropping the line-ending characters. A
        /// trailing newline that would produce an empty final element is dropped,
        /// so the editor shows one row per line the user would see — not one
        /// more for the terminator that is already at the end of the last.
        /// </summary>
        private static IReadOnlyList<string> SplitLines(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new string[0];

            string[] lines = content.Split(new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None);

            // A trailing line ending produces a final string that is empty;
            // drop it so "a\nb\n" yields ["a","b"] not ["a","b",""].
            if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
            {
                string[] trimmed = new string[lines.Length - 1];
                Array.Copy(lines, 0, trimmed, 0, lines.Length - 1);
                return trimmed;
            }

            return lines;
        }

        /// <summary>
        /// Re-applies the detected ending to joined lines, so a CRLF file is
        /// written back as CRLF and not LF.
        /// </summary>
        private static string ApplyEnding(string text, LineEnding ending)
        {
            string[] lines = text.Split(new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None);

            string separator;
            switch (ending)
            {
                case LineEnding.CRLF:
                    separator = "\r\n";
                    break;
                case LineEnding.LF:
                    separator = "\n";
                    break;
                case LineEnding.CR:
                    separator = "\r";
                    break;
                default:
                    separator = "\r\n";
                    break;
            }

            return string.Join(separator, lines);
        }
    }
}
