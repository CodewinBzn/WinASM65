/**********************************************************************************/
/*                                                                                */  
/*                                                                                */
/* Copyright (c) 2021 Abdelghani BOUZIANE                                         */
/*                                                                                */
/* Permission is hereby granted, free of charge, to any person obtaining a copy   */
/* of this software and associated documentation files (the "Software"), to deal  */
/* in the Software without restriction, including without limitation the rights   */
/* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell      */  
/* copies of the Software, and to permit persons to whom the Software is          */ 
/* furnished to do so, subject to the following conditions:                       */
/*                                                                                */
/* The above copyright notice and this permission notice shall be included in all */
/* copies or substantial portions of the Software.                                */
/*                                                                                */
/* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR     */
/* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,       */
/* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE    */
/* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER         */
/* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,  */
/* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE  */ 
/* SOFTWARE.                                                                      */  
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
     
            Console.In.ReadLine();
        }
    }

}
