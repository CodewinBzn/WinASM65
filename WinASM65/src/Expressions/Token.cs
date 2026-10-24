// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Token and TokenType definition

namespace WinASM65.Expressions
{
    public enum TokenType
    {
        Unknown = 0,
        DecimalNumber,
        HexNumber,
        BinaryNumber,
        CharacterConstant,
        Identifier,
        OpenParenthesis,
        CloseParenthesis,
        BitwiseShiftLeft,
        BitwiseShiftRight,
        LessThanOrEqual,
        GreaterThanOrEqual,
        NotEqual,
        Equal,
        Assignment,
        LessThan,
        GreaterThan,
        BitwiseOr,
        BitwiseAnd,
        BitwiseXor,
        Plus,
        Minus,
        Multiply,
        Divide,
        Modulo,
        BitwiseComplement,
        LogicalNot,
        LogicalOr,
        LogicalAnd,
        True,
        False,

        /// <summary>Text the tokenizer recognised as nothing; an expression containing it is an error.</summary>
        Invalid
    }

    public struct Token
    {
        public TokenType Type { get; private set; }
        public string Value { get; private set; }

        /// <summary>Offset of the token in the expression text.</summary>
        public int Index { get; private set; }

        /// <summary>Length of the token in the expression text, delimiters included.</summary>
        public int Length { get; private set; }

        public Token(TokenType type, string value)
            : this(type, value, 0, value == null ? 0 : value.Length)
        {
        }

        public Token(TokenType type, string value, int index, int length)
            : this()
        {
            Type = type;
            Value = value;
            Index = index;
            Length = length;
        }

        public override string ToString()
        {
            return string.Format("{0}: {1}", Type, Value);
        }
    }
}
