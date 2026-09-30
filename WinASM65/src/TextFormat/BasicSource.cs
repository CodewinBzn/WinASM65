// WinASM65 - reading a BASIC program as text
//
// The encoder takes lines, and a program on a disk is not lines: it is bytes.
// Something has to turn one into the other, and the rules of that conversion are
// the machine's, not this program's, so they are written down here rather than
// left to whoever calls the encoder.
//
// A line of BASIC source is a number, then the rest of the line. That is all,
// and the "rest" is taken verbatim: the spaces that follow the number are part
// of it, because a comment keeps its spacing and so does a data line. The
// encoder drops the spaces that are not in a comment, so it does not matter for
// code, and it matters for a comment, which is why the text is not trimmed.
//
// Three rules come from the machines rather than from taste:
//
//   - Lines are cut on a carriage return and a line feed, and on nothing else.
//     `str.SplitLines` also breaks on $0B, $0C, $1C to $1E and on a few Unicode
//     boundaries, and every one of those is a legitimate character inside a
//     string literal or a VDU control code.
//   - A program whose lines carry no number at all is numbered for it, from 10
//     every 10, which is what a machine does when you type it in without
//     numbers. A program whose lines are numbered *somewhere* is an error if
//     any of them is not: a line that silently takes a number it was not given
//     moves code the author put somewhere else.
//   - The order is the order of the file, and a number that goes backwards is
//     reported rather than sorted. An interpreter walks the chain of records in
//     the order they are stored, so sorting a program would make the listing
//     differ from the file, and the two are the same program only when the
//     author numbered them in the order they meant to run.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using WinASM65.Core;

namespace WinASM65.TextFormat
{
    public static class BasicSource
    {
        /// <summary>The number the first line takes when the source carries none.</summary>
        public const int FirstAutoNumber = 10;

        /// <summary>What each successive line takes when the source carries no number.</summary>
        public const int AutoNumberStep = 10;

        /// <summary>
        /// Reads a program out of text. Blank lines are dropped, a line that
        /// starts with a number is that line, and the rest of the line is the
        /// body unchanged.
        /// </summary>
        public static IReadOnlyList<BasicLine> Read(string text, List<Diagnostic> diagnostics)
        {
            List<Diagnostic> problems = diagnostics ?? new List<Diagnostic>();
            List<BasicLine> lines = new List<BasicLine>();

            int at = 0;
            int line = 0;
            bool numbered = false;
            bool unnumbered = false;

            while (at <= text.Length)
            {
                int end = at;
                while (end < text.Length && text[end] != '\r' && text[end] != '\n')
                    end++;
                string raw = text.Substring(at, end - at);
                at = end;
                if (at < text.Length && text[at] == '\r')
                    at++;
                if (at < text.Length && text[at] == '\n')
                    at++;
                if (at == end && end == text.Length)
                    break;

                line++;
                if (raw.Trim().Length == 0)
                    continue;

                int number;
                string body;
                if (TryNumber(raw, out number, out body))
                    numbered = true;
                else
                {
                    unnumbered = true;
                    number = 0;
                    body = raw;
                }

                lines.Add(new BasicLine(number, body));
            }

            if (unnumbered && numbered)
            {
                problems.Add(new Diagnostic(new SourceLocation("program", 0),
                    "Some lines carry a number and some do not. A program is"
                    + " numbered throughout, or not at all."));

                // The numbers these lines would carry are not decided, so
                // nothing after this can say whether the order is right. Saying
                // it anyway would be a second complaint about the same mistake.
                return lines;
            }

            if (unnumbered)
            {
                for (int i = 0; i < lines.Count; i++)
                    lines[i].Number = FirstAutoNumber + (i * AutoNumberStep);
            }

            for (int i = 1; i < lines.Count; i++)
            {
                if (lines[i].Number <= lines[i - 1].Number)
                {
                    problems.Add(new Diagnostic(new SourceLocation("program", 0),
                        "Line " + lines[i].Number + " is not after line " + lines[i - 1].Number
                        + ". The order of the file is the order the interpreter runs, so"
                        + " the lines are left where they are."));
                    break;
                }
            }

            return lines;
        }

        /// <summary>
        /// Whether a line opens with a number. Leading blanks are allowed, a
        /// machine allows them too, and a number is the longest run of digits at
        /// the front: `10 GOTO 100` is line 10, and `10PRINT` is not a line
        /// number followed by a keyword but a name that starts with a digit.
        /// </summary>
        private static bool TryNumber(string raw, out int number, out string body)
        {
            number = 0;
            body = raw;

            int at = 0;
            while (at < raw.Length && (raw[at] == ' ' || raw[at] == '\t'))
                at++;

            int start = at;
            while (at < raw.Length && raw[at] >= '0' && raw[at] <= '9')
                at++;

            if (at == start)
                return false;

            // A digit run glued to what follows is a name, not a number: a
            // number is followed by a space or by nothing at all.
            if (at < raw.Length && raw[at] != ' ' && raw[at] != '\t')
                return false;

            if (!int.TryParse(raw.Substring(start, at - start), NumberStyles.None,
                CultureInfo.InvariantCulture, out number))
                return false;

            while (at < raw.Length && (raw[at] == ' ' || raw[at] == '\t'))
                at++;
            body = raw.Substring(at);
            return true;
        }

        /// <summary>The same program as text, which is the form a person reads.</summary>
        public static string Render(IReadOnlyList<BasicLine> lines)
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                text.Append(lines[i].Number);
                text.Append(' ');
                text.Append(lines[i].Body ?? string.Empty);
                text.Append('\n');
            }
            return text.ToString();
        }
    }
}
