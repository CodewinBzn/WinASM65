using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Terminal.Gui;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// The shell palette: a colour per role, plus the 16-colour name to fall back
    /// to when the terminal cannot do 24-bit.
    ///
    /// Loaded from a file so it can be edited without recompiling, and built into
    /// the assembly as the default so the tool works with no file at all. Loading
    /// never throws and never throws away a working palette: an absent file, a file
    /// that is not JSON, a role spelled wrongly and a colour that is not a colour
    /// each degrade one step, and each step says what it did in
    /// <see cref="Diagnostics"/>.
    ///
    /// Why it cannot crash the app: the whole file is read inside a bounded catch,
    /// and every value is validated before use. A theme is a convenience; a theme
    /// that takes the monitor down with it would be a worse bug than no theme.
    ///
    /// Format (<c>theme.json</c>, beside the executable or in the project):
    /// <code>
    /// {
    ///   "name": "WinASM65 dark",
    ///   "colors": {
    ///     "mnemonic": { "true": "#7FB2E5", "fallback": "blue" }
    ///   }
    /// }
    /// </code>
    /// Comments and trailing commas are accepted. Every role in
    /// <see cref="ThemeRole"/> may appear; every other key is reported and ignored.
    /// </summary>
    public sealed class ShellTheme
    {
        /// <summary>File name looked for beside the executable and in the project.</summary>
        public const string FileName = "theme.json";

        private static readonly ShellTheme BuiltInTheme = CreateBuiltIn();

        private readonly Dictionary<string, Color> _colors;
        private readonly Dictionary<string, ColorName16> _fallbacks;

        private ShellTheme(
            string name,
            string path,
            bool builtIn,
            Dictionary<string, Color> colors,
            Dictionary<string, ColorName16> fallbacks,
            IReadOnlyList<string> diagnostics)
        {
            Name = name;
            FilePath = path;
            IsBuiltIn = builtIn;
            _colors = colors;
            _fallbacks = fallbacks;
            Diagnostics = diagnostics;
        }

        /// <summary>Theme name, for the status line and for the help screen.</summary>
        public string Name { get; }

        /// <summary>The file the theme came from, or null when there was none.</summary>
        public string FilePath { get; }

        /// <summary>
        /// True when nothing from the file reached the screen: no file, an
        /// unreadable file, or a file in which no role was usable. Reported in the
        /// status line, because a palette the user edited and never saw is a silent
        /// failure.
        ///
        /// A file whose roles partly applied is <em>not</em> built-in: the colours
        /// that did apply are on screen, and the ones that did not are named in
        /// <see cref="Diagnostics"/>. Calling that "built-in" would hide the half
        /// that worked.
        /// </summary>
        public bool IsBuiltIn { get; }

        /// <summary>
        /// What loading did, in plain sentences. Empty when the file loaded whole.
        /// Never an exception: a theme problem is displayed, not thrown.
        /// </summary>
        public IReadOnlyList<string> Diagnostics { get; }

        /// <summary>The built-in palette. Never null, never partial.</summary>
        public static ShellTheme Default
        {
            get { return BuiltInTheme; }
        }

        /// <summary>The 24-bit colour for a role, whatever the terminal can do.</summary>
        public Color TrueColor(string role)
        {
            Color color;
            if (_colors.TryGetValue(Normalise(role), out color))
                return color;
            return _colors[ThemeRole.Default];
        }

        /// <summary>The 16-colour stand-in for a role.</summary>
        public ColorName16 Fallback(string role)
        {
            ColorName16 name;
            if (_fallbacks.TryGetValue(Normalise(role), out name))
                return name;
            return _fallbacks[ThemeRole.Default];
        }

        /// <summary>
        /// The colour to paint a role with, given what the terminal can do.
        ///
        /// The fallback is a lookup on the role, not a second theme: an edit to a
        /// role's 24-bit value changes nothing about its 16-colour stand-in, and
        /// the two can never disagree about which role they belong to.
        /// </summary>
        public Color Resolve(string role, bool trueColorSupported)
        {
            return trueColorSupported ? TrueColor(role) : new Color(Fallback(role));
        }

        /// <summary>
        /// The colour to paint a role with, detecting 24-bit support from the
        /// Terminal.Gui driver. Falls back to the 16-colour column when the driver
        /// reports no TrueColor, which is what a plain Windows console does.
        /// </summary>
        public Color Resolve(string role)
        {
            return Resolve(role, SupportsTrueColor());
        }

        /// <summary>
        /// Foreground for a role over the terminal's own background. The palette
        /// never paints a background: a theme that recoloured the whole screen would
        /// fight the user's terminal setting rather than decorate it.
        /// </summary>
        public Terminal.Gui.Attribute Attribute(string role)
        {
            return new Terminal.Gui.Attribute(Resolve(role));
        }

        private static bool SupportsTrueColor()
        {
            try
            {
                // A driver only exists once the application has been initialised,
                // which is never the case in a unit test. No driver, no 24-bit.
                IConsoleDriver driver = Application.Driver;
                return driver != null && !driver.Force16Colors && driver.SupportsTrueColor;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Normalise(string role)
        {
            return role == null ? ThemeRole.Default : role.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Looks for the theme beside the executable, then in the source tree, and
        /// loads the first one found. A missing file is not an error: the built-in
        /// palette is the answer, and the reason is recorded.
        /// </summary>
        public static ShellTheme Load(string explicitPath)
        {
            if (!string.IsNullOrEmpty(explicitPath))
                return LoadFile(explicitPath);

            string beside = Path.Combine(AppContext.BaseDirectory, FileName);
            if (File.Exists(beside))
                return LoadFile(beside);

            string inTree = FindInSourceTree();
            if (inTree != null)
                return LoadFile(inTree);

            return WithDiagnostic(BuiltInTheme,
                "no " + FileName + " found beside the executable; using the built-in palette");
        }

        public static ShellTheme LoadFile(string path)
        {
            string text;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return WithDiagnostic(BuiltInTheme,
                        "theme file not found: " + path + "; using the built-in palette");
                }
                text = File.ReadAllText(path);
            }
            catch (IOException ex)
            {
                return WithDiagnostic(BuiltInTheme,
                    "cannot read " + path + ": " + ex.Message + "; using the built-in palette");
            }
            catch (UnauthorizedAccessException ex)
            {
                return WithDiagnostic(BuiltInTheme,
                    "cannot read " + path + ": " + ex.Message + "; using the built-in palette");
            }

            return Parse(text, path);
        }

        /// <summary>
        /// Parses theme JSON. Every failure path ends at the built-in palette,
        /// including the ones nobody expected: a theme file is input, and input is
        /// not trusted to be well formed.
        /// </summary>
        public static ShellTheme Parse(string json, string path)
        {
            if (json == null)
                return WithDiagnostic(BuiltInTheme, "the theme file is empty; using the built-in palette");

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
            }
            catch (JsonException ex)
            {
                return WithDiagnostic(BuiltInTheme,
                    "theme " + path + " is not valid JSON (" + ex.Message + "); using the built-in palette");
            }

            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return WithDiagnostic(BuiltInTheme,
                        "theme " + path + " is not a JSON object; using the built-in palette");
                }

                List<string> diagnostics = new List<string>();
                string name = BuiltInTheme.Name;

                JsonElement nameElement;
                if (root.TryGetProperty("name", out nameElement))
                {
                    if (nameElement.ValueKind == JsonValueKind.String)
                        name = nameElement.GetString();
                    else
                        diagnostics.Add("theme name is not a string; using '" + BuiltInTheme.Name + "'");
                }

                JsonElement palette;
                if (!root.TryGetProperty("colors", out palette) || palette.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add("no 'colors' object in " + path + "; using the built-in palette");
                    return new ShellTheme(name, path, true,
                        new Dictionary<string, Color>(BuiltInTheme._colors, StringComparer.OrdinalIgnoreCase),
                        new Dictionary<string, ColorName16>(BuiltInTheme._fallbacks, StringComparer.OrdinalIgnoreCase),
                        diagnostics);
                }

                // Start from the built-in palette, so a file that sets one role does
                // not blank the other eleven.
                Dictionary<string, Color> colors =
                    new Dictionary<string, Color>(BuiltInTheme._colors, StringComparer.OrdinalIgnoreCase);
                Dictionary<string, ColorName16> fallbacks =
                    new Dictionary<string, ColorName16>(BuiltInTheme._fallbacks, StringComparer.OrdinalIgnoreCase);

                int applied = 0;
                foreach (JsonProperty entry in palette.EnumerateObject())
                {
                    string role = entry.Name;
                    if (!ThemeRole.IsKnown(role))
                    {
                        diagnostics.Add("unknown role '" + role + "' ignored");
                        continue;
                    }

                    if (entry.Value.ValueKind != JsonValueKind.Object)
                    {
                        diagnostics.Add("role '" + role + "' is not an object; keeping the built-in colours");
                        continue;
                    }

                    if (ApplyRole(entry.Value, role, colors, fallbacks, diagnostics))
                        applied++;
                }

                return new ShellTheme(name, path, applied == 0, colors, fallbacks, diagnostics);
            }
        }

        private static bool ApplyRole(
            JsonElement entry,
            string role,
            Dictionary<string, Color> colors,
            Dictionary<string, ColorName16> fallbacks,
            List<string> diagnostics)
        {
            bool applied = false;
            JsonElement value;

            if (entry.TryGetProperty("true", out value))
            {
                Color parsed;
                if (value.ValueKind == JsonValueKind.String && TryParseColor(value.GetString(), out parsed))
                {
                    colors[role] = parsed;
                    applied = true;
                }
                else
                {
                    diagnostics.Add("role '" + role + "': '" + Describe(value)
                        + "' is not a colour; keeping the built-in one");
                }
            }

            if (entry.TryGetProperty("fallback", out value))
            {
                ColorName16 parsed;
                if (value.ValueKind == JsonValueKind.String && TryParseColorName(value.GetString(), out parsed))
                {
                    fallbacks[role] = parsed;
                    applied = true;
                }
                else
                {
                    diagnostics.Add("role '" + role + "': '" + Describe(value)
                        + "' is not a 16-colour name; keeping the built-in one");
                }
            }

            return applied;
        }

        private static string Describe(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind.ToString();
        }

        /// <summary>
        /// Accepts the two forms a person actually types for a 24-bit value,
        /// <c>#RRGGBB</c> and <c>#RRGGBBAA</c>, and otherwise defers to Terminal.Gui
        /// for the named colours it already knows. Inventing a second colour
        /// vocabulary beside the library's would give two answers to one question.
        /// </summary>
        internal static bool TryParseColor(string text, out Color color)
        {
            color = Color.White;
            if (string.IsNullOrEmpty(text))
                return false;

            string trimmed = text.Trim();
            if (trimmed.Length == 0)
                return false;

            if (trimmed[0] == '#')
            {
                string digits = trimmed.Substring(1);
                if (digits.Length != 6 && digits.Length != 8)
                    return false;

                for (int i = 0; i < digits.Length; i++)
                {
                    if (!IsHexDigit(digits[i]))
                        return false;
                }

                if (digits.Length == 6)
                {
                    color = new Color(Channel(digits, 0), Channel(digits, 2), Channel(digits, 4), 255);
                    return true;
                }

                // #RRGGBBAA is alpha last, which is how it is written here and in
                // every other tool that takes a colour. Reading the eight digits as
                // one ARGB number instead would put the red channel in the alpha
                // and the alpha in the blue: a plausible-looking palette that is
                // simply not the one the file describes.
                color = new Color(Channel(digits, 0), Channel(digits, 2), Channel(digits, 4), Channel(digits, 6));
                return true;
            }

            Color named;
            if (Color.TryParse(trimmed, null, out named))
            {
                color = named;
                return true;
            }

            return false;
        }

        private static bool IsHexDigit(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        /// <summary>Two hex digits at <paramref name="offset"/>, as a byte.</summary>
        private static int Channel(string digits, int offset)
        {
            return Convert.ToInt32(digits.Substring(offset, 2), 16);
        }

        /// <summary>
        /// 16-colour names, case-insensitively. The ANSI spellings are accepted as
        /// aliases because "bright black" is what a person reading the palette table
        /// in the plan will type, and rejecting it would be a gotcha with no lesson.
        /// </summary>
        internal static bool TryParseColorName(string text, out ColorName16 name)
        {
            name = ColorName16.Gray;
            if (string.IsNullOrEmpty(text))
                return false;

            string key = text.Trim().Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();

            switch (key)
            {
                case "none":
                case "default":
                    name = ColorName16.Gray;
                    return true;
                case "black":
                    name = ColorName16.Black;
                    return true;
                case "blue":
                    name = ColorName16.Blue;
                    return true;
                case "green":
                    name = ColorName16.Green;
                    return true;
                case "cyan":
                    name = ColorName16.Cyan;
                    return true;
                case "red":
                    name = ColorName16.Red;
                    return true;
                case "magenta":
                    name = ColorName16.Magenta;
                    return true;
                case "yellow":
                case "brown":
                    name = ColorName16.Yellow;
                    return true;
                case "gray":
                case "grey":
                case "white":
                    name = ColorName16.Gray;
                    return true;
                case "brightblack":
                    name = ColorName16.DarkGray;
                    return true;
                case "brightblue":
                    name = ColorName16.BrightBlue;
                    return true;
                case "brightgreen":
                    name = ColorName16.BrightGreen;
                    return true;
                case "brightcyan":
                    name = ColorName16.BrightCyan;
                    return true;
                case "brightred":
                    name = ColorName16.BrightRed;
                    return true;
                case "brightmagenta":
                    name = ColorName16.BrightMagenta;
                    return true;
                case "brightyellow":
                    name = ColorName16.BrightYellow;
                    return true;
                case "brightwhite":
                    name = ColorName16.White;
                    return true;
                default:
                    return false;
            }
        }

        private static ShellTheme CreateBuiltIn()
        {
            Dictionary<string, Color> colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                [ThemeRole.Default] = Rgb(0xC8, 0xD0, 0xD8),
                [ThemeRole.Chrome] = Rgb(0x5A, 0x66, 0x72),
                [ThemeRole.Gutter] = Rgb(0x5A, 0x66, 0x72),
                [ThemeRole.Mnemonic] = Rgb(0x7F, 0xB2, 0xE5),
                [ThemeRole.Operand] = Rgb(0xE8, 0xB8, 0x6D),
                [ThemeRole.Directive] = Rgb(0xC5, 0x8A, 0xF9),
                [ThemeRole.Label] = Rgb(0x8F, 0xD6, 0x7A),
                [ThemeRole.Number] = Rgb(0xF0, 0xC6, 0x74),
                [ThemeRole.Comment] = Rgb(0x6B, 0x77, 0x85),
                [ThemeRole.Error] = Rgb(0xF2, 0x6D, 0x6D),
                [ThemeRole.Breakpoint] = Rgb(0xE0, 0x6C, 0x75),
                [ThemeRole.Warning] = Rgb(0xE5, 0xC0, 0x7B),
            };

            Dictionary<string, ColorName16> fallbacks = new Dictionary<string, ColorName16>(StringComparer.OrdinalIgnoreCase)
            {
                [ThemeRole.Default] = ColorName16.Gray,
                [ThemeRole.Chrome] = ColorName16.DarkGray,
                [ThemeRole.Gutter] = ColorName16.DarkGray,
                [ThemeRole.Mnemonic] = ColorName16.Blue,
                [ThemeRole.Operand] = ColorName16.Yellow,
                [ThemeRole.Directive] = ColorName16.Magenta,
                [ThemeRole.Label] = ColorName16.Green,
                [ThemeRole.Number] = ColorName16.Yellow,
                [ThemeRole.Comment] = ColorName16.DarkGray,
                [ThemeRole.Error] = ColorName16.Red,
                [ThemeRole.Breakpoint] = ColorName16.Red,
                [ThemeRole.Warning] = ColorName16.Yellow,
            };

            return new ShellTheme("built-in", null, true, colors, fallbacks, new List<string>());
        }

        private static Color Rgb(int r, int g, int b)
        {
            return new Color(r, g, b, 255);
        }

        private static ShellTheme WithDiagnostic(ShellTheme theme, string diagnostic)
        {
            if (diagnostic == null)
                return theme;

            List<string> diagnostics = new List<string>(theme.Diagnostics);
            diagnostics.Add(diagnostic);

            return new ShellTheme(theme.Name, theme.FilePath, true,
                theme._colors, theme._fallbacks, diagnostics);
        }

        /// <summary>
        /// Walks up from the executable looking for the theme in the source tree,
        /// so running from <c>bin\Debug\net8.0</c> finds the file that is being
        /// edited rather than only the copy beside the binary.
        /// </summary>
        private static string FindInSourceTree()
        {
            try
            {
                DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "WinASM65.Monitor", FileName);
                    if (File.Exists(candidate))
                        return candidate;
                    dir = dir.Parent;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            return null;
        }

        /// <summary>One line for the status line: where the palette came from.</summary>
        public string DescribeSource()
        {
            if (FilePath == null)
                return "palette " + Name + " (built-in)";

            return string.Format(CultureInfo.InvariantCulture,
                "palette {0} ({1}{2})",
                Name,
                FilePath,
                Diagnostics.Count == 0 ? string.Empty : ", " + Diagnostics.Count + " problem(s)");
        }
    }
}