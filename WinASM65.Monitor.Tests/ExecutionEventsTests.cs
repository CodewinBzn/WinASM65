using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Cpu;
using WinASM65.Monitor.Protocol;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// Run state across the bridge: the machine says when it stops and when it runs
    /// again, so a debugger never has to poll to notice a breakpoint.
    /// </summary>
    [TestClass]
    public class ExecutionEventsTests
    {
        [TestMethod]
        public void AStopDescriptionIsParsedWithItsCartridgeOffset()
        {
            StopInfo stop;
            Assert.IsTrue(MonitorProtocol.TryParseStop("reason=breakpoint pc=$C123 prg=$04123", out stop));
            Assert.AreEqual(StopReason.Breakpoint, stop.Reason);
            Assert.AreEqual(0xC123, stop.Pc);
            Assert.AreEqual(0x04123, stop.PrgOffset);
        }

        [TestMethod]
        public void AHostThatCannotNameTheBankLeavesTheOffsetUnknown()
        {
            StopInfo stop;
            Assert.IsTrue(MonitorProtocol.TryParseStop("reason=step pc=$0200 prg=-", out stop));
            Assert.AreEqual(StopReason.Step, stop.Reason);
            Assert.IsNull(stop.PrgOffset);
        }

        [TestMethod]
        public void ANewReasonIsKeptAsUnknownRatherThanRefused()
        {
            StopInfo stop;
            Assert.IsTrue(MonitorProtocol.TryParseStop("reason=watchdog pc=$8000", out stop));
            Assert.AreEqual(StopReason.Unknown, stop.Reason);
        }

        [TestMethod]
        public void AStopWithoutAProgramCounterIsRefused()
        {
            StopInfo stop;
            Assert.IsFalse(MonitorProtocol.TryParseStop("reason=pause", out stop));
            Assert.IsFalse(MonitorProtocol.TryParseStop("reason=pause pc=$ZZZZ", out stop));
            Assert.IsFalse(MonitorProtocol.TryParseStop("reason=pause pc=$8000 prg=12", out stop));
            Assert.IsFalse(MonitorProtocol.TryParseStop("", out stop));
        }

        [TestMethod]
        public void APushedStopReachesTheBackendAndARunClearsIt()
        {
            using (ScriptedBridge host = new ScriptedBridge())
            using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(host.Port, poll: false))
            using (ManualResetEventSlim stopped = new ManualResetEventSlim())
            using (ManualResetEventSlim resumed = new ManualResetEventSlim())
            {
                StopInfo seen = null;
                bridge.Stopped += stop => { seen = stop; stopped.Set(); };
                bridge.Resumed += () => resumed.Set();

                Assert.IsTrue(bridge.ReportsExecutionState);
                Assert.IsTrue(bridge.IsRunning);

                host.Push("STOPPED reason=breakpoint pc=$C010 prg=$00010");
                Assert.IsTrue(stopped.Wait(5000), "the stop never reached the backend");
                Assert.AreEqual(0xC010, seen.Pc);
                Assert.IsFalse(bridge.IsRunning);
                Assert.IsTrue(bridge.IsStopped);
                Assert.AreEqual(0xC010, bridge.LastStop.Pc);

                host.Push("RESUMED");
                Assert.IsTrue(resumed.Wait(5000), "the resume never reached the backend");
                Assert.IsTrue(bridge.IsRunning);
                Assert.IsNull(bridge.LastStop);
            }
        }

        [TestMethod]
        public void AMonitorThatConnectsToAStoppedMachineKnowsItIsStopped()
        {
            using (ScriptedBridge host = new ScriptedBridge { Status = "OK stopped reason=pause pc=$8004 prg=-" })
            using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(host.Port, poll: false))
            {
                Assert.IsTrue(bridge.IsStopped);
                Assert.AreEqual(StopReason.Pause, bridge.LastStop.Reason);
                Assert.AreEqual(0x8004, bridge.LastStop.Pc);
            }
        }

        [TestMethod]
        public void ABridgeWithoutStatusReportsNoRunStateAndStillWorks()
        {
            using (ScriptedBridge host = new ScriptedBridge())
            {
                host.Answer = line => line.StartsWith("STATUS") ? "ERR unknown command: STATUS"
                    : line.StartsWith("PING") ? "OK MesenCE 2.2.1 nesDebug" : "OK";
                using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(host.Port, poll: false))
                {
                    Assert.IsFalse(bridge.ReportsExecutionState);
                    Assert.IsTrue(bridge.IsRunning);
                }
            }
        }

        [TestMethod]
        public void APushedRamChangeReachesTheBackend()
        {
            // The receiver strips PUSH and hands over "RAM $addr:byte ...". The kind
            // word has to be stripped too, or every live update is refused.
            using (ScriptedBridge host = new ScriptedBridge())
            using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(host.Port, poll: false))
            using (ManualResetEventSlim seen = new ManualResetEventSlim())
            {
                IReadOnlyList<(int Address, byte Value)> changes = null;
                bridge.RamChanged += c => { changes = c; seen.Set(); };

                host.Push("RAM $0010:42 $0011:43");

                Assert.IsTrue(seen.Wait(5000), "the pushed RAM change never reached the backend");
                Assert.AreEqual(2, changes.Count);
                Assert.AreEqual((0x0010, (byte)0x42), changes[0]);
            }
        }

        [TestMethod]
        public void AStopHandlerThatQueuesACommandDoesNotDeadlock()
        {
            // The documented rule: a handler runs on the receiver thread and must
            // queue its work. Queued, a command sent in reaction to the stop completes.
            using (ScriptedBridge host = new ScriptedBridge())
            using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(host.Port, poll: false))
            {
                Task<CpuSnapshot> read = null;
                using (ManualResetEventSlim queued = new ManualResetEventSlim())
                {
                    bridge.Stopped += stop => { read = Task.Run(() => bridge.ReadCpuState()); queued.Set(); };
                    host.Push("STOPPED reason=step pc=$C000 prg=-");
                    Assert.IsTrue(queued.Wait(5000));
                }
                Assert.IsTrue(read.Wait(5000), "a command sent after a stop never completed");
                Assert.AreEqual(0xC000, read.Result.Pc);
            }
        }

        [TestMethod]
        public void TheSessionRemovesOneBreakpointThroughTheBridge()
        {
            using (ScriptedBridge host = new ScriptedBridge())
            using (BridgeMemoryBackend bridge = BridgeMemoryBackend.Connect(host.Port, poll: false))
            {
                MonitorSession session = new MonitorSession(bridge, new Cpu6502(), AppContext.BaseDirectory);

                StringAssert.StartsWith(session.Execute("BREAK REMOVE exec $C000")[0], "OK");

                Assert.AreEqual("BREAK REMOVE exec $C000", host.WaitFor("BREAK"));
            }
        }
    }
}
