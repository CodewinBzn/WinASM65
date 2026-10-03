using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Monitor.Shell;
using WinASM65.Monitor.Protocol;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// Tests for the state command builders used by the shell.
    /// </summary>
    [TestClass]
    public class StateCommandsTests
    {
        [TestMethod]
        public void SaveWithValidSlotReturnsStateSaveCommand()
        {
            string cmd = StateCommands.Save("slot1");

            Assert.AreEqual("STATE SAVE", cmd);
        }

        [TestMethod]
        public void SaveWithEmptySlotReturnsError()
        {
            string cmd = StateCommands.Save("");

            StringAssert.StartsWith(cmd, MonitorProtocol.ErrPrefix);
            StringAssert.Contains(cmd, "empty");
        }

        [TestMethod]
        public void SaveWithWhitespaceSlotReturnsError()
        {
            string cmd = StateCommands.Save("   ");

            StringAssert.StartsWith(cmd, MonitorProtocol.ErrPrefix);
            StringAssert.Contains(cmd, "empty");
        }

        [TestMethod]
        public void SaveWithNullSlotReturnsError()
        {
            string cmd = StateCommands.Save(null);

            StringAssert.StartsWith(cmd, MonitorProtocol.ErrPrefix);
            StringAssert.Contains(cmd, "empty");
        }

        [TestMethod]
        public void LoadWithValidHexReturnsStateLoadCommand()
        {
            string cmd = StateCommands.Load("AABBCCDD");

            Assert.AreEqual("STATE LOAD AABBCCDD", cmd);
        }

        [TestMethod]
        public void LoadWithValidHexLowerCaseReturnsStateLoadCommand()
        {
            string cmd = StateCommands.Load("aabbccdd");

            Assert.AreEqual("STATE LOAD aabbccdd", cmd);
        }

        [TestMethod]
        public void LoadWithEmptySlotReturnsError()
        {
            string cmd = StateCommands.Load("");

            StringAssert.StartsWith(cmd, MonitorProtocol.ErrPrefix);
            StringAssert.Contains(cmd, "empty");
        }

        [TestMethod]
        public void LoadWithWhitespaceSlotReturnsError()
        {
            string cmd = StateCommands.Load("   ");

            StringAssert.StartsWith(cmd, MonitorProtocol.ErrPrefix);
            StringAssert.Contains(cmd, "empty");
        }

        [TestMethod]
        public void LoadWithNullSlotReturnsError()
        {
            string cmd = StateCommands.Load(null);

            StringAssert.StartsWith(cmd, MonitorProtocol.ErrPrefix);
            StringAssert.Contains(cmd, "empty");
        }

        [TestMethod]
        public void LoadWithInvalidHexReturnsError()
        {
            string cmd = StateCommands.Load("ZZZZ");

            StringAssert.StartsWith(cmd, MonitorProtocol.ErrPrefix);
            StringAssert.Contains(cmd, "valid hex");
        }

        [TestMethod]
        public void LoadWithOddLengthHexReturnsError()
        {
            string cmd = StateCommands.Load("ABC");

            StringAssert.StartsWith(cmd, MonitorProtocol.ErrPrefix);
            StringAssert.Contains(cmd, "valid hex");
        }

        [TestMethod]
        public void LoadWithInvalidHexCharactersReturnsError()
        {
            string cmd = StateCommands.Load("GHIJ");

            StringAssert.StartsWith(cmd, MonitorProtocol.ErrPrefix);
            StringAssert.Contains(cmd, "valid hex");
        }
    }
}