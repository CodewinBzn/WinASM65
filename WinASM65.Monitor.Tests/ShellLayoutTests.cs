using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Monitor.Shell;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// The breakpoints, asserted at the exact column where each one changes.
    ///
    /// One column either side of a boundary, not a sample: the whole point of a
    /// breakpoint is what happens at that number, and a test at 118 and 122 would
    /// pass with the boundaries in the wrong place.
    ///
    /// The numbers under test are the presentation specification's, and the last
    /// test holds the panes to them — a layout that keeps no room for the editor,
    /// or that overflows the terminal, is worse than no layout.
    /// </summary>
    [TestClass]
    public class ShellLayoutTests
    {
        private const int TallEnough = 24;

        [TestMethod]
        public void EveryColumnBelowSixtyIsRefused()
        {
            foreach (int width in new[] { 0, 1, 20, 40, 59 })
            {
                ShellLayout layout = ShellLayout.ForSize(width, TallEnough);
                Assert.AreEqual(ShellLayoutMode.Refuse, layout.Mode, width + " columns");
                Assert.IsTrue(layout.IsRefused);
                Assert.AreEqual(0, layout.TreeWidth, width + " columns");
                Assert.AreEqual(0, layout.RightPaneWidth, width + " columns");
            }
        }

        [TestMethod]
        public void SixtyColumnsIsTheFirstThatDraws()
        {
            ShellLayout layout = ShellLayout.ForSize(ShellLayout.AbsoluteMinimum, TallEnough);

            Assert.AreEqual(ShellLayoutMode.FullWidth, layout.Mode);
            Assert.IsFalse(layout.IsRefused);
        }

        [TestMethod]
        public void SeventyNineColumnsGivesEverythingToTheEditor()
        {
            foreach (int width in new[] { 60, 70, 79 })
            {
                ShellLayout layout = ShellLayout.ForSize(width, TallEnough);
                Assert.AreEqual(ShellLayoutMode.FullWidth, layout.Mode, width + " columns");
                Assert.AreEqual(0, layout.TreeWidth, width + " columns");
                Assert.AreEqual(0, layout.RightPaneWidth, width + " columns");
                Assert.AreEqual(width, layout.EditorWidth, width + " columns");
            }
        }

        [TestMethod]
        public void EightyColumnsIsWhereTheOverlayModeBegins()
        {
            Assert.AreEqual(ShellLayoutMode.FullWidth, ShellLayout.ForSize(79, TallEnough).Mode);
            Assert.AreEqual(ShellLayoutMode.Overlay, ShellLayout.ForSize(80, TallEnough).Mode);

            ShellLayout atEighty = ShellLayout.ForSize(80, TallEnough);
            Assert.AreEqual(ShellLayout.RightColumnWidth, atEighty.RightPaneWidth);
            Assert.IsFalse(atEighty.RightPaneIsColumn, "an overlay is not a column");
            Assert.IsFalse(atEighty.TreeIsColumn);
        }

        [TestMethod]
        public void NinetyNineColumnsIsStillAnOverlay()
        {
            Assert.AreEqual(ShellLayoutMode.Overlay, ShellLayout.ForSize(99, TallEnough).Mode);
        }

        [TestMethod]
        public void OneHundredColumnsCollapsesTheTreeToADrawer()
        {
            Assert.AreEqual(ShellLayoutMode.Overlay, ShellLayout.ForSize(99, TallEnough).Mode);

            ShellLayout atOneHundred = ShellLayout.ForSize(100, TallEnough);
            Assert.AreEqual(ShellLayoutMode.CollapsedTree, atOneHundred.Mode);
            Assert.AreEqual(ShellLayout.CollapsedTreeWidth, atOneHundred.TreeWidth);
            Assert.IsFalse(atOneHundred.TreeIsColumn, "three columns is a drawer, not a tree");
        }

        [TestMethod]
        public void OneHundredAndNineteenIsStillADrawer()
        {
            Assert.AreEqual(ShellLayoutMode.CollapsedTree, ShellLayout.ForSize(119, TallEnough).Mode);
        }

        [TestMethod]
        public void OneHundredAndTwentyGivesThreeColumns()
        {
            Assert.AreEqual(ShellLayoutMode.CollapsedTree, ShellLayout.ForSize(119, TallEnough).Mode);

            ShellLayout atOneTwenty = ShellLayout.ForSize(120, TallEnough);
            Assert.AreEqual(ShellLayoutMode.Full, atOneTwenty.Mode);
            Assert.AreEqual(ShellLayout.TreeColumnWidth, atOneTwenty.TreeWidth);
            Assert.AreEqual(ShellLayout.RightColumnWidth, atOneTwenty.RightPaneWidth);
            Assert.IsTrue(atOneTwenty.TreeIsColumn);
            Assert.IsTrue(atOneTwenty.RightPaneIsColumn);
        }

        [TestMethod]
        public void TheThreePanesLeaveTheEditorWhateverIsLeftOver()
        {
            ShellLayout atOneTwenty = ShellLayout.ForSize(120, TallEnough);

            Assert.AreEqual(
                120 - ShellLayout.TreeColumnWidth - ShellLayout.RightColumnWidth,
                atOneTwenty.EditorWidth);
            Assert.IsTrue(atOneTwenty.EditorWidth >= 40,
                "the editor is the point; 40 columns is the least a 6502 line fits in");
        }

        [TestMethod]
        public void EveryDrawingModeLeavesTheEditorSomeRoom()
        {
            for (int width = ShellLayout.AbsoluteMinimum; width <= 200; width++)
            {
                ShellLayout layout = ShellLayout.ForSize(width, TallEnough);
                if (layout.IsRefused)
                    continue;

                Assert.IsTrue(layout.EditorWidth > 0, width + " columns leaves the editor nothing");
                Assert.IsTrue(layout.EditorWidth + layout.TreeWidth + layout.RightPaneWidth <= width,
                    width + " columns overflows: " + layout);
                Assert.IsTrue(layout.EditorWidth >= 40,
                    width + " columns leaves only " + layout.EditorWidth + " for the editor");
            }
        }

        [TestMethod]
        public void AWindowTooShortToDrawIsRefusedRatherThanSquashed()
        {
            Assert.AreEqual(ShellLayoutMode.Refuse,
                ShellLayout.ForSize(120, ShellLayout.AbsoluteMinimumHeight - 1).Mode);

            Assert.AreEqual(ShellLayoutMode.Full,
                ShellLayout.ForSize(120, ShellLayout.AbsoluteMinimumHeight).Mode);
        }

        [TestMethod]
        public void TheRefusalSaysWhatIsNeededAndNotOnlyWhatIsWrong()
        {
            ShellLayout narrow = ShellLayout.ForSize(40, TallEnough);
            string narrowText = string.Join(" ", new System.Collections.Generic.List<string>(narrow.RefusalLines()).ToArray());

            StringAssert.Contains(narrowText, "40");
            StringAssert.Contains(narrowText, ShellLayout.AbsoluteMinimum.ToString());

            ShellLayout shortWindow = ShellLayout.ForSize(120, 3);
            string shortText = string.Join(" ", new System.Collections.Generic.List<string>(shortWindow.RefusalLines()).ToArray());

            StringAssert.Contains(shortText, "short");
            StringAssert.Contains(shortText, ShellLayout.AbsoluteMinimumHeight.ToString());
        }

        [TestMethod]
        public void TheModeIsTheOnlySourceOfTruthForPaneWidths()
        {
            // Two widths that land on the same mode must give the same pane widths,
            // which is what makes a mode name usable instead of a formula.
            ShellLayout narrow = ShellLayout.ForSize(80, TallEnough);
            ShellLayout wide = ShellLayout.ForSize(99, TallEnough);

            Assert.AreEqual(narrow.Mode, wide.Mode);
            Assert.AreEqual(narrow.TreeWidth, wide.TreeWidth);
            Assert.AreEqual(narrow.RightPaneWidth, wide.RightPaneWidth);
        }
    }
}