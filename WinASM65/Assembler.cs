/**********************************************************************************/
/*                                                                                */
/*                                                                                */
/* Copyright (c) 2021 Abdelghani BOUZIANE                                         */
/*                                                                                */
/* Permission is hereby granted, free of charge, to any person obtaining a copy   */
/* of this software and associated documentation files (the "Software"), to deal  */
/* in the Software without restriction, including without limitation the rights   */
/* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell      */
/* copies of the Software, and to permit persons to whom the Software is          */
/* furnished to do so, subject to the following conditions:                       */
/*                                                                                */
/* The above copyright notice and this permission notice shall be included in all */
/* copies or substantial portions of the Software.                                */
/*                                                                                */
/* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR     */
/* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,       */
/* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE    */
/* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER         */
/* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,  */
/* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE  */
/* SOFTWARE.                                                                      */
/*                                                                                */
/**********************************************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace WinASM65
{
    class Assembler
    {
        public static List<byte> fileOutMemory;
        private static List<Error> errorList;
        private static Stream stream;
        private static BinaryWriter bw = null;
        public static string objectFileName { get; set; }
        public delegate void DelHandler(Match lineReg);
        private static Dictionary<string, DelHandler> mapLineHandlers = new Dictionary<string, DelHandler>
        {
            {CPUDef.LABEL, LabelHandler },
            {CPUDef.DIRECTIVE, DirectiveHandler },
            {CPUDef.INSTRUCTION, InstructionHandler },
            {CPUDef.CONSTANT, ConstantHandler }
        };

        public static Dictionary<string, Symbol> symbolTable;
        private static ushort currentAddr;
        private static ushort originAddr;
        private delegate void DelDirectiveHandler(string value);

        public static string sourceFile;
        private static int currentLineNumber;
        public static Dictionary<string, List<TokenInfo>> unsolvedSymbols;

        #region directives
        private static Dictionary<string, DelDirectiveHandler> directiveHandlersMap = new Dictionary<string, DelDirectiveHandler>
        {
            { ".org", OrgHandler },
            { ".incbin", IncBinHandler },
            { ".byte", DataByteHandler },
            { ".word", DataWordHandler }
        };
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

        private static void IncBinHandler(string fileName)
        {
            string directoryName = Path.GetDirectoryName(sourceFile);
            string toInclude = directoryName + '/' + fileName;
            if (File.Exists(toInclude))
            {
                fileOutMemory.AddRange(File.ReadAllBytes(toInclude));
            }
            else
            {
                AddError(currentLineNumber, Errors.FILE_NOT_EXISTS);
            }
        }

        private static void DataByteHandler(string bytesIn)
        {
            string[] bytes = bytesIn.Split(',');
            foreach (string db in bytes)
            {
                string data = db.Trim();
                Match match = Regex.Match(data, @"^(" + CPUDef.byteRegex + @")$");
                if (match.Success)
                {
                    Token token = GetToken(match);
                    TokenResult tokenRes = ResolveToken(token, CPUDef.AddrModes.NO);
                    if (tokenRes.Bytes != null)
                    {
                        fileOutMemory.AddRange(tokenRes.Bytes);
                    }
                    else
                    {
                        string unsolvedLabel = tokenRes.UnsolvedLabel;
                        if (unsolvedLabel != null)
                        {
                            ushort position = (ushort)(currentAddr - originAddr);
                            TokenInfo tokenInfo = new TokenInfo { Position = position, Type = tokenRes.VType, addrMode = CPUDef.AddrModes.NO };
                            AddUnsolvedSymbol(unsolvedLabel, tokenInfo);
                        }
                    }
                    currentAddr++;
                } else
                {
                    AddError(currentLineNumber, Errors.DATA_BYTE);
                }
            }
        }

        private static void DataWordHandler(string wordsIn)
        {
            string[] words = wordsIn.Split(',');
            foreach (string dw in words)
            {
                string data = dw.Trim();
                Match match = Regex.Match(data, @"^(" + CPUDef.wordRegex + @")$");
                if (match.Success)
                {
                    Token token = GetToken(match);
                    TokenResult tokenRes = ResolveToken(token, CPUDef.AddrModes.NO);
                    if (tokenRes.Bytes != null)
                    {
                        fileOutMemory.AddRange(tokenRes.Bytes);
                    }
                    else
                    {
                        string unsolvedLabel = tokenRes.UnsolvedLabel;
                        if (unsolvedLabel != null)
                        {
                            ushort position = (ushort)(currentAddr - originAddr);
                            TokenInfo tokenInfo = new TokenInfo { Position = position, Type = tokenRes.VType, addrMode = CPUDef.AddrModes.NO };
                            AddUnsolvedSymbol(unsolvedLabel, tokenInfo);
                        }
                    }
                    currentAddr += 2;
                }
                else
                {
                    AddError(currentLineNumber, Errors.DATA_WORD);
                }
            }
        }

        #endregion
        private static void ConstantHandler(Match lineReg)
        {
            string label = lineReg.Groups["label"].Value;
            Symbol constant = new Symbol();
            foreach (string tt in CPUDef.constantTypes)
            {
                if (!string.IsNullOrWhiteSpace(lineReg.Groups[tt].Value))
                {
                    switch (tt)
                    {
                        case "HB":
                            constant.Value = Byte.Parse(lineReg.Groups[tt].Value, NumberStyles.HexNumber);
                            constant.Type = SymbolType.BYTE;
                            break;
                        case "HW":
                            constant.Value = ushort.Parse(lineReg.Groups[tt].Value, NumberStyles.HexNumber);
                            constant.Type = SymbolType.WORD;
                            break;
                        case "binByte":
                            constant.Value = Convert.ToByte(lineReg.Groups[tt].Value, 2);
                            constant.Type = SymbolType.BYTE;
                            break;
                    }
                    break;
                }
            }
            if (!symbolTable.ContainsKey(label))
            {
                symbolTable.Add(label, constant);
            }
            else
            {
                AddError(currentLineNumber, Errors.LABEL_EXISTS);
            }
        }

        private static void InstructionHandler(Match lineReg)
        {
            string label = lineReg.Groups["label"].Value;
            string opcode = lineReg.Groups["opcode"].Value.ToUpper();
            string operands = lineReg.Groups["operands"].Value;
            ushort opcodeAddr = currentAddr;

            if (!string.IsNullOrWhiteSpace(label))
            {
                if (symbolTable.ContainsKey(label))
                {
                    AddError(currentLineNumber, Errors.LABEL_EXISTS);
                }
                else
                {
                    symbolTable.Add(label, new Symbol { Type = SymbolType.WORD, Value = currentAddr });
                }
            }

            Byte[] addrModesValues = CPUDef.OPC_TABLE[opcode];
            CPUDef.AddrModes addrMode = CPUDef.AddrModes.NO;
            List<byte> instBytes = new List<byte>();
            CPUDef.InstructionInfo instInfo = new CPUDef.InstructionInfo();
            bool syntaxError = true;
            Match match = null;

            if (string.IsNullOrWhiteSpace(operands))
            {
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
                syntaxError = false;
            }
            else
            {
                // 2 bytes opcode
                if (Array.Exists(CPUDef.REL_OPC, opc => opc.Equals(opcode)))
                {
                    match = Regex.Match(operands, @"^(" + CPUDef.byteRegex + @")$");
                    if (match.Success)
                    {
                        syntaxError = false;
                        instInfo = new CPUDef.InstructionInfo { addrMode = CPUDef.AddrModes.REL, nbrBytes = 2 };
                    }
                }
                else
                {

                    instInfo = new CPUDef.InstructionInfo();
                    foreach (KeyValuePair<string, CPUDef.InstructionInfo> entry in CPUDef.addrModesRegMap)
                    {
                        match = Regex.Match(operands, entry.Key);
                        if (match.Success)
                        {
                            syntaxError = false;
                            instInfo = entry.Value;
                            break;
                        }
                    }
                }
            }
            if (syntaxError)
            {
                Console.WriteLine("Operands Syntax Error {0}", operands);
            }
            else
            {
                addrMode = instInfo.addrMode;
                instBytes.Add(addrModesValues[(int)addrMode]);
                // instruction with operands
                if (match != null)
                {
                    Token token = GetToken(match);
                    TokenResult tokenRes = ResolveToken(token, addrMode);
                    if (tokenRes.Bytes != null)
                    {
                        instBytes.AddRange(tokenRes.Bytes);
                    }
                    else
                    {
                        string unsolvedLabel = tokenRes.UnsolvedLabel;
                        if (unsolvedLabel != null)
                        {
                            ushort position = (ushort)(opcodeAddr - originAddr + 1);
                            TokenInfo tokenInfo = new TokenInfo { Position = position, Type = tokenRes.VType, addrMode = addrMode };
                            AddUnsolvedSymbol(unsolvedLabel, tokenInfo);
                        }
                    }
                }
                currentAddr += instInfo.nbrBytes;
                fileOutMemory.AddRange(instBytes.ToArray());
                MainConsole.WriteLine($"{opcode} mode {addrMode.ToString()}");
            }
        }

        private static void AddUnsolvedSymbol(string unsolvedLabel, TokenInfo tokenInfo)
        {
            if (unsolvedSymbols.ContainsKey(unsolvedLabel))
            {
                unsolvedSymbols[unsolvedLabel].Add(tokenInfo);
            }
            else
            {
                List<TokenInfo> l = new List<TokenInfo>();
                l.Add(tokenInfo);
                unsolvedSymbols.Add(unsolvedLabel, l);
            }
        }

        private static Token GetToken(Match match)
        {
            Token token = new Token();
            foreach (string tt in CPUDef.operandTypes)
            {
                if (!string.IsNullOrWhiteSpace(match.Groups[tt].Value))
                {
                    token = new Token
                    {
                        Type = tt,
                        Value = match.Groups[tt].Value
                    };
                    break;
                }
            }
            return token;
        }
        private static TokenResult ResolveToken(Token token, CPUDef.AddrModes addrMode)
        {
            switch (token.Type)
            {
                case "HB":
                    return new TokenResult { Bytes = new byte[1] { Byte.Parse(token.Value, NumberStyles.HexNumber) } };
                case "HW":
                    return new TokenResult { Bytes = GetWordBytes(token.Value) };
                case "label":
                    if (symbolTable.ContainsKey(token.Value))
                    {
                        SymbolType labelType = symbolTable[token.Value].Type;
                        dynamic labelValue = symbolTable[token.Value].Value;
                        if ((addrMode == CPUDef.AddrModes.REL) && (labelType == SymbolType.WORD))
                        {
                            int delta = labelValue - (currentAddr + 1);
                            byte res;
                            if (delta > 127 || delta < -128)
                            {
                                AddError(currentLineNumber, Errors.REL_JUMP);
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
                                return new TokenResult { Bytes = new byte[1] { res } };
                            }

                        }
                        else
                        {
                            if (labelType == SymbolType.WORD)
                            {
                                return new TokenResult { Bytes = GetWordBytes(labelValue) };
                            }
                            else
                            {
                                return new TokenResult { Bytes = new byte[1] { labelValue } };
                            }
                        }
                    }
                    else
                    {
                        return new TokenResult { UnsolvedLabel = token.Value, VType = SymbolType.WORD };
                    }
                    break;
                case "loLabel":
                    if (symbolTable.ContainsKey(token.Value))
                    {
                        ushort addr = symbolTable[token.Value].Value;
                        return new TokenResult { Bytes = new byte[1] { GetLowByte(addr) } };
                    }
                    else
                    {
                        return new TokenResult { UnsolvedLabel = token.Value, VType = SymbolType.LO };
                    }
                case "hiLabel":
                    if (symbolTable.ContainsKey(token.Value))
                    {
                        ushort addr = symbolTable[token.Value].Value;
                        return new TokenResult { Bytes = new byte[1] { GetHighByte(addr) } };

                    }
                    else
                    {
                        return new TokenResult { UnsolvedLabel = token.Value, VType = SymbolType.HI };
                    }
                case "loHW":
                    ushort word = ushort.Parse(token.Value, NumberStyles.HexNumber);
                    return new TokenResult { Bytes = new byte[1] { GetLowByte(word) } };
                case "hiHW":
                    ushort _word = ushort.Parse(token.Value, NumberStyles.HexNumber);
                    return new TokenResult { Bytes = new byte[1] { GetHighByte(_word) } };
                case "binByte":
                    return new TokenResult { Bytes = new byte[1] { Convert.ToByte(token.Value, 2) } };

            }
            return new TokenResult();
        }

        private static void LabelHandler(Match lineReg)
        {
            string label = lineReg.Groups["label"].Value;
            if (symbolTable.ContainsKey(label))
            {
                AddError(currentLineNumber, Errors.LABEL_EXISTS);
            }
            else
            {
                Symbol symb = new Symbol { Value = currentAddr, Type = SymbolType.WORD };
                symbolTable.Add(label, symb);
            }
        }

        private static void ResolveSymbol(string label, Symbol symb)
        {
            if (!unsolvedSymbols.ContainsKey(label))
            {
                return;
            }
            List<TokenInfo> tokensInfo = unsolvedSymbols[label];
            byte[] bytes = null;

            foreach (TokenInfo tokenInfo in tokensInfo)
            {
                switch (tokenInfo.Type)
                {
                    case SymbolType.WORD:
                        if (symb.Type == SymbolType.BYTE)
                        {
                            bytes = new byte[1] { (byte)symb.Value };
                        }
                        else
                        {
                            if (tokenInfo.addrMode == CPUDef.AddrModes.REL)
                            {
                                int delta = (ushort)symb.Value - (tokenInfo.Position + originAddr);
                                byte res;
                                if (delta > 127 || delta < -128)
                                {
                                    AddError(-1, Errors.REL_JUMP);
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
                                bytes = GetWordBytes((ushort)symb.Value);
                            }
                        }
                        break;
                    case SymbolType.LO:
                        bytes = new byte[1] { GetLowByte((ushort)symb.Value) };
                        break;
                    case SymbolType.HI:
                        bytes = new byte[1] { GetHighByte((ushort)symb.Value) };
                        break;
                }

                fileOutMemory.InsertRange(tokenInfo.Position, bytes);
            }
        }

        public static void ProcessLine(Match lineReg, string type)
        {
            DelHandler handler = mapLineHandlers[type];
            handler(lineReg);
        }

        public static void AddError(int line, string type)
        {
            errorList.Add(new Error(line, type));
        }

        public static void Assemble()
        {
            unsolvedSymbols = new Dictionary<string, List<TokenInfo>>();
            symbolTable = new Dictionary<string, Symbol>();
            fileOutMemory = new List<byte>();
            errorList = new List<Error>();

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
                Symbol symb = symbolTable[symbName];
                ResolveSymbol(symbName, symb);
                resolved.Add(symbName);

            }
            foreach (string symbName in resolved)
            {
                unsolvedSymbols.Remove(symbName);
            }
        }

        private static void Process()
        {
            using (StreamReader reader = new StreamReader(sourceFile))
            {
                string line;
                currentLineNumber = -1;
                while ((line = reader.ReadLine()) != null)
                {
                    currentLineNumber++;
                    string originalLine = line;
                    line = Regex.Replace(line, ";(.)*", "").Trim();
                    if (String.IsNullOrWhiteSpace(line))
                    {
                        MainConsole.WriteLine(originalLine);
                        continue;
                    }
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
                        AddError(currentLineNumber, Errors.SYNTAX);
                        // MainConsole.WriteLine("[{0}] Syntax Error - {1}", lineNumber, originalLine);
                    }
                }
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
    }
    struct Error
    {
        public int line { get; set; }
        public string type { get; set; }
        public Error(int line, string type)
        {
            this.line = line;
            this.type = type;
        }
    }

    struct Token
    {
        public string Type { get; set; }
        public string Value { get; set; }
    }

    struct TokenResult
    {
        public Byte[] Bytes { get; set; }
        public string UnsolvedLabel { get; set; }
        public SymbolType VType { get; set; }
    }

    struct Symbol
    {
        public dynamic Value { get; set; }
        public SymbolType Type { get; set; }
    }

    enum SymbolType
    {
        BYTE = 0,
        WORD = 1,
        LO = 2,
        HI = 3
    }

    struct Errors
    {
        public static string LABEL_EXISTS = "Label already declared";
        public static string REL_JUMP = "Relative jump is too big";
        public static string SYNTAX = "Syntax Error";
        public static string FILE_NOT_EXISTS = "File doesn't exist";
        public static string DATA_BYTE = "Error in insert data byte";
        public static string DATA_WORD = "Error in insert data word";
    }

    struct TokenInfo
    {
        public ushort Position { get; set; }
        public SymbolType Type { get; set; }
        public CPUDef.AddrModes addrMode { get; set; }
    }
}
