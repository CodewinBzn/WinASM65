using WinASM65.Core;
using WinASM65.Segments;
using WinASM65.Targets;

namespace WinASM65
{
    public class Program
    {
        public static int Main(string[] args)
        {
            CommandLineApplication application = new CommandLineApplication(
                new AssemblerFactory(),
                new JsonConfigurationReader(),
                new BinaryCombiner(),
                new SystemConsoleOutput(),
                new ExecutablePublisher());
            return application.Run(args);
        }
    }
}
