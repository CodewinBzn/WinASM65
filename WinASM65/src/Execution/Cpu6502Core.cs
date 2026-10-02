using System;
using System.Collections.Generic;

namespace WinASM65.Execution
{
    /// <summary>
    /// A small NMOS 6502, used to run the code the toolchain generates.
    /// <para>
    /// Two of the plans' tasks produce 6502 that nothing else can check: the
    /// relocator and the decompressor stub. Comparing their bytes to an expected
    /// array only proves the generator is still itself. Running them proves they
    /// work, and running them here is the difference between "these bytes were
    /// produced" and "this code relocates the image".
    /// </para>
    /// <para>
    /// It started as a test fixture and is now the toolchain's own deterministic
    /// machine: a debugger can be pointed at it without an emulator being
    /// installed, and it answers the same questions an emulator answers — the
    /// registers, the memory, why it stopped. What it still refuses is what no
    /// generated code uses: the documented instruction set and nothing else, and
    /// decimal mode refused rather than faked.
    /// </para>
    /// <para>
    /// It executes the processor and nothing else. There is no video, no timing
    /// and no hardware, so a capability that a user interface can switch on has to
    /// be answered by the type that owns the machine, not by this one.
    /// </para>
    /// </summary>
    public sealed class Cpu6502Core
    {
        public const ushort IrqVector = 0xFFFE;
        public const ushort ResetVector = 0xFFFC;

        private readonly ICpuBus _bus;
        private readonly HashSet<ushort> _breakpoints = new HashSet<ushort>();
        private readonly WatchpointSet _watchpoints = new WatchpointSet();

        // Set only while an instruction is executing, so a debugger reading memory
        // through the bus is never reported as an access the processor made.
        private bool _executing;
        private ExecutionStop _watchHit;

        /// <summary>A machine over flat, writable 64K of memory.</summary>
        public Cpu6502Core()
            : this(new RamBus())
        {
        }

        /// <summary>A machine over a bus the caller supplies.</summary>
        public Cpu6502Core(ICpuBus bus)
        {
            if (bus == null)
                throw new ArgumentNullException("bus");
            _bus = bus;
            Reset();
        }

        /// <summary>
        /// The memory this machine runs on. A debugger reads and writes through it
        /// directly; doing so is not an access the processor made, so it never
        /// triggers a watchpoint.
        /// </summary>
        public ICpuBus Bus
        {
            get { return _bus; }
        }

        public ushort PC { get; set; }
        public byte A { get; set; }
        public byte X { get; set; }
        public byte Y { get; set; }
        public byte SP { get; set; }

        public bool Carry { get; set; }
        public bool Zero { get; set; }
        public bool InterruptDisable { get; set; }
        public bool Decimal { get; set; }
        public bool Overflow { get; set; }
        public bool Negative { get; set; }

        /// <summary>
        /// The status register as one byte: the unused bit set, the break bit
        /// clear. A caller setting it sets the six flags and ignores both of those,
        /// which is what the processor does with a byte it pulls.
        /// </summary>
        public byte Status
        {
            get { return Flags(false); }
            set { SetFlags(value); }
        }

        public long Instructions { get; private set; }

        /// <summary>
        /// Cycles executed since the last reset, counted at base cost per opcode.
        /// It is how a caller tells a machine that ran from one that stood still.
        /// </summary>
        public long Cycles { get; private set; }

        /// <summary>The base cost of an opcode, in cycles.</summary>
        public static int GetBaseCycles(byte opcode)
        {
            return Cpu6502CycleTable.Base(opcode);
        }

        /// <summary>
        /// Everything a debugger panel shows, read at one instant.
        /// <para>
        /// Taken before a run rather than after: a caller that renders a machine
        /// which is moving wants the state it can show now, and the reason it
        /// stopped says which instruction the next read should be about.
        /// </para>
        /// </summary>
        public ProcessorState CaptureState()
        {
            return new ProcessorState(PC, A, X, Y, SP, Status, Cycles, Instructions);
        }

        /// <summary>
        /// Puts the processor back at the reset vector with empty registers.
        /// <para>
        /// What the debugger asked to be told about survives: the machine is in a
        /// new state, but the breakpoints and watchpoints belong to the user, and
        /// losing them because a program jumped to its own entry point would be
        /// the machine deciding what the user asked for.
        /// </para>
        /// </summary>
        public void Reset()
        {
            SP = 0xFD;
            Carry = false;
            Zero = false;
            InterruptDisable = true;
            Decimal = false;
            Overflow = false;
            Negative = false;
            A = 0;
            X = 0;
            Y = 0;
            Instructions = 0;
            Cycles = 0;
            PC = Peek16(ResetVector);
        }

        public byte this[ushort address]
        {
            get { return _bus.Read(address); }
            set { _bus.Write(address, value); }
        }

        public void Load(ushort address, byte[] data)
        {
            if (data == null)
                return;
            for (int i = 0; i < data.Length; i++)
                _bus.Write((ushort)(address + i), data[i]);
        }

        public ushort Peek16(ushort address)
        {
            // The 6501 bug wrapped the pointer inside page zero. Nothing
            // generated here needs it, and reproducing it silently would make a
            // passing test mean something slightly weaker than it looks.
            return (ushort)(_bus.Read(address) | (_bus.Read((ushort)(address + 1)) << 8));
        }

        /// <summary>Places a routine and its end address, the way a machine would.</summary>
        public void LoadProgram(ushort address, byte[] code, ushort returnAddress)
        {
            Load(address, code);
            _bus.Write(ResetVector, (byte)(address & 0xFF));
            _bus.Write((ushort)(ResetVector + 1), (byte)(address >> 8));
            Push16(returnAddress);
            PC = address;
        }

        public void Push(byte value)
        {
            _bus.Write(SP, value);
            SP--;
        }

        public byte Pull()
        {
            SP++;
            return _bus.Read(SP);
        }

        public void Push16(ushort value)
        {
            Push((byte)(value >> 8));
            Push((byte)(value & 0xFF));
        }

        public ushort Pull16()
        {
            byte low = Pull();
            byte high = Pull();
            return (ushort)(low | (high << 8));
        }

        // ------------------------------------------------------------------ stops

        /// <summary>
        /// Stops execution at the next address whose execution the debugger asked
        /// to be told about. False when the address was already being watched:
        /// that is a state already reached, not a failure.
        /// </summary>
        public bool AddBreakpoint(ushort address)
        {
            return _breakpoints.Add(address);
        }

        public bool RemoveBreakpoint(ushort address)
        {
            return _breakpoints.Remove(address);
        }

        public void ClearBreakpoints()
        {
            _breakpoints.Clear();
        }

        public bool HasBreakpoint(ushort address)
        {
            return _breakpoints.Contains(address);
        }

        public int BreakpointCount
        {
            get { return _breakpoints.Count; }
        }

        /// <summary>
        /// Adds a watchpoint over a straight range of addresses. False when a
        /// watchpoint of the same kind already covers the range.
        /// </summary>
        public bool AddWatchpoint(WatchpointKind kind, ushort start, ushort end)
        {
            return _watchpoints.Add(new Watchpoint(kind, start, end));
        }

        public bool RemoveWatchpoint(WatchpointKind kind, ushort start, ushort end)
        {
            return _watchpoints.Remove(kind, start, end);
        }

        public void ClearWatchpoints()
        {
            _watchpoints.Clear();
        }

        /// <summary>The watchpoints in effect, in the order they were added.</summary>
        public IList<Watchpoint> Watchpoints
        {
            get { return _watchpoints.Items; }
        }

        // ------------------------------------------------------------------ running

        /// <summary>
        /// Runs until the program branches out through a jump to this address,
        /// or until the step budget runs out.
        /// </summary>
        public void Run(ushort stopAt, long maxSteps = ExecutionOptions.DefaultMaxSteps)
        {
            for (long i = 0; i < maxSteps; i++)
            {
                if (PC == stopAt)
                    return;
                Step();
            }
            throw new InvalidOperationException(
                "The program did not reach $" + stopAt.ToString("X4")
                + " within " + maxSteps + " instructions; it is at $" + PC.ToString("X4") + ".");
        }

        /// <summary>
        /// Runs until a stop condition is met, and says which one.
        /// <para>
        /// The run stops at an instruction boundary. A watchpoint is noticed while
        /// the instruction that caused it is executing, and the machine is allowed
        /// to finish that instruction before it stops: reporting a watchpoint with
        /// the program counter half way through the instruction that set it would
        /// hand the user a state no machine is ever in, and the address to
        /// disassemble would be the middle of an operand.
        /// </para>
        /// </summary>
        public ExecutionStop Run(ExecutionOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            for (long i = 0; i < options.MaxSteps; i++)
            {
                if (options.CheckBreakpoints && _breakpoints.Contains(PC))
                    return ExecutionStop.BreakpointReached(PC);

                ExecutionStop stop = Step();
                if (stop.Reason != ExecutionStopReason.StepComplete)
                    return stop;
            }

            return ExecutionStop.BudgetExhausted(PC);
        }

        /// <summary>
        /// Executes one instruction and says why it stopped.
        /// <para>
        /// A single step is not a resume, so breakpoints are not consulted: the
        /// caller has already said which instruction comes next. A watchpoint is
        /// still reported, because the instruction the user asked for is the
        /// instruction that touched the address they are watching.
        /// </para>
        /// </summary>
        public ExecutionStop Step()
        {
            _watchHit = null;
            _executing = true;
            try
            {
                byte opcode = Read(PC);
                PC++;
                Instructions++;
                Execute(opcode);
                Cycles += GetBaseCycles(opcode);
            }
            finally
            {
                _executing = false;
            }

            if (_watchHit != null)
                return _watchHit;
            return ExecutionStop.StepComplete(PC);
        }

        private void Execute(byte op)
        {
            switch (op)
            {
                // ---- load and store
                case 0xA9: A = Read(PC++); SetNZ(A); return;
                case 0xA5: A = Read(Zp(Read(PC++))); SetNZ(A); return;
                case 0xB5: A = Read((ushort)(Zp(Read(PC++)) + X)); SetNZ(A); return;
                case 0xAD: A = Read(Next16()); SetNZ(A); return;
                case 0xBD: A = Read((ushort)(Next16() + X)); SetNZ(A); return;
                case 0xB9: A = Read((ushort)(Next16() + Y)); SetNZ(A); return;
                case 0xA1: A = Read((ushort)(ZpIndexedX(Read(PC++)))); SetNZ(A); return;
                case 0xB1: A = Read(IndirectY(Read(PC++))); SetNZ(A); return;

                case 0xA2: X = Read(PC++); SetNZ(X); return;
                case 0xA6: X = Read(Zp(Read(PC++))); SetNZ(X); return;
                case 0xB6: X = Read((ushort)(Zp(Read(PC++)) + Y)); SetNZ(X); return;
                case 0xAE: X = Read(Next16()); SetNZ(X); return;
                case 0xBE: X = Read((ushort)(Next16() + Y)); SetNZ(X); return;

                case 0xA0: Y = Read(PC++); SetNZ(Y); return;
                case 0xA4: Y = Read(Zp(Read(PC++))); SetNZ(Y); return;
                case 0xB4: Y = Read((ushort)(Zp(Read(PC++)) + X)); SetNZ(Y); return;
                case 0xAC: Y = Read(Next16()); SetNZ(Y); return;
                case 0xBC: Y = Read((ushort)(Next16() + X)); SetNZ(Y); return;

                case 0x85: Write(Zp(Read(PC++)), A); return;
                case 0x95: Write((ushort)(Zp(Read(PC++)) + X), A); return;
                case 0x8D: Write(Next16(), A); return;
                case 0x9D: Write((ushort)(Next16() + X), A); return;
                case 0x99: Write((ushort)(Next16() + Y), A); return;
                case 0x81: Write(ZpIndexedX(Read(PC++)), A); return;
                case 0x91: Write(IndirectY(Read(PC++)), A); return;

                case 0x86: Write(Zp(Read(PC++)), X); return;
                case 0x96: Write((ushort)(Zp(Read(PC++)) + Y), X); return;
                case 0x8E: Write(Next16(), X); return;
                case 0x84: Write(Zp(Read(PC++)), Y); return;
                case 0x94: Write((ushort)(Zp(Read(PC++)) + X), Y); return;
                case 0x8C: Write(Next16(), Y); return;

                // ---- transfers
                case 0xAA: X = A; SetNZ(X); return;
                case 0xA8: Y = A; SetNZ(Y); return;
                case 0x8A: A = X; SetNZ(A); return;
                case 0x98: A = Y; SetNZ(A); return;
                case 0xBA: X = SP; SetNZ(X); return;
                case 0x9A: SP = X; return;

                // ---- stack
                case 0x48: Push(A); return;
                case 0x68: A = Pull(); SetNZ(A); return;
                case 0x08: Push(Flags(true)); return;
                case 0x28: SetFlags(Pull()); return;

                // ---- logic
                case 0x29: A &= Read(PC++); SetNZ(A); return;
                case 0x25: A &= Read(Zp(Read(PC++))); SetNZ(A); return;
                case 0x35: A &= Read((ushort)(Zp(Read(PC++)) + X)); SetNZ(A); return;
                case 0x2D: A &= Read(Next16()); SetNZ(A); return;
                case 0x3D: A &= Read((ushort)(Next16() + X)); SetNZ(A); return;
                case 0x39: A &= Read((ushort)(Next16() + Y)); SetNZ(A); return;
                case 0x21: A &= Read(ZpIndexedX(Read(PC++))); SetNZ(A); return;
                case 0x31: A &= Read(IndirectY(Read(PC++))); SetNZ(A); return;

                case 0x09: A |= Read(PC++); SetNZ(A); return;
                case 0x05: A |= Read(Zp(Read(PC++))); SetNZ(A); return;
                case 0x15: A |= Read((ushort)(Zp(Read(PC++)) + X)); SetNZ(A); return;
                case 0x0D: A |= Read(Next16()); SetNZ(A); return;
                case 0x1D: A |= Read((ushort)(Next16() + X)); SetNZ(A); return;
                case 0x19: A |= Read((ushort)(Next16() + Y)); SetNZ(A); return;
                case 0x01: A |= Read(ZpIndexedX(Read(PC++))); SetNZ(A); return;
                case 0x11: A |= Read(IndirectY(Read(PC++))); SetNZ(A); return;

                case 0x49: A ^= Read(PC++); SetNZ(A); return;
                case 0x45: A ^= Read(Zp(Read(PC++))); SetNZ(A); return;
                case 0x55: A ^= Read((ushort)(Zp(Read(PC++)) + X)); SetNZ(A); return;
                case 0x4D: A ^= Read(Next16()); SetNZ(A); return;
                case 0x5D: A ^= Read((ushort)(Next16() + X)); SetNZ(A); return;
                case 0x59: A ^= Read((ushort)(Next16() + Y)); SetNZ(A); return;
                case 0x41: A ^= Read(ZpIndexedX(Read(PC++))); SetNZ(A); return;
                case 0x51: A ^= Read(IndirectY(Read(PC++))); SetNZ(A); return;

                case 0x24: Bit(Read(Zp(Read(PC++)))); return;
                case 0x2C: Bit(Read(Next16())); return;

                // ---- arithmetic
                case 0x69: Adc(Read(PC++)); return;
                case 0x65: Adc(Read(Zp(Read(PC++)))); return;
                case 0x75: Adc(Read((ushort)(Zp(Read(PC++)) + X))); return;
                case 0x6D: Adc(Read(Next16())); return;
                case 0x7D: Adc(Read((ushort)(Next16() + X))); return;
                case 0x79: Adc(Read((ushort)(Next16() + Y))); return;
                case 0x61: Adc(Read(ZpIndexedX(Read(PC++)))); return;
                case 0x71: Adc(Read(IndirectY(Read(PC++)))); return;

                case 0xE9: Sbc(Read(PC++)); return;
                case 0xE5: Sbc(Read(Zp(Read(PC++)))); return;
                case 0xF5: Sbc(Read((ushort)(Zp(Read(PC++)) + X))); return;
                case 0xED: Sbc(Read(Next16())); return;
                case 0xFD: Sbc(Read((ushort)(Next16() + X))); return;
                case 0xF9: Sbc(Read((ushort)(Next16() + Y))); return;
                case 0xE1: Sbc(Read(ZpIndexedX(Read(PC++)))); return;
                case 0xF1: Sbc(Read(IndirectY(Read(PC++)))); return;

                case 0xC9: Compare(A, Read(PC++)); return;
                case 0xC5: Compare(A, Read(Zp(Read(PC++)))); return;
                case 0xD5: Compare(A, Read((ushort)(Zp(Read(PC++)) + X))); return;
                case 0xCD: Compare(A, Read(Next16())); return;
                case 0xDD: Compare(A, Read((ushort)(Next16() + X))); return;
                case 0xD9: Compare(A, Read((ushort)(Next16() + Y))); return;
                case 0xC1: Compare(A, Read(ZpIndexedX(Read(PC++)))); return;
                case 0xD1: Compare(A, Read(IndirectY(Read(PC++)))); return;

                case 0xE0: Compare(X, Read(PC++)); return;
                case 0xE4: Compare(X, Read(Zp(Read(PC++)))); return;
                case 0xEC: Compare(X, Read(Next16())); return;

                case 0xC0: Compare(Y, Read(PC++)); return;
                case 0xC4: Compare(Y, Read(Zp(Read(PC++)))); return;
                case 0xCC: Compare(Y, Read(Next16())); return;

                // ---- increments
                case 0xE6: Inc8(Zp(Read(PC++))); return;
                case 0xF6: Inc8((ushort)(Zp(Read(PC++) ) + X)); return;
                case 0xEE: Inc8(Next16()); return;
                case 0xFE: Inc8((ushort)(Next16() + X)); return;
                case 0xC6: Dec8(Zp(Read(PC++))); return;
                case 0xD6: Dec8((ushort)(Zp(Read(PC++)) + X)); return;
                case 0xCE: Dec8(Next16()); return;
                case 0xDE: Dec8((ushort)(Next16() + X)); return;

                case 0xE8: X++; SetNZ(X); return;
                case 0xCA: X--; SetNZ(X); return;
                case 0xC8: Y++; SetNZ(Y); return;
                case 0x88: Y--; SetNZ(Y); return;

                // ---- shifts
                case 0x0A: A = Shl(A); return;
                case 0x06: ShiftMemory(Shl, Zp(Read(PC++)), true); return;
                case 0x16: ShiftMemory(Shl, (ushort)(Zp(Read(PC++)) + X), true); return;
                case 0x0E: ShiftMemory(Shl, Next16(), true); return;
                case 0x1E: ShiftMemory(Shl, (ushort)(Next16() + X), true); return;

                case 0x4A: A = Shr(A); return;
                case 0x46: ShiftMemory(Shr, Zp(Read(PC++)), true); return;
                case 0x56: ShiftMemory(Shr, (ushort)(Zp(Read(PC++)) + X), true); return;
                case 0x4E: ShiftMemory(Shr, Next16(), true); return;
                case 0x5E: ShiftMemory(Shr, (ushort)(Next16() + X), true); return;

                case 0x2A: A = Rol(A); return;
                case 0x26: ShiftMemory(Rol, Zp(Read(PC++)), true); return;
                case 0x36: ShiftMemory(Rol, (ushort)(Zp(Read(PC++)) + X), true); return;
                case 0x2E: ShiftMemory(Rol, Next16(), true); return;
                case 0x3E: ShiftMemory(Rol, (ushort)(Next16() + X), true); return;

                case 0x6A: A = Ror(A); return;
                case 0x66: ShiftMemory(Ror, Zp(Read(PC++)), true); return;
                case 0x76: ShiftMemory(Ror, (ushort)(Zp(Read(PC++)) + X), true); return;
                case 0x6E: ShiftMemory(Ror, Next16(), true); return;
                case 0x7E: ShiftMemory(Ror, (ushort)(Next16() + X), true); return;

                // ---- jumps and calls
                case 0x4C: PC = Next16(); return;
                case 0x6C:
                {
                    ushort pointer = Next16();
                    PC = Peek16(pointer);
                    return;
                }
                case 0x20:
                {
                    ushort target = Next16();
                    // The program counter already sits past the three byte
                    // instruction, so the processor pushes the address of its
                    // own last byte and RTS adds one. Pushing PC here and
                    // returning to it unchanged would also work, and would land
                    // on the same instruction; but the one that works by
                    // accident is the one that stops working the first time a
                    // stub uses an indirect call, so both halves are done as
                    // the processor does them.
                    Push16((ushort)(PC - 1));
                    PC = target;
                    return;
                }
                case 0x60: PC = (ushort)(Pull16() + 1); return;
                case 0xFF:
                {
                    // JSR (zp),Y is the one call whose target the stub cannot
                    // know when it is written, so it is the one the test CPU
                    // needed. Two byte operand: opcode $FF is the zero page
                    // form, and $FC below is the absolute one. Both push the
                    // address of their own last byte, which is what RTS adds
                    // one to.
                    ushort pointer = Read(PC++);
                    Push16((ushort)(PC - 1));
                    PC = (ushort)(Peek16(pointer) + Y);
                    return;
                }
                case 0xFC:
                {
                    ushort base_ = Next16();
                    Push16((ushort)(PC - 1));
                    ushort at = (ushort)(base_ + X);
                    PC = (ushort)(Read(at) | (Read((ushort)(at + 1)) << 8));
                    return;
                }
                case 0x40:
                {
                    SetFlags(Pull());
                    PC = (ushort)(Pull16() + 1);
                    return;
                }
                case 0x00:
                {
                    Push16(PC);
                    Push(Flags(true));
                    InterruptDisable = true;
                    PC = Peek16(IrqVector);
                    return;
                }

                // ---- branches
                case 0x10: Branch(!Negative); return;
                case 0x30: Branch(Negative); return;
                case 0x50: Branch(!Overflow); return;
                case 0x70: Branch(Overflow); return;
                case 0x90: Branch(!Carry); return;
                case 0xB0: Branch(Carry); return;
                case 0xD0: Branch(!Zero); return;
                case 0xF0: Branch(Zero); return;

                // ---- flags
                case 0x18: Carry = false; return;
                case 0x38: Carry = true; return;
                case 0x58: InterruptDisable = false; return;
                case 0x78: InterruptDisable = true; return;
                case 0xB8: Overflow = false; return;
                case 0xD8: Decimal = false; return;
                case 0xF8: Decimal = true; return;

                case 0xEA: return;

                default:
                    throw new NotSupportedException(
                        "Opcode $" + op.ToString("X2") + " at $"
                        + ((ushort)(PC - 1)).ToString("X4")
                        + " is not implemented. A generated stub should not use it.");
            }
        }

        // ------------------------------------------------------------------ parts

        private byte Read(ushort address)
        {
            byte value = _bus.Read(address);
            WatchpointHit(WatchpointKind.Read, address);
            return value;
        }

        private void Write(ushort address, byte value)
        {
            _bus.Write(address, value);
            WatchpointHit(WatchpointKind.Write, address);
        }

        /// <summary>
        /// Records the first watched address the processor touches during an
        /// instruction, and reports nothing when it is not executing one.
        /// <para>
        /// The first match wins, in the order the watchpoints were added, so the
        /// address a stop reports does not depend on how a set happened to
        /// enumerate. The access itself has already happened by then: a debugger
        /// that stops on a write still sees the byte written, which is the whole
        /// reason to watch a range.
        /// </para>
        /// </summary>
        private void WatchpointHit(WatchpointKind kind, ushort address)
        {
            if (!_executing || _watchHit != null)
                return;

            if (_watchpoints.Match(kind, address) != null)
                _watchHit = ExecutionStop.WatchpointHit(kind, address);
        }

        private ushort Read16(ushort at)
        {
            return Peek16(at);
        }

        /// <summary>
        /// Reads a 16 bit operand and steps over it. Absolute addressing is
        /// almost every instruction in a generated stub, and leaving the program
        /// counter where it was would make the interpreter read its operand
        /// bytes as opcodes.
        /// </summary>
        private ushort Next16()
        {
            ushort value = Peek16(PC);
            PC = (ushort)(PC + 2);
            return value;
        }

        private static ushort Zp(byte value)
        {
            return value;
        }

        private ushort ZpIndexedX(byte zp)
        {
            byte basePointer = (byte)(zp + X);
            return Peek16(basePointer);
        }

        private ushort IndirectY(byte zp)
        {
            ushort pointer = Peek16(zp);
            return (ushort)(pointer + Y);
        }

        private void SetNZ(byte value)
        {
            Zero = value == 0;
            Negative = (value & 0x80) != 0;
        }

        private void Bit(byte value)
        {
            Zero = (A & value) == 0;
            Overflow = (value & 0x40) != 0;
            Negative = (value & 0x80) != 0;
        }

        private void Adc(byte value)
        {
            RefuseDecimal();
            int sum = A + value + (Carry ? 1 : 0);
            Carry = sum > 0xFF;
            byte result = (byte)(sum & 0xFF);
            Overflow = ((A ^ result) & (value ^ result) & 0x80) != 0;
            A = result;
            SetNZ(A);
        }

        private void Sbc(byte value)
        {
            RefuseDecimal();
            Adc((byte)~value);
        }

        private void RefuseDecimal()
        {
            if (Decimal)
            {
                throw new NotSupportedException(
                    "Decimal mode is not implemented. No generated stub sets it, and a stub that "
                    + "did would deserve a test that says so.");
            }
        }

        private void Compare(byte register, byte value)
        {
            int difference = register - value;
            Carry = register >= value;
            SetNZ((byte)(difference & 0xFF));
        }

        private void Inc8(ushort address)
        {
            byte value = (byte)(Read(address) + 1);
            Write(address, value);
            SetNZ(value);
        }

        private void Dec8(ushort address)
        {
            byte value = (byte)(Read(address) - 1);
            Write(address, value);
            SetNZ(value);
        }

        private byte Shl(byte value)
        {
            Carry = (value & 0x80) != 0;
            byte result = (byte)(value << 1);
            SetNZ(result);
            return result;
        }

        private byte Shr(byte value)
        {
            Carry = (value & 0x01) != 0;
            byte result = (byte)(value >> 1);
            SetNZ(result);
            return result;
        }

        private byte Rol(byte value)
        {
            bool before = Carry;
            Carry = (value & 0x80) != 0;
            byte result = (byte)((value << 1) | (before ? 1 : 0));
            SetNZ(result);
            return result;
        }

        private byte Ror(byte value)
        {
            bool before = Carry;
            Carry = (value & 0x01) != 0;
            byte result = (byte)((value >> 1) | (before ? 0x80 : 0));
            SetNZ(result);
            return result;
        }

        private void ShiftMemory(Func<byte, byte> shift, ushort address, bool store)
        {
            byte result = shift(Read(address));
            Write(address, result);
        }

        private void Branch(bool taken)
        {
            sbyte offset = (sbyte)Read(PC++);
            if (taken)
                PC = (ushort)(PC + offset);
        }

        private byte Flags(bool withBreak)
        {
            byte value = 0x20;
            if (Carry) value |= 0x01;
            if (Zero) value |= 0x02;
            if (InterruptDisable) value |= 0x04;
            if (Decimal) value |= 0x08;
            if (Overflow) value |= 0x40;
            if (Negative) value |= 0x80;
            if (withBreak) value |= 0x10;
            return value;
        }

        private void SetFlags(byte value)
        {
            Carry = (value & 0x01) != 0;
            Zero = (value & 0x02) != 0;
            InterruptDisable = (value & 0x04) != 0;
            Decimal = (value & 0x08) != 0;
            Overflow = (value & 0x40) != 0;
            Negative = (value & 0x80) != 0;
        }
    }
}
