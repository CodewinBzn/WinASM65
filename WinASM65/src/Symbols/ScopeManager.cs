// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Scope and Symbol Table Manager (Pure OOP, SOLID)

using System;
using System.Collections.Generic;
using WinASM65.Core;
using WinASM65.Expressions;

namespace WinASM65.Symbols
{
    public interface IScopeManager : ISymbolResolver
    {
        byte Level { get; }
        LexicalScopeData CurrentScope { get; }
        LexicalScopeData GlobalScope { get; }
        Dictionary<ushort, UnresolvedExpr> UnsolvedExprList { get; }
        bool EnterLocalScope(out string error);
        bool ExitLocalScope(out string error);
        bool AddSymbol(string label, long value, bool replaceIfExist, out string error);
        void AddUnresolvedSymbol(string name, UnresolvedSymbol symbol);
        void AddUnresolvedExpression(ushort position, UnresolvedExpr expr);
        void AddDependingSymbol(string symbol, string dependingSymbol);
        void ResolveSymbols(IExpressionEvaluator evaluator, Action<UnresolvedExpr, Value> patchCallback);
        void Reset();
    }

    public class ScopeManager : IScopeManager
    {
        private readonly List<LexicalScopeData> _scopeList = new List<LexicalScopeData>();
        private readonly Dictionary<ushort, UnresolvedExpr> _unsolvedExprList = new Dictionary<ushort, UnresolvedExpr>();
        private byte _level;

        public byte Level
        {
            get { return _level; }
        }

        public LexicalScopeData CurrentScope
        {
            get { return _scopeList[_level]; }
        }

        public LexicalScopeData GlobalScope
        {
            get { return _scopeList[0]; }
        }

        public Dictionary<ushort, UnresolvedExpr> UnsolvedExprList
        {
            get { return _unsolvedExprList; }
        }

        public ScopeManager()
        {
            Reset();
        }

        public void Reset()
        {
            _scopeList.Clear();
            _unsolvedExprList.Clear();
            _level = 0;
            _scopeList.Add(new LexicalScopeData());
        }

        public bool EnterLocalScope(out string error)
        {
            error = null;
            if (_level == byte.MaxValue)
            {
                error = ErrorCodes.MAX_LOCAL_SCOPE;
                return false;
            }
            _level++;
            _scopeList.Add(new LexicalScopeData());
            return true;
        }

        public bool ExitLocalScope(out string error)
        {
            error = null;
            if (_level == 0)
            {
                error = ErrorCodes.NO_LOCAL_SCOPE;
                return false;
            }

            Dictionary<string, UnresolvedSymbol> currentUnsolved = _scopeList[_level].UnsolvedSymbols;
            Dictionary<string, UnresolvedSymbol> parentUnsolved = _scopeList[_level - 1].UnsolvedSymbols;

            foreach (KeyValuePair<string, UnresolvedSymbol> entry in currentUnsolved)
            {
                string symb = entry.Key;
                UnresolvedSymbol localSymb = entry.Value;

                if (parentUnsolved.ContainsKey(symb))
                {
                    UnresolvedSymbol parentSymb = parentUnsolved[symb];
                    foreach (string dep in localSymb.DependingList)
                    {
                        if (!parentSymb.DependingList.Contains(dep))
                            parentSymb.DependingList.Add(dep);
                    }
                    foreach (ushort expr in localSymb.ExprList)
                    {
                        if (!parentSymb.ExprList.Contains(expr))
                            parentSymb.ExprList.Add(expr);
                    }
                    parentUnsolved[symb] = parentSymb;
                }
                else
                {
                    localSymb.Expr = null;
                    parentUnsolved.Add(symb, localSymb);
                }
            }

            _scopeList.RemoveAt(_level);
            _level--;
            return true;
        }

        public bool TryResolveSymbol(string name, out Value value)
        {
            value = default(Value);
            if (string.IsNullOrEmpty(name))
                return false;

            int current = _level;
            while (current >= 0)
            {
                long val;
                if (_scopeList[current].SymbolTable.TryGetValue(name, out val))
                {
                    value = new Value(val);
                    return true;
                }
                current--;
            }
            return false;
        }

        public bool AddSymbol(string label, long value, bool replaceIfExist, out string error)
        {
            error = null;
            Dictionary<string, long> symbTable = CurrentScope.SymbolTable;

            if (symbTable.ContainsKey(label))
            {
                if (replaceIfExist)
                {
                    symbTable[label] = value;
                    return true;
                }
                error = ErrorCodes.LABEL_EXISTS;
                return false;
            }

            symbTable.Add(label, value);
            return true;
        }

        public void AddUnresolvedSymbol(string name, UnresolvedSymbol symbol)
        {
            Dictionary<string, UnresolvedSymbol> unsolved = CurrentScope.UnsolvedSymbols;
            if (unsolved.ContainsKey(name))
            {
                UnresolvedSymbol existing = unsolved[name];
                foreach (string dep in symbol.DependingList)
                {
                    if (!existing.DependingList.Contains(dep))
                        existing.DependingList.Add(dep);
                }
                foreach (ushort expr in symbol.ExprList)
                {
                    if (!existing.ExprList.Contains(expr))
                        existing.ExprList.Add(expr);
                }
                if (!string.IsNullOrEmpty(symbol.Expr))
                    existing.Expr = symbol.Expr;
                unsolved[name] = existing;
            }
            else
            {
                unsolved.Add(name, symbol);
            }
        }

        public void AddUnresolvedExpression(ushort position, UnresolvedExpr expr)
        {
            _unsolvedExprList[position] = expr;
        }

        public void AddDependingSymbol(string symbol, string dependingSymbol)
        {
            Dictionary<string, UnresolvedSymbol> unsolved = CurrentScope.UnsolvedSymbols;
            if (unsolved.ContainsKey(symbol))
            {
                unsolved[symbol].DependingList.Add(dependingSymbol);
            }
            else
            {
                UnresolvedSymbol unres = new UnresolvedSymbol();
                unres.DependingList.Add(dependingSymbol);
                unsolved.Add(symbol, unres);
            }
        }

        public void ResolveSymbols(IExpressionEvaluator evaluator, Action<UnresolvedExpr, Value> patchCallback)
        {
            Dictionary<string, UnresolvedSymbol> unsolvedSymbols = GlobalScope.UnsolvedSymbols;
            List<string> resolvedNames = new List<string>();

            foreach (string symbName in unsolvedSymbols.Keys)
            {
                if (!GlobalScope.SymbolTable.ContainsKey(symbName))
                    continue;

                UnresolvedSymbol unresSymb = unsolvedSymbols[symbName];
                ResolveSymbolDepsAndExprs(unresSymb, evaluator, patchCallback);
                resolvedNames.Add(symbName);
            }

            foreach (string name in resolvedNames)
            {
                unsolvedSymbols.Remove(name);
            }
        }

        private void ResolveSymbolDepsAndExprs(UnresolvedSymbol unresSymb, IExpressionEvaluator evaluator, Action<UnresolvedExpr, Value> patchCallback)
        {
            Dictionary<string, UnresolvedSymbol> unsolvedSymbols = CurrentScope.UnsolvedSymbols;

            // Resolve depending symbols
            foreach (string dep in unresSymb.DependingList)
            {
                if (unsolvedSymbols.ContainsKey(dep))
                {
                    UnresolvedSymbol unresDep = unsolvedSymbols[dep];
                    unresDep.NbrUndefinedSymb--;
                    if (unresDep.NbrUndefinedSymb <= 0)
                    {
                        ExpressionResult res = evaluator.Evaluate(unresDep.Expr, this);
                        if (res.IsResolved)
                        {
                            string err;
                            AddSymbol(dep, res.Value.AsInteger, true, out err);
                        }
                    }
                }
            }

            // Resolve expressions
            List<ushort> resolvedExprs = new List<ushort>();
            foreach (ushort exprPos in unresSymb.ExprList)
            {
                UnresolvedExpr unresExp;
                if (_unsolvedExprList.TryGetValue(exprPos, out unresExp))
                {
                    unresExp.NbrUndefinedSymb--;
                    if (unresExp.NbrUndefinedSymb <= 0)
                    {
                        ExpressionResult res = evaluator.Evaluate(unresExp.Expr, this);
                        if (res.IsResolved && patchCallback != null)
                        {
                            patchCallback(unresExp, res.Value);
                            resolvedExprs.Add(exprPos);
                        }
                    }
                }
            }

            foreach (ushort exprPos in resolvedExprs)
            {
                _unsolvedExprList.Remove(exprPos);
            }
        }
    }
}
