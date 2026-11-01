using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Cpu;

namespace WinASM65.Tests
{
    [TestClass]
    public class InstructionDescriptionsTests
    {
        [TestMethod]
        public void EveryMnemonicOfEveryCpuIsDescribed()
        {
            foreach (ICpuInstructionSet cpu in CpuFactory.All.Values)
            {
                foreach (string mnemonic in cpu.Mnemonics)
                {
                    InstructionDescription description;
                    Assert.IsTrue(InstructionDescriptions.TryGet(mnemonic, out description), mnemonic + " has no description");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(description.Name), mnemonic);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(description.Summary), mnemonic);
                }
            }
        }

        [TestMethod]
        public void BitInstructionsNameTheirBit()
        {
            InstructionDescription description;
            Assert.IsTrue(InstructionDescriptions.TryGet("bbs5", out description));
            Assert.AreEqual("Branch on Bit 5 Set", description.Name);
        }
    }
}
