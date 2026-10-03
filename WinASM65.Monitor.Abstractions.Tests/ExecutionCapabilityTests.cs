using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WinASM65.Monitor.Abstractions.Tests
{
    /// <summary>
    /// The flag set, checked against the measurements it encodes.
    ///
    /// The three profiles below are not aspirations. Each is what a backend answered
    /// on the build named here, and each citation is a file in this repository:
    /// <c>Bridge/bridge.lua</c> for MesenCE 2.2.1, <c>Bridge/bridge_mesen2.lua</c> for
    /// Mesen2 2.1.1, and <c>MameCapabilityTests</c> for MAME 0.289.
    ///
    /// They are written down here, in the test project, and not in the contract,
    /// because a contract that named backends would be asserting that those builds are
    /// the ones forever. The contract declares the vocabulary; the measurements belong
    /// to whoever took them.
    ///
    /// What is being defended: no bit without a measurement behind it, and no
    /// measurement missing from the set. A capability no measured backend has would be
    /// greened out for everyone in the published table, and one that is measured but
    /// undeclared would silently grey out a working backend.
    /// </summary>
    [TestClass]
    public class ExecutionCapabilityTests
    {
        /// <summary>
        /// Mesen2 2.1.1, the only full-control backend measured. Pause, reset,
        /// execution breakpoints, read and write watchpoints, stepping and state
        /// save/load all answer, and two successive reads of the paused machine are
        /// identical, which is what makes it the deterministic one.
        /// </summary>
        private const ExecutionCapability Mesen2 = ExecutionCapability.FullControl;

        /// <summary>
        /// MesenCE 2.2.1: memory, and snapshots. Its <c>emu.write</c> lands in RAM and
        /// is dropped into PRG ROM; <c>emu.getState</c> and <c>emu.setState</c> exist;
        /// and <c>emu.pause</c>, <c>emu.step</c> and <c>emu.addMemoryCallback</c> do
        /// not answer at all, so it is memory-only for execution.
        /// </summary>
        private const ExecutionCapability MesenCe =
            ExecutionCapability.Memory | ExecutionCapability.StateSaveLoad;

        /// <summary>
        /// MAME 0.289: memory and registers, nothing else. Its writes are observable
        /// and its registers read, while <c>debug:step</c> returns without moving the
        /// program counter and <c>debug:bpset</c> blocks indefinitely.
        /// </summary>
        private const ExecutionCapability Mame =
            ExecutionCapability.Memory | ExecutionCapability.CpuState;

        private static readonly ExecutionCapability[] EveryFlag =
        {
            ExecutionCapability.MemoryRead,
            ExecutionCapability.MemoryWrite,
            ExecutionCapability.CpuState,
            ExecutionCapability.Pause,
            ExecutionCapability.Resume,
            ExecutionCapability.Reset,
            ExecutionCapability.StepInstruction,
            ExecutionCapability.BreakpointExecution,
            ExecutionCapability.WatchpointRead,
            ExecutionCapability.WatchpointWrite,
            ExecutionCapability.StateSaveLoad
        };

        [TestMethod]
        public void TheFlagSetIsFlagsSoItCanCarrySeveralAtOnce()
        {
            Assert.IsTrue(typeof(ExecutionCapability).IsDefined(typeof(FlagsAttribute), false),
                "without [Flags] a combined set is not printable and the status line would show a number");
        }

        [TestMethod]
        public void EveryCapabilityIsOneDistinctBit()
        {
            var seen = new HashSet<ExecutionCapability>();

            foreach (ExecutionCapability flag in EveryFlag)
            {
                Assert.AreNotEqual(ExecutionCapability.None, flag, "a capability that means nothing");
                Assert.IsTrue(seen.Add(flag), flag + " shares its value with another capability");

                int bits = ToInt32(flag);
                Assert.AreEqual(0, bits & (bits - 1), flag + " is not a single bit");
            }

            Assert.AreEqual(0, ToInt32(ExecutionCapability.None), "None must be the empty set, not a flag");
        }

        [TestMethod]
        public void FullControlIsExactlyTheUnionOfTheFlagsAndNotMore()
        {
            ExecutionCapability all = ExecutionCapability.None;
            foreach (ExecutionCapability flag in EveryFlag)
                all |= flag;

            // Asserted against the parts and not against a copy of the same
            // expression, so a bit added to one and forgotten in the other fails here
            // instead of quietly going undeclared.
            Assert.AreEqual(all, ExecutionCapability.FullControl);
        }

        [TestMethod]
        public void MemoryIsReadingAndWritingAndNothingElse()
        {
            Assert.AreEqual(ExecutionCapability.MemoryRead | ExecutionCapability.MemoryWrite,
                ExecutionCapability.Memory);

            foreach (ExecutionCapability flag in EveryFlag)
            {
                if (flag == ExecutionCapability.MemoryRead || flag == ExecutionCapability.MemoryWrite)
                    continue;

                Assert.AreNotEqual(flag, ExecutionCapability.Memory,
                    "a capability beyond memory is not part of the memory-only set");
            }
        }

        [TestMethod]
        public void Mesen2IsTheOnlyMeasuredBackendWithFullControl()
        {
            foreach (ExecutionCapability flag in EveryFlag)
                Assert.IsTrue(Mesen2.HasFlag(flag), "Mesen2 2.1.1 answers " + flag + " and it is not declared");

            Assert.IsTrue(MesenCe != ExecutionCapability.FullControl,
                "MesenCE answers no execution control and must not be declared as full control");
            Assert.IsTrue(Mame != ExecutionCapability.FullControl,
                "MAME's step does not advance and its bpset blocks, so it is not full control");
        }

        [TestMethod]
        public void MesenCeIsMemoryOnlyButStillSnapshottable()
        {
            Assert.IsTrue(MesenCe.HasFlag(ExecutionCapability.MemoryRead));
            Assert.IsTrue(MesenCe.HasFlag(ExecutionCapability.MemoryWrite));
            Assert.IsTrue(MesenCe.HasFlag(ExecutionCapability.StateSaveLoad));

            // The measured absences, one by one. emu.pause does not exist;
            // emu.step and emu.resume refuse calls outside a callback;
            // emu.addMemoryCallback refuses every function, so neither breakpoints nor
            // watchpoints can be set; emu.reset is reserved for manual validation.
            foreach (ExecutionCapability absent in new[]
            {
                ExecutionCapability.CpuState,
                ExecutionCapability.Pause,
                ExecutionCapability.Resume,
                ExecutionCapability.Reset,
                ExecutionCapability.StepInstruction,
                ExecutionCapability.BreakpointExecution,
                ExecutionCapability.WatchpointRead,
                ExecutionCapability.WatchpointWrite
            })
            {
                Assert.IsFalse(MesenCe.HasFlag(absent),
                    "MesenCE 2.2.1 does not answer " + absent + " and declaring it would be a fiction");
            }
        }

        [TestMethod]
        public void MameHasMemoryAndRegistersButNoExecutionControl()
        {
            Assert.IsTrue(Mame.HasFlag(ExecutionCapability.MemoryRead));
            Assert.IsTrue(Mame.HasFlag(ExecutionCapability.MemoryWrite));
            Assert.IsTrue(Mame.HasFlag(ExecutionCapability.CpuState));

            // The two measurements that decide it: debug:step returns without moving
            // the program counter, and debug:bpset blocks indefinitely. A flag for
            // either would make MAME look like a Mesen2 replacement.
            Assert.IsFalse(Mame.HasFlag(ExecutionCapability.StepInstruction));
            Assert.IsFalse(Mame.HasFlag(ExecutionCapability.BreakpointExecution));

            // Neither was measured, so neither is claimed. Not measured is not the
            // same as absent, and the difference matters: it is why these are absent
            // rather than refused.
            foreach (ExecutionCapability unmeasured in new[]
            {
                ExecutionCapability.Pause,
                ExecutionCapability.Resume,
                ExecutionCapability.Reset,
                ExecutionCapability.WatchpointRead,
                ExecutionCapability.WatchpointWrite,
                ExecutionCapability.StateSaveLoad
            })
            {
                Assert.IsFalse(Mame.HasFlag(unmeasured),
                    unmeasured + " was never measured on MAME 0.289 and must not be claimed");
            }
        }

        [TestMethod]
        public void TheThreeMeasuredBackendsDoNotPretendToBeEachOther()
        {
            // Three different answers from three real backends. If these collapsed to
            // one, the flags would be saying nothing and the published table built on
            // them would be a decoration.
            var distinct = new HashSet<ExecutionCapability> { Mesen2, MesenCe, Mame };
            Assert.AreEqual(3, distinct.Count);

            Assert.IsTrue(Mesen2.HasFlag(ExecutionCapability.Memory), "the one thing all three agree on");
            Assert.IsTrue(MesenCe.HasFlag(ExecutionCapability.Memory), "the one thing all three agree on");
            Assert.IsTrue(Mame.HasFlag(ExecutionCapability.Memory), "the one thing all three agree on");
        }

        private static int ToInt32(ExecutionCapability capability)
        {
            return Convert.ToInt32(capability);
        }
    }
}