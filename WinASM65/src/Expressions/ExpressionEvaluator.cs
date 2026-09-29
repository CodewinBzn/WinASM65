// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Strongly-typed Shunting-Yard Expression Evaluator

using System;
using System.Collections.Generic;
using System.Globalization;
using WinASM65.Core;

namespace WinASM65.Expressions
{
    public interface IExpressionEvaluator
    {
        /// <summary>
        /// <paramref name="location"/> is the file and line the expression was read
        /// from. It is attached to every symbol the expression uses, so a relocation
        /// recorded later can name the source it came from.
        /// </summary>
        ExpressionResult Evaluate(string expression, ISymbolResolver resolver = null,
            SourceLocation location = default(SourceLocation));
    }

    public class ExpressionEvaluator : IExpressionEvaluator
    {
        private readonly ITokenizer _tokenizer;

        public ExpressionEvaluator(ITokenizer tokenizer)
        {
            if (tokenizer == null)
                throw new ArgumentNullException("tokenizer");
            _tokenizer = tokenizer;
        }

        public ExpressionResult Evaluate(string expression, ISymbolResolver resolver = null,
            SourceLocation location = default(SourceLocation))
        {
            if (string.IsNullOrWhiteSpace(expression))
                return ExpressionResult.Success(new Value(0), ExpressionRole.None, null, string.Empty, location);

            IReadOnlyList<Token> tokens = _tokenizer.Tokenize(expression);
            if (tokens.Count == 0)
                return ExpressionResult.Success(new Value(0), ExpressionRole.None, null, string.Empty, location);

            List<string> undefinedSymbols = new List<string>();
            List<SymbolReference> usedSymbols = new List<SymbolReference>();
            List<EvaluatorToken> evalTokens = new List<EvaluatorToken>();

            for (int i = 0; i < tokens.Count; i++)
            {
                Token token = tokens[i];
                switch (token.Type)
                {
                    case TokenType.DecimalNumber:
                        evalTokens.Add(EvaluatorToken.ForValue(new Value(long.Parse(token.Value, CultureInfo.InvariantCulture))));
                        break;

                    case TokenType.HexNumber:
                        evalTokens.Add(EvaluatorToken.ForValue(new Value(long.Parse(token.Value.TrimStart('$'), NumberStyles.HexNumber, CultureInfo.InvariantCulture))));
                        break;

                    case TokenType.BinaryNumber:
                        evalTokens.Add(EvaluatorToken.ForValue(new Value(Convert.ToInt64(token.Value.TrimStart('%'), 2))));
                        break;

                    case TokenType.CharacterConstant:
                        string charVal = token.Value.Trim('"');
                        char c = charVal.Length > 0 ? charVal[0] : '\0';
                        evalTokens.Add(EvaluatorToken.ForValue(new Value((byte)c)));
                        break;

                    case TokenType.True:
                        evalTokens.Add(EvaluatorToken.ForValue(new Value(true)));
                        break;

                    case TokenType.False:
                        evalTokens.Add(EvaluatorToken.ForValue(new Value(false)));
                        break;

                    case TokenType.Identifier:
                        // The symbol is recorded as used whether or not it resolves.
                        // A resolved expression that read a symbol still needs a
                        // relocation; that is exactly what used to be lost here.
                        usedSymbols.Add(new SymbolReference(token.Value, location));
                        Value resolvedVal;
                        if (resolver != null && resolver.TryResolveSymbol(token.Value, out resolvedVal))
                        {
                            evalTokens.Add(EvaluatorToken.ForValue(resolvedVal));
                        }
                        else
                        {
                            if (!undefinedSymbols.Contains(token.Value))
                                undefinedSymbols.Add(token.Value);
                        }
                        break;

                    default:
                        evalTokens.Add(EvaluatorToken.ForOperator(token.Type));
                        break;
                }
            }

            // Undefined symbols abort before any reduction: a partially evaluated
            // result is never returned. The symbols read are still reported.
            if (undefinedSymbols.Count > 0)
            {
                return ExpressionResult.WithUndefinedSymbols(undefinedSymbols, ExpressionRole.None,
                    usedSymbols, expression, location);
            }

            Value result = EvaluateTokens(evalTokens);
            return ExpressionResult.Success(result, ExpressionRole.None, usedSymbols, expression, location);
        }

        private Value EvaluateTokens(List<EvaluatorToken> tokens)
        {
            Stack<Value> values = new Stack<Value>();
            Stack<OperatorInfo> ops = new Stack<OperatorInfo>();

            for (int i = 0; i < tokens.Count; i++)
            {
                EvaluatorToken token = tokens[i];

                if (token.IsValue)
                {
                    values.Push(token.Value);
                }
                else if (token.TokenType == TokenType.OpenParenthesis)
                {
                    ops.Push(new OperatorInfo(OpCode.OpenParen, 0, false));
                }
                else if (token.TokenType == TokenType.CloseParenthesis)
                {
                    while (ops.Count > 0 && ops.Peek().Code != OpCode.OpenParen)
                    {
                        ApplyTopOperator(values, ops);
                    }
                    if (ops.Count > 0 && ops.Peek().Code == OpCode.OpenParen)
                    {
                        ops.Pop();
                    }
                }
                else if (IsOperator(token.TokenType))
                {
                    bool isUnary = false;
                    if (CanBeUnary(token.TokenType))
                    {
                        if (i == 0)
                        {
                            isUnary = true;
                        }
                        else
                        {
                            EvaluatorToken prev = tokens[i - 1];
                            if (!prev.IsValue && prev.TokenType != TokenType.CloseParenthesis)
                            {
                                isUnary = true;
                            }
                        }
                    }

                    OperatorInfo op = GetOperatorInfo(token.TokenType, isUnary);

                    while (ops.Count > 0 && ops.Peek().Code != OpCode.OpenParen && ops.Peek().Precedence >= op.Precedence)
                    {
                        ApplyTopOperator(values, ops);
                    }

                    ops.Push(op);
                }
            }

            while (ops.Count > 0)
            {
                ApplyTopOperator(values, ops);
            }

            return values.Count > 0 ? values.Pop() : new Value(0);
        }

        private void ApplyTopOperator(Stack<Value> values, Stack<OperatorInfo> ops)
        {
            if (ops.Count == 0)
                return;

            OperatorInfo op = ops.Pop();

            if (op.IsUnary)
            {
                if (values.Count == 0)
                    return;
                Value val = values.Pop();
                switch (op.Code)
                {
                    case OpCode.LowByte:
                        values.Push(new Value(val.AsInteger & 0xFF));
                        break;
                    case OpCode.HighByte:
                        values.Push(new Value((val.AsInteger >> 8) & 0xFF));
                        break;
                    case OpCode.UnaryPlus:
                        values.Push(+val);
                        break;
                    case OpCode.UnaryMinus:
                        values.Push(-val);
                        break;
                    case OpCode.BitwiseNot:
                        values.Push(~val);
                        break;
                    case OpCode.LogicalNot:
                        values.Push(!val);
                        break;
                }
            }
            else
            {
                if (values.Count < 2)
                    return;
                Value val2 = values.Pop();
                Value val1 = values.Pop();

                switch (op.Code)
                {
                    case OpCode.Multiply:
                        values.Push(val1 * val2);
                        break;
                    case OpCode.Divide:
                        values.Push(val1 / val2);
                        break;
                    case OpCode.Modulo:
                        values.Push(val1 % val2);
                        break;
                    case OpCode.Add:
                        values.Push(val1 + val2);
                        break;
                    case OpCode.Subtract:
                        values.Push(val1 - val2);
                        break;
                    case OpCode.ShiftLeft:
                        values.Push(Value.BitwiseShiftLeft(val1, val2));
                        break;
                    case OpCode.ShiftRight:
                        values.Push(Value.BitwiseShiftRight(val1, val2));
                        break;
                    case OpCode.LessThan:
                        values.Push(Value.LessThan(val1, val2));
                        break;
                    case OpCode.GreaterThan:
                        values.Push(Value.GreaterThan(val1, val2));
                        break;
                    case OpCode.LessThanOrEqual:
                        values.Push(Value.LessThanOrEqual(val1, val2));
                        break;
                    case OpCode.GreaterThanOrEqual:
                        values.Push(Value.GreaterThanOrEqual(val1, val2));
                        break;
                    case OpCode.Equal:
                    case OpCode.Assignment:
                        values.Push(Value.Equal(val1, val2));
                        break;
                    case OpCode.NotEqual:
                        values.Push(Value.NotEqual(val1, val2));
                        break;
                    case OpCode.BitwiseAnd:
                        values.Push(val1 & val2);
                        break;
                    case OpCode.BitwiseXor:
                        values.Push(val1 ^ val2);
                        break;
                    case OpCode.BitwiseOr:
                        values.Push(val1 | val2);
                        break;
                    case OpCode.LogicalAnd:
                        values.Push(Value.LogicalAnd(val1, val2));
                        break;
                    case OpCode.LogicalOr:
                        values.Push(Value.LogicalOr(val1, val2));
                        break;
                }
            }
        }

        private static bool IsOperator(TokenType type)
        {
            switch (type)
            {
                case TokenType.Plus:
                case TokenType.Minus:
                case TokenType.Multiply:
                case TokenType.Divide:
                case TokenType.Modulo:
                case TokenType.BitwiseComplement:
                case TokenType.LogicalNot:
                case TokenType.LessThan:
                case TokenType.GreaterThan:
                case TokenType.LessThanOrEqual:
                case TokenType.GreaterThanOrEqual:
                case TokenType.Equal:
                case TokenType.NotEqual:
                case TokenType.Assignment:
                case TokenType.BitwiseShiftLeft:
                case TokenType.BitwiseShiftRight:
                case TokenType.BitwiseAnd:
                case TokenType.BitwiseXor:
                case TokenType.BitwiseOr:
                case TokenType.LogicalAnd:
                case TokenType.LogicalOr:
                    return true;
                default:
                    return false;
            }
        }

        private static bool CanBeUnary(TokenType type)
        {
            switch (type)
            {
                case TokenType.Plus:
                case TokenType.Minus:
                case TokenType.BitwiseComplement:
                case TokenType.LogicalNot:
                case TokenType.LessThan:
                case TokenType.GreaterThan:
                    return true;
                default:
                    return false;
            }
        }

        private static OperatorInfo GetOperatorInfo(TokenType type, bool isUnary)
        {
            if (isUnary)
            {
                switch (type)
                {
                    case TokenType.LessThan:
                        return new OperatorInfo(OpCode.LowByte, 11, true);
                    case TokenType.GreaterThan:
                        return new OperatorInfo(OpCode.HighByte, 11, true);
                    case TokenType.Plus:
                        return new OperatorInfo(OpCode.UnaryPlus, 11, true);
                    case TokenType.Minus:
                        return new OperatorInfo(OpCode.UnaryMinus, 11, true);
                    case TokenType.BitwiseComplement:
                        return new OperatorInfo(OpCode.BitwiseNot, 11, true);
                    case TokenType.LogicalNot:
                        return new OperatorInfo(OpCode.LogicalNot, 11, true);
                }
            }

            switch (type)
            {
                case TokenType.Multiply:
                    return new OperatorInfo(OpCode.Multiply, 10, false);
                case TokenType.Divide:
                    return new OperatorInfo(OpCode.Divide, 10, false);
                case TokenType.Modulo:
                    return new OperatorInfo(OpCode.Modulo, 10, false);
                case TokenType.Plus:
                    return new OperatorInfo(OpCode.Add, 9, false);
                case TokenType.Minus:
                    return new OperatorInfo(OpCode.Subtract, 9, false);
                case TokenType.BitwiseShiftLeft:
                    return new OperatorInfo(OpCode.ShiftLeft, 8, false);
                case TokenType.BitwiseShiftRight:
                    return new OperatorInfo(OpCode.ShiftRight, 8, false);
                case TokenType.LessThan:
                    return new OperatorInfo(OpCode.LessThan, 7, false);
                case TokenType.GreaterThan:
                    return new OperatorInfo(OpCode.GreaterThan, 7, false);
                case TokenType.LessThanOrEqual:
                    return new OperatorInfo(OpCode.LessThanOrEqual, 7, false);
                case TokenType.GreaterThanOrEqual:
                    return new OperatorInfo(OpCode.GreaterThanOrEqual, 7, false);
                case TokenType.Equal:
                    return new OperatorInfo(OpCode.Equal, 6, false);
                case TokenType.NotEqual:
                    return new OperatorInfo(OpCode.NotEqual, 6, false);
                case TokenType.Assignment:
                    return new OperatorInfo(OpCode.Assignment, 6, false);
                case TokenType.BitwiseAnd:
                    return new OperatorInfo(OpCode.BitwiseAnd, 5, false);
                case TokenType.BitwiseXor:
                    return new OperatorInfo(OpCode.BitwiseXor, 4, false);
                case TokenType.BitwiseOr:
                    return new OperatorInfo(OpCode.BitwiseOr, 3, false);
                case TokenType.LogicalAnd:
                    return new OperatorInfo(OpCode.LogicalAnd, 2, false);
                case TokenType.LogicalOr:
                    return new OperatorInfo(OpCode.LogicalOr, 1, false);
                default:
                    return new OperatorInfo(OpCode.None, 0, false);
            }
        }

        private enum OpCode
        {
            None,
            OpenParen,
            LowByte,
            HighByte,
            UnaryPlus,
            UnaryMinus,
            BitwiseNot,
            LogicalNot,
            Multiply,
            Divide,
            Modulo,
            Add,
            Subtract,
            ShiftLeft,
            ShiftRight,
            LessThan,
            GreaterThan,
            LessThanOrEqual,
            GreaterThanOrEqual,
            Equal,
            NotEqual,
            Assignment,
            BitwiseAnd,
            BitwiseXor,
            BitwiseOr,
            LogicalAnd,
            LogicalOr
        }

        private struct OperatorInfo
        {
            public OpCode Code { get; private set; }
            public int Precedence { get; private set; }
            public bool IsUnary { get; private set; }

            public OperatorInfo(OpCode code, int precedence, bool isUnary)
                : this()
            {
                Code = code;
                Precedence = precedence;
                IsUnary = isUnary;
            }
        }

        private struct EvaluatorToken
        {
            public bool IsValue { get; private set; }
            public Value Value { get; private set; }
            public TokenType TokenType { get; private set; }

            public static EvaluatorToken ForValue(Value value)
            {
                EvaluatorToken t = new EvaluatorToken();
                t.IsValue = true;
                t.Value = value;
                return t;
            }

            public static EvaluatorToken ForOperator(TokenType type)
            {
                EvaluatorToken t = new EvaluatorToken();
                t.IsValue = false;
                t.TokenType = type;
                return t;
            }
        }
    }
}
