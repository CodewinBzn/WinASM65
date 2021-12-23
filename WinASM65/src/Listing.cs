using System;
using System.IO;

namespace WinASM65
{
    class Listing
    {
        private const string LineDelimiter = "±|±";
        public static bool EnableListing { get; set; }
        private static StreamWriter _listingFilePtr;
        private static string _listingFile;
        public static string ListingFile
        {
            get
            {
                return _listingFile;
            }
            set
            {
                _listingFile = $"{value.Split('.')[0]}.lst";
            }
        }


        static Listing()
        {
            EnableListing = false;
        }

        public static void PrintLine(string line)
        {
            if (!EnableListing)
            {
                return;
            }
            _listingFilePtr.Write(line);
        }

        public static void PrintLine(LineType type, ushort value)
        {
            if (!EnableListing)
            {
                return;
            }
            _listingFilePtr.Write($"{LineDelimiter}{type}{LineDelimiter}{value}");
        }

        public static void EndLine()
        {
            if (!EnableListing)
            {
                return;
            }
            _listingFilePtr.Write("\n");
        }

        public static void StartListing()
        {
            if (!EnableListing)
            {
                return;
            }
            _listingFilePtr = new StreamWriter($"{_listingFile}.tmp", false);

        }
        public static void EndListing()
        {
            if (!EnableListing)
            {
                return;
            }
            _listingFilePtr.Flush();
            _listingFilePtr.Close();
        }
        public static void GenerateListing()
        {
            if (!EnableListing)
            {
                return;
            }
            string[] stringSeparators = new string[] { LineDelimiter };
            ushort currentAddr = 0;
            ushort memoryIndex = 0;
            Byte[] memory = Assembler.FileOutMemory.ToArray();
            string listingTmpFilePath = $"{_listingFile}.tmp";
            using (StreamReader sr = new StreamReader(listingTmpFilePath))
            {
                using (StreamWriter sw = new StreamWriter(_listingFile))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        string[] lineValues = line.Split(stringSeparators, StringSplitOptions.None);

                        switch (lineValues.Length)
                        {
                            case 1:
                                sw.WriteLine("".PadLeft(18) + $"{lineValues[0]}");
                                break;
                            case 3:
                                LineType lineType = (LineType)Enum.Parse(typeof(LineType), lineValues[1]);
                                switch (lineType)
                                {
                                    case LineType.ORG:
                                        currentAddr = ushort.Parse(lineValues[2]);
                                        sw.WriteLine("{0:X4}" + "".PadLeft(14) + "{1}", currentAddr, lineValues[0]);
                                        break;
                                    case LineType.INST:
                                        int nbrBytes = int.Parse(lineValues[2]);
                                        int bytesWritten = 0;
                                        sw.Write("{0:X4} ", currentAddr);
                                        bool lineWritten = false;
                                        while (nbrBytes > 0)
                                        {
                                            bytesWritten++;
                                            sw.Write("{0:X2} ", memory[memoryIndex]);
                                            memoryIndex++;
                                            currentAddr++;
                                            nbrBytes--;
                                            if (bytesWritten == 4)
                                            {
                                                if (!lineWritten)
                                                {
                                                    lineWritten = true;
                                                    sw.Write(" {0}", lineValues[0]);
                                                }
                                                if (nbrBytes > 0)
                                                {
                                                    sw.Write("\n{0:X4} ", currentAddr);
                                                }
                                                bytesWritten = 0;
                                            }
                                        }
                                        if (!lineWritten && bytesWritten < 4)
                                        {
                                            int left = 4 - bytesWritten;
                                            int leftSpace = (left - 1) > 0 ? left - 1 : 0;
                                            leftSpace = leftSpace + (left * 2);
                                            sw.Write("".PadLeft(leftSpace) + " {0}", lineValues[0]);
                                        }
                                        sw.Write("\n");
                                        break;
                                    case LineType.LABEL:
                                        ushort addr = ushort.Parse(lineValues[2]);
                                        sw.WriteLine("{0:X4}" + "".PadLeft(14) + "{1}", addr, lineValues[0]);
                                        break;
                                    case LineType.RES:
                                    case LineType.CONST:
                                        ushort val = ushort.Parse(lineValues[2]);
                                        if(val <= 255)
                                        {                                            
                                            sw.WriteLine("".PadLeft(11) + "{0:X2} =   " + "{1}", val, lineValues[0]);
                                        } else
                                        {
                                            sw.WriteLine("".PadLeft(9) + "{0:X4} =   " + "{1}", val, lineValues[0]);
                                        }                                        
                                        break;                               
                                }                                
                                break;
                        }
                    }
                }
            }
            File.Delete(listingTmpFilePath);
        }
    }

    enum LineType
    {
        NONE,
        ORG,
        INST,
        LABEL,
        RES,
        CONST
    }
}
