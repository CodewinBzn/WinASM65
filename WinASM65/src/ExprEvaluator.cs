using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinASM65
{
    public static class ExprEvaluator
    {
        private static readonly string[] OPERATORS = { "BSL", "BSR", "LESSEQ", "GREATEREQ", "NOTEQ", "EQ", "AF",
            "LESS", "GREATER", "BOR", "BAND", "XOR", "PLUS", "MINUS", "MULT", "DIV", "MOD", "BOC","NOT","OR", "AND" };
        private static readonly string[] UNARY_OPERATORS = { "PLUS", "MINUS", "BOC", "NOT", "LESS", "GREATER" };
        private static void EvalNode(Stack<dynamic> values, Stack<string> ops)
        {
            dynamic val;
            switch (ops.Peek())
            {
                case "LO": // low byte
                    ops.Pop();
                    val = values.Pop();
                    values.Push(Assembler.GetLowByte((ushort)val)); ;
                    break;
                case "HI": // high byte
                    ops.Pop();
                    val = values.Pop();
                    values.Push(Assembler.GetHighByte((ushort)val)); ;
                    break;
                case "U+":
                    ops.Pop();
                    val = values.Pop();
                    values.Push(+val);
                    break;
                case "U-":
                    ops.Pop();
                    val = values.Pop();
                    values.Push(-val);
                    break;
                case "U~":
                    ops.Pop();
                    val = values.Pop();
                    values.Push(~val);
                    break;
                case "U!":
                    ops.Pop();
                    val = values.Pop();
                    values.Push(!val);
                    break;
                default:
                    dynamic val2 = values.Pop();
                    dynamic val1 = values.Pop();
                    string op = ops.Pop();
                    values.Push(ApplyOp(val1, val2, op));
                    break;
            }
        }
        public static dynamic Eval(string expr)
        {
            Stack<dynamic> values = new Stack<dynamic>();
            Stack<string> ops = new Stack<string>();
            List<Token> tokens = Tokenizer.Tokenize(expr);
            for (int i = 0; i < tokens.Count; i++)
            {
                Token token = tokens[i];
                if (token.Type.Equals("OpenRoundBracket"))
                {
                    ops.Push(token.Value);
                }
                else if (token.Type.Equals("CloseRoundBracket"))
                {
                    while (ops.Count > 0 && ops.Peek() != "(")
                    {
                        EvalNode(values, ops);
                    }
                    if (ops.Count > 0)
                        ops.Pop();
                }
                else if (IsOperator(token.Type))
                {
                    string tokenType = token.Type;
                    if (IsUnary(token.Type))
                    {
                        string precType = null;
                        if (i > 0)
                        {
                            precType = tokens[i - 1].Type;
                        }
                        if (precType == null || IsOperator(precType) || precType.Equals("OpenRoundBracket"))
                        {
                            tokenType = GetOpUnaryType(tokenType);
                        }
                    }

                    while (ops.Count > 0 && Precedence(ops.Peek())
                                        >= Precedence(tokenType))
                    {
                        EvalNode(values, ops);
                    }

                    ops.Push(tokenType);
                }
                else
                {
                    // current token is digit                        
                    values.Push(int.Parse(token.Value));
                }
            }
            while (ops.Count > 0)
            {
                EvalNode(values, ops);
            }
            return values.Pop();
        }

        private static string GetOpUnaryType(string op)
        {
            switch (op)
            {
                case "PLUS":
                    return "U+";
                case "MINUS":
                    return "U-";
                case "BOC":
                    return "U~";
                case "NOT":
                    return "U!";
                case "LESS":
                    return "LO";
                case "GREATER":
                    return "HI";
                default:
                    return null;
            }
        }
        private static int Precedence(string op)
        {
            switch (op)
            {


                case "LO":
                case "HI":
                case "U+":
                case "U-":
                case "U~":
                case "U!":
                    return 11;
                case "MULT":
                case "DIV":
                case "MOD":
                    return 10;
                case "PLUS":
                case "MINUS":
                    return 9;
                case "BSL":
                case "BSR":
                    return 8;
                case "LESS":
                case "GREATER":
                case "LESSEQ":
                case "GREATEREQ":
                    return 7;
                case "NOTEQ":
                case "EQ":
                case "AF":
                    return 6;
                case "BAND":
                    return 5;
                case "XOR":
                    return 4;
                case "BOR":
                    return 3;
                case "AND":
                    return 2;
                case "OR":
                    return 1;
                default:
                    return 0;
            }
        }
        private static dynamic ApplyOp(dynamic a, dynamic b, string op)
        {
            switch (op)
            {
                case "PLUS": return a + b;
                case "MINUS": return a - b;
                case "MULT": return a * b;
                case "DIV": return a / b;
                case "MOD": return a % b;
                case "BAND": return a & b;
                case "XOR": return a ^ b;
                case "BOR": return a | b;
                case "OR": return a || b;
                case "AND": return a && b;
                case "BSL": return a << b;
                case "BSR": return a >> b;
                case "LESSEQ": return a <= b;
                case "GREATEREQ": return a >= b;
                case "NOTEQ": return a != b;
                case "EQ": return a == b;
                case "AF": return b;
                case "LESS": return a < b;
                case "GREATER": return a > b;                
                default:
                    return -1;
            }
        }

        private static bool IsOperator(string op)
        {            
            return OPERATORS.Contains(op);
        }
        private static bool IsUnary(string op)
        {           
            return UNARY_OPERATORS.Contains(op);
        }
    }
}
