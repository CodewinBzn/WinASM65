// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Binary Combiner (Pure OOP, SOLID, KISS)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using WinASM65.Core;

namespace WinASM65.Segments
{
    public interface IBinaryCombiner
    {
        OperationResult Combine(CombineConf config);
        OperationResult Concatenate(CombineConf config, out byte[] payload);
    }

    public class BinaryCombiner : IBinaryCombiner
    {
        public OperationResult Combine(CombineConf config)
        {
            byte[] payload;
            OperationResult concatenated = Concatenate(config, out payload);
            if (!concatenated.Success)
                return concatenated;

            string targetDir = Path.GetDirectoryName(config.ObjectFile);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);
            File.WriteAllBytes(config.ObjectFile, payload);
            return concatenated;
        }

        public OperationResult Concatenate(CombineConf config, out byte[] payload)
        {
            payload = new byte[0];
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            if (config == null || string.IsNullOrEmpty(config.ObjectFile))
                return Failure(diagnostics, "An output object file is required.");
            if (config.Files == null)
                return Failure(diagnostics, "At least one input file is required.");

            List<byte> combined = new List<byte>();
            foreach (FileConf fileConf in config.Files)
            {
                if (fileConf == null || string.IsNullOrWhiteSpace(fileConf.FileName) || !File.Exists(fileConf.FileName))
                    return Failure(diagnostics, "Input file doesn't exist: " + (fileConf == null ? string.Empty : fileConf.FileName));

                byte[] bytes = File.ReadAllBytes(fileConf.FileName);
                if (!string.IsNullOrWhiteSpace(fileConf.Size))
                {
                    int targetSize;
                    string size = fileConf.Size.Trim().TrimStart('$');
                    if (!int.TryParse(size, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out targetSize) || targetSize < bytes.Length)
                        return Failure(diagnostics, "Invalid target size for: " + fileConf.FileName);
                    if (bytes.Length < targetSize)
                        Array.Resize(ref bytes, targetSize);
                }
                combined.AddRange(bytes);
            }

            payload = combined.ToArray();
            return new OperationResult(true, diagnostics);
        }

        private static OperationResult Failure(List<Diagnostic> diagnostics, string message)
        {
            diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0), message));
            return new OperationResult(false, diagnostics);
        }
    }
}
