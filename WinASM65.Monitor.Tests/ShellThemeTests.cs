using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Terminal.Gui;
using WinASM65.Monitor.Shell;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// The theme: an editable file, and what happens when it is absent, wrong, or
    /// half wrong.
    ///
    /// The theme is the one piece of configuration a user is expected to edit by
    /// hand, so it is the one piece of input that arrives malformed. Every test
    /// here therefore asserts the same two things about a bad file: that it does
    /// not throw, and that the palette that comes back still answers for every
    /// role. A theme file must never be able to stop the monitor from starting.
    /// </summary>
    [TestClass]
    public class ShellThemeTests
    {
        [TestMethod]
        public void TheBuiltInPaletteAnswersForEveryRole()
        {
            ShellTheme theme = ShellTheme.Default;

            foreach (string role in ThemeRole.Names)
            {
                // Not compared to a constant: the assertion is that a role exists at
                // all. Which colour it is, is the palette's business.
                Assert.AreNotEqual(default(Color), theme.TrueColor(role), role + " has no 24-bit colour");
            }
        }

        [TestMethod]
        public void TheBuiltInPaletteIsTheSpecificationTable()
        {
            ShellTheme theme = ShellTheme.Default;

            Assert.AreEqual(0xC8D0D8, Rgb(theme.TrueColor(ThemeRole.Default)));
            Assert.AreEqual(0x5A6672, Rgb(theme.TrueColor(ThemeRole.Chrome)));
            Assert.AreEqual(0x7FB2E5, Rgb(theme.TrueColor(ThemeRole.Mnemonic)));
            Assert.AreEqual(0xE8B86D, Rgb(theme.TrueColor(ThemeRole.Operand)));
            Assert.AreEqual(0xC58AF9, Rgb(theme.TrueColor(ThemeRole.Directive)));
            Assert.AreEqual(0x8FD67A, Rgb(theme.TrueColor(ThemeRole.Label)));
            Assert.AreEqual(0xF0C674, Rgb(theme.TrueColor(ThemeRole.Number)));
            Assert.AreEqual(0x6B7785, Rgb(theme.TrueColor(ThemeRole.Comment)));
            Assert.AreEqual(0xF26D6D, Rgb(theme.TrueColor(ThemeRole.Error)));
            Assert.AreEqual(0xE06C75, Rgb(theme.TrueColor(ThemeRole.Breakpoint)));
            Assert.AreEqual(0xE5C07B, Rgb(theme.TrueColor(ThemeRole.Warning)));
        }

        [TestMethod]
        public void AnAbsentFileFallsBackAndSaysSo()
        {
            ShellTheme theme = ShellTheme.LoadFile(
                Path.Combine(Path.GetTempPath(), "no-such-theme-" + Guid.NewGuid().ToString("N") + ".json"));

            Assert.IsTrue(theme.IsBuiltIn, "a missing file must report the built-in palette");
            Assert.AreEqual(ShellTheme.Default.TrueColor(ThemeRole.Mnemonic),
                theme.TrueColor(ThemeRole.Mnemonic));
            Assert.IsTrue(theme.Diagnostics.Count > 0, "the reason must be recorded, not swallowed");
            StringAssert.Contains(theme.DescribeSource(), "built-in");
        }

        [TestMethod]
        public void MalformedJsonFallsBackWithoutThrowing()
        {
            ShellTheme theme = ShellTheme.Parse("{ \"name\": \"broken\", ", "theme.json");

            Assert.IsTrue(theme.IsBuiltIn);
            Assert.AreEqual(ShellTheme.Default.TrueColor(ThemeRole.Label),
                theme.TrueColor(ThemeRole.Label));
            Assert.IsTrue(theme.Diagnostics.Count > 0);
        }

        [TestMethod]
        public void JsonThatIsNotAnObjectFallsBack()
        {
            Assert.IsTrue(ShellTheme.Parse("[1, 2, 3]", "theme.json").IsBuiltIn);
            Assert.IsTrue(ShellTheme.Parse("null", "theme.json").IsBuiltIn);
            Assert.IsTrue(ShellTheme.Parse("\"a string\"", "theme.json").IsBuiltIn);
        }

        [TestMethod]
        public void NullJsonFallsBack()
        {
            Assert.IsTrue(ShellTheme.Parse(null, "theme.json").IsBuiltIn);
        }

        [TestMethod]
        public void OneUsableRoleAppliesAndTheRestKeepTheirBuiltInColours()
        {
            ShellTheme theme = ShellTheme.Parse(
                "{ \"colors\": { \"mnemonic\": { \"true\": \"#010203\" } } }", "theme.json");

            Assert.IsFalse(theme.IsBuiltIn, "a colour that reached the screen is not the built-in palette");
            Assert.AreEqual(0x010203, Rgb(theme.TrueColor(ThemeRole.Mnemonic)));
            Assert.AreEqual(Rgb(ShellTheme.Default.TrueColor(ThemeRole.Label)),
                Rgb(theme.TrueColor(ThemeRole.Label)),
                "the roles the file did not mention must be untouched");
        }

        [TestMethod]
        public void AnUnknownRoleIsReportedAndIgnoredRatherThanBlankingThePalette()
        {
            ShellTheme theme = ShellTheme.Parse(
                "{ \"colors\": { \"mnemonic\": { \"true\": \"#010203\" }, \"gadjet\": { \"true\": \"#040506\" } } }",
                "theme.json");

            Assert.AreEqual(0x010203, Rgb(theme.TrueColor(ThemeRole.Mnemonic)));
            Assert.AreEqual(Rgb(ShellTheme.Default.TrueColor(ThemeRole.Label)),
                Rgb(theme.TrueColor(ThemeRole.Label)));
            Assert.IsTrue(ContainsDiagnostic(theme, "gadjet"));
        }

        [TestMethod]
        public void AColourThatIsNotAColourIsReportedAndTheBuiltInIsKept()
        {
            ShellTheme theme = ShellTheme.Parse(
                "{ \"colors\": { \"mnemonic\": { \"true\": \"#ZZZZZZ\" } } }", "theme.json");

            Assert.IsTrue(theme.IsBuiltIn);
            Assert.AreEqual(Rgb(ShellTheme.Default.TrueColor(ThemeRole.Mnemonic)),
                Rgb(theme.TrueColor(ThemeRole.Mnemonic)));
            Assert.IsTrue(theme.Diagnostics.Count > 0);
        }

        [TestMethod]
        public void AFileWhereNothingAppliesIsReportedAsBuiltIn()
        {
            ShellTheme theme = ShellTheme.Parse(
                "{ \"colors\": { \"mnemonic\": { \"true\": \"not a colour at all\" } } }", "theme.json");

            // Nothing reached the screen, so saying otherwise would be a lie the
            // status line repeats to the user.
            Assert.IsTrue(theme.IsBuiltIn);
        }

        [TestMethod]
        public void CommentsAndTrailingCommasAreAccepted()
        {
            ShellTheme theme = ShellTheme.Parse(
                "{ /* a palette */ \"colors\": { \"mnemonic\": { \"true\": \"#010203\", }, }, }",
                "theme.json");

            Assert.AreEqual(0x010203, Rgb(theme.TrueColor(ThemeRole.Mnemonic)));
            Assert.AreEqual(0, theme.Diagnostics.Count, string.Join("; ", new List<string>(theme.Diagnostics).ToArray()));
        }

        [TestMethod]
        public void AFallbackNameIsResolvedCaseInsensitivelyAndWithSpaces()
        {
            ShellTheme theme = ShellTheme.Parse(
                "{ \"colors\": { \"mnemonic\": { \"fallback\": \"Bright Blue\" }, \"label\": { \"fallback\": \"BRIGHT_BLACK\" } } }",
                "theme.json");

            Assert.AreEqual(ColorName16.BrightBlue, theme.Fallback(ThemeRole.Mnemonic));
            Assert.AreEqual(ColorName16.DarkGray, theme.Fallback(ThemeRole.Label));
        }

        [TestMethod]
        public void AnUnknownFallbackNameIsRejectedRatherThanDefaultedSilently()
        {
            ShellTheme theme = ShellTheme.Parse(
                "{ \"colors\": { \"mnemonic\": { \"fallback\": \"chartreusey\" } } }", "theme.json");

            Assert.IsTrue(theme.Diagnostics.Count > 0, "a misspelled colour name must be visible");
            Assert.IsTrue(theme.IsBuiltIn);
        }

        [TestMethod]
        public void TheSixteenColourColumnIsWhatATerminalWithoutTrueColorGets()
        {
            ShellTheme theme = ShellTheme.Default;

            Assert.AreEqual(ColorName16.DarkGray, theme.Fallback(ThemeRole.Gutter));
            Assert.AreEqual(ColorName16.Blue, theme.Fallback(ThemeRole.Mnemonic));
            Assert.AreEqual(ColorName16.Yellow, theme.Fallback(ThemeRole.Operand));
            Assert.AreEqual(ColorName16.Magenta, theme.Fallback(ThemeRole.Directive));
            Assert.AreEqual(ColorName16.Green, theme.Fallback(ThemeRole.Label));
            Assert.AreEqual(ColorName16.Red, theme.Fallback(ThemeRole.Error));
        }

        [TestMethod]
        public void ResolvingWithoutTrueColorReturnsTheFallbackRatherThanTheRgbValue()
        {
            ShellTheme theme = ShellTheme.Parse(
                "{ \"colors\": { \"mnemonic\": { \"true\": \"#010203\", \"fallback\": \"red\" } } }", "theme.json");

            Assert.AreEqual(ColorName16.Red, new Color(theme.Resolve(ThemeRole.Mnemonic, false)));
            Assert.AreEqual(0x010203, Rgb(theme.Resolve(ThemeRole.Mnemonic, true)));
        }

        [TestMethod]
        public void AnAlphaChannelIsAcceptedAndKeptInTheOrderTheFileWritesIt()
        {
            ShellTheme theme = ShellTheme.Parse(
                "{ \"colors\": { \"mnemonic\": { \"true\": \"#01020380\" } } }", "theme.json");

            // The file says RRGGBBAA. Reading it as ARGB would put 0x01 in the alpha
            // and 0x80 in the blue, which is a different palette rather than an
            // error the user would notice.
            Assert.AreEqual(0x01, theme.TrueColor(ThemeRole.Mnemonic).R);
            Assert.AreEqual(0x02, theme.TrueColor(ThemeRole.Mnemonic).G);
            Assert.AreEqual(0x03, theme.TrueColor(ThemeRole.Mnemonic).B);
            Assert.AreEqual(0x80, theme.TrueColor(ThemeRole.Mnemonic).A);
        }

        [TestMethod]
        public void AColourWithoutAnAlphaChannelIsFullyOpaque()
        {
            ShellTheme theme = ShellTheme.Parse(
                "{ \"colors\": { \"mnemonic\": { \"true\": \"#010203\" } } }", "theme.json");

            Assert.AreEqual(255, theme.TrueColor(ThemeRole.Mnemonic).A);
        }

        [TestMethod]
        public void TheShippedThemeFileLoadsAndNamesEveryRole()
        {
            string path = Path.Combine(AppContext.BaseDirectory, ShellTheme.FileName);
            if (!File.Exists(path))
                Assert.Inconclusive("the theme file is not beside the tests; nothing to check here");

            ShellTheme theme = ShellTheme.LoadFile(path);

            Assert.AreEqual(0, theme.Diagnostics.Count,
                "the shipped palette must load clean: " + string.Join("; ", new List<string>(theme.Diagnostics).ToArray()));
            Assert.IsFalse(theme.IsBuiltIn);
        }

        [TestMethod]
        public void TheShippedThemeFileMentionsEveryKnownRole()
        {
            string path = Path.Combine(AppContext.BaseDirectory, ShellTheme.FileName);
            if (!File.Exists(path))
                Assert.Inconclusive("the theme file is not beside the tests; nothing to check here");

            string json = File.ReadAllText(path);

            foreach (string role in ThemeRole.Names)
            {
                StringAssert.Contains(json, "\"" + role + "\"",
                    "the shipped palette should document the role '" + role + "'");
            }
        }

        private static bool ContainsDiagnostic(ShellTheme theme, string needle)
        {
            foreach (string line in theme.Diagnostics)
            {
                if (line.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static int Rgb(Color color)
        {
            // The 24-bit channels only, because the palette table in the plan is
            // written as #RRGGBB and comparing a 24-bit literal against a full
            // ARGB value would fail on the alpha, not on the colour.
            return (color.R << 16) | (color.G << 8) | color.B;
        }
    }
}