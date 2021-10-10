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
            Combine = 2,
            Monitor = 3
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
                    case "-help":
                    case "-h":
                        DisplayHelp();
                        return;
                    case "*":
                        command = CommandType.Monitor;
                        break;
                }
            }
            switch (command)
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
                case CommandType.Monitor:
                    Monitor.Start(); 
                    break;
            }
        }

        private static void DisplayHelp()
        {           
            Console.WriteLine("\t Usage \t");
            Console.WriteLine("\t WinASM65 [-option] filePath? \t");
            Console.WriteLine("\t Options \t");
            Console.WriteLine("\t help \t show help \t");
            Console.WriteLine("\t h \t show help \t");
            Console.WriteLine("\t f \t sourceFile \t");
            Console.WriteLine("\t o \t objectFile \t");
            Console.WriteLine("\t m \t Assemble one or several segments \t");
            Console.WriteLine("\t c \t Combine assembled segments/binary files \t");

            Console.WriteLine("\t Assemble segments \t");
            Console.WriteLine("\t JSON File format \t");
            Console.WriteLine("\t [\t");
            Console.WriteLine("\t\t {\t");
            Console.WriteLine("\t\t\t FileName: \"path_to_main_file_seg1\",\t");
            Console.WriteLine("\t\t\t Dependencies:[\"path_to_main_file_seg2\"]\t");
            Console.WriteLine("\t\t },\t");
            Console.WriteLine("\t\t {\t");
            Console.WriteLine("\t\t\t FileName: \"path_to_main_file_seg2\",\t");
            Console.WriteLine("\t\t\t Dependencies:[\"path_to_main_file_seg1\"]\t");
            Console.WriteLine("\t\t },\t");
            Console.WriteLine("\t\t {\t");
            Console.WriteLine("\t\t\t FileName: \"path_to_main_file_seg3\",\t");
            Console.WriteLine("\t\t\t Dependencies:[]\t");
            Console.WriteLine("\t\t },\t");
            Console.WriteLine("\t\t ......");
            Console.WriteLine("\t ]\t");
            Console.WriteLine("\t Dependencies \t");
            Console.WriteLine("\t\t - If a segment refers to labels, variables ... or to routines declared in other segments then it must mention them in this array as [\"path_to_main_file_seg1\", .....]. \t");

            Console.WriteLine("\t Combine assembled segments / Binary files \t");
            Console.WriteLine("\t JSON File format \t");
            Console.WriteLine("\t {\t");
            Console.WriteLine("\t\t ObjectFile: \"final_object_file\",\t");
            Console.WriteLine("\t\t Files:\t");
            Console.WriteLine("\t\t [\t");
            Console.WriteLine("\t\t\t {\t");
            Console.WriteLine("\t\t\t\t FileName: \"path_to_seg1_object_file\",\t");
            Console.WriteLine("\t\t\t\t Size:\"$hex\"\t");
            Console.WriteLine("\t\t\t },\t");
            Console.WriteLine("\t\t\t {\t");
            Console.WriteLine("\t\t\t\t FileName: \"path_to_seg2_object_file\",\t");
            Console.WriteLine("\t\t\t\t Size:\"$hex\"\t");
            Console.WriteLine("\t\t\t },\t");
            Console.WriteLine("\t\t\t {\t");
            Console.WriteLine("\t\t\t\t FileName: \"path_to_seg3_object_file\"\t");          
            Console.WriteLine("\t\t\t },\t");
            Console.WriteLine("\t\t\t ......");
            Console.WriteLine("\t\t ]\t");
            Console.WriteLine("\t }\t");
            Console.WriteLine("\t The Segments are declared in the order of their insertion in the final object file.\t");
            Console.WriteLine("\t Size\t");
            Console.WriteLine("\t\t - The size of the segment object file. If the size of the assembled segment is less then the declared size then the assembler will fill the rest of bytes with the value $00.\t");

            Console.WriteLine("\n\t For more information see the following link https://github.com/CodewinBzn/WinASM65/blob/master/README.md.");
        }
    }

}
