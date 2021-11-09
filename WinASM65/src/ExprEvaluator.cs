using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinASM65
{
    public class ExprEvaluator
    {
        public static int Eval(string expr)
        {
            Stack<int> values = new Stack<int>();
            Stack<char> ops = new Stack<char>();
            List<Token> tokens = Tokenizer.Tokenize(expr);
            foreach (Token token in tokens)
            {
                switch (token.Type)
                {
                    case "OpenRoundBracket":
                        ops.Push(token.Value.ElementAt(0));
                        break;
                    case "CloseRoundBracket":
                        while (ops.Count > 0 && ops.Peek() != '(')
                        {
                            int val2 = values.Pop();
                            int val1 = values.Pop();
                            char op = ops.Pop();
                            values.Push(ApplyOp(val1, val2, op));
                        }                        
                        if (ops.Count > 0)
                            ops.Pop();
                        break;
                    case "arOp":                        
                        while (ops.Count > 0 && Precedence(ops.Peek())
                                            >= Precedence(token.Value.ElementAt(0)))
                        {
                            int val2 = values.Pop();                       
                            int val1 = values.Pop();                            
                            char op = ops.Pop();                            
                            values.Push(ApplyOp(val1, val2, op));
                        }
                        ops.Push(token.Value.ElementAt(0));

                        break;
                    default: // current token is digit                        
                        values.Push(int.Parse(token.Value));                   
                        break; 
                }
            }
            while (ops.Count > 0)
            {
                int val2 = values.Pop();                
                int val1 = values.Pop();
                char op = ops.Pop();
                values.Push(ApplyOp(val1, val2, op));
            }
            return values.Pop();
        }

        private static int Precedence(char op)
        {
            switch (op)
            {
                case '*':
                case '/':
                case '%':
                    return 5;
                case '+':
                case '-':
                    return 4;
                case '&':
                    return 3;
                case '^':
                    return 2;
                case '|':
                    return 1;
                default:
                    return 0;
            }
        }

        private static int ApplyOp(int a, int b, char op)
        {
            switch (op)
            {
                case '+': return a + b;
                case '-': return a - b;
                case '*': return a * b;
                case '/': return a / b;
                case '%': return a % b;
                case '&': return a & b;
                case '^': return a ^ b;
                case '|': return a | b;
                default:
                    return -1;
            }
        }
    }
}
