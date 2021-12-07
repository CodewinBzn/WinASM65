using System.Collections.Generic;
using System.Text.RegularExpressions;
namespace WinASM65
{
    public static class Tokenizer
    {
        private static readonly List<string> captureGroupNames;
        private const string pattern =
         @"(?<OpenRoundBracket>\()|" +
         @"(?<CloseRoundBracket>\))|" +
         @"(?<whitespace>\s+)|" +
         @"(?<OR>(OR|or|\|\|))|" +
         @"(?<AND>(AND|and|&&))|" +
         @"(?<TRUE>(TRUE|true))|" +
         @"(?<FALSE>(FALSE|false))|" +
         "\"(?<CHAR>[\x00-\xFF])\"|" +
         CPUDef.binByteRegex + "|" +
         CPUDef.decRegex + "|" +
         CPUDef.hwRegex + "|" +
         CPUDef.hbRegex + "|" +         
         CPUDef.labelRegex + "|" +
        @"(?<BSL>(<<))|" +
        @"(?<BSR>(>>))|" +
        @"(?<LESSEQ>(<=))|" +
        @"(?<GREATEREQ>(>=))|" +
        @"(?<NOTEQ>(!=|<>))|" +
        @"(?<EQ>(==))|" +
        @"(?<AF>(=))|" +
        @"(?<LESS>(<))|" +
        @"(?<GREATER>(>))|" +
        @"(?<BOR>(\|))|" +
        @"(?<BAND>(&))|" +
        @"(?<XOR>(\^))|" +
        @"(?<PLUS>(\+))|" +
        @"(?<MINUS>(-))|" +
        @"(?<MULT>(\*))|" +
        @"(?<DIV>(/))|" +
        @"(?<MOD>(%))|" +
        @"(?<BOC>(~))|" +
        @"(?<NOT>(!))|" +
        @"(?<invalid>[^\s]+)"
    ;
        private static Regex regexPattern; 

        static Tokenizer()
        {
            MainConsole.WriteLine(pattern);
            regexPattern = new Regex(pattern, RegexOptions.Compiled);
            captureGroupNames = new List<string>();
            captureGroupNames.Add("DEC");
            captureGroupNames.Add("HB");
            captureGroupNames.Add("HW");
            captureGroupNames.Add("label");            
            captureGroupNames.Add("binByte");
            captureGroupNames.Add("BSL");
            captureGroupNames.Add("BSR");
            captureGroupNames.Add("LESSEQ");
            captureGroupNames.Add("GREATEREQ");
            captureGroupNames.Add("NOTEQ");
            captureGroupNames.Add("EQ");
            captureGroupNames.Add("AF");
            captureGroupNames.Add("LESS");
            captureGroupNames.Add("GREATER");
            captureGroupNames.Add("BOR");
            captureGroupNames.Add("BAND");
            captureGroupNames.Add("XOR");
            captureGroupNames.Add("PLUS");
            captureGroupNames.Add("MINUS");
            captureGroupNames.Add("MULT");
            captureGroupNames.Add("DIV");
            captureGroupNames.Add("MOD");
            captureGroupNames.Add("BOC");
            captureGroupNames.Add("NOT");            
            captureGroupNames.Add("OpenRoundBracket");
            captureGroupNames.Add("CloseRoundBracket");
            captureGroupNames.Add("OR");
            captureGroupNames.Add("AND");
            captureGroupNames.Add("TRUE");
            captureGroupNames.Add("FALSE");
            captureGroupNames.Add("CHAR");
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
