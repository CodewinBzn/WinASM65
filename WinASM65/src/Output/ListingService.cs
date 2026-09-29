// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Listing Service (Pure OOP, SOLID)

using System;
using System.IO;

namespace WinASM65.Output
{
    public enum LineType
    {
        NONE,
        ORG,
        INST,
        LABEL,
        RES,
        CONST
    }

    public interface IListingService
    {
        bool IsEnabled { get; set; }
        void Start(string sourceFilePath);
        void PrintLine(string line);
        void PrintLine(LineType type, int value);
        void EndLine();
        void Finish(byte[] memoryBytes);
    }

    public class ListingService : IListingService
    {
        private const string LineDelimiter = "±|±";
        private StreamWriter _writer;
        private string _listingFilePath;
        private string _tempFilePath;

        public bool IsEnabled { get; set; }

        public ListingService()
        {
            IsEnabled = false;
        }

        public void Start(string sourceFilePath)
        {
            if (!IsEnabled || string.IsNullOrEmpty(sourceFilePath))
                return;

            string baseName = sourceFilePath.Split('.')[0];
            _listingFilePath = string.Format("{0}.lst", baseName);
            _tempFilePath = string.Format("{0}.tmp", _listingFilePath);

            _writer = new StreamWriter(_tempFilePath, false);
        }

        public void PrintLine(string line)
        {
            if (!IsEnabled || _writer == null)
                return;
            _writer.Write(line);
        }

        public void PrintLine(LineType type, int value)
        {
            if (!IsEnabled || _writer == null)
                return;
            _writer.Write(string.Format("{0}{1}{0}{2}", LineDelimiter, type, value));
        }

        public void EndLine()
        {
            if (!IsEnabled || _writer == null)
                return;
            _writer.Write("\n");
        }

        public void Finish(byte[] memoryBytes)
        {
            if (!IsEnabled || _writer == null)
                return;

            _writer.Flush();
            _writer.Close();
            _writer = null;

            if (!File.Exists(_tempFilePath))
                return;

            string[] stringSeparators = new string[] { LineDelimiter };
            ushort currentAddr = 0;
            ushort memoryIndex = 0;
            byte[] memory = memoryBytes ?? new byte[0];

            using (StreamReader sr = new StreamReader(_tempFilePath))
            {
                using (StreamWriter sw = new StreamWriter(_listingFilePath))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        string[] lineValues = line.Split(stringSeparators, StringSplitOptions.None);

                        switch (lineValues.Length)
                        {
                            case 1:
                                sw.WriteLine("".PadLeft(18) + lineValues[0]);
                                break;

                            case 3:
                                LineType lineType = (LineType)Enum.Parse(typeof(LineType), lineValues[1]);
                                switch (lineType)
                                {
                                    case LineType.ORG:
                                        currentAddr = ushort.Parse(lineValues[2]);
                                        sw.WriteLine(string.Format("{0:X4}", currentAddr) + "".PadLeft(14) + lineValues[0]);
                                        break;

                                    case LineType.INST:
                                        int nbrBytes = int.Parse(lineValues[2]);
                                        int bytesWritten = 0;
                                        sw.Write(string.Format("{0:X4} ", currentAddr));
                                        bool lineWritten = false;
                                        while (nbrBytes > 0)
                                        {
                                            bytesWritten++;
                                            byte b = memoryIndex < memory.Length ? memory[memoryIndex] : (byte)0;
                                            sw.Write(string.Format("{0:X2} ", b));
                                            memoryIndex++;
                                            currentAddr++;
                                            nbrBytes--;
                                            if (bytesWritten == 4)
                                            {
                                                if (!lineWritten)
                                                {
                                                    lineWritten = true;
                                                    sw.Write(string.Format(" {0}", lineValues[0]));
                                                }
                                                if (nbrBytes > 0)
                                                {
                                                    sw.Write(string.Format("\n{0:X4} ", currentAddr));
                                                }
                                                bytesWritten = 0;
                                            }
                                        }
                                        if (!lineWritten && bytesWritten < 4)
                                        {
                                            int left = 4 - bytesWritten;
                                            int leftSpace = (left - 1) > 0 ? left - 1 : 0;
                                            leftSpace = leftSpace + (left * 2);
                                            sw.Write("".PadLeft(leftSpace) + string.Format(" {0}", lineValues[0]));
                                        }
                                        sw.Write("\n");
                                        break;

                                    case LineType.LABEL:
                                        ushort addr = ushort.Parse(lineValues[2]);
                                        sw.WriteLine(string.Format("{0:X4}", addr) + "".PadLeft(14) + lineValues[0]);
                                        break;

                                    case LineType.RES:
                                    case LineType.CONST:
                                        int val = int.Parse(lineValues[2]);
                                        sw.WriteLine(string.Format("{0:X} =   ", val).PadLeft(18) + lineValues[0]);
                                        break;
                                }
                                break;
                        }
                    }
                }
            }

            try
            {
                File.Delete(_tempFilePath);
            }
            catch
            {
                // Ignore temp file deletion failure
            }
        }
    }
}
