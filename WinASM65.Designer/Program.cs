using System;
using System.IO;
using WinASM65.Designer.Rpc;

namespace WinASM65.Designer
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 1 && (args[0] == "--server" || args[0] == "-server"))
                return RunServer();
            return Cli.Run(args, Console.Out, Console.Error);
        }

        /// <summary>
        /// Takes the raw standard streams for the protocol, then points Console.Out
        /// at stderr: anything printed would otherwise land in the middle of a
        /// message and break the framing.
        /// </summary>
        private static int RunServer()
        {
            Stream input = Console.OpenStandardInput();
            Stream output = Console.OpenStandardOutput();
            Console.SetOut(Console.Error);
            try
            {
                new DesignerServer(new MessageStream(input, output)).Run();
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine("designer: " + ex.Message);
            }
            return 0;
        }
    }
}
