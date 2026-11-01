using System.Collections.Generic;
using System.Text;

namespace WinASM65.Graphics.Assets
{
    public enum AssetSeverity
    {
        /// <summary>The asset cannot be used as it is: an export would be wrong or impossible.</summary>
        Error,

        /// <summary>Something worth a look that does not stop anything: a missing reference, a reduced colour.</summary>
        Warning
    }

    /// <summary>
    /// One thing wrong with an asset, located by a JSON path (<c>$.metasprites[2].tiles[0].tile</c>)
    /// so an editor can point at the value rather than at the file.
    ///
    /// Loading and validating report these instead of throwing: a half-edited
    /// asset is the normal state of a file in an editor, and every problem is
    /// worth showing at once.
    /// </summary>
    public sealed class AssetDiagnostic
    {
        public AssetDiagnostic(AssetSeverity severity, string message, string path, string file)
        {
            Severity = severity;
            Message = message;
            Path = string.IsNullOrEmpty(path) ? "$" : path;
            File = file;
        }

        public AssetSeverity Severity { get; private set; }
        public string Message { get; private set; }

        /// <summary>JSON path of the value at fault; <c>$</c> for the whole file.</summary>
        public string Path { get; private set; }

        /// <summary>The file the diagnostic is about, or null.</summary>
        public string File { get; private set; }

        public bool IsError { get { return Severity == AssetSeverity.Error; } }

        public static AssetDiagnostic Error(string file, string path, string message)
        {
            return new AssetDiagnostic(AssetSeverity.Error, message, path, file);
        }

        public static AssetDiagnostic Warning(string file, string path, string message)
        {
            return new AssetDiagnostic(AssetSeverity.Warning, message, path, file);
        }

        /// <summary>Whether any of the diagnostics is an error.</summary>
        public static bool AnyErrors(IEnumerable<AssetDiagnostic> diagnostics)
        {
            if (diagnostics == null)
                return false;
            foreach (AssetDiagnostic diagnostic in diagnostics)
            {
                if (diagnostic.IsError)
                    return true;
            }
            return false;
        }

        /// <summary><c>file: error: $.path: message</c>, the form the command line prints.</summary>
        public override string ToString()
        {
            StringBuilder text = new StringBuilder();
            if (!string.IsNullOrEmpty(File))
                text.Append(File).Append(": ");
            text.Append(IsError ? "error" : "warning").Append(": ");
            text.Append(Path).Append(": ").Append(Message);
            return text.ToString();
        }
    }

    /// <summary>An asset as loaded, or null when it could not be read, and what is wrong with it.</summary>
    public sealed class AssetLoadResult<T> where T : class
    {
        public AssetLoadResult(T asset, IReadOnlyList<AssetDiagnostic> diagnostics)
        {
            Asset = asset;
            Diagnostics = diagnostics ?? new AssetDiagnostic[0];
        }

        /// <summary>The asset, or null when the file is missing or not valid JSON of the right shape.</summary>
        public T Asset { get; private set; }

        public IReadOnlyList<AssetDiagnostic> Diagnostics { get; private set; }

        public bool HasErrors { get { return AssetDiagnostic.AnyErrors(Diagnostics); } }
    }
}
