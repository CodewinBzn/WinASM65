// Abdelghani BOUZIANE    
// 2021

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace WinASM65
{
    public class Assembler
    {
        public static List<byte> FileOutMemory { get; set; }
        private static List<Error> _errorList;
        private static Stream _stream;
        private static BinaryWriter _bw = null;
        public static string ObjectFileName { get; set; }
        public delegate void DelHandler(Match lineReg);
        private static readonly Dictionary<string, DelHandler> MapLineHandlers = new Dictionary<string, DelHandler>
        {
            {CPUDef.StartLocalScope, StartLocalScopeHandler },
            {CPUDef.EndLocalScope, EndLocalScopeHandler },
            {CPUDef.Label, LabelHandler },
            {CPUDef.Directive, DirectiveHandler },
            {CPUDef.Instruction, InstructionHandler },
            {CPUDef.Constant, ConstantHandler },
            {CPUDef.MemReserve, MemResHandler },
            {CPUDef.CallMacro, CallMacroHandler }
        };
        private static ushort _currentAddr;
        private static ushort _originAddr;
        private delegate void DelDirectiveHandler(string value);
        public static string SourceFile { get; set; }
        public static Dictionary<ushort, UnresolvedExpr> UnsolvedExprList { get; set; }
        private static Dictionary<string, MacroDef> _macros;
        private static Stack<FileInfo> _fileStack;
        private static FileInfo _filePtr;
        public static FileInfo FilePtr
        {
            get
            {
                return _filePtr;
            }
            set
            {
                _filePtr = value;
            }
        }
        private static bool _startMacroDef;
        private static string _currentMacro;
        private static ConditionalAsm _cAsm;
        public static LexicalScope LexicalScope { get; set; }
        private static RepBlock _repBlock;
        private static bool _stopAssembling;

        private static void AddNewLexicalScopeData()
        {
            LexicalScope.LexicalScopeDataList.Add(new LexicalScopeData
            {
                SymbolTable = new Dictionary<string, dynamic>(),
                UnsolvedSymbols = new Dictionary<string, UnresolvedSymbol>(),
                MemArea = 0
            });
        }
        private static void StartLocalScopeHandler(Match lineReg)
        {
            if (LexicalScope.Level == byte.MaxValue)
            {
                AddError(Errors.MAX_LOCAL_SCOPE);
                return;
            }
            LexicalScope.Level++;
            AddNewLexicalScopeData();
        }

        private static void EndLocalScopeHandler(Match lineReg)
        {
            if (LexicalScope.Level == 0)
            {
                AddError(Errors.NO_LOCAL_SCOPE);
                return;
            }
            Dictionary<string, UnresolvedSymbol> currentUnsolvedSymbols = LexicalScope.LexicalScopeDataList[LexicalScope.Level].UnsolvedSymbols;
            Dictionary<string, UnresolvedSymbol> parentUnsolvedSymbols = LexicalScope.LexicalScopeDataList[LexicalScope.Level - 1].UnsolvedSymbols;
            foreach (string symb in currentUnsolvedSymbols.Keys)
            {
                if (LexicalScope.LexicalScopeDataList[LexicalScope.Level - 1].UnsolvedSymbols.ContainsKey(symb))
                {
                    UnresolvedSymbol glUnsolvedSymbol = parentUnsolvedSymbols[symb];
                    UnresolvedSymbol localUnsolvedSymbol = currentUnsolvedSymbols[symb];
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
                    parentUnsolvedSymbols[symb] = glUnsolvedSymbol;
                }
                else
                {
                    UnresolvedSymbol localUnsolvedSymbol = currentUnsolvedSymbols[symb];
                    localUnsolvedSymbol.Expr = null;
                    parentUnsolvedSymbols.Add(symb, localUnsolvedSymbol);
                }

            }
            LexicalScope.LexicalScopeDataList.RemoveAt(LexicalScope.Level);
            LexicalScope.Level--;
        }

        #region directives
        private static readonly Dictionary<string, DelDirectiveHandler> DirectiveHandlersMap = new Dictionary<string, DelDirectiveHandler>
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
            { ".if", IfHandler },
            { ".else", ElseHandler },
            { ".endif", EndIfHandler },
            { ".rep", RepHandler },
            { ".endrep", EndRepHandler },
            { ".end", EndHandler },
        };

        private static void IfHandler(string value)
        {
            if (_cAsm.Level == byte.MaxValue)
            {
                AddError(Errors.NESTED_CONDITIONAL_ASSEMBLY);
                return;
            }

            ExprResult res = ResolveExpr(value.Trim(), CPUDef.AddrModes.NO, true);
            if (res.UndefinedSymbs.Count > 0)
            {
                AddError(Errors.UNDEFINED_SYMBOL);
            }
            else
            {
                if (_cAsm.InCondition)
                {
                    _cAsm.Level++;
                }
                else
                {
                    _cAsm.InCondition = true;
                }
                _cAsm.Values.Add((bool)res.Result);
            }
        }

        private static void IfDefHandler(string value)
        {
            if (_cAsm.Level == byte.MaxValue)
            {
                AddError(Errors.NESTED_CONDITIONAL_ASSEMBLY);
                return;
            }
            if (_cAsm.InCondition)
            {
                _cAsm.Level++;
            }
            else
            {
                _cAsm.InCondition = true;
            }
            string label = value.Trim();
            _cAsm.Values.Add(GetSymbolValue(label) != null);
        }

        private static void IfnDefHandler(string value)
        {
            if (_cAsm.Level == byte.MaxValue)
            {
                AddError(Errors.NESTED_CONDITIONAL_ASSEMBLY);
                return;
            }
            if (_cAsm.InCondition)
            {
                _cAsm.Level++;
            }
            else
            {
                _cAsm.InCondition = true;
            }
            string label = value.Trim();
            _cAsm.Values.Add(GetSymbolValue(label) == null);
        }
        private static void ElseHandler(string value)
        {
            if (!_cAsm.InCondition)
            {
                AddError(Errors.NO_CONDITIONAL_ASSEMBLY);
                return;
            }
            _cAsm.Values[_cAsm.Level] = !_cAsm.Values[_cAsm.Level];
        }

        private static void EndIfHandler(string value)
        {
            if (!_cAsm.InCondition)
            {
                AddError(Errors.NO_CONDITIONAL_ASSEMBLY);
                return;
            }
            _cAsm.Values.RemoveAt(_cAsm.Level);
            if (_cAsm.Values.Count == 0)
            {
                _cAsm.InCondition = false;
            }
            if (_cAsm.Level > 0)
            {
                _cAsm.Level--;
            }
        }

        private static void StartMacroDefHandler(string value)
        {
            if (_startMacroDef)
            {
                AddError(Errors.NESTED_MACROS);
                return;
            }

            Match mReg = CPUDef.MacroReg.Match(value);
            string macroName = _currentMacro = mReg.Groups["label"].Value;
            string defParts = mReg.Groups["value"].Value;
            MacroDef macroDef = new MacroDef
            {
                Lines = new List<string>()
            };

            _startMacroDef = true;
            if (!string.IsNullOrEmpty(defParts))
            {
                macroDef.ListParam = Regex.Replace(defParts, @"\s+", "").Split(',');
            }
            else
            {
                macroDef.ListParam = new string[] { };
            }
            if (_macros.ContainsKey(macroName))
            {
                AddError(Errors.MACRO_EXISTS);
            }
            else
            {
                _macros.Add(macroName, macroDef);
            }
        }

        private static void EndMacroDefHandler(string value)
        {
            if (!_startMacroDef)
            {
                AddError(Errors.NO_MACRO);
                return;
            }
            _startMacroDef = false;
            _currentMacro = string.Empty;
        }

        private static void RepHandler(string value)
        {
            if (_repBlock.IsInRepBlock)
            {
                AddError(Errors.NESTED_REP);
                return;
            }
            _repBlock.IsInRepBlock = true;
            _repBlock.Lines = new List<string>();
            ExprResult res = ResolveExpr(value.Trim());
            if (res.UndefinedSymbs.Count > 0)
            {
                AddError(Errors.UNDEFINED_SYMBOL);
                return;
            }
            _repBlock.Counter = (int)res.Result;
        }

        private static void EndRepHandler(string value)
        {
            if (!_repBlock.IsInRepBlock)
            {
                AddError(Errors.NO_REP);
                return;
            }
            Listing.EndLine();
            _repBlock.IsInRepBlock = false;
            for (int i = 0; i < _repBlock.Counter; i++)
            {
                foreach (string line in _repBlock.Lines)
                {
                    Listing.PrintLine(line);
                    ParseLine(line, line);
                }
            }
        }

        private static void EndHandler(string value)
        {
            _stopAssembling = true;
        }

        private static void DirectiveHandler(Match lineReg)
        {
            string directive = lineReg.Groups["directive"].Value.ToLower();
            string value = lineReg.Groups["value"].Value;
            if (DirectiveHandlersMap.ContainsKey(directive))
            {
                DelDirectiveHandler handler = DirectiveHandlersMap[directive];
                handler(value);
            }
        }

        private static void OrgHandler(string value)
        {
            ExprResult res = ResolveExpr(value);
            if (res.UndefinedSymbs.Count == 0)
            {
                _currentAddr = _originAddr = (ushort)res.Result;
                Listing.PrintLine(LineType.ORG, _currentAddr);
            }
            else
            {
                AddError(Errors.UNDEFINED_SYMBOL);
            }
        }

        private static void MemAreaHandler(string value)
        {
            LexicalScopeData currentScopeData = LexicalScope.LexicalScopeDataList[LexicalScope.Level];
            ExprResult res = ResolveExpr(value);
            if (res.UndefinedSymbs.Count == 0)
            {
                currentScopeData.MemArea = (ushort)res.Result;
            }
            else
            {
                AddError(Errors.UNDEFINED_SYMBOL);
            }
        }

        private static void IncBinHandler(string fileName)
        {
            fileName = fileName.Replace("\"", String.Empty);
            string directoryName = Path.GetDirectoryName(_filePtr.SourceFile);
            string toInclude = string.IsNullOrEmpty(directoryName) ? fileName : directoryName + '/' + fileName;
            if (System.IO.File.Exists(toInclude))
            {
                byte[] bytesToInc = System.IO.File.ReadAllBytes(toInclude);
                FileOutMemory.AddRange(bytesToInc);
                _currentAddr += (ushort)bytesToInc.Length;
                Listing.PrintLine(LineType.INST, bytesToInc.Length);
            }
            else
            {
                AddError(Errors.FILE_NOT_EXISTS);
            }
        }
        private static void IncludeHandler(string fileName)
        {
            fileName = fileName.Replace("\"", String.Empty);
            string directoryName = Path.GetDirectoryName(_filePtr.SourceFile);
            string toInclude = string.IsNullOrEmpty(directoryName) ? fileName : directoryName + '/' + fileName;
            if (System.IO.File.Exists(toInclude))
            {
                _fileStack.Push(_filePtr);
                _filePtr = new FileInfo() { FileStreamReader = new StreamReader(toInclude), SourceFile = toInclude };
            }
            else
            {
                AddError(Errors.FILE_NOT_EXISTS);
            }
        }

        private static void DataByteHandler(string bytesIn)
        {
            string[] bytes = bytesIn.Split(',');
            ushort startAddr = _currentAddr;
            foreach (string db in bytes)
            {
                string data = db.Trim();
                if (CPUDef.IsString(data)) // handle strings 
                {
                    data = data.Substring(1, data.Length - 2);
                    foreach (char c in data)
                    {
                        FileOutMemory.Add(Convert.ToByte(c));
                        _currentAddr++;
                    }
                }
                else
                {
                    ExprResult res = ResolveExpr(data);
                    if (res.UndefinedSymbs.Count == 0)
                    {
                        FileOutMemory.Add((byte)res.Result);
                    }
                    else
                    {

                        FileOutMemory.Add(0);
                        ushort position = (ushort)(_currentAddr - _originAddr);
                        UnresolvedExpr expr = new UnresolvedExpr
                        {
                            Position = position,
                            Type = SymbolType.BYTE,
                            AddrMode = CPUDef.AddrModes.NO,
                            NbrUndefinedSymb = res.UndefinedSymbs.Count,
                            Expr = data
                        };

                        UnsolvedExprList.Add(position, expr);

                        foreach (string symb in res.UndefinedSymbs)
                        {
                            UnresolvedSymbol unResSymb = new UnresolvedSymbol
                            {
                                DependingList = new List<string>(),
                                ExprList = new List<ushort>()
                            };
                            unResSymb.ExprList.Add(position);
                            AddUnsolvedSymbol(symb, unResSymb);
                        }
                    }

                    _currentAddr++;
                }
            }
            Listing.PrintLine(LineType.INST, _currentAddr - startAddr);
        }

        private static void DataWordHandler(string wordsIn)
        {
            string[] words = wordsIn.Split(',');
            Listing.PrintLine(LineType.INST, words.Length * 2);
            foreach (string dw in words)
            {
                string data = dw.Trim();
                ExprResult res = ResolveExpr(data);
                if (res.UndefinedSymbs.Count == 0)
                {
                    FileOutMemory.AddRange(GetWordBytes((ushort)res.Result));
                }
                else
                {
                    FileOutMemory.AddRange(new byte[2] { 0, 0 });
                    ushort position = (ushort)(_currentAddr - _originAddr);
                    UnresolvedExpr expr = new UnresolvedExpr
                    {
                        Position = position,
                        Type = SymbolType.WORD,
                        AddrMode = CPUDef.AddrModes.NO,
                        NbrUndefinedSymb = res.UndefinedSymbs.Count,
                        Expr = data
                    };

                    UnsolvedExprList.Add(position, expr);
                    foreach (string symb in res.UndefinedSymbs)
                    {
                        UnresolvedSymbol unResSymb = new UnresolvedSymbol
                        {
                            DependingList = new List<string>(),
                            ExprList = new List<ushort>()
                        };
                        unResSymb.ExprList.Add(position);
                        AddUnsolvedSymbol(symb, unResSymb);
                    }
                }
                _currentAddr += 2;
            }
        }

        #endregion
        private static void ConstantHandler(Match lineReg)
        {
            string label = lineReg.Groups["label"].Value;
            string value = lineReg.Groups["value"].Value;
            ExprResult res = ResolveExpr(value);
            if (res.UndefinedSymbs.Count == 0)
            {
                AddSymbol(label, res.Result, true);
                Listing.PrintLine(LineType.CONST, (int)res.Result);
            }
            else
            {
                UnresolvedSymbol unResSymb = new UnresolvedSymbol
                {
                    NbrUndefinedSymb = res.UndefinedSymbs.Count,
                    DependingList = new List<string>(),
                    Expr = value,
                    ExprList = new List<ushort>()
                };

                foreach (string symb in res.UndefinedSymbs)
                {
                    AddDependingSymb(symb, label);
                }

                AddUnsolvedSymbol(label, unResSymb);
            }
        }

        private static void AddDependingSymb(string symbol, string dependingSymb)
        {
            Dictionary<string, UnresolvedSymbol> unsolvedSymbs = LexicalScope.LexicalScopeDataList[LexicalScope.Level].UnsolvedSymbols;
            if (unsolvedSymbs.ContainsKey(symbol))
            {
                unsolvedSymbs[symbol].DependingList.Add(dependingSymb);
            }
            else
            {
                UnresolvedSymbol unsolved = new UnresolvedSymbol
                {
                    DependingList = new List<string>()
                };
                unsolved.DependingList.Add(dependingSymb);
                unsolved.ExprList = new List<ushort>();
                unsolvedSymbs.Add(symbol, unsolved);
            }
        }
        private static ExprResult ResolveExpr(string exprIn, CPUDef.AddrModes addrMode = CPUDef.AddrModes.NO, bool isLogical = false)
        {
            List<Token> tokens = Tokenizer.Tokenize(exprIn);
            string expr = string.Empty;
            dynamic symb;
            ExprResult exprRes = new ExprResult
            {
                UndefinedSymbs = new List<string>()
            };
            foreach (Token token in tokens)
            {
                switch (token.Type)
                {
                    case "CHAR":
                        expr = $"{expr} {Convert.ToByte(token.Value[0])}";
                        break;
                    case "HEX":
                        expr = $"{expr} {int.Parse(token.Value, NumberStyles.HexNumber)}";
                        break;
                    case "bin":
                        expr = $"{expr} {Convert.ToInt32(token.Value, 2)}";
                        break;
                    case "DEC":
                        expr = $"{expr} {int.Parse(token.Value)}";
                        break;
                    case "label":
                        symb = GetSymbolValue(token.Value);
                        if (symb != null)
                        {
                            expr = $"{expr} {symb}";
                        }
                        else
                        {
                            exprRes.UndefinedSymbs.Add(token.Value);
                        }
                        break;
                    default:
                        expr = $"{expr} {token.Value}";
                        break;
                }
            }
            if (exprRes.UndefinedSymbs.Count == 0)
            {
                if (isLogical)
                {
                    exprRes.Result = ExprEvaluator.Eval(expr);
                }
                else
                {
                    exprRes.Result = ExprEvaluator.Eval(expr);
                    if (addrMode == CPUDef.AddrModes.REL && exprRes.Result > 255)
                    {
                        int delta = (int)exprRes.Result - (_currentAddr + 1);
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
                        }
                    }
                }
            }
            return exprRes;
        }
        private static void InstructionHandler(Match lineReg)
        {
            string opcode = lineReg.Groups["opcode"].Value;
            string operands = lineReg.Groups["operands"].Value;
            if (_macros.ContainsKey(opcode))
            {
                Match macroReg = CPUDef.MacroReg.Match($"{opcode} {operands}");
                CallMacroHandler(macroReg);
                return;
            }
            string label = lineReg.Groups["label"].Value;

            ushort opcodeAddr = _currentAddr;
            opcode = lineReg.Groups["opcode"].Value.ToUpper();
            if (!string.IsNullOrWhiteSpace(label))
            {
                string tmpLabel = label.ToUpper();
                if (CPUDef.OpcTable.ContainsKey(tmpLabel))
                {
                    operands = lineReg.Groups["opcode"].Value;
                    opcode = tmpLabel;
                }
                else
                {
                    AddSymbol(label, _currentAddr);
                }
            }

            Byte[] addrModesValues = CPUDef.OpcTable[opcode];
            CPUDef.AddrModes addrMode = CPUDef.AddrModes.NO;
            List<byte> instBytes = new List<byte>();
            CPUDef.InstructionInfo instInfo = new CPUDef.InstructionInfo();
            bool syntaxError = true;
            if (string.IsNullOrWhiteSpace(operands))
            {
                syntaxError = false;
                // 1 byte opcode                
                if (Array.Exists(CPUDef.AccOpc, opc => opc.Equals(opcode)))
                {
                    addrMode = CPUDef.AddrModes.ACC;
                }
                else
                {
                    addrMode = CPUDef.AddrModes.IMP;
                }
                instInfo = new CPUDef.InstructionInfo { AddrMode = addrMode, NbrBytes = 1 };
                instBytes.Add(addrModesValues[(int)addrMode]);
            }
            else
            {
                string expr = string.Empty;
                // 2 bytes opcode
                if (Array.Exists(CPUDef.RelOpc, opc => opc.Equals(opcode)))
                {
                    syntaxError = false;
                    expr = operands;
                    instInfo = new CPUDef.InstructionInfo { AddrMode = CPUDef.AddrModes.REL, NbrBytes = 2 };
                }
                else
                {
                    instInfo = CPUDef.GetInstructionInfo(operands);
                    syntaxError = false;
                    expr = instInfo.Expr;


                }
                if (syntaxError)
                {
                    AddError(Errors.OPERANDS);
                }
                else
                {
                    addrMode = instInfo.AddrMode;
                    ExprResult exprRes = ResolveExpr(expr, addrMode);
                    if (exprRes.UndefinedSymbs.Count == 0)
                    {
                        // convert to zero page if symbol is a single byte
                        if (CPUDef.IsAbsoluteAddr(addrMode) && exprRes.Result <= 255 && addrModesValues[(int)addrMode + 3] != 0xff)
                        {
                            addrMode += 3;
                            instInfo.NbrBytes = 2;
                        }
                        instBytes.Add(addrModesValues[(int)addrMode]);
                        if (instInfo.NbrBytes == 2)
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
                        if (instInfo.NbrBytes == 2)
                        {
                            instBytes.AddRange(new byte[1] { 0 });
                            typeExpr = SymbolType.BYTE;
                        }
                        else // 3 bytes instr
                        {
                            instBytes.AddRange(new byte[2] { 0, 0 });
                            typeExpr = SymbolType.WORD;
                        }

                        ushort position = (ushort)(opcodeAddr - _originAddr + 1);
                        UnresolvedExpr exprObj = new UnresolvedExpr
                        {
                            Position = position,
                            Type = typeExpr,
                            AddrMode = addrMode,
                            NbrUndefinedSymb = exprRes.UndefinedSymbs.Count,
                            Expr = expr
                        };
                        UnsolvedExprList.Add(position, exprObj);

                        foreach (string symb in exprRes.UndefinedSymbs)
                        {
                            UnresolvedSymbol unResSymb = new UnresolvedSymbol
                            {
                                DependingList = new List<string>(),
                                ExprList = new List<ushort>
                                {
                                    position
                                }
                            };
                            AddUnsolvedSymbol(symb, unResSymb);
                        }
                    }
                }
            }
            if (!syntaxError)
            {
                _currentAddr += instInfo.NbrBytes;
                FileOutMemory.AddRange(instBytes.ToArray());
                Listing.PrintLine(LineType.INST, instInfo.NbrBytes);
                MainConsole.WriteLine($"{opcode} mode {addrMode}");
            }
        }

        private static void MemResHandler(Match lineReg)
        {
            string label = lineReg.Groups["label"].Value;
            string value = lineReg.Groups["value"].Value;
            ExprResult res = ResolveExpr(value);
            if (res.UndefinedSymbs.Count == 0)
            {
                LexicalScopeData currentScopeData = LexicalScope.LexicalScopeDataList[LexicalScope.Level];
                Dictionary<string, dynamic> symbTable = LexicalScope.LexicalScopeDataList[LexicalScope.Level].SymbolTable;
                dynamic variable = currentScopeData.MemArea;
                if (!symbTable.ContainsKey(label))
                {
                    Listing.PrintLine(LineType.RES, currentScopeData.MemArea);
                    AddSymbol(label, variable);
                    currentScopeData.MemArea += (ushort)res.Result;
                    MainConsole.WriteLine($"{label} {variable.ToString("x")}");
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

            if (!_macros.ContainsKey(macroName))
            {
                AddError(Errors.MACRO_NOT_EXISTS);
                return;
            }
            Listing.EndLine();
            MacroDef macroDef = _macros[macroName];
            if (!string.IsNullOrEmpty(value))
            {
                string[] paramValues = Regex.Replace(value, @"\s+", "").Split(',');
                foreach (string line in macroDef.Lines)
                {
                    string finalLine = line;
                    for (int i = 0; i < paramValues.Length; i++)
                    {
                        string paramValue = paramValues[i];
                        string paramName = macroDef.ListParam[i];
                        finalLine = finalLine.Replace(paramName, paramValue);
                    }
                    Listing.PrintLine(finalLine);
                    ParseLine(finalLine, finalLine);
                }
            }
            else
            {
                if (macroDef.ListParam.Length > 0)
                {
                    AddError(Errors.MACRO_CALL_WITHOUT_PARAMS);
                    return;
                }
                foreach (string line in macroDef.Lines)
                {
                    Listing.PrintLine(line);
                    ParseLine(line, line);
                }
            }
        }

        private static void AddUnsolvedSymbol(string unsolvedLabel, UnresolvedSymbol unresSymb)
        {
            Dictionary<string, UnresolvedSymbol> unsolvedSymbs = LexicalScope.LexicalScopeDataList[LexicalScope.Level].UnsolvedSymbols;
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

        private static dynamic GetSymbolValue(string symbol)
        {
            byte level = LexicalScope.Level;
            while (true)
            {
                if (LexicalScope.LexicalScopeDataList[level].SymbolTable.ContainsKey(symbol))
                {
                    return LexicalScope.LexicalScopeDataList[level].SymbolTable[symbol];
                }
                if (level == 0)
                {
                    return null;
                }
                else
                {
                    level--;
                }
            }
        }

        private static void LabelHandler(Match lineReg)
        {
            string label = lineReg.Groups["label"].Value;
            AddSymbol(label, _currentAddr);
            Listing.PrintLine(LineType.LABEL, _currentAddr);
        }

        private static void ResolveSymbol(string label)
        {
            Dictionary<string, UnresolvedSymbol> unsolvedSymbs = LexicalScope.LexicalScopeDataList[LexicalScope.Level].UnsolvedSymbols;
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
            Dictionary<string, UnresolvedSymbol> unsolvedSymbs = LexicalScope.LexicalScopeDataList[LexicalScope.Level].UnsolvedSymbols;
            // resolve depending symbols
            foreach (string dep in unresSymb.DependingList)
            {
                if (unsolvedSymbs.ContainsKey(dep))
                {
                    UnresolvedSymbol unresDep = unsolvedSymbs[dep];
                    unresDep.NbrUndefinedSymb--;
                    if (unresDep.NbrUndefinedSymb <= 0)
                    {
                        ExprResult res = ResolveExpr(unresDep.Expr);
                        AddSymbol(dep, res.Result);
                    }
                }
            }
            // resolve expressions
            foreach (ushort expr in unresSymb.ExprList)
            {
                UnresolvedExpr unresExp = UnsolvedExprList[expr];
                unresExp.NbrUndefinedSymb--;
                if (unresExp.NbrUndefinedSymb <= 0)
                {
                    ExprResult res = ResolveExpr(unresExp.Expr);
                    GenerateExprBytes(unresExp, res);
                    UnsolvedExprList.Remove(expr);
                }
            }
        }

        private static void GenerateExprBytes(UnresolvedExpr expr, ExprResult exprRes)
        {
            byte[] bytes = null;
            if (expr.AddrMode == CPUDef.AddrModes.REL)
            {
                int delta = (int)exprRes.Result - (expr.Position + _originAddr);
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
                switch (expr.Type)
                {
                    case SymbolType.WORD:
                        if ((exprRes.Result <= 255) && !CPUDef.IsAbsoluteAddr(expr.AddrMode) && (expr.AddrMode != CPUDef.AddrModes.IND))
                        {
                            bytes = new byte[1] { (byte)exprRes.Result };
                        }
                        else
                        {
                            bytes = GetWordBytes((ushort)exprRes.Result);
                        }
                        break;
                    case SymbolType.BYTE:
                        bytes = new byte[1] { (byte)exprRes.Result };
                        break;
                }
            }
            if (bytes != null)
            {
                FileOutMemory.RemoveRange(expr.Position, bytes.Length);
                FileOutMemory.InsertRange(expr.Position, bytes);
            }
        }
        public static void ProcessLine(Match lineReg, string type)
        {
            DelHandler handler = MapLineHandlers[type];
            handler(lineReg);
        }

        public static void AddError(string type)
        {
            _errorList.Add(new Error(_filePtr.CurrentLineNumber, _filePtr.SourceFile, type));
        }
        public static void InitLexicalScope()
        {
            LexicalScope = new LexicalScope()
            {
                Level = 0,
                LexicalScopeDataList = new List<LexicalScopeData>()
            };
            // add global scope
            AddNewLexicalScopeData();
            LexicalScope.GlobalScope = LexicalScope.LexicalScopeDataList[0];
        }
        public static void Assemble()
        {
            _stopAssembling = false;
            UnsolvedExprList = new Dictionary<ushort, UnresolvedExpr>();
            InitLexicalScope();
            FileOutMemory = new List<byte>();
            _errorList = new List<Error>();
            _fileStack = new Stack<FileInfo>();
            _macros = new Dictionary<string, MacroDef>();
            _startMacroDef = false;
            _repBlock = new RepBlock
            {
                IsInRepBlock = false
            };
            _cAsm = new ConditionalAsm()
            {
                Level = 0,
                InCondition = false,
                Values = new List<bool>()
            };
            _currentAddr = 0;
            _originAddr = 0;

            Boolean contextError = false;
            if (SourceFile == null)
            {
                contextError = true;
                Console.Error.WriteLine("undefined Source file");
            }
            if (ObjectFileName == null)
            {
                contextError = true;
                Console.Error.WriteLine("undefined object file");

            }
            if (contextError)
            {
                Console.Error.WriteLine("\n Tape -help to learn more about the tool");
                return;
            }
            if (Listing.EnableListing)
            {
                //set listing file name
                Listing.ListingFile = SourceFile;
                Listing.StartListing();
            }
            OpenFiles();
            Process();
            ResolveSymbols();
            _bw.Write(FileOutMemory.ToArray());
            CloseFiles();
            ExportUnsolvedFile();
            ExportSymbolTable();
            Listing.EndListing();
            DisplayErrors();

        }

        private static void ExportSymbolTable()
        {
            if (LexicalScope.GlobalScope.SymbolTable.Count > 0)
            {
                System.IO.File.WriteAllText($"{SourceFile.Split('.')[0]}.symb", JsonConvert.SerializeObject(LexicalScope.GlobalScope.SymbolTable));
            }
        }

        private static void ExportUnsolvedFile()
        {
            Dictionary<string, UnresolvedSymbol> unsolvedSymbols = LexicalScope.GlobalScope.UnsolvedSymbols;
            if (unsolvedSymbols.Count > 0)
            {
                System.IO.File.WriteAllText($"{SourceFile.Split('.')[0]}.Unsolved", JsonConvert.SerializeObject(unsolvedSymbols));
            }
            if (UnsolvedExprList.Count > 0)
            {
                System.IO.File.WriteAllText($"{SourceFile.Split('.')[0]}.UnsolvedExpr", JsonConvert.SerializeObject(UnsolvedExprList));
            }
        }

        public static void ResolveSymbols()
        {
            Dictionary<string, UnresolvedSymbol> unsolvedSymbols = LexicalScope.GlobalScope.UnsolvedSymbols;
            List<string> resolved = new List<string>();
            foreach (string symbName in unsolvedSymbols.Keys)
            {
                if (!LexicalScope.GlobalScope.SymbolTable.ContainsKey(symbName))
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
            _filePtr = new FileInfo
            {
                FileStreamReader = new StreamReader(SourceFile),
                SourceFile = SourceFile
            };

            _fileStack.Push(_filePtr);
            while (_fileStack.Count > 0)
            {
                _filePtr = _fileStack.Pop();
                string line;
                _filePtr.CurrentLineNumber = 0;
                while (!_stopAssembling && (line = _filePtr.FileStreamReader.ReadLine()) != null)
                {
                    string currentLine = line;
                    Listing.PrintLine(currentLine);
                    line = line.Trim();
                    _filePtr.CurrentLineNumber++;
                    line = Regex.Replace(line, ";(.)*", "").Trim();
                    if (String.IsNullOrWhiteSpace(line))
                    {
                        MainConsole.WriteLine(currentLine);
                        Listing.EndLine();
                        continue;
                    }
                    if (_startMacroDef &&
                        !line.ToLower().Equals(".endmacro") &&
                        !line.ToLower().StartsWith(".macro"))
                    {
                        _macros[_currentMacro].Lines.Add(line);
                        MainConsole.WriteLine(string.Format("{0}   --- {1}", currentLine, "MACRO DEF"));
                        Listing.EndLine();
                    }
                    else
                    {
                        ParseLine(line, currentLine);
                    }
                }
                _filePtr.FileStreamReader.Close();
            }
        }

        private static void ParseLine(string line, string originalLine)
        {
            if (_cAsm.InCondition &&
            !_cAsm.Values[_cAsm.Level] &&
            !line.ToLower().Equals(".endif") &&
            !line.ToLower().Equals(".else"))
            {
                MainConsole.WriteLine(string.Format("{0}   --- {1}", line, "NOT Assembled"));                
            }
            else if (_repBlock.IsInRepBlock &&
                       !line.ToLower().Equals(".endrep") &&
                       !line.ToLower().StartsWith(".rep"))
            {
                _repBlock.Lines.Add(line);
                MainConsole.WriteLine(string.Format("{0}   --- {1}", originalLine, "Repeat block line"));
            }
            else
            {
                bool syntaxError = true;
                foreach (KeyValuePair<Regex, string> entry in CPUDef.RegMap)
                {
                    Match match = entry.Key.Match(line);
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
            Listing.EndLine();
        }

        private static void DisplayErrors()
        {
            if (_errorList.Count > 0)
            {
                Console.Error.WriteLine("****************************************** Errors ******************************************");
                foreach (Error err in _errorList)
                {
                    Console.Error.WriteLine($"Line {err.Line}  - File {err.SourceFile} - Type {err.Type}");
                }
                Console.Error.WriteLine("********************************************************************************************");
            }
        }

        private static void OpenFiles()
        {
            string dir = Path.GetDirectoryName(ObjectFileName);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _stream = new FileStream(ObjectFileName, FileMode.Create);
            _bw = new BinaryWriter(_stream);
        }

        public static void CloseFiles()
        {
            if (_bw != null)
            {
                _bw.Flush();
                _bw.Close();
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

        public static byte GetLowByte(ushort word)
        {
            return (byte)(word & 0xff);
        }

        public static byte GetHighByte(ushort word)
        {
            return (byte)(word >> 8);
        }

        private static void AddSymbol(string label, dynamic symb, bool replaceIfExist = false)
        {
            Dictionary<string, dynamic> symbTable = LexicalScope.LexicalScopeDataList[LexicalScope.Level].SymbolTable;
            if (symbTable.ContainsKey(label))
            {
                if (replaceIfExist)
                {
                    symbTable[label] = symb;
                }
                else
                {
                    AddError(Errors.LABEL_EXISTS);
                }
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
        public int Line { get; set; }
        public string SourceFile { get; set; }
        public string Type { get; set; }
        public Error(int line, string sourceFile, string type)
        {
            this.Line = line;
            this.Type = type;
            this.SourceFile = sourceFile;
        }
    }

    public struct Token
    {
        public string Type { get; set; }
        public string Value { get; set; }
    }

    public class ExprResult
    {
        public dynamic Result { get; set; }
        public List<string> UndefinedSymbs { get; set; }
    }

    public enum SymbolType
    {
        BYTE = 0,
        WORD = 1
    }

    public struct Errors
    {
        public const string LABEL_EXISTS = "Label already declared";
        public const string REL_JUMP = "Relative jump is too big";
        public const string SYNTAX = "Syntax Error";
        public const string FILE_NOT_EXISTS = "File doesn't exist";
        public const string DATA_BYTE = "Error in insert data byte";
        public const string DATA_WORD = "Error in insert data word";
        public const string DATA_TYPE = "Error in data type";
        public const string MACRO_EXISTS = "Macro with the same name already defined";
        public const string MACRO_NOT_EXISTS = "Undefined Macro";
        public const string MACRO_CALL_WITHOUT_PARAMS = "Macro called without params";
        public const string NESTED_MACROS = "Nested macros are not supported";
        public const string NESTED_REP = "Nested Reps are not supported";
        public const string NO_MACRO = "No macro is defined";
        public const string NO_REP = "No repeat is defined";
        public const string OPERANDS = "Error in operands";
        public const string UNDEFINED_SYMBOL = "Undefined symbol";
        public const string NESTED_CONDITIONAL_ASSEMBLY = "Too much nested conditional assembly";
        public const string NO_CONDITIONAL_ASSEMBLY = "No conditional assembly is defined";
        public const string MAX_LOCAL_SCOPE = "Too much nested local lexical levels";
        public const string NO_LOCAL_SCOPE = "No local scope is defined";
    }

    public class UnresolvedSymbol
    {
        public List<string> DependingList { get; set; }
        public List<ushort> ExprList { get; set; }
        public string Expr { get; set; }
        public int NbrUndefinedSymb { get; set; }
    }

    public class UnresolvedExpr
    {
        public string Expr { get; set; }
        public int NbrUndefinedSymb { get; set; }
        public ushort Position { get; set; }
        public SymbolType Type { get; set; }
        public CPUDef.AddrModes AddrMode { get; set; }
    }

    public struct FileInfo
    {
        public StreamReader FileStreamReader { get; set; }
        public string SourceFile { get; set; }
        public int CurrentLineNumber { get; set; }
    }

    public struct MacroDef
    {
        public string[] ListParam { get; set; }
        public List<string> Lines { get; set; }
    }

    public class ConditionalAsm
    {
        public List<bool> Values { get; set; }
        public byte Level { get; set; }
        public bool InCondition { get; set; }
    }

    public class LexicalScopeData
    {
        public Dictionary<string, dynamic> SymbolTable { get; set; }
        public Dictionary<string, UnresolvedSymbol> UnsolvedSymbols { get; set; }
        public ushort MemArea { get; set; }
    }
    public class LexicalScope
    {
        public List<LexicalScopeData> LexicalScopeDataList { get; set; }
        // pointer to global scope
        public LexicalScopeData GlobalScope { get; set; }
        // 0: global
        public byte Level { get; set; }
    }

    public struct RepBlock
    {
        public bool IsInRepBlock { get; set; }
        public List<string> Lines { get; set; }
        public int Counter { get; internal set; }
    }
}
