using WinASM65.Core;

namespace WinASM65.Targets
{
    public interface IExecutableFormat
    {
        string Name { get; }
        OperationResult Write(string path, byte[] payload, ResolvedTarget target);
    }
}
