using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Projects;
using static WinASM65.Tests.SyntaxTestKit;

namespace WinASM65.Tests
{
    /// <summary>Local labels (@name) and anonymous labels (- and +).</summary>
    [TestClass]
    public class LabelSyntaxTests
    {
        [TestMethod]
        public void ALocalLabelWorksForwardAndBackward()
        {
            AreBytes("A2 03 CA D0 FD F0 00 60", Bytes(".org $8000\nMain:\n ldx #3\n@loop:\n dex\n bne @loop\n beq @done\n@done:\n rts"));
        }

        [TestMethod]
        public void TheSameLocalNameLivesInTwoGlobals()
        {
            AreBytes("D0 FE D0 FE", Bytes(".org $8000\nFirst:\n@wait: bne @wait\nSecond:\n@wait: bne @wait"));
        }

        [TestMethod]
        public void ALocalFromAnotherScopeIsNotFound()
        {
            StringAssert.Contains(Errors(".org $8000\nFirst:\n bne @later\nSecond:\n@later: rts"),
                "Local label '@later' is not defined in the scope of 'First'");
        }

        [TestMethod]
        public void ALocalBeforeAnyGlobalIsReported()
        {
            StringAssert.Contains(Errors(".org $8000\n@orphan: rts"), "Local label '@orphan' has no global label before it");
        }

        [TestMethod]
        public void AConstantDoesNotOpenAScope()
        {
            AreBytes("D0 FE", Bytes(".org $8000\nMain:\nlimit = 5\n@spin: bne @spin"));
        }

        [TestMethod]
        public void ALocalInAMacroBelongsToEachExpansion()
        {
            AreBytes("D0 FE D0 FE", Bytes(".macro spin\n@here: bne @here\n.endmacro\n.org $8000\nMain:\n spin\n spin"));
        }

        [TestMethod]
        public void ALocalInARepBelongsToEachIteration()
        {
            AreBytes("D0 FE D0 FE", Bytes(".org $8000\nMain:\n.rep 2\n@here: bne @here\n.endrep"));
        }

        [TestMethod]
        public void AnAnonymousLabelGoesBackward()
        {
            AreBytes("A2 03 CA D0 FD 60", Bytes(".org $8000\n ldx #3\n-\n dex\n bne -\n rts"));
        }

        [TestMethod]
        public void AnAnonymousLabelGoesForward()
        {
            AreBytes("F0 01 EA 60", Bytes(".org $8000\n beq +\n nop\n+ rts"));
        }

        [TestMethod]
        public void RunsOfDifferentLengthAreDifferentLabels()
        {
            // "bne --" goes to the "--" line, not to the second "-" back.
            AreBytes("EA EA D0 FD D0 FA", Bytes(".org $8000\n--\n nop\n-\n nop\n bne -\n bne --"));
        }

        [TestMethod]
        public void TheNearestAnonymousLabelWins()
        {
            AreBytes("EA D0 FD EA D0 FD", Bytes(".org $8000\n- nop\n bne -\n- nop\n bne -"));
        }

        [TestMethod]
        public void AMissingAnonymousLabelIsReported()
        {
            StringAssert.Contains(Errors(".org $8000\n bne -"), "No anonymous label '-' before this line");
            StringAssert.Contains(Errors(".org $8000\n bne +\n rts"), "No anonymous label '+' after this line");
        }

        [TestMethod]
        public void LocalAndAnonymousLabelsStayInTheirUnit()
        {
            string dir = Path.Combine(Path.GetTempPath(), "winasm65-labels-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "a.asm"), ".org $8000\nMain:\n- nop\n bne -\n@x: rts\n");
                File.WriteAllText(Path.Combine(dir, "b.asm"), ".org $9000\nOther:\n bne +\n nop\n+ rts\n jsr Main\n");
                File.WriteAllText(Path.Combine(dir, "config.json"),
                    "{ \"Input\": [ { \"FileName\": \"a.asm\", \"Dependencies\": [] },"
                    + " { \"FileName\": \"b.asm\", \"Dependencies\": [\"a.asm\"] } ],"
                    + " \"Output\": { \"ObjectFile\": \"out.bin\", \"Files\": [ { \"FileName\": \"a.o\", \"Size\": \"$4\" }, { \"FileName\": \"b.o\" } ] } }");

                ProjectBuildResult build = ProjectSession.Open(Path.Combine(dir, "config.json")).Build();
                Assert.IsTrue(build.Success, string.Join(" | ", build.Diagnostics));
                AreBytes("EA D0 FD 60 D0 01 EA 60 20 00 80", build.Image);

                string symbols = File.ReadAllText(Path.Combine(dir, "a.symb"));
                StringAssert.Contains(symbols, "Main");
                Assert.IsFalse(symbols.Contains("__anon") || symbols.Contains("@"), "a.symb exported " + symbols);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
