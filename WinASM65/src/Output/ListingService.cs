// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Listing Service (Pure OOP, SOLID)

using System;
using System.Collections.Generic;
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

            List<ListingRow> rows = ListingRowBuilder.Build(ReadTempFile(), memoryBytes);

            using (StreamWriter sw = new StreamWriter(_listingFilePath))
            {
                foreach (ListingRow row in rows)
                    sw.WriteLine(row.Render());
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

        /// <summary>
        /// Reads the intermediate stream back, one entry per listing line.
        /// The row builder is the same one the in-memory sink uses, so the file
        /// and the in-memory listing are one implementation rather than two.
        /// </summary>
        private List<IReadOnlyList<ListingFragment>> ReadTempFile()
        {
            string[] separators = new string[] { LineDelimiter };
            List<IReadOnlyList<ListingFragment>> lines = new List<IReadOnlyList<ListingFragment>>();

            using (StreamReader sr = new StreamReader(_tempFilePath))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    string[] values = line.Split(separators, StringSplitOptions.None);
                    List<ListingFragment> fragments = new List<ListingFragment>();

                    if (values.Length == 1)
                    {
                        fragments.Add(new ListingFragment(values[0]));
                    }
                    else if (values.Length == 3)
                    {
                        fragments.Add(new ListingFragment(values[0]));
                        fragments.Add(new ListingFragment(
                            (LineType)Enum.Parse(typeof(LineType), values[1]), int.Parse(values[2])));
                    }

                    lines.Add(fragments);
                }
            }

            return lines;
        }
    }
}
