namespace WinASM65.Targets
{
    public class RawBinaryFormat : IExecutableFormat
    {
        public string Name { get { return "bin"; } }

        public Core.OperationResult Write(string path, byte[] payload, ResolvedTarget target)
        {
            return ExecutableFile.WriteBytes(path, payload ?? new byte[0]);
        }
    }
}
