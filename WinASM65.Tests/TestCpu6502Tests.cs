using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WinASM65.Tests
{
    /// <summary>
    /// The test CPU, checked against known answers.
    /// <para>
    /// Everything T9 and T11 claim rests on this interpreter being right. A test
    /// fixture that is itself wrong does not fail quietly: it makes the stub
    /// tests pass for the wrong reason, which is worse than having no stub test.
    /// </para>
    /// </summary>
    [TestClass]
    public class TestCpu6502Tests
    {
        /// <summary>Halts when PC reaches a jump to this address.</summary>
        private const ushort Halt = 0x9000;

        [TestMethod]
        public void UneAdditionSetLaRetenueEtLeDebordementSeParment()
        {
            // The two flags answer different questions and both matter: the
            // carry is about the eight bits, the overflow about the signed
            // range. A stub that confuses them is wrong in a way no byte compare
            // would catch.
            TestCpu6502 cpu = new TestCpu6502();
            byte[] program =
            {
                0xA9, 0x90,       // LDA #$90
                0x18,             // CLC
                0x69, 0x90,       // ADC #$90
                0x4C, 0x00, 0x90  // JMP $9000
            };
            cpu.LoadProgram(0x8000, program, Halt);
            cpu.Run(Halt);

            Assert.AreEqual(0x20, cpu.A, "0x90 + 0x90 is $120");
            Assert.IsTrue(cpu.Carry, "the eighth bit carried out");
            Assert.IsTrue(cpu.Overflow, "and -112 + -112 does not fit in a signed byte");
            Assert.IsFalse(cpu.Negative);
        }

        [TestMethod]
        public void UneAdditionSansDebordementNeLeMetPas()
        {
            TestCpu6502 cpu = new TestCpu6502();
            byte[] program =
            {
                0xA9, 0x50,       // LDA #$50
                0x18,             // CLC
                0x69, 0x50,       // ADC #$50
                0x4C, 0x00, 0x90
            };
            cpu.LoadProgram(0x8000, program, Halt);
            cpu.Run(Halt);

            Assert.AreEqual(0xA0, cpu.A);
            Assert.IsFalse(cpu.Carry, "0x50 + 0x50 does not leave the eight bits");
            Assert.IsTrue(cpu.Overflow, "but +80 + +80 is not a signed byte");
            Assert.IsTrue(cpu.Negative);
        }

        [TestMethod]
        public void UneSoustractionEmprunteCommeSurLaMachine()
        {
            // SBC is ADC with the operand inverted, carry included.
            TestCpu6502 cpu = new TestCpu6502();
            byte[] program =
            {
                0xA9, 0x50,       // LDA #$50
                0x38,             // SEC
                0xE9, 0xB0,       // SBC #$B0
                0x4C, 0x00, 0x90
            };
            cpu.LoadProgram(0x8000, program, Halt);
            cpu.Run(Halt);

            Assert.AreEqual(0xA0, cpu.A, "0x50 - 0xB0 with carry in is 0xA0");
            Assert.IsFalse(cpu.Carry, "the subtraction borrowed");
        }

        [TestMethod]
        public void UnAccesAbsoluAvanceLeCompteurDeProgramme()
        {
            // If the operand bytes were read as opcodes, this would not land on
            // the halt and the test would time out rather than fail cleanly.
            TestCpu6502 cpu = new TestCpu6502();
            cpu.Load(0x4000, new byte[] { 0x11, 0x22 });

            byte[] program =
            {
                0xAD, 0x00, 0x40,  // LDA $4000
                0x8D, 0x10, 0x40,  // STA $4010
                0x4C, 0x00, 0x90
            };
            cpu.LoadProgram(0x8000, program, Halt);
            cpu.Run(Halt);

            Assert.AreEqual(0x11, cpu.A);
            Assert.AreEqual(0x11, cpu[0x4010]);
        }

        [TestMethod]
        public void UnIndexageCroisePageNePerdPasLOctetDePoidsFort()
        {
            TestCpu6502 cpu = new TestCpu6502();
            cpu.Load(0x40FF, new byte[] { 0x5A });
            cpu.Load(0x4000, new byte[] { 0x00 });

            byte[] program =
            {
                0xA2, 0xFF,       // LDX #$FF
                0xBD, 0x00, 0x40, // LDA $4000,X
                0x4C, 0x00, 0x90
            };
            cpu.LoadProgram(0x8000, program, Halt);
            cpu.Run(Halt);

            Assert.AreEqual(0x5A, cpu.A, "l'adressage absolute indexe ne s'arrete pas a la page");
        }

        [TestMethod]
        public void UnIndexageIndirectSuitLePointeur()
        {
            TestCpu6502 cpu = new TestCpu6502();
            // The pointer lives in page zero, which is where (zp),Y looks for
            // it; the data lives where the pointer says.
            cpu[0x0000] = 0x00;
            cpu[0x0001] = 0x02;
            cpu.Load(0x0206, new byte[] { 0x7E });

            byte[] program =
            {
                0xA0, 0x06,       // LDY #$06
                0xB1, 0x00,       // LDA ($00),Y
                0x4C, 0x00, 0x90
            };
            cpu.LoadProgram(0x8000, program, Halt);
            cpu.Run(Halt);

            Assert.AreEqual(0x7E, cpu.A);
        }

        [TestMethod]
        public void UnAppelEtUnRetourRendentLaMain()
        {
            // This is exactly the trick the generated relocator uses to find
            // its own address, so it is tested here before it is relied on.
            TestCpu6502 cpu = new TestCpu6502();
            byte[] program =
            {
                0x20, 0x10, 0x80, // JSR $8010
                0x4C, 0x00, 0x90  // JMP $9000
            };
            byte[] subroutine =
            {
                0x60              // RTS
            };
            cpu.LoadProgram(0x8000, program, Halt);
            cpu.Load(0x8010, subroutine);
            cpu.Run(Halt);

            Assert.AreEqual(Halt, cpu.PC);
        }

        [TestMethod]
        public void UnDecalageEnPageZeroEcritEnMemoire()
        {
            TestCpu6502 cpu = new TestCpu6502();
            cpu[0x40] = 0x81;

            byte[] program =
            {
                0x06, 0x40,       // ASL $40
                0xA5, 0x40,       // LDA $40
                0x4C, 0x00, 0x90
            };
            cpu.LoadProgram(0x8000, program, Halt);
            cpu.Run(Halt);

            Assert.AreEqual(0x02, cpu.A);
            Assert.IsTrue(cpu.Carry, "le bit sorti doit rester dans la retenue");
        }

        [TestMethod]
        public void UneComparaisonRangeeCommeLaMachine()
        {
            // CMP sets the carry when the register is at least the operand.
            // Getting that backwards turns every unsigned branch into its
            // opposite, and the loop simply never ends.
            TestCpu6502 cpu = new TestCpu6502();
            byte[] program =
            {
                0xA9, 0x10,       // LDA #$10
                0xC9, 0x20,       // CMP #$20
                0x4C, 0x00, 0x90
            };
            cpu.LoadProgram(0x8000, program, Halt);
            cpu.Run(Halt);

            Assert.IsFalse(cpu.Carry, "10 < 20, donc pas de retenue");
            Assert.IsFalse(cpu.Zero);
            Assert.IsTrue(cpu.Negative, "la difference tient en huit bits");
        }

        [TestMethod]
        public void UneOpcodeInconnueEstRefuseeEtNonIgnoree()
        {
            // A fixture that ignored an unknown opcode would let a broken stub
            // pass. The name of the opcode is the whole point of the message.
            TestCpu6502 cpu = new TestCpu6502();
            byte[] program = { 0x02 };  // an illegal NMOS opcode
            cpu.LoadProgram(0x8000, program, Halt);

            try
            {
                cpu.Step();
                Assert.Fail("une opcode non implementee doit etre refusee");
            }
            catch (System.NotSupportedException ex)
            {
                StringAssert.Contains(ex.Message, "$02");
            }
        }

        [TestMethod]
        public void LeModeDecimalEstRefuseEtNonApproximé()
        {
            TestCpu6502 cpu = new TestCpu6502();
            byte[] program =
            {
                0xF8,             // SED
                0xA9, 0x01,
                0x69, 0x01,       // ADC with the decimal flag set
                0x4C, 0x00, 0x90
            };
            cpu.LoadProgram(0x8000, program, Halt);

            try
            {
                cpu.Run(Halt);
                Assert.Fail("le mode decimal doit etre refuse, pas calcule de travers");
            }
            catch (System.NotSupportedException ex)
            {
                StringAssert.Contains(ex.Message, "Decimal");
            }
        }
    }
}
