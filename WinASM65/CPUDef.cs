﻿/**********************************************************************************/
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

using System.Collections.Generic;

namespace WinASM65
{
    class CPUDef
    {

        public static readonly string LABEL = "LABEL";
        public static readonly string DIRECTIVE = "DIRECTIVE";
        public static readonly string INSTRUCTION = "INSTRUCTION";
        public static readonly string CONSTANT = "CONSTANT";
        public enum AddrModes
        {
            NO = -1,
            IMP = 0,        // OPC
            ACC = 1,        // OPC A
            IMM = 2,        // OPC #byte
            ABS = 3,        // OPC word
            ABX = 4,        // OPC word,X
            ABY = 5,        // OPC word,Y
            ZPG = 6,        // OPC byte         OPC ]label
            ZPX = 7,        // OPC byte,X       OPC ]label,X
            ZPY = 8,        // OPC byte,Y       OPC ]label,Y
            IND = 9,        // OPC (word)
            INX = 10,       // OPC (byte,x)
            INY = 11,       // OPC (byte),y
            REL = 12,       // OPC byte
        }
        private static readonly string binByte = @"[01]{8}";
        private static readonly string hex = @"[0-9a-fA-f]";
        private static readonly string hexByte = hex + @"{2}";
        private static readonly string hexWord = hex + @"{4}";
        private static readonly string label = @"[A-Za-z]\w*";

        // hex byte regex
        private static readonly string hbRegex = @"(\$(?<HB>" + hexByte + "))";
        // hex word regex
        private static readonly string hwRegex = @"(\$(?<HW>" + hexWord + "))";
        // label regex
        private static readonly string labelRegex = @"(?<label>" + label + ")";
        // zero page addr label
        private static readonly string zpLabelRegex = @"(\](?<label>" + label + "))";
        // label's low byte regex
        private static readonly string loLabelRegex = @"(<(?<loLabel>" + label + "))";
        // label's high byte regex
        private static readonly string hiLabelRegex = @"(>(?<hiLabel>" + label + "))";
        // hex word's low byte regex
        private static readonly string loHexWordRegex = @"(<\$(?<loHW>" + hexWord + "))";
        // hex word's high byte regex
        private static readonly string hiHexWordRegex = @"(>\$(?<hiHW>" + hexWord + "))";
        // binary byte regex
        private static readonly string binByteRegex = @"(%(?<binByte>" + binByte + "))";

        public static readonly string[] REL_OPC = new string[] { "BCC", "BCS", "BEQ", "BMI", "BNE", "BPL", "BVC", "BVS" };
        public static readonly string[] ACC_OPC = new string[] { "ASL ", "LSR", "ROL", "ROR" };
        public static readonly Dictionary<string, byte[]> OPC_TABLE = new Dictionary<string, byte[]>
        {
            { "ADC", new byte[] {0xff, 0xff, 0x69, 0x6d, 0x7d, 0x79, 0x65, 0x75, 0xff, 0xff, 0x61, 0x71, 0xff } },
            { "AND", new byte[] {0xff,  0xff,0x29,0x2d,0x3d,0x39,0x25,0x35,  0xff,  0xff,0x21,0x31,  0xff}},
            { "ASL", new byte[] {0xff,0x0a,  0xff,0x0e,0x1e,  0xff,0x06,0x16,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "BCC", new byte[] {0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,0x90}},
            { "BCS", new byte[] {0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,0xb0}},
            { "BEQ", new byte[] {0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,0xf0}},
            { "BIT", new byte[] {0xff,  0xff,  0xff,0x2c,  0xff,  0xff,0x24,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "BMI", new byte[] {0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,0x30}},
            { "BNE", new byte[] {0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,0xd0}},
            { "BPL", new byte[] {0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,0x10}},
            { "BRK", new byte[] {0x00,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "BVC", new byte[] {0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,0x50}},
            { "BVS", new byte[] {0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,0x70}},
            { "CLC", new byte[] {0x18,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "CLD", new byte[] {0xd8,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "CLI", new byte[] {0x58,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "CLV", new byte[] {0xb8,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "CMP", new byte[] {0xff,  0xff,0xc9,0xcd,0xdd,0xd9,0xc5,0xd5,  0xff,  0xff,0xc1,0xd1,  0xff}},
            { "CPX", new byte[] {0xff,  0xff,0xe0,0xec,  0xff,  0xff,0xe4,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "CPY", new byte[] {0xff,  0xff,0xc0,0xcc,  0xff,  0xff,0xc4,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "DEC", new byte[] {0xff,  0xff,  0xff,0xce,0xde,  0xff,0xc6,0xd6,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "DEX", new byte[] {0xca,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "DEY", new byte[] {0x88,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "EOR", new byte[] {0xff,  0xff,0x49,0x4d,0x5d,0x59,0x45,0x55,  0xff,  0xff,0x41,0x51,  0xff}},
            { "INC", new byte[] {0xff,  0xff,  0xff,0xee,0xfe,  0xff,0xe6,0xf6,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "INX", new byte[] {0xe8,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "INY", new byte[] {0xc8,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "JMP", new byte[] {0xff,  0xff,  0xff,0x4c,  0xff,  0xff,  0xff,  0xff,  0xff,0x6c,  0xff,  0xff,  0xff}},
            { "JSR", new byte[] {0xff,  0xff,  0xff,0x20,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "LDA", new byte[] {0xff,  0xff,0xa9,0xad,0xbd,0xb9,0xa5,0xb5,  0xff,  0xff,0xa1,0xb1,  0xff}},
            { "LDX", new byte[] {0xff,  0xff,0xa2,0xae,  0xff,0xbe,0xa6,  0xff,0xb6,  0xff,  0xff,  0xff,  0xff}},
            { "LDY", new byte[] {0xff,  0xff,0xa0,0xac,0xbc,  0xff,0xa4,0xb4,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "LSR", new byte[] {0xff,0x4a,  0xff,0x4e,0x5e,  0xff,0x46,0x56,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "NOP", new byte[] {0xea,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "ORA", new byte[] {0xff,  0xff,0x09,0x0d,0x1d,0x19,0x05,0x15,  0xff,  0xff,0x01,0x11,  0xff}},
            { "PHA", new byte[] {0x48,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "PHP", new byte[] {0x08,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "PLA", new byte[] {0x68,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "PLP", new byte[] {0x28,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "ROL", new byte[] {0xff,0x2a,  0xff,0x2e,0x3e,  0xff,0x26,0x36,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "ROR", new byte[] {0xff,0x6a,  0xff,0x6e,0x7e,  0xff,0x66,0x76,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "RTI", new byte[] {0x40,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "RTS", new byte[] {0x60,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "SBC", new byte[] {0xff,  0xff,0xe9,0xed,0xfd,0xf9,0xe5,0xf5,  0xff,  0xff,0xe1,0xf1,  0xff}},
            { "SEC", new byte[] {0x38,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "SED", new byte[] {0xf8,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "SEI", new byte[] {0x78,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "STA", new byte[] {0xff,  0xff,  0xff,0x8d,0x9d,0x99,0x85,0x95,  0xff,  0xff,0x81,0x91,  0xff}},
            { "STX", new byte[] {0xff,  0xff,  0xff,0x8e,  0xff,  0xff,0x86,  0xff,0x96,  0xff,  0xff,  0xff,  0xff}},
            { "STY", new byte[] {0xff,  0xff,  0xff,0x8c,  0xff,  0xff,0x84,0x94,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "TAX", new byte[] {0xaa,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "TAY", new byte[] {0xa8,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "TSX", new byte[] {0xba,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "TXA", new byte[] {0x8a,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "TXS", new byte[] {0x9a,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}},
            { "TYA", new byte[] {0x98,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff,  0xff}}
        };
        public static readonly string labelDeclareReg = @"\s*(?<label>\w+):\s*";
        public static readonly string directiveReg = @"\s*(?<directive>\.[a-zA-Z]+)\s+(?<value>(.)+)";
        // instruction = label? opcode operands?
        public static readonly string instrReg = @"^(\s*(?<label>\w+)\s+)?(?<opcode>[a-zA-Z]{3})((\s+(?<operands>(.)+))|$)";
        public static readonly string constantReg = @"^\s*(?<label>\w+)\s*=\s*(" + hbRegex  + "|" + hwRegex + "|" + binByteRegex + ")$";
        public static readonly Dictionary<string, string> regMap = new Dictionary<string, string>
        {
            { labelDeclareReg, LABEL},
            { directiveReg, DIRECTIVE},
            { instrReg, INSTRUCTION},
            { constantReg, CONSTANT }
        };

        public static List<string> operandTypes = new List<string> {
         "HB", "HW", "label", "loLabel", "hiLabel", "loHW", "hiHW", "binByte"
        };

        public static List<string> constantTypes = new List<string> {
         "HB", "HW", "binByte"
        };

        public static readonly string wordRegex = hwRegex + @"|" + labelRegex;
        public static readonly string byteRegex = hbRegex + @"|" + zpLabelRegex + @"|" + labelRegex + @"|" + loLabelRegex + @"|" + hiLabelRegex + @"|" + loHexWordRegex + @"|" + hiHexWordRegex + @"|" + binByteRegex;
        private static readonly string zpByteRegex = hbRegex + @"|" + binByteRegex + @"|" + zpLabelRegex + @"|" + loLabelRegex + @"|" + hiLabelRegex + @"|" + loHexWordRegex + @"|" + hiHexWordRegex;

        public struct InstructionInfo
        {
            public AddrModes addrMode { get; set; }
            public ushort nbrBytes { get; set; }

        }
        // operands Addr modes regex
        // IMP, ACC, REL are managed outside
        public static readonly Dictionary<string, InstructionInfo> addrModesRegMap = new Dictionary<string, InstructionInfo>
        {
            { @"^#(" + binByteRegex + @"|" + loHexWordRegex + @"|" + hiHexWordRegex + @"|"+ hbRegex + @"|" + labelRegex + @"|" + loLabelRegex + @"|" + hiLabelRegex + @")$", new InstructionInfo {addrMode = AddrModes.IMM, nbrBytes= 2}},
            { @"^(" + wordRegex + @")$", new InstructionInfo {addrMode =AddrModes.ABS, nbrBytes= 3}},
            { @"^(" + wordRegex + @")\s*,\s*[xX]$", new InstructionInfo {addrMode = AddrModes.ABX, nbrBytes= 3}},
            { @"^(" +wordRegex + @")\s*,\s*[yY]$", new InstructionInfo {addrMode = AddrModes.ABY, nbrBytes= 3}},
            { @"^(" + byteRegex + @")$", new InstructionInfo {addrMode = AddrModes.ZPG, nbrBytes= 2}},
            { @"^(" + zpByteRegex + @")\s*,\s*[xX]$", new InstructionInfo {addrMode = AddrModes.ZPX, nbrBytes= 2}},
            { @"^(" + zpByteRegex + @")\s*,\s*[yY]$", new InstructionInfo {addrMode = AddrModes.ZPY, nbrBytes= 2}},
            { @"^(\(\s*(" + wordRegex + @")\s*\)$)", new InstructionInfo {addrMode = AddrModes.IND, nbrBytes= 3}},
            { @"^\(\s*(" + byteRegex + @")\s*,\s*[xX]\s*\)$", new InstructionInfo {addrMode = AddrModes.INX, nbrBytes= 2}},
            { @"^\(\s*(" + byteRegex + @")\s*\)\s*,\s*[yY]$", new InstructionInfo {addrMode = AddrModes.INY, nbrBytes= 2}}
        };

        public static bool isAbsoluteAddr(AddrModes addrMode)
        {
            return ((int)addrMode < 6 && (int)addrMode > 2); 
        }
    }

}