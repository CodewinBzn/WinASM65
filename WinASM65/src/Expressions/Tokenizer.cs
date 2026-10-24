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
        // A name, a local "@name", or a local already qualified by its scope
        // ("Main@loop"), which is how a local is stored for a later pass.
        public const string LabelPattern = @"@?[a-zA-Z_][a-zA-Z_0-9]*(?:@[a-zA-Z_][a-zA-Z_0-9]*)?";

        private const string DecPattern = @"[0-9]+";
        private const string BinPattern = @"[01]+";
        private const string HexPattern = @"[a-fA-F0-9]+";

        private const string DecRegex = @"(?<DEC>" + DecPattern + ")";
        private const string HexRegex = @"(\$(?<HEX>" + HexPattern + "))";
        private const string LabelRegex = @"(?<label>" + LabelPattern + ")";
        // % is a binary prefix only where a value is expected: after an operand
        // (a digit, a name, a closing parenthesis) it is the modulo operator, so
        // "7%10" is 7 mod 10 and not 7 followed by binary 2.
        private const string BinRegex = @"((?<![\w)]\s*)%(?<bin>" + BinPattern + "))";

        private const string FullPattern =
            @"(?<OpenRoundBracket>\()|" +
            @"(?<CloseRoundBracket>\))|" +
            @"(?<whitespace>\s+)|" +
            // Whole words only: a name such as "origin" or "and_mask" is a name.
            @"(?<OR>(\b(OR|or)\b|\|\|))|" +
            @"(?<AND>(\b(AND|and)\b|&&))|" +
            @"(?<TRUE>\b(TRUE|true)\b)|" +
            @"(?<FALSE>\b(FALSE|false)\b)|" +
            // One character in double or single quotes, escapes allowed.
            @"""(?<CHAR>\\x[0-9a-fA-F]{2}|\\.|[\x00-\xFF])""|" +
            @"'(?<SCHAR>\\x[0-9a-fA-F]{2}|\\.|[^'\\])'|" +
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
            { "SCHAR", TokenType.CharacterConstant },
            { "invalid", TokenType.Invalid },
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
                            tokenList.Add(new Token(tokenType, group.Value, match.Index, match.Length));
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
