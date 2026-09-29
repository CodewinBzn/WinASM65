using System.Collections.Generic;

namespace WinASM65.Core
{
    public class OperationResult
    {
        public bool Success { get; private set; }
        public IReadOnlyList<Diagnostic> Diagnostics { get; private set; }

        public OperationResult(bool success, IReadOnlyList<Diagnostic> diagnostics)
        {
            Success = success;
            Diagnostics = diagnostics ?? new List<Diagnostic>();
        }
    }
}
