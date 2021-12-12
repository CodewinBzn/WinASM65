using System.Collections.Generic;
using System.Text.RegularExpressions;
namespace WinASM65
{
    public class Tokenizer
    {
        private const string Dec = @"[0-9]+";
        private const string BinByte = @"[01]+";      
        private const string Hex = @"[a-fA-F0-9]{1,4}";
        private const string Label = @"[a-zA-Z_][a-zA-Z_0-9]*";

        private const string DecRegex = @"(?<DEC>" + Dec + ")";        
        private const string HexRegex = @"(\$(?<HEX>" + Hex + "))";
        public const string LabelRegex = @"(?<label>" + Label + ")";
        private const string BinByteRegex = @"(%(?<binByte>" + BinByte + "))";

        private static readonly List<string> CaptureGroupNames;
        private const string Pattern =
         @"(?<OpenRoundBracket>\()|" +
         @"(?<CloseRoundBracket>\))|" +
         @"(?<whitespace>\s+)|" +
         @"(?<OR>(OR|or|\|\|))|" +
         @"(?<AND>(AND|and|&&))|" +
         @"(?<TRUE>(TRUE|true))|" +
         @"(?<FALSE>(FALSE|false))|" +
         "\"(?<CHAR>[\x00-\xFF])\"|" +
         BinByteRegex + "|" +
         DecRegex + "|" +
         HexRegex + "|" +         
         LabelRegex + "|" +
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
        private static readonly Regex RegexPattern; 

        static Tokenizer()
        {
            MainConsole.WriteLine(Pattern);
            RegexPattern = new Regex(Pattern, RegexOptions.Compiled);
            CaptureGroupNames = new List<string>();
            CaptureGroupNames.Add("DEC");            
            CaptureGroupNames.Add("HEX");
            CaptureGroupNames.Add("label");            
            CaptureGroupNames.Add("binByte");
            CaptureGroupNames.Add("BSL");
            CaptureGroupNames.Add("BSR");
            CaptureGroupNames.Add("LESSEQ");
            CaptureGroupNames.Add("GREATEREQ");
            CaptureGroupNames.Add("NOTEQ");
            CaptureGroupNames.Add("EQ");
            CaptureGroupNames.Add("AF");
            CaptureGroupNames.Add("LESS");
            CaptureGroupNames.Add("GREATER");
            CaptureGroupNames.Add("BOR");
            CaptureGroupNames.Add("BAND");
            CaptureGroupNames.Add("XOR");
            CaptureGroupNames.Add("PLUS");
            CaptureGroupNames.Add("MINUS");
            CaptureGroupNames.Add("MULT");
            CaptureGroupNames.Add("DIV");
            CaptureGroupNames.Add("MOD");
            CaptureGroupNames.Add("BOC");
            CaptureGroupNames.Add("NOT");            
            CaptureGroupNames.Add("OpenRoundBracket");
            CaptureGroupNames.Add("CloseRoundBracket");
            CaptureGroupNames.Add("OR");
            CaptureGroupNames.Add("AND");
            CaptureGroupNames.Add("TRUE");
            CaptureGroupNames.Add("FALSE");
            CaptureGroupNames.Add("CHAR");
        }

        public static List<Token> Tokenize(string line)
        {
            MatchCollection matches = RegexPattern.Matches(line);
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
                        string groupName = RegexPattern.GroupNameFromNumber(i);
                        if (CaptureGroupNames.Contains(groupName))
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
