// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Tokenizer implementation

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WinASM65.Expressions
{
    public interface ITokenizer
    {
        IReadOnlyList<Token> Tokenize(string expression);
    }

    public class Tokenizer : ITokenizer
    {
        public const string LabelPattern = @"[a-zA-Z_][a-zA-Z_0-9]*";

        private const string DecPattern = @"[0-9]+";
        private const string BinPattern = @"[01]+";
        private const string HexPattern = @"[a-fA-F0-9]+";

        private const string DecRegex = @"(?<DEC>" + DecPattern + ")";
        private const string HexRegex = @"(\$(?<HEX>" + HexPattern + "))";
        private const string LabelRegex = @"(?<label>" + LabelPattern + ")";
        private const string BinRegex = @"(%(?<bin>" + BinPattern + "))";

        private const string FullPattern =
            @"(?<OpenRoundBracket>\()|" +
            @"(?<CloseRoundBracket>\))|" +
            @"(?<whitespace>\s+)|" +
            @"(?<OR>(OR|or|\|\|))|" +
            @"(?<AND>(AND|and|&&))|" +
            @"(?<TRUE>(TRUE|true))|" +
            @"(?<FALSE>(FALSE|false))|" +
            "\"(?<CHAR>[\x00-\xFF])\"|" +
            BinRegex + "|" +
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
            @"(?<invalid>[^\s]+)";

        private static readonly Regex RegexPattern = new Regex(FullPattern, RegexOptions.Compiled);

        private static readonly Dictionary<string, TokenType> GroupToTypeMap = new Dictionary<string, TokenType>
        {
            { "DEC", TokenType.DecimalNumber },
            { "HEX", TokenType.HexNumber },
            { "label", TokenType.Identifier },
            { "bin", TokenType.BinaryNumber },
            { "CHAR", TokenType.CharacterConstant },
            { "OpenRoundBracket", TokenType.OpenParenthesis },
            { "CloseRoundBracket", TokenType.CloseParenthesis },
            { "BSL", TokenType.BitwiseShiftLeft },
            { "BSR", TokenType.BitwiseShiftRight },
            { "LESSEQ", TokenType.LessThanOrEqual },
            { "GREATEREQ", TokenType.GreaterThanOrEqual },
            { "NOTEQ", TokenType.NotEqual },
            { "EQ", TokenType.Equal },
            { "AF", TokenType.Assignment },
            { "LESS", TokenType.LessThan },
            { "GREATER", TokenType.GreaterThan },
            { "BOR", TokenType.BitwiseOr },
            { "BAND", TokenType.BitwiseAnd },
            { "XOR", TokenType.BitwiseXor },
            { "PLUS", TokenType.Plus },
            { "MINUS", TokenType.Minus },
            { "MULT", TokenType.Multiply },
            { "DIV", TokenType.Divide },
            { "MOD", TokenType.Modulo },
            { "BOC", TokenType.BitwiseComplement },
            { "NOT", TokenType.LogicalNot },
            { "OR", TokenType.LogicalOr },
            { "AND", TokenType.LogicalAnd },
            { "TRUE", TokenType.True },
            { "FALSE", TokenType.False }
        };

        public IReadOnlyList<Token> Tokenize(string expression)
        {
            if (string.IsNullOrEmpty(expression))
                return new List<Token>();

            MatchCollection matches = RegexPattern.Matches(expression);
            List<Token> tokenList = new List<Token>();

            foreach (Match match in matches)
            {
                int i = 0;
                foreach (Group group in match.Groups)
                {
                    if (group.Success && i > 1)
                    {
                        string groupName = RegexPattern.GroupNameFromNumber(i);
                        TokenType tokenType;
                        if (GroupToTypeMap.TryGetValue(groupName, out tokenType))
                        {
                            tokenList.Add(new Token(tokenType, group.Value));
                            break;
                        }
                    }
                    i++;
                }
            }

            return tokenList;
        }
    }
}
