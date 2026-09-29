using System;
using System.IO;
using System.Collections.Generic;
using WinASM65.Core;

namespace WinASM65.Targets
{
    internal static class ExecutableFile
    {
        public static OperationResult WriteBytes(string path, byte[] bytes)
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            if (string.IsNullOrWhiteSpace(path))
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0), "An output object file is required."));
                return new OperationResult(false, diagnostics);
            }
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, bytes ?? new byte[0]);
            return new OperationResult(true, diagnostics);
        }

        public static OperationResult WriteText(string path, string content)
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            if (string.IsNullOrWhiteSpace(path))
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0), "An output object file is required."));
                return new OperationResult(false, diagnostics);
            }
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, content ?? string.Empty);
            return new OperationResult(true, diagnostics);
        }

        public static byte[] Pad(byte[] payload, int size)
        {
            byte[] result = new byte[size];
            if (payload != null && payload.Length > 0)
                Array.Copy(payload, result, Math.Min(payload.Length, size));
            return result;
        }
    }
}
