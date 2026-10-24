using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using WinASM65.Projects;
using WinASM65.Tooling.Dap;
using WinASM65.Tooling.Lsp;
using WinASM65.Tooling.Rpc;

namespace WinASM65.Tooling
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : null;
            switch (mode)
            {
                case "--dap":
                    return RunDebugAdapter();
                case "--lsp":
                    return RunLanguageServer();
                case "--build":
                    if (args.Length != 2)
                        break;
                    return RunBuild(args[1]);
            }

            Console.Error.WriteLine("WinASM65 tooling for editors");
            Console.Error.WriteLine("  --dap              Debug Adapter Protocol on stdin/stdout");
            Console.Error.WriteLine("  --lsp              Language Server Protocol on stdin/stdout");
            Console.Error.WriteLine("  --build <config>   build a project, one file:line: message per diagnostic");
            return 2;
        }

        /// <summary>
        /// Takes the raw standard streams for the protocol, then points Console.Out
        /// at stderr: anything the libraries print would otherwise land in the middle
        /// of a message and break the framing.
        /// </summary>
        private static MessageStream TakeProtocolStreams()
        {
            Stream input = Console.OpenStandardInput();
            Stream output = Console.OpenStandardOutput();
            Console.SetOut(Console.Error);
            return new MessageStream(input, output);
        }

        private static int RunDebugAdapter()
        {
            MessageStream stream = TakeProtocolStreams();
            using (DebugSession session = new DebugSession(new DebugEnvironment(), stream.Write))
            {
                Thread reader = new Thread(() =>
                {
                    try
                    {
                        JsonObject message;
                        while ((message = stream.Read()) != null)
                        {
                            if ((string)message["type"] == "request")
                                session.Dispatch(message);
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.Text.Json.JsonException)
                    {
                        Console.Error.WriteLine("dap: " + ex.Message);
                    }
                    session.InputClosed();
                })
                { IsBackground = true, Name = "dap-reader" };
                reader.Start();
                session.Run();
            }
            return 0;
        }

        private static int RunLanguageServer()
        {
            MessageStream stream = TakeProtocolStreams();
            using (LanguageServer server = new LanguageServer(stream.Write))
            {
                Thread reader = new Thread(() =>
                {
                    try
                    {
                        JsonObject message;
                        while ((message = stream.Read()) != null)
                            server.Dispatch(message);
                    }
                    catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.Text.Json.JsonException)
                    {
                        Console.Error.WriteLine("lsp: " + ex.Message);
                    }
                    server.InputClosed();
                })
                { IsBackground = true, Name = "lsp-reader" };
                reader.Start();
                server.Run();
            }
            return 0;
        }

        /// <summary>
        /// Builds a project the way the debugger does and prints each diagnostic as
        /// <c>file:line: severity: message</c>, the form the extension's problem
        /// matcher reads. Exit code 1 when the build failed.
        /// </summary>
        private static int RunBuild(string configuration)
        {
            string config = Path.GetFullPath(configuration);
            ProjectBuildResult result;
            try
            {
                result = ProjectSession.Open(config).Build(new ProjectBuildOptions { WriteImage = true });
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException)
            {
                Console.WriteLine(config + ": error: " + ex.Message);
                return 1;
            }

            foreach (WinASM65.Core.Diagnostic diagnostic in result.Diagnostics)
            {
                string severity = diagnostic.Severity == WinASM65.Core.DiagnosticSeverity.Error ? "error" : "warning";
                string text = DebugSession.FormatDiagnostic(diagnostic, config);
                int split = text.LastIndexOf(": " + diagnostic.Message, StringComparison.Ordinal);
                Console.WriteLine(split < 0 ? text : text.Substring(0, split) + ": " + severity + ": " + diagnostic.Message);
            }

            if (!result.Success)
            {
                Console.WriteLine("build failed: " + result.Diagnostics.Count(d => d.Severity == WinASM65.Core.DiagnosticSeverity.Error) + " error(s)");
                return 1;
            }
            Console.WriteLine("built " + result.ImagePath + " (" + result.Image.Length + " bytes)");
            return 0;
        }
    }
}
