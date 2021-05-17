using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace WinASM65
{
    public class Tokenizer
    {
        private static readonly List<string> captureGroupNames;
        private static string pattern =
         @"(?<OpenRoundBracket>\()|" +
         @"(?<CloseRoundBracket>\))|" +
         @"(?<whitespace>\s+)|" +
         CPUDef.binByteRegex + "|" +
         CPUDef.decRegex + "|" +
         CPUDef.hwRegex + "|" +
         CPUDef.hbRegex + "|" +         
         CPUDef.labelRegex + "|" +
         CPUDef.loLabelRegex + "|" +
         CPUDef.hiLabelRegex + "|" +
         CPUDef.loHexWordRegex + "|" +
         CPUDef.hiHexWordRegex + "|" +
         CPUDef.arOpRegex + "|" +
        @"(?<invalid>[^\s]+)"
    ;
        private static Regex regexPattern = new Regex(pattern, RegexOptions.Compiled);

        static Tokenizer()
        {
            captureGroupNames = new List<string>();
            captureGroupNames.Add("DEC");
            captureGroupNames.Add("HB");
            captureGroupNames.Add("HW");
            captureGroupNames.Add("label");
            captureGroupNames.Add("loLabel");
            captureGroupNames.Add("hiLabel");
            captureGroupNames.Add("loHW");
            captureGroupNames.Add("hiHW");
            captureGroupNames.Add("binByte");
            captureGroupNames.Add("arOp");
            captureGroupNames.Add("OpenRoundBracket");
            captureGroupNames.Add("CloseRoundBracket");
        }

        public static List<Token> Tokenize(string line)
        {
            MatchCollection matches = regexPattern.Matches(line);
            List<Token> tokenList = new List<Token>();
            foreach (Match match in matches)
            {
                int i = 0;
                foreach (Group group in match.Groups)
                {
                    string matchValue = group.Value;
                    bool success = group.Success;
                    // ignore capture index 0 and 1 (general and WhiteSpace)
                    if (success && i > 1)
                    {
                        string groupName = regexPattern.GroupNameFromNumber(i);
                        if (captureGroupNames.Contains(groupName))
                        {
                            tokenList.Add(new Token() { Type = groupName, Value = matchValue });
                        }
                    }
                    i++;
                }

            }
            return tokenList;
        }

    }
}
