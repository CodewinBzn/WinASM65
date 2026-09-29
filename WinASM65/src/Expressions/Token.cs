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
        False
    }

    public struct Token
    {
        public TokenType Type { get; private set; }
        public string Value { get; private set; }

        public Token(TokenType type, string value)
            : this()
        {
            Type = type;
            Value = value;
        }

        public override string ToString()
        {
            return string.Format("{0}: {1}", Type, Value);
        }
    }
}
