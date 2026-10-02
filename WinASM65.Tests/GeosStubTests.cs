// WinASM65 - what the GEOS relocation stub actually does to a loaded image.
//
// The stub is the part of the GEOS writer whose bug would be invisible: a wrong
// stub does not refuse, it loads and runs and quietly hands the program an
// address one page off, or jumps into the middle of the table. So it is tested
// by running it, on the bytes the writer really emits, against a synthetic
// image -- and at more than one load address, because a stub that carried an
// address of its own would pass at exactly the address it was built for and
// corrupt memory everywhere else.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Targets;

namespace WinASM65.Tests
{
    [TestClass]
    public class GeosStubTests
    {
        private const ushort CodeBase = 0x4000;   // the address the code is assembled for
        private const int CodeLength = 16;

        // The code holds two absolute references: a whole address at +4 and a
        // single low byte at +8. Their assembled values are what the linker would
        // have produced for CodeBase.
        private const int WideReferenceOffset = 4;
        private const ushort WideReferenceValue = 0x1234;
        private const int ByteReferenceOffset = 8;
        private const byte ByteReferenceValue = 0x5A;

        /// <summary>
        /// The table that describes those two references, in the layout the stub
        /// reads. It is built by hand rather than by the linker so that the test
        /// pins the stub to the table format and not to whatever the linker
        /// happens to produce today.
        /// </summary>
        private static byte[] Table()
        {
            byte[] t = new byte[10 + 2 * 3];
            t[0] = 0x52; t[1] = 0x36; t[2] = 0x35; t[3] = 0x41; // "R65A"
            t[4] = 0x00; t[5] = 0x40;                           // base = $4000
            t[6] = 0x02; t[7] = 0x00;                           // two entries
            t[8] = 0x00; t[9] = 0x40;                           // entry = $4000
            // entry 1: address $4004, width 2
            t[10] = 0x04; t[11] = 0x40; t[12] = 0x02;
            // entry 2: address $4008, width 1
            t[13] = 0x08; t[14] = 0x40; t[15] = 0x01;
            return t;
        }

        /// <summary>The code image exactly as it is linked: whole address, then low byte.</summary>
        private static byte[] Code()
        {
            byte[] c = new byte[CodeLength];
            c[WideReferenceOffset] = (byte)(WideReferenceValue & 0xFF);
            c[WideReferenceOffset + 1] = (byte)(WideReferenceValue >> 8);
            c[ByteReferenceOffset] = ByteReferenceValue;
            return c;
        }

        /// <summary>
        /// Loads the whole record -- stub, table, code -- at <paramref name="load"/>,
        /// the way the kernal would, and runs the stub. Returns where the stub
        /// left the program counter and the memory it left behind.
        /// </summary>
        private static Tiny6502 RunStubAt(ushort load)
        {
            byte[] table = Table();
            byte[] code = Code();
            List<Diagnostic> problems = new List<Diagnostic>();
            byte[] stub = GeosStub.Assemble(CodeBase, table.Length, problems);
            Assert.AreEqual(0, problems.Count, "the stub must assemble cleanly");

            int codeOffset = stub.Length + table.Length;
            int bias = (load + codeOffset) - CodeBase;
            ushort entry = (ushort)(CodeBase + bias);

            Tiny6502 cpu = new Tiny6502();
            cpu.Load(stub, load);
            cpu.Load(table, (ushort)(load + stub.Length));
            cpu.Load(code, (ushort)(load + codeOffset));
            // The kernal leaves the load address here for LOAD2 and for LOAD.
            cpu.Memory[0x886C] = (byte)(load & 0xFF);
            cpu.Memory[0x886D] = (byte)(load >> 8);
            cpu.PC = load;

            cpu.RunUntil(entry, 100000);

            Assert.AreEqual(entry, cpu.PC,
                "the stub must leave the program counter on the relocated entry point");
            return cpu;
        }

        private static int ExpectedBiasFor(ushort load, int codeOffset)
        {
            return (load + codeOffset) - CodeBase;
        }


        [TestMethod]
        public void RelocatesWholeAddressAndLowByteWhenLoadedAway()
        {
            const ushort load = 0x3000;
            byte[] table = Table();
            List<Diagnostic> problems = new List<Diagnostic>();
            byte[] stub = GeosStub.Assemble(CodeBase, table.Length, problems);
            int codeOffset = stub.Length + table.Length;
            int bias = ExpectedBiasFor(load, codeOffset);
            Assert.AreNotEqual(0, bias, "this load must actually move the image");

            Tiny6502 cpu = RunStubAt(load);

            int wideSite = load + codeOffset + WideReferenceOffset;
            int gotWide = cpu.Memory[wideSite] | (cpu.Memory[wideSite + 1] << 8);
            Assert.AreEqual((ushort)(WideReferenceValue + bias), (ushort)gotWide,
                "the whole address must be moved by the bias");

            int byteSite = load + codeOffset + ByteReferenceOffset;
            Assert.AreEqual((byte)(ByteReferenceValue + bias), cpu.Memory[byteSite],
                "the low byte must be moved by the low half of the bias");
        }

        [TestMethod]
        public void MovesTheSameWayAtEveryLoadAddress()
        {
            // A stub that held an address of its own would pass at the address it
            // was built for and be wrong everywhere else. Running it at several
            // loads and asking the same question each time is what tells the two
            // apart.
            byte[] table = Table();
            List<Diagnostic> problems = new List<Diagnostic>();
            byte[] stub = GeosStub.Assemble(CodeBase, table.Length, problems);
            int codeOffset = stub.Length + table.Length;

            ushort[] loads = new ushort[] { 0x2000, 0x3000, 0x5000, 0x8000 };
            foreach (ushort load in loads)
            {
                int bias = ExpectedBiasFor(load, codeOffset);
                Tiny6502 cpu = RunStubAt(load);

                int wideSite = load + codeOffset + WideReferenceOffset;
                int gotWide = cpu.Memory[wideSite] | (cpu.Memory[wideSite + 1] << 8);
                Assert.AreEqual((ushort)(WideReferenceValue + bias), (ushort)gotWide,
                    "load $" + load.ToString("X4") + ": the whole address must move by the bias");

                int byteSite = load + codeOffset + ByteReferenceOffset;
                Assert.AreEqual((byte)(ByteReferenceValue + bias), cpu.Memory[byteSite],
                    "load $" + load.ToString("X4") + ": the low byte must move by the bias");

                Assert.AreEqual((ushort)(CodeBase + bias), cpu.PC,
                    "load $" + load.ToString("X4") + ": the entry must be entered where the code is");
            }
        }

        [TestMethod]
        public void LeavesEverythingAloneAtItsDesignedAddress()
        {
            // The address the record is written to load at. Here the bias is zero,
            // so a correct stub changes nothing: this is the case a normal GEOS
            // launch produces, and it must be a no-op.
            byte[] table = Table();
            List<Diagnostic> problems = new List<Diagnostic>();
            byte[] stub = GeosStub.Assemble(CodeBase, table.Length, problems);
            int codeOffset = stub.Length + table.Length;
            ushort load = (ushort)(CodeBase - codeOffset);

            Tiny6502 cpu = RunStubAt(load);

            int wideSite = load + codeOffset + WideReferenceOffset;
            int gotWide = cpu.Memory[wideSite] | (cpu.Memory[wideSite + 1] << 8);
            Assert.AreEqual(WideReferenceValue, (ushort)gotWide,
                "at its own address the whole address must be untouched");
            int byteSite = load + codeOffset + ByteReferenceOffset;
            Assert.AreEqual(ByteReferenceValue, cpu.Memory[byteSite],
                "at its own address the low byte must be untouched");
            Assert.AreEqual(CodeBase, cpu.PC, "the entry must be the code's own address");
        }

        [TestMethod]
        public void TouchesOnlyTheBytesTheTableNames()
        {
            // A width of 1 must move one byte and a width of 2 must move two. A
            // stub that moved a whole word either way would corrupt the byte
            // beside the reference, which no test of the value alone would see.
            const ushort load = 0x3000;
            byte[] table = Table();
            byte[] code = Code();
            List<Diagnostic> problems = new List<Diagnostic>();
            byte[] stub = GeosStub.Assemble(CodeBase, table.Length, problems);
            int codeOffset = stub.Length + table.Length;

            // A sentinel after the wide reference and after the byte reference.
            byte[] marker = (byte[])code.Clone();
            marker[WideReferenceOffset + 2] = 0xAA;
            marker[ByteReferenceOffset + 1] = 0xBB;

            Tiny6502 cpu = new Tiny6502();
            cpu.Load(stub, load);
            cpu.Load(table, (ushort)(load + stub.Length));
            cpu.Load(marker, (ushort)(load + codeOffset));
            cpu.Memory[0x886C] = (byte)(load & 0xFF);
            cpu.Memory[0x886D] = (byte)(load >> 8);
            cpu.PC = load;
            int bias = ExpectedBiasFor(load, codeOffset);
            cpu.RunUntil((ushort)(CodeBase + bias), 100000);

            int base_ = load + codeOffset;
            Assert.AreEqual(0xAA, cpu.Memory[base_ + WideReferenceOffset + 2],
                "the byte after a two byte reference must not be moved");
            Assert.AreEqual(0xBB, cpu.Memory[base_ + ByteReferenceOffset + 1],
                "the byte after a one byte reference must not be moved");
        }
    }

    /// <summary>
    /// Just enough 6502 to run the relocation stub: the opcodes the stub uses and
    /// nothing else, so an opcode the stub should not contain is a failure rather
    /// than something quietly tolerated.
    /// </summary>
    internal sealed class Tiny6502
    {
        public byte[] Memory = new byte[0x10000];
        public int A, X, Y, S = 0xFF;
        public int PC;
        private bool carry, zero, negative;

        public void Load(byte[] data, ushort at)
        {
            for (int i = 0; i < data.Length; i++)
                Memory[(at + i) & 0xFFFF] = data[i];
        }

        public void RunUntil(ushort address, int limit)
        {
            int steps = 0;
            while (PC != address)
            {
                if (++steps > limit)
                    throw new InvalidOperationException(
                        "the stub did not reach $" + address.ToString("X4") + " in " + limit + " steps");
                Step();
            }
        }

        private byte Fetch()
        {
            return Memory[PC++ & 0xFFFF];
        }

        private int FetchWord()
        {
            int lo = Fetch();
            int hi = Fetch();
            return lo | (hi << 8);
        }

        private void SetNZ(int v)
        {
            v &= 0xFF;
            zero = v == 0;
            negative = (v & 0x80) != 0;
        }

        private void Adc(int m)
        {
            int sum = A + m + (carry ? 1 : 0);
            carry = sum > 0xFF;
            A = sum & 0xFF;
            SetNZ(A);
        }

        private void Sbc(int m)
        {
            int sum = A - m - (carry ? 0 : 1);
            carry = sum >= 0;
            A = sum & 0xFF;
            SetNZ(A);
        }

        private void Compare(int m)
        {
            int r = A - m;
            carry = A >= m;
            zero = (A & 0xFF) == (m & 0xFF);
            negative = (r & 0x80) != 0;
        }

        private int Zp() { return Fetch(); }

        private int ZpWord() { int lo = Zp(); return lo | (Zp() << 8); }

        private int Abs() { return FetchWord(); }

        private int AbsY() { return (Abs() + Y) & 0xFFFF; }

        private int AbsX() { return (Abs() + X) & 0xFFFF; }

        private int IndY()
        {
            int zp = Zp();
            int baseAddr = Memory[zp] | (Memory[(zp + 1) & 0xFF] << 8);
            return (baseAddr + Y) & 0xFFFF;
        }

        private bool Branch(bool take)
        {
            int offset = (sbyte)Fetch();
            if (take) PC = (PC + offset) & 0xFFFF;
            return take;
        }

        public void Step()
        {
            byte op = Fetch();
            switch (op)
            {
                case 0x18: carry = false; break;                       // CLC
                case 0x38: carry = true; break;                        // SEC
                case 0xA9: A = Fetch(); SetNZ(A); break;               // LDA #imm
                case 0xA5: A = Memory[Zp()]; SetNZ(A); break;          // LDA zp
                case 0xAD: A = Memory[Abs()]; SetNZ(A); break;         // LDA abs
                case 0xB1: A = Memory[IndY()]; SetNZ(A); break;        // LDA (zp),Y
                case 0x85: Memory[Zp()] = (byte)A; break;              // STA zp
                case 0x8D: Memory[Abs()] = (byte)A; break;             // STA abs
                case 0x91: Memory[IndY()] = (byte)A; break;            // STA (zp),Y
                case 0x69: Adc(Fetch()); break;                        // ADC #imm
                case 0x65: Adc(Memory[Zp()]); break;                  // ADC zp
                case 0x6D: Adc(Memory[Abs()]); break;                 // ADC abs
                case 0x71: Adc(Memory[IndY()]); break;                 // ADC (zp),Y
                case 0xE5: Sbc(Memory[Zp()]); break;                  // SBC zp
                case 0xED: Sbc(Memory[Abs()]); break;                 // SBC abs
                case 0xE9: Sbc(Fetch()); break;                        // SBC #imm
                case 0x09: A |= Fetch(); SetNZ(A); break;              // ORA #imm
                case 0x05: A |= Memory[Zp()]; SetNZ(A); break;         // ORA zp
                case 0x0D: A |= Memory[Abs()]; SetNZ(A); break;        // ORA abs
                case 0xC9: Compare(Fetch()); break;                    // CMP #imm
                case 0xC5: Compare(Memory[Zp()]); break;              // CMP zp
                case 0xCD: Compare(Memory[Abs()]); break;             // CMP abs
                case 0xA0: Y = Fetch(); SetNZ(Y); break;               // LDY #imm
                case 0xA2: X = Fetch(); SetNZ(X); break;               // LDX #imm
                case 0xC8: Y = (Y + 1) & 0xFF; SetNZ(Y); break;        // INY
                case 0xE8: X = (X + 1) & 0xFF; SetNZ(X); break;        // INX
                case 0xCA: X = (X - 1) & 0xFF; SetNZ(X); break;        // DEX
                case 0x88: Y = (Y - 1) & 0xFF; SetNZ(Y); break;        // DEY
                case 0xC6: { int z = Zp(); Memory[z] = (byte)(Memory[z] - 1); } break; // DEC zp
                case 0xCE: { int a = Abs(); Memory[a] = (byte)(Memory[a] - 1); } break; // DEC abs
                case 0xE6: { int z = Zp(); Memory[z] = (byte)(Memory[z] + 1); } break; // INC zp
                case 0xEE: { int a = Abs(); Memory[a] = (byte)(Memory[a] + 1); } break; // INC abs
                case 0x4C: PC = Abs(); break;                          // JMP abs
                case 0x6C: { int p = Abs(); PC = Memory[p] | (Memory[(p + 1) & 0xFFFF] << 8); } break; // JMP (abs)
                case 0x60: { int lo = Memory[S]; int hi = Memory[(S - 1) & 0xFF]; S = (S - 2) & 0xFF; PC = lo | (hi << 8); } break; // RTS
                case 0xD0: Branch(!zero); break;                        // BNE
                case 0xF0: Branch(zero); break;                         // BEQ
                case 0xB0: Branch(carry); break;                        // BCS
                case 0x90: Branch(!carry); break;                       // BCC
                case 0x48: Memory[S-- & 0xFF] = (byte)A; break;        // PHA
                case 0x68: A = Memory[S++ & 0xFF]; SetNZ(A); break;    // PLA
                case 0xEA: break;                                        // NOP
                case 0x8A: A = X; SetNZ(A); break;                      // TXA
                case 0x98: A = Y; SetNZ(A); break;                      // TYA
                case 0xAA: X = A; SetNZ(X); break;                      // TAX
                case 0xA8: Y = A; SetNZ(Y); break;                      // TAY
                default:
                    throw new InvalidOperationException(
                        "unimplemented opcode $" + op.ToString("X2")
                        + " at $" + ((PC - 1) & 0xFFFF).ToString("X4"));
            }
        }
    }
}
