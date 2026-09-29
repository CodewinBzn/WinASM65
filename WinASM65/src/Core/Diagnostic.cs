// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Source Location & Diagnostic System

using System;
using System.Collections.Generic;

namespace WinASM65.Core
{
    public struct SourceLocation : IEquatable<SourceLocation>
    {
        public string FilePath { get; private set; }
        public int LineNumber { get; private set; }

        public SourceLocation(string filePath, int lineNumber)
            : this()
        {
            FilePath = filePath ?? string.Empty;
            LineNumber = lineNumber;
        }

        public bool Equals(SourceLocation other)
        {
            return string.Equals(FilePath, other.FilePath, StringComparison.OrdinalIgnoreCase) && LineNumber == other.LineNumber;
        }

        public override bool Equals(object obj)
        {
            if (obj is SourceLocation)
                return Equals((SourceLocation)obj);
            return false;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((FilePath != null ? FilePath.ToLowerInvariant().GetHashCode() : 0) * 397) ^ LineNumber;
            }
        }

        public override string ToString()
        {
            return string.Format("{0}:{1}", FilePath, LineNumber);
        }
    }

    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    public class Diagnostic
    {
        public SourceLocation Location { get; private set; }
        public string Message { get; private set; }
        public DiagnosticSeverity Severity { get; private set; }

        public Diagnostic(SourceLocation location, string message, DiagnosticSeverity severity = DiagnosticSeverity.Error)
        {
            Location = location;
            Message = message;
            Severity = severity;
        }

        public override string ToString()
        {
            return string.Format("Line {0}  - File {1} - Type {2}", Location.LineNumber, Location.FilePath, Message);
        }
    }

    public static class ErrorCodes
    {
        public const string LABEL_EXISTS = "Label already declared";
        public const string REL_JUMP = "Relative jump is too big";
        public const string SYNTAX = "Syntax Error";
        public const string FILE_NOT_EXISTS = "File doesn't exist";
        public const string DATA_BYTE = "Error in insert data byte";
        public const string DATA_WORD = "Error in insert data word";
        public const string DATA_TYPE = "Error in data type";
        public const string MACRO_EXISTS = "Macro with the same name already defined";
        public const string MACRO_NOT_EXISTS = "Undefined Macro";
        public const string MACRO_CALL_WITHOUT_PARAMS = "Macro called without params";
        public const string NESTED_MACROS = "Nested macros are not supported";
        public const string NESTED_REP = "Nested Reps are not supported";
        public const string NO_MACRO = "No macro is defined";
        public const string NO_REP = "No repeat is defined";
        public const string OPERANDS = "Error in operands";
        public const string UNDEFINED_SYMBOL = "Undefined symbol";
        public const string NESTED_CONDITIONAL_ASSEMBLY = "Too much nested conditional assembly";
        public const string NO_CONDITIONAL_ASSEMBLY = "No conditional assembly is defined";
        public const string MAX_LOCAL_SCOPE = "Too much nested local lexical levels";
        public const string NO_LOCAL_SCOPE = "No local scope is defined";
        public const string VALUE_OUT_OF_RANGE_BYTE = "Value {0} is out of range for a 1-byte operand (expected -128 to 255)";
        public const string VALUE_OUT_OF_RANGE_WORD = "Value {0} is out of range for a 2-byte operand (expected -32768 to 65535)";
    }

    public interface IDiagnosticReporter
    {
        IReadOnlyList<Diagnostic> Diagnostics { get; }
        bool HasErrors { get; }
        void Report(Diagnostic diagnostic);
        void ReportError(SourceLocation location, string message);
        void ReportWarning(SourceLocation location, string message);
        void Clear();
    }

    public class DiagnosticReporter : IDiagnosticReporter
    {
        private readonly List<Diagnostic> _diagnostics = new List<Diagnostic>();

        public IReadOnlyList<Diagnostic> Diagnostics
        {
            get { return _diagnostics; }
        }

        public bool HasErrors
        {
            get
            {
                foreach (var diag in _diagnostics)
                {
                    if (diag.Severity == DiagnosticSeverity.Error)
                        return true;
                }
                return false;
            }
        }

        public void Report(Diagnostic diagnostic)
        {
            if (diagnostic != null)
                _diagnostics.Add(diagnostic);
        }

        public void ReportError(SourceLocation location, string message)
        {
            Report(new Diagnostic(location, message, DiagnosticSeverity.Error));
        }

        public void ReportWarning(SourceLocation location, string message)
        {
            Report(new Diagnostic(location, message, DiagnosticSeverity.Warning));
        }

        public void Clear()
        {
            _diagnostics.Clear();
        }
    }
}
