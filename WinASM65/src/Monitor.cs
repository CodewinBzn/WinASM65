using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WinASM65
{
    public class Monitor
    {
        private static ushort currentCodeAddress = 0;
        private static byte[] memory = new byte[65535];
        public static void ClearCurrentConsoleLine()
        {
            int currentLineCursor = Console.CursorTop;
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, currentLineCursor);
        }
        private static void DisplayCodeLine(byte[] code, string asmLine)
        {
            for (int i = 0; i < code.Length; i++) memory[currentCodeAddress + i] = code[i];
            Console.SetCursorPosition(0, Console.CursorTop - 1);
            ClearCurrentConsoleLine();
            Console.WriteLine($"{currentCodeAddress.ToString("X").PadLeft(4, '0')} {BitConverter.ToString(code).PadRight(8)} - {asmLine}");
        }

        public static void Start()
        {
            Assembler.macros = new Dictionary<string, MacroDef>();
            Assembler.fileOutMemory = new List<byte>();
            
            string str;
            do
            {
                str = Console.ReadLine();
                if (!str.ToLower().Equals("quit"))
                {
                    Assemble(str);
                }
            } while (!str.ToLower().Equals("quit"));
            using (BinaryWriter writer = new BinaryWriter(File.Open("out.o", FileMode.Create)))
            {
                writer.Write(memory.ToArray());
            }
        }
        public static void Assemble(string asmLine)
        {
            currentCodeAddress = Assembler.currentAddr;
            Assembler.ParseLine(asmLine, asmLine);
            if (!asmLine.ToLower().StartsWith(".org"))
            {
                DisplayCodeLine(Assembler.fileOutMemory.ToArray(), asmLine);
                Assembler.fileOutMemory.Clear();                
            }
        }


    }
}
