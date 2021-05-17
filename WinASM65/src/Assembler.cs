/**********************************************************************************/
/*                                                                                */
/*                                                                                */
/* 2021 Abdelghani BOUZIANE                                                       */
/*                                                                                */
/*                                                                                */
/**********************************************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Data;

namespace WinASM65
{
    public class Assembler
    {
        public static List<byte> fileOutMemory;
        private static List<Error> errorList;
        private static Stream stream;
        private static BinaryWriter bw = null;
        public static string objectFileName { get; set; }
        public delegate void DelHandler(Match lineReg);
        private static Dictionary<string, DelHandler> mapLineHandlers = new Dictionary<string, DelHandler>
        {
            {CPUDef.START_LOCAL_SCOPE, StartLocalScopeHandler },
            {CPUDef.END_LOCAL_SCOPE, EndLocalScopeHandler },
            {CPUDef.LABEL, LabelHandler },
            {CPUDef.DIRECTIVE, DirectiveHandler },
            {CPUDef.INSTRUCTION, InstructionHandler },
            {CPUDef.CONSTANT, ConstantHandler },
            {CPUDef.MEM_RESERVE, MemResHandler },
            {CPUDef.CALL_MACRO, CallMacroHandler }
        };
        public static Dictionary<string, Symbol> symbolTable;
        private static ushort currentAddr;
        private static ushort originAddr;
        private delegate void DelDirectiveHandler(string value);
        public static string sourceFile;
        public static Dictionary<string, UnresolvedSymbol> unsolvedSymbols;
        public static Dictionary<ushort, UnresolvedExpr> unsolvedExprList;
        public static Dictionary<string, MacroDef> macros;
        public static Stack<FileInfo> fileStack;
        public static FileInfo file;
        public static MemArea memArea;
        public static bool startMacroDef;
        public static string currentMacro;
        public static ConditionalAsm cAsm;
        public static LocalScope localScope;

        private static void StartLocalScopeHandler(Match lineReg)
        {
            localScope.isLocalScope = true;
        }

        private static void EndLocalScopeHandler(Match lineReg)
        {
            foreach (string symb in localScope.unsolvedSymbols.Keys)
            {
                if (unsolvedSymbols.ContainsKey(symb))
                {
                    UnresolvedSymbol glUnsolvedSymbol = unsolvedSymbols[symb];
                    UnresolvedSymbol localUnsolvedSymbol = localScope.unsolvedSymbols[symb];
                    foreach (string dep in localUnsolvedSymbol.DependingList)
                    {
                        if (!glUnsolvedSymbol.DependingList.Contains(dep))
                        {
                            glUnsolvedSymbol.DependingList.Add(dep);
                        }
                    }
                    foreach (ushort expr in localUnsolvedSymbol.ExprList)
                    {
                        if (!glUnsolvedSymbol.ExprList.Contains(expr))
                        {
                            glUnsolvedSymbol.ExprList.Add(expr);
                        }
                    }
                    unsolvedSymbols[symb] = glUnsolvedSymbol;
                }
                else
                {
                    UnresolvedSymbol localUnsolvedSymbol = localScope.unsolvedSymbols[symb];
                    localUnsolvedSymbol.Expr = null;
                    unsolvedSymbols.Add(symb, localUnsolvedSymbol);
                }

            }
            localScope.isLocalScope = false;
            localScope.symbolTable.Clear();
            localScope.unsolvedSymbols.Clear();
        }

        #region directives
        private static Dictionary<string, DelDirectiveHandler> directiveHandlersMap = new Dictionary<string, DelDirectiveHandler>
        {
            { ".org", OrgHandler },
            { ".memarea", MemAreaHandler },
            { ".incbin", IncBinHandler },
            { ".include", IncludeHandler },
            { ".byte", DataByteHandler },
            { ".word", DataWordHandler },
            { ".macro", StartMacroDefHandler },
            { ".endmacro", EndMacroDefHandler },
            { ".ifdef", IfDefHandler },
            { ".ifndef", IfnDefHandler },
            { ".else", ElseHandler },
            { ".endif", EndIfHandler },
        };

        private static void IfDefHandler(string value)
        {
            cAsm.inCondition = true;
            string label = value.Trim();
            cAsm.val = symbolTable.ContainsKey(label) || (localScope.isLocalScope && localScope.symbolTable.ContainsKey(label));
        }

        private static void IfnDefHandler(string value)
        {
            cAsm.inCondition = true;
            string label = value.Trim();
            cAsm.val = !symbolTable.ContainsKey(label) && (!localScope.isLocalScope || !localScope.symbolTable.ContainsKey(label));
        }
        private static void ElseHandler(string value)
        {
            cAsm.val = !cAsm.val;
        }

        private static void EndIfHandler(string value)
        {
            cAsm.inCondition = false;
        }

        private static void StartMacroDefHandler(string value)
        {

            Match mReg = Regex.Match(value, CPUDef.macroReg);
            string macroName = currentMacro = mReg.Groups["label"].Value;
            string defParts = mReg.Groups["value"].Value;
            MacroDef macroDef = new MacroDef();
            macroDef.lines = new List<string>();

            startMacroDef = true;
            if (!string.IsNullOrEmpty(defParts))
            {
                macroDef.listParam = Regex.Replace(defParts, @"\s+", "").Split(',');
            }
            if (macros.ContainsKey(macroName))
            {
                AddError(Errors.MACRO_EXISTS);
            }
            else
            {
                macros.Add(macroName, macroDef);
            }
        }

        private static void EndMacroDefHandler(string value)
        {
            startMacroDef = false;
            currentMacro = string.Empty;
        }

        private static void DirectiveHandler(Match lineReg)
        {
            string directive = lineReg.Groups["directive"].Value;
            string value = lineReg.Groups["value"].Value;
            if (directiveHandlersMap.ContainsKey(directive.ToLower()))
            {
                DelDirectiveHandler handler = directiveHandlersMap[directive.ToLower()];
                handler(value);
            }
        }

        private static void OrgHandler(string value)
        {
            value = value.Trim().Replace("$", string.Empty);
            originAddr = ushort.Parse(value, NumberStyles.HexNumber);
            currentAddr = originAddr;
        }

        private static void MemAreaHandler(string value)
        {
            MemArea ma;
            if (!localScope.isLocalScope)
            {
                ma = memArea;
            }
            else
            {
                ma = localScope.memArea;
            }
            ExprResult res = ResolveExpr(value, CPUDef.AddrModes.NO);
            if (res.undefinedSymbs.Count == 0)
            {
                ma.val = res.Result;
                ma.type = res.Type;
            }
            else
            {
                AddError(Errors.UNDEFINED_SYMBOL);
            }
        }

        private static void IncBinHandler(string fileName)
        {
            fileName = fileName.Replace("\"", String.Empty);
            string directoryName = Path.GetDirectoryName(file.sourceFile);
            string toInclude = directoryName + '/' + fileName;
            if (File.Exists(toInclude))
            {
                fileOutMemory.AddRange(File.ReadAllBytes(toInclude));
            }
            else
            {
                AddError(Errors.FILE_NOT_EXISTS);
            }
        }
        private static void IncludeHandler(string fileName)
        {
            fileName = fileName.Replace("\"", String.Empty);
            string directoryName = Path.GetDirectoryName(file.sourceFile);
            string toInclude = directoryName + '/' + fileName;
            if (File.Exists(toInclude))
            {
                fileStack.Push(file);
                file = new FileInfo() { fp = new StreamReader(toInclude), sourceFile = toInclude };
            }
            else
            {
                AddError(Errors.FILE_NOT_EXISTS);
            }

        }

        private static void DataByteHandler(string bytesIn)
        {
            string[] bytes = bytesIn.Split(',');
            foreach (string db in bytes)
            {
                string data = db.Trim();
                if (CPUDef.isString(data)) // handle strings 
                {
                    data = data.Substring(1, data.Length - 2);
                    foreach (char c in data)
                    {
                        fileOutMemory.Add(Convert.ToByte(c));
                        currentAddr++;
                    }
                }
                else
                {
                    ExprResult res = ResolveExpr(data, CPUDef.AddrModes.NO);
                    if (res.undefinedSymbs.Count == 0)
                    {
                        fileOutMemory.Add((byte)res.Result);
                    }
                    else
                    {

                        fileOutMemory.Add(0);
                        ushort position = (ushort)(currentAddr - originAddr);
                        UnresolvedExpr expr = new UnresolvedExpr
                        {
                            Position = position,
                            Type = SymbolType.BYTE,
                            addrMode = CPUDef.AddrModes.NO,
                            NbrUndefinedSymb = res.undefinedSymbs.Count,
                            Expr = data
                        };

                        unsolvedExprList.Add(position, expr);

                        foreach (string symb in res.undefinedSymbs)
                        {
                            UnresolvedSymbol unResSymb = new UnresolvedSymbol();
                            unResSymb.DependingList = new List<string>();
                            unResSymb.ExprList = new List<ushort>();
                            unResSymb.ExprList.Add(position);
                            AddUnsolvedSymbol(symb, unResSymb);
                        }
                    }

                    currentAddr++;
                }
            }
        }

        private static void DataWordHandler(string wordsIn)
        {
            string[] words = wordsIn.Split(',');
            foreach (string dw in words)
            {
                string data = dw.Trim();
                ExprResult res = ResolveExpr(data, CPUDef.AddrModes.NO);
                if (res.undefinedSymbs.Count == 0)
                {
                    fileOutMemory.AddRange(GetWordBytes((ushort)res.Result));
                }
                else
                {
                    fileOutMemory.AddRange(new byte[2] { 0, 0 });
                    ushort position = (ushort)(currentAddr - originAddr);
                    UnresolvedExpr expr = new UnresolvedExpr
                    {
                        Position = position,
                        Type = SymbolType.WORD,
                        addrMode = CPUDef.AddrModes.NO,
                        NbrUndefinedSymb = res.undefinedSymbs.Count,
                        Expr = data
                    };

                    unsolvedExprList.Add(position, expr);
                    foreach (string symb in res.undefinedSymbs)
                    {
                        UnresolvedSymbol unResSymb = new UnresolvedSymbol();
                        unResSymb.DependingList = new List<string>();
                        unResSymb.ExprList = new List<ushort>();
                        unResSymb.ExprList.Add(position);
                        AddUnsolvedSymbol(symb, unResSymb);
                    }
                }
                currentAddr += 2;
            }
        }

        #endregion
        private static void ConstantHandler(Match lineReg)
        {
            string label = lineReg.Groups["label"].Value;
            string value = lineReg.Groups["value"].Value;
            ExprResult res = ResolveExpr(value, CPUDef.AddrModes.NO);
            if (res.undefinedSymbs.Count == 0)
            {
                Symbol symb = new Symbol()
                {
                    Value = res.Result,
                    Type = res.Type
                };
                AddSymbol(label, symb);
            }
            else
            {
                UnresolvedSymbol unResSymb = new UnresolvedSymbol();
                unResSymb.NbrUndefinedSymb = res.undefinedSymbs.Count;
                unResSymb.DependingList = new List<string>();
                unResSymb.Expr = value;
                unResSymb.ExprList = new List<ushort>();

                foreach (string symb in res.undefinedSymbs)
                {
                    AddDependingSymb(symb, label);
                }

                AddUnsolvedSymbol(label, unResSymb);
            }
        }

        private static void AddDependingSymb(string symbol, string dependingSymb)
        {
            Dictionary<string, UnresolvedSymbol> unsolvedSymbs;
            if (localScope.isLocalScope)
            {
                unsolvedSymbs = localScope.unsolvedSymbols;
            }
            else
            {
                unsolvedSymbs = unsolvedSymbols;
            }
            if (unsolvedSymbs.ContainsKey(symbol))
            {
                unsolvedSymbs[symbol].DependingList.Add(dependingSymb);
            }
            else
            {
                UnresolvedSymbol unsolved = new UnresolvedSymbol();
                unsolved.DependingList = new List<string>();
                unsolved.DependingList.Add(dependingSymb);
                unsolved.ExprList = new List<ushort>();
                unsolvedSymbs.Add(symbol, unsolved);
            }
        }
        private static ExprResult ResolveExpr(string exprIn, CPUDef.AddrModes addrMode)
        {
            List<Token> tokens = Tokenizer.Tokenize(exprIn);
            string expr = string.Empty;
            Symbol symb;
            ExprResult exprRes = new ExprResult();
            exprRes.undefinedSymbs = new List<string>();
            foreach (Token token in tokens)
            {
                switch (token.Type)
                {
                    case "HB":
                        expr = $"{expr} {Byte.Parse(token.Value, NumberStyles.HexNumber)}";
                        break;
                    case "HW":
                        expr = $"{expr} {ushort.Parse(token.Value, NumberStyles.HexNumber)}";
                        break;
                    case "binByte":
                        expr = $"{expr} {Convert.ToByte(token.Value, 2)}";
                        break;
                    case "DEC":
                        int val = int.Parse(token.Value);
                        if (val <= 255)
                        {
                            expr = $"{expr} {(byte)val}";
                        }
                        else
                        {
                            expr = $"{expr} {(ushort)val}";
                        }
                        break;
                    case "label":
                        symb = GetSymbolValue(token.Value);
                        if (symb != null)
                        {
                            expr = $"{expr} {symb.Value}";
                        }
                        else
                        {
                            exprRes.undefinedSymbs.Add(token.Value);
                        }
                        break;
                    case "loLabel":
                        symb = GetSymbolValue(token.Value);
                        if (symb != null)
                        {
                            expr = $"{expr} {GetLowByte((ushort)symb.Value)}";
                        }
                        else
                        {
                            exprRes.undefinedSymbs.Add(token.Value);
                        }
                        break;
                    case "hiLabel":
                        symb = GetSymbolValue(token.Value);
                        if (symb != null)
                        {
                            expr = $"{expr} {GetHighByte((ushort)symb.Value)}";
                        }
                        else
                        {
                            exprRes.undefinedSymbs.Add(token.Value);
                        }
                        break;
                    case "loHW":
                        expr = $"{expr} {GetLowByte(ushort.Parse(token.Value, NumberStyles.HexNumber))}";
                        break;
                    case "hiHW":
                        expr = $"{expr} {GetHighByte(ushort.Parse(token.Value, NumberStyles.HexNumber))}";
                        break;
                    default:
                        expr = $"{expr} {token.Value}";
                        break;
                }
            }
            if (exprRes.undefinedSymbs.Count == 0)
            {
                Symbol constant = new Symbol();
                DataTable dt = new DataTable();
                exprRes.Result = dt.Compute(expr, "");
                exprRes.Type = exprRes.Result <= 255 ? SymbolType.BYTE : SymbolType.WORD;
                if (addrMode == CPUDef.AddrModes.REL && exprRes.Type == SymbolType.WORD)
                {
                    int delta = (ushort)exprRes.Result - (currentAddr + 1);
                    byte res;
                    if (delta > 127 || delta < -128)
                    {
                        AddError(Errors.REL_JUMP);
                    }
                    else
                    {
                        if (delta < 0)
                        {
                            res = (byte)(255 + delta);
                        }
                        else
                        {
                            res = (byte)(delta - 1);
                        }
                        exprRes.Result = res;
                        exprRes.Type = SymbolType.BYTE;
                    }
                }
            }
            return exprRes;
        }
        private static void InstructionHandler(Match lineReg)
        {
            string opcode = lineReg.Groups["opcode"].Value;
            string operands = lineReg.Groups["operands"].Value;
            if (macros.ContainsKey(opcode))
            {
                Match macroReg = Regex.Match($"{opcode} {operands}", CPUDef.macroReg);
                CallMacroHandler(macroReg);
                return;
            }
            string label = lineReg.Groups["label"].Value;

            ushort opcodeAddr = currentAddr;
            opcode = lineReg.Groups["opcode"].Value.ToUpper();
            if (!string.IsNullOrWhiteSpace(label))
            {
                Symbol lSymb = new Symbol { Type = SymbolType.WORD, Value = currentAddr };
                AddSymbol(label, lSymb);
            }

            Byte[] addrModesValues = CPUDef.OPC_TABLE[opcode];
            CPUDef.AddrModes addrMode = CPUDef.AddrModes.NO;
            List<byte> instBytes = new List<byte>();
            CPUDef.InstructionInfo instInfo = new CPUDef.InstructionInfo();
            bool syntaxError = true;
            if (string.IsNullOrWhiteSpace(operands))
            {
                syntaxError = false;
                // 1 byte opcode                
                if (Array.Exists(CPUDef.ACC_OPC, opc => opc.Equals(opcode)))
                {
                    addrMode = CPUDef.AddrModes.ACC;
                }
                else
                {
                    addrMode = CPUDef.AddrModes.IMP;
                }
                instInfo = new CPUDef.InstructionInfo { addrMode = addrMode, nbrBytes = 1 };
                instBytes.Add(addrModesValues[(int)addrMode]);
            }
            else
            {
                string expr = string.Empty;
                // 2 bytes opcode
                if (Array.Exists(CPUDef.REL_OPC, opc => opc.Equals(opcode)))
                {
                    syntaxError = false;
                    expr = operands;
                    instInfo = new CPUDef.InstructionInfo { addrMode = CPUDef.AddrModes.REL, nbrBytes = 2 };
                }
                else
                {
                    instInfo = CPUDef.GetInstructionInfo(operands);
                    syntaxError = false;
                    expr = instInfo.expr;


                }
                if (syntaxError)
                {
                    AddError(Errors.OPERANDS);
                }
                else
                {
                    addrMode = instInfo.addrMode;
                    ExprResult exprRes = ResolveExpr(expr, addrMode);
                    if (exprRes.undefinedSymbs.Count == 0)
                    {
                        // convert to zero page if symbol is a single byte
                        if (CPUDef.isAbsoluteAddr(addrMode) && exprRes.Type == SymbolType.BYTE)
                        {
                            addrMode = addrMode + 3;
                            instInfo.nbrBytes = 2;
                        }
                        instBytes.Add(addrModesValues[(int)addrMode]);
                        if (instInfo.nbrBytes == 2)
                        {
                            instBytes.Add((byte)exprRes.Result);
                        }
                        else
                        {
                            instBytes.AddRange(GetWordBytes((ushort)exprRes.Result));
                        }
                    }
                    else
                    {
                        instBytes.Add(addrModesValues[(int)addrMode]);
                        SymbolType typeExpr;
                        if (instInfo.nbrBytes == 2)
                        {
                            instBytes.AddRange(new byte[1] { 0 });
                            typeExpr = SymbolType.BYTE;
                        }
                        else // 3 bytes instr
                        {
                            instBytes.AddRange(new byte[2] { 0, 0 });
                            typeExpr = SymbolType.WORD;
                        }

                        ushort position = (ushort)(opcodeAddr - originAddr + 1);
                        UnresolvedExpr exprObj = new UnresolvedExpr
                        {
                            Position = position,
                            Type = typeExpr,
                            addrMode = addrMode,
                            NbrUndefinedSymb = exprRes.undefinedSymbs.Count,
                            Expr = expr
                        };
                        unsolvedExprList.Add(position, exprObj);

                        foreach (string symb in exprRes.undefinedSymbs)
                        {
                            UnresolvedSymbol unResSymb = new UnresolvedSymbol();
                            unResSymb.DependingList = new List<string>();
                            unResSymb.ExprList = new List<ushort>();
                            unResSymb.ExprList.Add(position);
                            AddUnsolvedSymbol(symb, unResSymb);
                        }
                    }
                }
            }
            if (!syntaxError)
            {
                currentAddr += instInfo.nbrBytes;
                fileOutMemory.AddRange(instBytes.ToArray());
                MainConsole.WriteLine($"{opcode} mode {addrMode.ToString()}");
            }
        }

        private static void MemResHandler(Match lineReg)
        {
            string label = lineReg.Groups["label"].Value;
            string value = lineReg.Groups["value"].Value;
            ExprResult res = ResolveExpr(value, CPUDef.AddrModes.NO);
            if (res.undefinedSymbs.Count == 0)
            {
                MemArea ma;
                Dictionary<string, Symbol> symbTable;
                if (localScope.isLocalScope)
                {
                    ma = localScope.memArea;
                    symbTable = localScope.symbolTable;
                }
                else
                {
                    ma = memArea;
                    symbTable = symbolTable;
                }
                Symbol variable = new Symbol() { Type = ma.type, Value = ma.val };
                if (!symbTable.ContainsKey(label))
                {
                    AddSymbol(label, variable);
                    ma.val += res.Result;
                    ma.type = ma.val <= 255 ? SymbolType.BYTE : SymbolType.WORD;
                    MainConsole.WriteLine($"{label} {variable.Value.ToString("x")}");
                }
                else
                {
                    AddError(Errors.LABEL_EXISTS);
                }
            }
            else
            {
                AddError(Errors.UNDEFINED_SYMBOL);
            }
        }

        private static void CallMacroHandler(Match lineReg)
        {
            string macroName = lineReg.Groups["label"].Value;
            string value = lineReg.Groups["value"].Value;

            if (!macros.ContainsKey(macroName))
            {
                AddError(Errors.MACRO_NOT_EXISTS);
                return;
            }
            MacroDef macroDef = macros[macroName];
            if (!string.IsNullOrEmpty(value))
            {
                string[] paramValues = Regex.Replace(value, @"\s+", "").Split(',');
                foreach (string line in macroDef.lines)
                {
                    string finalLine = line;
                    for (int i = 0; i < paramValues.Length; i++)
                    {
                        string paramValue = paramValues[i];
                        string paramName = macroDef.listParam[i];
                        finalLine = finalLine.Replace(paramName, paramValue);
                    }
                    ParseLine(finalLine, finalLine);
                }
            }
            else
            {
                if (macroDef.listParam.Length > 0)
                {
                    AddError(Errors.MACRO_CALL_WITHOUT_PARAMS);
                    return;
                }
                foreach (string line in macroDef.lines)
                {
                    ParseLine(line, line);
                }
            }
        }

        private static void AddUnsolvedSymbol(string unsolvedLabel, UnresolvedSymbol unresSymb)
        {
            Dictionary<string, UnresolvedSymbol> unsolvedSymbs;
            if (localScope.isLocalScope)
            {
                unsolvedSymbs = localScope.unsolvedSymbols;
            }
            else
            {
                unsolvedSymbs = unsolvedSymbols;
            }
            if (unsolvedSymbs.ContainsKey(unsolvedLabel))
            {
                UnresolvedSymbol unsolved = unsolvedSymbs[unsolvedLabel];
                foreach (string dep in unresSymb.DependingList)
                {
                    if (!unsolved.DependingList.Contains(dep))
                    {
                        unsolved.DependingList.Add(dep);
                    }
                }
                foreach (ushort expr in unresSymb.ExprList)
                {
                    if (!unsolved.ExprList.Contains(expr))
                    {
                        unsolved.ExprList.Add(expr);
                    }
                }
                if (!string.IsNullOrEmpty(unresSymb.Expr))
                {
                    unsolved.Expr = unresSymb.Expr;
                }
                unsolvedSymbs[unsolvedLabel] = unsolved;
            }
            else
            {
                unsolvedSymbs.Add(unsolvedLabel, unresSymb);
            }
        }

        private static Symbol GetSymbolValue(string symbol)
        {
            Dictionary<string, Symbol> symbTable = null;
            if (localScope.isLocalScope && localScope.symbolTable.ContainsKey(symbol))
            {
                symbTable = localScope.symbolTable;
            }
            else
            {
                symbTable = symbolTable;
            }
            if (symbTable.ContainsKey(symbol))
            {
                return symbTable[symbol];
            }
            return null;
        }

        private static void LabelHandler(Match lineReg)
        {
            string label = lineReg.Groups["label"].Value;
            Symbol symb = new Symbol { Value = currentAddr, Type = SymbolType.WORD };
            AddSymbol(label, symb);
        }

        private static void ResolveSymbol(string label)
        {
            Dictionary<string, UnresolvedSymbol> unsolvedSymbs;
            if (localScope.isLocalScope)
            {
                unsolvedSymbs = localScope.unsolvedSymbols;
            }
            else
            {
                unsolvedSymbs = unsolvedSymbols;
            }
            if (!unsolvedSymbs.ContainsKey(label))
            {
                return;
            }
            UnresolvedSymbol unresSymb = unsolvedSymbs[label];
            ResolveSymbolDepsAndExprs(unresSymb);
            unsolvedSymbs.Remove(label);
        }

        private static void ResolveSymbolDepsAndExprs(UnresolvedSymbol unresSymb)
        {
            Dictionary<string, UnresolvedSymbol> unsolvedSymbs;
            if (localScope.isLocalScope)
            {
                unsolvedSymbs = localScope.unsolvedSymbols;
            }
            else
            {
                unsolvedSymbs = unsolvedSymbols;
            }
            // resolve depending symbols
            foreach (string dep in unresSymb.DependingList)
            {
                UnresolvedSymbol unresDep = unsolvedSymbs[dep];
                unresDep.NbrUndefinedSymb--;
                if (unresDep.NbrUndefinedSymb <= 0)
                {
                    ExprResult res = ResolveExpr(unresDep.Expr, CPUDef.AddrModes.NO);
                    AddSymbol(dep, new Symbol()
                    {
                        Value = res.Result,
                        Type = res.Type
                    });
                }
            }
            // resolve expressions
            foreach (ushort expr in unresSymb.ExprList)
            {
                UnresolvedExpr unresExp = unsolvedExprList[expr];
                unresExp.NbrUndefinedSymb--;
                if (unresExp.NbrUndefinedSymb <= 0)
                {
                    ExprResult res = ResolveExpr(unresExp.Expr, unresExp.addrMode);
                    GenerateExprBytes(unresExp, res);
                    unsolvedExprList.Remove(expr);
                }
            }
        }

        private static void GenerateExprBytes(UnresolvedExpr expr, ExprResult exprRes)
        {
            byte[] bytes = null;
            switch (expr.Type)
            {
                case SymbolType.WORD:
                    if (exprRes.Type == SymbolType.BYTE && !CPUDef.isAbsoluteAddr(expr.addrMode) && (expr.addrMode != CPUDef.AddrModes.IND))
                    {
                        bytes = new byte[1] { (byte)exprRes.Result };
                    }
                    else
                    {
                        if (expr.addrMode == CPUDef.AddrModes.REL)
                        {
                            int delta = (ushort)exprRes.Result - (expr.Position + originAddr);
                            byte res;
                            if (delta > 127 || delta < -128)
                            {
                                AddError(Errors.REL_JUMP);
                            }
                            else
                            {
                                if (delta < 0)
                                {
                                    res = (byte)(255 + delta);
                                }
                                else
                                {
                                    res = (byte)(delta - 1);
                                }
                                bytes = new byte[1] { res };
                            }
                        }
                        else
                        {
                            bytes = GetWordBytes((ushort)exprRes.Result);
                        }
                    }
                    break;
                case SymbolType.BYTE:
                    bytes = new byte[1] { (byte)exprRes.Result };
                    break;
            }
            if (bytes != null)
            {
                fileOutMemory.RemoveRange(expr.Position, bytes.Length);
                fileOutMemory.InsertRange(expr.Position, bytes);
            }
        }
        public static void ProcessLine(Match lineReg, string type)
        {
            DelHandler handler = mapLineHandlers[type];
            handler(lineReg);
        }

        public static void AddError(string type)
        {
            errorList.Add(new Error(file.currentLineNumber, file.sourceFile, type));
        }

        public static void Assemble()
        {
            unsolvedSymbols = new Dictionary<string, UnresolvedSymbol>();
            unsolvedExprList = new Dictionary<ushort, UnresolvedExpr>();
            symbolTable = new Dictionary<string, Symbol>();
            localScope = new LocalScope()
            {
                symbolTable = new Dictionary<string, Symbol>(),
                unsolvedSymbols = new Dictionary<string, UnresolvedSymbol>(),
                isLocalScope = false,
                memArea = new MemArea()
            };
            fileOutMemory = new List<byte>();
            errorList = new List<Error>();
            fileStack = new Stack<FileInfo>();
            memArea = new MemArea();
            macros = new Dictionary<string, MacroDef>();
            startMacroDef = false;
            cAsm = new ConditionalAsm()
            {
                inCondition = false,
                val = false
            };

            Boolean contextError = false;
            if (sourceFile == null)
            {
                contextError = true;
                Console.Error.WriteLine("undefined Source file");
            }
            if (objectFileName == null)
            {
                contextError = true;
                Console.Error.WriteLine("undefined object file");

            }
            if (contextError)
            {
                Console.Error.WriteLine("\nTape help to learn more about the tool");
                return;
            }
            OpenFiles();
            Process();
            ResolveSymbols();
            bw.Write(fileOutMemory.ToArray());
            CloseFiles();
            ExportUnsolvedFile();
            ExportSymbolTable();
            DisplayErrors();

        }

        private static void ExportSymbolTable()
        {
            if (symbolTable.Count > 0)
            {
                File.WriteAllText(objectFileName + "_Symbol.txt", JsonConvert.SerializeObject(symbolTable));
            }
        }

        private static void ExportUnsolvedFile()
        {
            if (unsolvedSymbols.Count > 0)
            {
                File.WriteAllText(objectFileName + "_Unsolved.txt", JsonConvert.SerializeObject(unsolvedSymbols));
            }
        }

        public static void ResolveSymbols()
        {
            List<string> resolved = new List<string>();
            foreach (string symbName in unsolvedSymbols.Keys)
            {
                if (!symbolTable.ContainsKey(symbName))
                {
                    continue;
                }
                UnresolvedSymbol unresSymb = unsolvedSymbols[symbName];
                ResolveSymbolDepsAndExprs(unresSymb);
                resolved.Add(symbName);
            }
            foreach (string symbName in resolved)
            {
                unsolvedSymbols.Remove(symbName);
            }
        }

        private static void Process()
        {
            file = new FileInfo();
            file.fp = new StreamReader(sourceFile);
            file.sourceFile = sourceFile;

            fileStack.Push(file);
            while (fileStack.Count > 0)
            {
                file = fileStack.Pop();
                string line;
                file.currentLineNumber = -1;
                while ((line = file.fp.ReadLine()) != null)
                {
                    file.currentLineNumber++;
                    if (cAsm.inCondition && !cAsm.val)
                    {
                        MainConsole.WriteLine(string.Format("{0}   --- {1}", line, "NOT Assembled"));
                        continue;
                    }
                    string originalLine = line;
                    line = Regex.Replace(line, ";(.)*", "").Trim();
                    if (String.IsNullOrWhiteSpace(line))
                    {
                        MainConsole.WriteLine(originalLine);
                        continue;
                    }
                    if (startMacroDef && !line.ToLower().Equals(".endmacro"))
                    {
                        macros[currentMacro].lines.Add(line);
                        MainConsole.WriteLine(string.Format("{0}   --- {1}", originalLine, "MACRO DEF"));
                    }
                    else
                    {
                        ParseLine(line, originalLine);
                    }
                }
                file.fp.Close();
            }
        }

        private static void ParseLine(string line, string originalLine)
        {
            bool syntaxError = true;
            foreach (KeyValuePair<string, string> entry in CPUDef.regMap)
            {
                Match match = Regex.Match(line, entry.Key);
                if (match.Success)
                {
                    syntaxError = false;
                    MainConsole.WriteLine(string.Format("{0}   --- {1}", originalLine, entry.Value));
                    ProcessLine(match, entry.Value);
                    break;
                }
            }
            if (syntaxError)
            {
                AddError(Errors.SYNTAX);
            }
        }

        private static void DisplayErrors()
        {
            if (errorList.Count > 0)
            {
                Console.Error.WriteLine("****************************************** Errors ******************************************");
                foreach (Error err in errorList)
                {
                    Console.Error.WriteLine($"Line {err.line}   - Type {err.type}");
                }
                Console.Error.WriteLine("********************************************************************************************");
            }
        }

        private static void OpenFiles()
        {
            stream = new FileStream(objectFileName, FileMode.Create);
            bw = new BinaryWriter(stream);
        }

        public static void CloseFiles()
        {
            if (bw != null)
            {
                bw.Flush();
                bw.Close();
            }
        }

        private static byte[] GetWordBytes(string strWord)
        {
            ushort word = ushort.Parse(strWord, NumberStyles.HexNumber);
            return GetWordBytes(word);
        }

        private static byte[] GetWordBytes(ushort word)
        {
            byte lower = GetLowByte(word);
            byte upper = GetHighByte(word);
            return new byte[2] { lower, upper };
        }

        private static byte GetLowByte(ushort word)
        {
            return (byte)(word & 0xff);
        }

        private static byte GetHighByte(ushort word)
        {
            return (byte)(word >> 8);
        }

        private static void AddSymbol(string label, Symbol symb)
        {
            Dictionary<string, Symbol> symbTable;
            if (localScope.isLocalScope)
            {
                symbTable = localScope.symbolTable;
            }
            else
            {
                symbTable = symbolTable;
            }
            if (symbTable.ContainsKey(label))
            {
                AddError(Errors.LABEL_EXISTS);
            }
            else
            {
                symbTable.Add(label, symb);
                ResolveSymbol(label);
            }
        }
    }
    public struct Error
    {
        public int line { get; set; }
        public string sourceFile { get; set; }
        public string type { get; set; }
        public Error(int line, string sourceFile, string type)
        {
            this.line = line;
            this.type = type;
            this.sourceFile = sourceFile;
        }
    }

    public struct Token
    {
        public string Type { get; set; }
        public string Value { get; set; }
    }

    public struct ExprResult
    {
        public dynamic Result { get; set; }
        public List<string> undefinedSymbs { get; set; }
        public SymbolType Type { get; set; }
    }

    public class Symbol
    {
        public dynamic Value { get; set; }
        public SymbolType Type { get; set; }
    }

    public enum SymbolType
    {
        BYTE = 0,
        WORD = 1
    }

    public struct Errors
    {
        public static string LABEL_EXISTS = "Label already declared";
        public static string REL_JUMP = "Relative jump is too big";
        public static string SYNTAX = "Syntax Error";
        public static string FILE_NOT_EXISTS = "File doesn't exist";
        public static string DATA_BYTE = "Error in insert data byte";
        public static string DATA_WORD = "Error in insert data word";
        public static string DATA_TYPE = "Error in data type";
        public static string MACRO_EXISTS = "Macro with the same name already defined";
        public static string MACRO_NOT_EXISTS = "Undefined Macro";
        public static string MACRO_CALL_WITHOUT_PARAMS = "Macro called without params";
        public static string OPERANDS = "Error in operands";
        public static string UNDEFINED_SYMBOL = "Undefined symbol";
    }

    public struct UnresolvedSymbol
    {
        public List<string> DependingList { get; set; }
        public List<ushort> ExprList { get; set; }
        public string Expr { get; set; }
        public int NbrUndefinedSymb { get; set; }
    }

    public struct UnresolvedExpr
    {
        public string Expr { get; set; }
        public int NbrUndefinedSymb { get; set; }
        public ushort Position { get; set; }
        public SymbolType Type { get; set; }
        public CPUDef.AddrModes addrMode { get; set; }
    }

    public struct FileInfo
    {
        public StreamReader fp { get; set; }
        public string sourceFile { get; set; }
        public int currentLineNumber { get; set; }
    }

    public struct MemArea
    {
        public SymbolType type;
        public dynamic val;
    }
    public struct MacroDef
    {
        public string[] listParam;
        public List<string> lines;
    }

    public struct ConditionalAsm
    {
        public bool val;
        public bool inCondition;
    }

    public struct LocalScope
    {
        public Dictionary<string, Symbol> symbolTable;
        public Dictionary<string, UnresolvedSymbol> unsolvedSymbols;
        public bool isLocalScope;
        public MemArea memArea;
    }
}
