// WinASM65 - the execution core as a debugger sees it
//
// The relocated core is proven twice, by two different questions. The suite in
// TestCpu6502Tests.cs asks whether the instructions are right. This one asks
// whether a user interface can drive the machine: can it stop it, can it be told
// why it stopped, and can it tell a machine that ran from one that stood still.
//
// A core that executes perfectly and cannot be stopped is not usable by a
// debugger, and a stop that does not say why is a stop the caller has to guess
// about. That is what these tests are for.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Execution;

namespace WinASM65.Tests
{
    [TestClass]
    public class ExecutionCoreTests
    {
        /// <summary>An address the program jumps to when it has nothing left to do.</summary>
        private const ushort Halt = 0x9000;

        /// <summary>Where the test programs are loaded.</summary>
        private const ushort Origin = 0x8000;

        private static Cpu6502Core Programmed(byte[] code)
        {
            Cpu6502Core cpu = new Cpu6502Core();
            cpu.LoadProgram(Origin, code, Halt);
            return cpu;
        }

        /// <summary>
        /// A program that ends by jumping back to its own first instruction.
        /// <para>
        /// A program that runs off the end would execute whatever zero memory
        /// holds, and a run with no stop condition has to end on its budget
        /// rather than on an accident. The loop makes the end deliberate.
        /// </para>
        /// </summary>
        private static Cpu6502Core Looping(byte[] code)
        {
            List<byte> program = new List<byte>(code);
            program.AddRange(new byte[] { 0x4C, (byte)(Origin & 0xFF), (byte)(Origin >> 8) });
            return Programmed(program.ToArray());
        }

        /// <summary>A run with a small budget, for a program that would not stop.</summary>
        private static ExecutionOptions Budget(long steps)
        {
            ExecutionOptions options = new ExecutionOptions();
            options.MaxSteps = steps;
            return options;
        }

        [TestMethod]
        public void ASingleStepExecutesOneInstructionAndSaysSo()
        {
            Cpu6502Core cpu = Programmed(new byte[]
            {
                0xA9, 0x42,       // LDA #$42
                0x4C, 0x00, 0x90  // JMP $9000
            });

            ExecutionStop stop = cpu.Step();

            Assert.AreEqual(ExecutionStopReason.StepComplete, stop.Reason);
            Assert.AreEqual(0x42, cpu.A, "exactly one instruction ran");
            Assert.AreEqual(0x8002, cpu.PC, "the program counter is past LDA #$42");
            Assert.AreEqual(1L, cpu.Instructions);
        }

        [TestMethod]
        public void AStepCompletesAtAnInstructionBoundary()
        {
            // The stop a caller receives has to be a state the machine can be in.
            // A stop reported in the middle of an instruction would hand the user
            // a program counter pointing at an operand, and disassembling that
            // would show them a jump to nowhere.
            Cpu6502Core cpu = Programmed(new byte[]
            {
                0x8D, 0x00, 0x20,  // STA $2000
                0x4C, 0x00, 0x90   // JMP $9000
            });
            cpu.A = 0x11;

            cpu.Step();

            Assert.AreEqual(0x8003, cpu.PC, "the whole three byte instruction is done");
            Assert.AreEqual(0x11, cpu[0x2000]);
        }

        [TestMethod]
        public void ARunStopsOnTheBreakpointAndNamesIt()
        {
            Cpu6502Core cpu = Programmed(new byte[]
            {
                0xA9, 0x01,       // LDA #$01
                0xA9, 0x02,       // LDA #$02
                0x4C, 0x00, 0x90  // JMP $9000
            });
            Assert.IsTrue(cpu.AddBreakpoint(0x8002), "the breakpoint was not already set");

            ExecutionStop stop = cpu.Run(ExecutionOptions.Default);

            Assert.AreEqual(ExecutionStopReason.Breakpoint, stop.Reason);
            Assert.AreEqual(0x8002, stop.Address, "the stop is about the breakpoint address");
            Assert.AreEqual(0x8002, cpu.PC);
            Assert.AreEqual(0x01, cpu.A, "the instruction at the breakpoint has not run yet");
        }

        [TestMethod]
        public void ABreakpointSetTwiceIsNotTwoBreakpoints()
        {
            Cpu6502Core cpu = new Cpu6502Core();

            Assert.IsTrue(cpu.AddBreakpoint(0x1234));
            Assert.IsFalse(cpu.AddBreakpoint(0x1234), "the second add changed nothing");
            Assert.AreEqual(1, cpu.BreakpointCount);
        }

        [TestMethod]
        public void ARunIgnoresBreakpointsWhenTheyAreSwitchedOff()
        {
            // A caller that has just written a watched range and then resumes does
            // not want to stop on the address it wrote to get there.
            Cpu6502Core cpu = Looping(new byte[]
            {
                0xA9, 0x07        // LDA #$07
            });
            cpu.AddBreakpoint(0x8002);

            ExecutionOptions options = Budget(3);
            options.CheckBreakpoints = false;
            ExecutionStop stop = cpu.Run(options);

            Assert.AreEqual(ExecutionStopReason.StepBudgetExhausted, stop.Reason);
            Assert.AreEqual(0x07, cpu.A, "the program ran instead of stopping at $8002");
            Assert.AreEqual(3L, cpu.Instructions);
        }

        [TestMethod]
        public void ASingleStepIgnoresBreakpointsOnItsOwnAddress()
        {
            // Stepping is not resuming: the caller has already said which
            // instruction comes next.
            Cpu6502Core cpu = Programmed(new byte[]
            {
                0xA9, 0x05,       // LDA #$05
                0x4C, 0x00, 0x90  // JMP $9000
            });
            cpu.AddBreakpoint(0x8000);

            ExecutionStop stop = cpu.Step();

            Assert.AreEqual(ExecutionStopReason.StepComplete, stop.Reason);
            Assert.AreEqual(0x05, cpu.A);
        }

        [TestMethod]
        public void AReadWatchpointStopsOnTheReadAndLeavesTheByteReadable()
        {
            Cpu6502Core cpu = Programmed(new byte[]
            {
                0xAD, 0x34, 0x12,  // LDA $1234
                0x4C, 0x00, 0x90   // JMP $9000
            });
            cpu[0x1234] = 0x5A;
            cpu.AddWatchpoint(WatchpointKind.Read, 0x1234, 0x1234);

            ExecutionStop stop = cpu.Run(ExecutionOptions.Default);

            Assert.AreEqual(ExecutionStopReason.ReadWatchpoint, stop.Reason);
            Assert.AreEqual(0x1234, stop.Address, "the stop is about the address that was read");
            Assert.AreEqual(0x5A, cpu.A, "the read happened, so the value is in the register");
            Assert.AreEqual(0x8003, cpu.PC, "and the machine stopped past the instruction");
        }

        [TestMethod]
        public void AReadWatchpointCoversAWholeRangeAndNotJustItsEnds()
        {
            Cpu6502Core cpu = Programmed(new byte[]
            {
                0xA2, 0x01,       // LDX #$01
                0xBD, 0x00, 0x12,  // LDA $1200,X   -> reads $1201
                0x4C, 0x00, 0x90   // JMP $9000
            });
            cpu[0x1201] = 0x99;
            cpu.AddWatchpoint(WatchpointKind.Read, 0x1200, 0x120F);

            ExecutionStop stop = cpu.Run(ExecutionOptions.Default);

            Assert.AreEqual(ExecutionStopReason.ReadWatchpoint, stop.Reason);
            Assert.AreEqual(0x1201, stop.Address);
        }

        [TestMethod]
        public void AReadWatchpointDoesNotFireOnAWrite()
        {
            Cpu6502Core cpu = Looping(new byte[]
            {
                0xA9, 0xAA,       // LDA #$AA
                0x8D, 0x78, 0x56   // STA $5678
            });
            cpu.AddWatchpoint(WatchpointKind.Read, 0x5678, 0x5678);

            ExecutionStop stop = cpu.Run(Budget(4));

            Assert.AreEqual(ExecutionStopReason.StepBudgetExhausted, stop.Reason);
            Assert.AreEqual(0xAA, cpu[0x5678], "the write still happened");
        }

        [TestMethod]
        public void AWriteWatchpointStopsOnTheWrite()
        {
            Cpu6502Core cpu = Programmed(new byte[]
            {
                0xA9, 0xC3,       // LDA #$C3
                0x8D, 0x00, 0x30,  // STA $3000
                0x4C, 0x00, 0x90   // JMP $9000
            });
            cpu.AddWatchpoint(WatchpointKind.Write, 0x3000, 0x3000);

            ExecutionStop stop = cpu.Run(ExecutionOptions.Default);

            Assert.AreEqual(ExecutionStopReason.WriteWatchpoint, stop.Reason);
            Assert.AreEqual(0x3000, stop.Address);
            Assert.AreEqual(0xC3, cpu[0x3000], "the byte is written even though the run stopped");
        }

        [TestMethod]
        public void AWatchpointAlsoCoversTheIncrementsThatWriteBack()
        {
            // INC is a read and a write. A caller watching for a program to change
            // a byte would otherwise miss the most common way it happens.
            Cpu6502Core cpu = Programmed(new byte[]
            {
                0xE6, 0x80,       // INC $80
                0x4C, 0x00, 0x90  // JMP $9000
            });
            cpu[0x80] = 0x10;
            cpu.AddWatchpoint(WatchpointKind.Write, 0x80, 0x80);

            ExecutionStop stop = cpu.Step();

            Assert.AreEqual(ExecutionStopReason.WriteWatchpoint, stop.Reason);
            Assert.AreEqual(0x11, cpu[0x80]);
        }

        [TestMethod]
        public void ADebuggerReadingMemoryIsNotAnAccessTheProcessorMade()
        {
            // A memory pane refreshing the watched range must not stop the machine.
            // If it did, watching an address would be a way of freezing the program
            // from the display side.
            Cpu6502Core cpu = Programmed(new byte[]
            {
                0xA9, 0x0F,       // LDA #$0F
                0x4C, 0x00, 0x90  // JMP $9000
            });
            cpu.AddWatchpoint(WatchpointKind.Read, 0x0000, 0x00FF);

            byte read = cpu[0x0042];
            ExecutionStop stop = cpu.Step();

            Assert.AreEqual(0x00, read);
            Assert.AreEqual(ExecutionStopReason.StepComplete, stop.Reason);
        }

        [TestMethod]
        public void AWatchpointSetTwiceIsNotTwoWatchpoints()
        {
            Cpu6502Core cpu = new Cpu6502Core();

            Assert.IsTrue(cpu.AddWatchpoint(WatchpointKind.Read, 0x1000, 0x10FF));
            Assert.IsFalse(cpu.AddWatchpoint(WatchpointKind.Read, 0x1000, 0x10FF));
            Assert.IsFalse(cpu.AddWatchpoint(WatchpointKind.Read, 0x1080, 0x1080),
                "a narrower range inside a watched one changes nothing");
            Assert.IsTrue(cpu.AddWatchpoint(WatchpointKind.Write, 0x1000, 0x10FF),
                "a read watchpoint does not shadow a write watchpoint");
            Assert.AreEqual(2, cpu.Watchpoints.Count);
        }

        [TestMethod]
        public void ARemovedWatchpointStopsStoppingTheMachine()
        {
            Cpu6502Core cpu = Looping(new byte[]
            {
                0x8D, 0x00, 0x20   // STA $2000
            });
            Assert.IsTrue(cpu.AddWatchpoint(WatchpointKind.Write, 0x2000, 0x2000));
            Assert.IsTrue(cpu.RemoveWatchpoint(WatchpointKind.Write, 0x2000, 0x2000));
            Assert.IsFalse(cpu.RemoveWatchpoint(WatchpointKind.Write, 0x2000, 0x2000),
                "removing it a second time changed nothing");

            ExecutionStop stop = cpu.Run(Budget(4));

            Assert.AreEqual(ExecutionStopReason.StepBudgetExhausted, stop.Reason);
            Assert.AreEqual(0, cpu.Watchpoints.Count);
        }

        [TestMethod]
        public void ABudgetThatRunsOutIsAStopAndNotAnException()
        {
            // A program that loops forever is a program the user is watching. The
            // answer is where it was, not a crash, and the machine says so itself.
            Cpu6502Core cpu = Looping(new byte[0]);

            ExecutionStop stop = cpu.Run(Budget(10));

            Assert.AreEqual(ExecutionStopReason.StepBudgetExhausted, stop.Reason);
            Assert.AreEqual(0x8000, stop.Address);
            Assert.AreEqual(10L, cpu.Instructions, "exactly the budget, no more");
        }

        [TestMethod]
        public void AColdMachineIsDistinguishableFromOneThatHasNotRun()
        {
            // The reading the monitor's CPU panel relies on: a machine that has
            // been reset and not run is a machine at $8000 with no cycles, and it
            // must not be mistaken for one that ran a few instructions and came
            // back to the same place.
            Cpu6502Core cpu = Programmed(new byte[]
            {
                0x4C, 0x00, 0x90  // JMP $9000
            });

            Assert.IsTrue(cpu.CaptureState().LooksPoweredDown, "nothing has run yet");

            cpu.Reset();
            cpu.Step();
            cpu.Step();

            Assert.IsFalse(cpu.CaptureState().LooksPoweredDown, "the machine has run");
        }

        [TestMethod]
        public void AResetPutsTheMachineBackButKeepsWhatTheDebuggerAskedFor()
        {
            Cpu6502Core cpu = Programmed(new byte[]
            {
                0xA9, 0x33,       // LDA #$33
                0x4C, 0x00, 0x90  // JMP $9000
            });
            cpu.AddBreakpoint(0x8002);
            cpu.AddWatchpoint(WatchpointKind.Read, 0x3000, 0x30FF);
            cpu.Step();

            cpu.Reset();

            Assert.AreEqual(0, cpu.A, "the accumulator is empty again");
            Assert.AreEqual(0L, cpu.Instructions, "and so is the instruction count");
            Assert.AreEqual(0L, cpu.Cycles);
            Assert.AreEqual(0x8000, cpu.PC, "the machine is back at what the reset vector says");
            Assert.AreEqual(1, cpu.BreakpointCount, "a breakpoint belongs to the user, not the machine");
            Assert.AreEqual(1, cpu.Watchpoints.Count);
        }

        [TestMethod]
        public void TheStateAPanelShowsCarriesTheSixRegistersAndBothCounters()
        {
            Cpu6502Core cpu = Programmed(new byte[]
            {
                0xA9, 0x12,       // LDA #$12
                0xA2, 0x34,       // LDX #$34
                0xA0, 0x56,       // LDY #$56
                0x38,             // SEC
                0x4C, 0x00, 0x90  // JMP $9000
            });
            for (int i = 0; i < 4; i++)
                cpu.Step();

            ProcessorState state = cpu.CaptureState();

            Assert.AreEqual(0x8007, state.Pc);
            Assert.AreEqual(0x12, state.A);
            Assert.AreEqual(0x34, state.X);
            Assert.AreEqual(0x56, state.Y);
            Assert.AreEqual(cpu.SP, state.Sp);
            Assert.AreEqual(0x25, state.Ps, "carry and interrupt disable set, unused bit set, break bit clear");
            Assert.AreEqual(cpu.Cycles, state.Cycles);
            Assert.AreEqual(4L, state.Instructions);
        }

        [TestMethod]
        public void TheStatusByteCanBeSetAsOneValue()
        {
            Cpu6502Core cpu = new Cpu6502Core();
            cpu.Status = 0xFF;

            Assert.IsTrue(cpu.Carry);
            Assert.IsTrue(cpu.Zero);
            Assert.IsTrue(cpu.InterruptDisable);
            Assert.IsTrue(cpu.Decimal);
            Assert.IsTrue(cpu.Overflow);
            Assert.IsTrue(cpu.Negative);

            cpu.Status = 0x00;
            Assert.IsFalse(cpu.Carry);
            Assert.IsFalse(cpu.Negative);
        }

        [TestMethod]
        public void CyclesAreCountedAsInstructionsRun()
        {
            Cpu6502Core cpu = Programmed(new byte[]
            {
                0xA9, 0x50,       // LDA #$50    2 cycles
                0x8D, 0x00, 0x20,  // STA $2000   4 cycles
                0x4C, 0x00, 0x90   // JMP $9000   3 cycles
            });

            cpu.Step();
            Assert.AreEqual(2L, cpu.Cycles);
            cpu.Step();
            Assert.AreEqual(6L, cpu.Cycles);
            cpu.Step();
            Assert.AreEqual(9L, cpu.Cycles);
        }

        /// <summary>
        /// Every opcode the core answers to, and the cycles the published table
        /// gives it. Written out here rather than read back from the table, so a
        /// mistyped entry is caught instead of being confirmed by the same data it
        /// came from.
        /// </summary>
        private const string PublishedCycles =
            "00:7 01:6 05:3 06:5 08:3 09:2 0A:2 0D:4 0E:6 " +
            "10:2 11:5 15:4 16:6 18:2 19:4 1D:4 1E:7 " +
            "20:6 21:6 24:3 25:3 26:5 28:4 29:2 2A:2 2C:4 2D:4 2E:6 " +
            "30:2 31:5 35:4 36:6 38:2 39:4 3D:4 3E:7 " +
            "40:6 41:6 45:3 46:5 48:3 49:2 4A:2 4C:3 4D:4 4E:6 " +
            "50:2 51:5 55:4 56:6 58:2 59:4 5D:4 5E:7 " +
            "60:6 61:6 65:3 66:5 68:3 69:2 6A:2 6C:5 6D:4 6E:6 " +
            "70:2 71:5 75:4 76:6 78:2 79:4 7D:4 7E:7 " +
            "81:6 84:3 85:3 86:3 88:2 8A:2 8C:4 8D:4 8E:4 " +
            "90:2 91:6 94:4 95:4 96:4 98:2 99:5 9A:2 9D:5 " +
            "A0:2 A1:6 A2:2 A4:3 A5:3 A6:3 A8:2 A9:2 AA:2 AC:4 AD:4 AE:4 " +
            "B0:2 B1:5 B4:4 B5:4 B6:4 B8:2 B9:4 BA:2 BC:4 BD:4 BE:4 " +
            "C0:2 C1:6 C4:3 C5:3 C6:5 C8:2 C9:2 CA:2 CC:4 CD:4 CE:6 " +
            "D0:2 D1:5 D5:4 D6:6 D8:2 D9:4 DD:4 DE:7 " +
            "E0:2 E1:6 E4:3 E5:3 E6:5 E8:2 E9:2 EA:2 EC:4 ED:4 EE:6 " +
            "F0:2 F1:5 F5:4 F6:6 F8:2 F9:4 FC:6 FD:4 FE:7 FF:6";

        [TestMethod]
        public void EveryInstructionTheCoreImplementsHasThePublishedCycleCount()
        {
            // A count of zero would make a machine look faster than one that does
            // less work, which is the one thing a cycle counter must never do. A
            // count that is merely wrong is just as bad and harder to notice.
            string[] entries = PublishedCycles.Split(' ');
            List<string> wrong = new List<string>();

            foreach (string entry in entries)
            {
                string[] parts = entry.Split(':');
                byte opcode = byte.Parse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                int expected = int.Parse(parts[1], CultureInfo.InvariantCulture);
                int actual = Cpu6502Core.GetBaseCycles(opcode);
                if (actual != expected)
                    wrong.Add("$" + parts[0] + " is " + actual + ", the table says " + expected);
            }

            Assert.AreEqual(0, wrong.Count, string.Join("; ", wrong));
            Assert.AreEqual(153, entries.Length, "every implemented opcode is on the list");
        }

        [TestMethod]
        public void EveryOpcodeTheCoreRunsHasAPriceAndEveryOneItRefusesHasNone()
        {
            // The list above can only be as complete as the interpreter. This asks
            // the interpreter, one clean machine per opcode so no flag left behind
            // by the previous one can make an instruction look refused: for all 256
            // opcodes, the ones it executes must cost something and the ones it
            // refuses must cost nothing. A price for an instruction nobody can run
            // is a number waiting to be believed.
            List<string> wrong = new List<string>();

            for (int opcode = 0; opcode <= 0xFF; opcode++)
            {
                Cpu6502Core machine = new Cpu6502Core();
                machine.PC = Origin;
                machine[Origin] = (byte)opcode;
                int price = Cpu6502Core.GetBaseCycles((byte)opcode);

                bool ran;
                try
                {
                    machine.Step();
                    ran = true;
                }
                catch (NotSupportedException)
                {
                    ran = false;
                }

                if (ran && price == 0)
                    wrong.Add("$" + opcode.ToString("X2") + " runs and costs nothing");
                if (!ran && price != 0)
                    wrong.Add("$" + opcode.ToString("X2") + " is refused and costs " + price);
            }

            Assert.AreEqual(0, wrong.Count, string.Join("; ", wrong));
        }

        [TestMethod]
        public void AnOpcodeTheCoreDoesNotImplementCostsNothing()
        {
            // $02 is an illegal NMOS opcode. It must not be given a price: the
            // core refuses it before the count is consulted, and a table entry
            // would only let an unimplemented instruction look like a fast one.
            Assert.AreEqual(0, Cpu6502Core.GetBaseCycles(0x02));
        }

        [TestMethod]
        public void AMachineCanRunOnABusTheCallerSupplies()
        {
            // The seam the rest of the toolchain will point at something other than
            // flat RAM. A spy proves the core really goes through it, rather than
            // keeping a private array that happens to agree.
            SpyBus bus = new SpyBus();
            Cpu6502Core cpu = new Cpu6502Core(bus);
            cpu.PC = Origin;
            bus.Write(Origin, 0xA9);
            bus.Write((ushort)(Origin + 1), 0x21);
            bus.Forget();

            cpu.Step();

            Assert.AreEqual(0x21, cpu.A);
            Assert.AreEqual(2, bus.Reads.Count);
            Assert.AreEqual(Origin, bus.Reads[0], "the opcode was fetched through the bus");
            Assert.AreEqual((ushort)(Origin + 1), bus.Reads[1], "and the operand after it");
        }

        [TestMethod]
        public void TheDefaultMachineIsFlatWritableMemory()
        {
            Cpu6502Core cpu = new Cpu6502Core();

            Assert.IsInstanceOfType(cpu.Bus, typeof(RamBus));
            cpu[0xFFFF] = 0x5A;
            Assert.AreEqual(0x5A, cpu[0xFFFF], "the top of the address space is writable");

            RamBus ram = (RamBus)cpu.Bus;
            ram.Fill(0x0000, 0x00FF, 0x7E);
            Assert.AreEqual(0x7E, cpu[0x0000]);
            Assert.AreEqual(0x7E, cpu[0x00FF], "the end of the range is included");
            Assert.AreEqual(0x5A, cpu[0xFFFF], "and nothing past it moved");
        }

        [TestMethod]
        public void ARunWithNothingToStopAtReportsWhereTheMachineStuck()
        {
            Cpu6502Core cpu = new Cpu6502Core();

            ExecutionStop stop = cpu.Run(Budget(0));

            Assert.AreEqual(ExecutionStopReason.StepBudgetExhausted, stop.Reason);
            Assert.AreEqual(0L, cpu.Instructions, "a budget of zero runs nothing");
        }

        [TestMethod]
        public void ARunRefusesToBeToldNothingAboutHowLongItMayGo()
        {
            Cpu6502Core cpu = new Cpu6502Core();

            try
            {
                cpu.Run((ExecutionOptions)null);
                Assert.Fail("a run with no options cannot know when to stop");
            }
            catch (ArgumentNullException)
            {
            }
        }

        [TestMethod]
        public void ARangeWhoseStartIsAboveItsEndIsRefusedRatherThanGuessed()
        {
            // "$FF80-$0010" is two ranges, not one. Accepting it would mean
            // deciding whether the ends are part of it, and getting that backwards
            // would silently never fire.
            try
            {
                new Cpu6502Core().AddWatchpoint(WatchpointKind.Read, 0xFF80, 0x0010);
                Assert.Fail("a range whose start is above its end is not a range");
            }
            catch (ArgumentException)
            {
            }
        }

        [TestMethod]
        public void AMachineRefusesToBeBuiltWithoutMemory()
        {
            try
            {
                new Cpu6502Core(null);
                Assert.Fail("a machine with no bus is not a machine");
            }
            catch (ArgumentNullException)
            {
            }
        }

        [TestMethod]
        public void AStopSaysWhichReasonAndWhichAddress()
        {
            // This string is what a monitor prints, so a caller reading a log can
            // tell a breakpoint from a watchpoint without the type in hand.
            Assert.AreEqual("Breakpoint at $C01A",
                ExecutionStop.BreakpointReached(0xC01A).ToString());
            Assert.AreEqual("ReadWatchpoint at $1200",
                ExecutionStop.WatchpointHit(WatchpointKind.Read, 0x1200).ToString());
            Assert.AreEqual("WriteWatchpoint at $0300",
                ExecutionStop.WatchpointHit(WatchpointKind.Write, 0x0300).ToString());
        }

        [TestMethod]
        public void AWatchpointDescribesItselfTheWayItWasAskedFor()
        {
            Cpu6502Core cpu = new Cpu6502Core();
            cpu.AddWatchpoint(WatchpointKind.Read, 0x1200, 0x1200);
            cpu.AddWatchpoint(WatchpointKind.Write, 0x0000, 0x00FF);

            Assert.AreEqual("read $1200", cpu.Watchpoints[0].ToString());
            Assert.AreEqual("write $0000-$00FF", cpu.Watchpoints[1].ToString());
        }

        /// <summary>A machine stopped at its own first instruction, vectors in place.</summary>
        private static Cpu6502Core Waiting(ushort irqVectorTarget, ushort nmiVectorTarget)
        {
            Cpu6502Core cpu = Programmed(new byte[]
            {
                0xA9, 0x01        // LDA #$01
            });
            cpu[0xFFFE] = (byte)(irqVectorTarget & 0xFF);
            cpu[0xFFFF] = (byte)(irqVectorTarget >> 8);
            cpu[0xFFFA] = (byte)(nmiVectorTarget & 0xFF);
            cpu[0xFFFB] = (byte)(nmiVectorTarget >> 8);
            cpu.InterruptDisable = false;
            return cpu;
        }

        [TestMethod]
        public void AnInterruptIsTakenWhenTheProgramHasNotMaskedIt()
        {
            Cpu6502Core cpu = Waiting(0xC000, 0xD000);

            cpu.Interrupt();

            Assert.AreEqual(0xC000, cpu.PC, "the interrupt vector was taken");
            Assert.IsTrue(cpu.InterruptDisable, "and the processor masked the next one");
        }

        [TestMethod]
        public void AnInterruptIsIgnoredWhileTheProgramHasMaskedIt()
        {
            // The program turned interrupts off, so the machine must not decide
            // otherwise. This is the difference between a machine and a mock.
            Cpu6502Core cpu = Waiting(0xC000, 0xD000);
            cpu.InterruptDisable = true;

            cpu.Interrupt();

            Assert.AreEqual(0x8000, cpu.PC, "the program is still where it was");
            Assert.AreEqual(0L, cpu.Cycles, "and nothing was executed");
        }

        [TestMethod]
        public void ANonMaskableInterruptIsTakenEvenWhenTheProgramHasMaskedIt()
        {
            // The one interrupt a program cannot refuse is the one that lets a
            // watchdog work, and a core that let the flag hide it would be a
            // machine able to explain away its own hangs.
            Cpu6502Core cpu = Waiting(0xC000, 0xD000);
            cpu.InterruptDisable = true;

            cpu.NonMaskableInterrupt();

            Assert.AreEqual(0xD000, cpu.PC, "the non-maskable vector, not the masked one");
            Assert.IsTrue(cpu.InterruptDisable);
        }

        [TestMethod]
        public void AnInterruptPushesTheNextAddressAndTheFlagsWithoutTheBreakBit()
        {
            Cpu6502Core cpu = Waiting(0xC000, 0xD000);
            cpu.Carry = true;
            ushort returnAddress = cpu.PC;

            cpu.Interrupt();

            byte flags = cpu.Pull();
            Assert.AreEqual(returnAddress, cpu.Pull16(), "a handler returns to the next instruction");
            Assert.IsTrue((flags & 0x01) != 0, "carry was pushed");
            Assert.AreEqual(0, flags & 0x10, "the break bit is clear, because nothing broke");
            Assert.AreEqual(0x20, flags & 0x20, "the unused bit is set, as it is on the processor");
        }

        [TestMethod]
        public void ASoftwareBreakPushesTheBreakBit()
        {
            Cpu6502Core cpu = Waiting(0xC000, 0xD000);

            cpu.Break();

            Assert.AreEqual(0xC000, cpu.PC);
            Assert.AreEqual(0x10, cpu.Pull() & 0x10, "a break says so on the stack");
        }

        [TestMethod]
        public void TakingAnInterruptCostsTheSameCyclesAsABreakInstruction()
        {
            // Seven, and not zero: a debugger watching the cycle counter would
            // otherwise see the machine stop dead and read as a hang.
            Cpu6502Core cpu = Waiting(0xC000, 0xD000);

            cpu.Interrupt();
            Assert.AreEqual(7L, cpu.Cycles);
            Assert.AreEqual(0L, cpu.Instructions, "an interrupt is not an instruction");
            Assert.AreEqual(7, Cpu6502Core.InterruptCycles);
            Assert.AreEqual(7, Cpu6502Core.GetBaseCycles(0x00), "and BRK costs the same");
        }
        [TestMethod]
        public void ClearingTheStopsLeavesTheMachineRunning()
        {
            // What the user does when they decide the program is not where they
            // thought it was. Every stop has to be answerable for, or one of them
            // stays on forever.
            Cpu6502Core cpu = Looping(new byte[]
            {
                0x8D, 0x00, 0x20   // STA $2000
            });
            cpu.AddBreakpoint(0x8000);
            cpu.AddWatchpoint(WatchpointKind.Write, 0x2000, 0x2000);
            Assert.IsTrue(cpu.HasBreakpoint(0x8000));

            cpu.ClearBreakpoints();
            cpu.ClearWatchpoints();

            Assert.IsFalse(cpu.HasBreakpoint(0x8000));
            Assert.IsFalse(cpu.RemoveBreakpoint(0x8000), "it was already gone");
            Assert.AreEqual(0, cpu.BreakpointCount);
            Assert.AreEqual(0, cpu.Watchpoints.Count);

            ExecutionStop stop = cpu.Run(Budget(4));
            Assert.AreEqual(ExecutionStopReason.StepBudgetExhausted, stop.Reason);
        }

        /// <summary>A bus that records the addresses the processor went through.</summary>
        private sealed class SpyBus : ICpuBus
        {
            private readonly Dictionary<ushort, byte> _memory = new Dictionary<ushort, byte>();

            public IList<ushort> Reads { get; private set; }

            public SpyBus()
            {
                Reads = new List<ushort>();
            }

            /// <summary>Forgets what was read, so a test can watch one run only.</summary>
            public void Forget()
            {
                Reads.Clear();
            }

            public byte Read(ushort address)
            {
                Reads.Add(address);
                byte value;
                return _memory.TryGetValue(address, out value) ? value : (byte)0;
            }

            public void Write(ushort address, byte value)
            {
                _memory[address] = value;
            }
        }
    }
}
