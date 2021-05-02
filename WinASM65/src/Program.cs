/**********************************************************************************/
/*                                                                                */
/*                                                                                */
/* 2021 Abdelghani BOUZIANE                                                       */
/*                                                                                */
/*                                                                                */
/**********************************************************************************/

using System;

namespace WinASM65
{
    class MainConsole
    {
        public static void WriteLine(string line)
        {
#if DEBUG
            Console.WriteLine(line);
#endif
        }
    }

    class Program
    {
        enum CommandType
        {
            SingleSegment = 0,
            MultiSegment = 1,
            Combine = 2
        }
        static void Main(string[] args)
        {
            CommandType command = CommandType.SingleSegment;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-f":
                        Assembler.sourceFile = args[++i];
                        break;
                    case "-o":
                        Assembler.objectFileName = args[++i];
                        break;
                    case "-m":
                        command = CommandType.MultiSegment;
                        MultiSegment.configFile = args[++i];
                        break;
                    case "-c":
                        command = CommandType.Combine;
                        Combine.configFile = args[++i];
                        break;
                }
            }
            switch(command)
            {
                case CommandType.SingleSegment:
                    Assembler.Assemble();
                    break;
                case CommandType.MultiSegment:
                    MultiSegment.Assemble();
                    break;
                case CommandType.Combine:
                    Combine.Process();
                    break;
            }
        }
    }

}
