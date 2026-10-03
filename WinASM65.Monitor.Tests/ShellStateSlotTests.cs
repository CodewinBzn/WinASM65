using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Terminal.Gui;
using WinASM65.Cpu;
using WinASM65.Monitor.Shell;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// F7 and the slot round trip behind it.
    ///
    /// These exist because the first version of <c>EditorCommands</c> spelled the
    /// STATE lines out itself and got the grammar wrong: it appended a slot to
    /// <c>STATE SAVE</c>, and the session accepts that verb as exactly two tokens.
    /// Nothing caught it, because nothing had bound F7 yet — an unbound key has no
    /// test to fail. The tests below are the ones that would have.
    /// </summary>
    [TestClass]
    public class ShellStateSlotTests
    {
        private static ShellWindow BuildWindow(FakeMemoryBackend machine)
        {
            return new ShellWindow(
                new MonitorSession(machine, new Cpu6502(), AppContext.BaseDirectory),
                ShellTheme.Default,
                ListingSourceFactory.Create(),
                AppContext.BaseDirectory);
        }

        private static void WithShell(Action<ShellWindow, FakeMemoryBackend> body)
        {
            FakeDriver driver = new FakeDriver();
            Application.Init(driver);
            try
            {
                driver.SetWindowSize(120, 24);
                driver.Refresh();

                using (FakeMemoryBackend machine = new FakeMemoryBackend())
                {
                    ShellWindow window = BuildWindow(machine);
                    window.Frame = new System.Drawing.Rectangle(0, 0, 120, 24);
                    window.ApplyLayout(ShellLayout.ForSize(120, 24));

                    body(window, machine);
                }
            }
            finally
            {
                Application.Shutdown();
            }
        }

        [TestMethod]
        public void SaveStateIssuesTheTwoTokenVerbTheSessionAccepts()
        {
            // "STATE SAVE <slot>" would be three tokens, and the session answers that
            // with "STATE expects SAVE or LOAD <hex>". The slot is the shell's own
            // bookkeeping and has no business on the wire.
            Assert.AreEqual("STATE SAVE", EditorCommands.SaveState("0"));
            Assert.AreEqual("STATE SAVE", EditorCommands.SaveState(null));
        }

        [TestMethod]
        public void LoadStateSendsHexAndNotASlotName()
        {
            Assert.AreEqual("STATE LOAD AABB", EditorCommands.LoadState("AABB"));

            // A slot name is not hex, and must be refused here rather than at the
            // bridge, where the user would see an error about bytes they never typed.
            string refused = EditorCommands.LoadState("slot0");
            StringAssert.StartsWith(refused, "ERR ");
        }

        [TestMethod]
        public void LoadStateRefusesAnEmptyPayload()
        {
            StringAssert.StartsWith(EditorCommands.LoadState(null), "ERR ");
            StringAssert.StartsWith(EditorCommands.LoadState("   "), "ERR ");
        }

        [TestMethod]
        public void SavingCapturesTheStateAndCountsIt()
        {
            WithShell(delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                Assert.AreEqual(0, window.CapturedStateCount);

                string report = window.SaveStateSlot(null);

                Assert.AreEqual(1, window.CapturedStateCount);
                StringAssert.Contains(report, "state saved in slot 0");
            });
        }

        [TestMethod]
        public void SavingIntoANamedSlotKeepsThatSlotSeparate()
        {
            WithShell(delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                window.SaveStateSlot("before");
                window.SaveStateSlot("after");

                Assert.AreEqual(2, window.CapturedStateCount);
            });
        }

        [TestMethod]
        public void RestoringASlotPutsTheMachineBackAsItWas()
        {
            WithShell(delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                window.SaveStateSlot("mark");

                machine.Write(0x0040, new byte[] { 0x11 });
                window.LoadStateSlot("mark");

                Assert.AreEqual(0x00, machine.Read(0x0040, 1)[0],
                    "the machine did not go back to the captured bytes");
            });
        }

        [TestMethod]
        public void RestoringAnUncapturedSlotSaysSoAndTouchesNothing()
        {
            WithShell(delegate (ShellWindow window, FakeMemoryBackend machine)
            {
                machine.Write(0x0041, new byte[] { 0x5A });

                string report = window.LoadStateSlot("never-saved");

                StringAssert.Contains(report, "no state in slot");
                Assert.AreEqual(0x5A, machine.Read(0x0041, 1)[0]);
                Assert.AreEqual(0, window.CapturedStateCount);
            });
        }

        [TestMethod]
        public void TheSlotIsBoundToAKeyInThisBuild()
        {
            // A key that is listed as unbound keeps saying so; the help screen reads
            // this table, so a stale flag here is a lie the user acts on.
            ShellKey found = null;
            foreach (ShellKey key in ShellWindow.Keys)
            {
                if (key.Key == "F7")
                    found = key;
            }

            Assert.IsNotNull(found, "F7 vanished from the key table");
            Assert.IsTrue(found.Bound, "F7 is bound now, so the table must not say otherwise");
        }

        [TestMethod]
        public void TheKeysStillUnboundAreTheOnesWithNoImplementation()
        {
            foreach (ShellKey key in ShellWindow.Keys)
            {
                if (key.Key == "F12" || key.Key == "Ctrl+A")
                    Assert.IsFalse(key.Bound, key.Key + " has no implementation yet and must say so");

                if (key.Key == "F5" || key.Key == "F7" || key.Key == "F8"
                    || key.Key == "F9" || key.Key == "F10")
                    Assert.IsTrue(key.Bound, key.Key + " is implemented and must not claim otherwise");
            }
        }
    }
}