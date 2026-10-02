using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Monitor.Shell;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// The listing seam, while the library side of it does not exist.
    ///
    /// The contract these tests hold is the one the panes are written against: an
    /// unavailable source produces no rows, says why, and cannot throw. That is what
    /// lets A3 and A1 land in either order — the panes are finished now and will
    /// not change when the real source arrives.
    ///
    /// The tests also pin the shape a new implementation has to produce, because
    /// that shape is the instruction the other session is working to: bytes per row,
    /// an address that is absent rather than zero, and role-tagged text rather than
    /// colours.
    /// </summary>
    [TestClass]
    public class ListingSeamTests
    {
        [TestMethod]
        public void TheSeamIsNarrowEnoughToRePoint()
        {
            // If this ever grows, the seam has stopped being a seam and has become a
            // second listing implementation inside the monitor.
            Type contract = typeof(IListingSource);

            foreach (System.Reflection.MethodInfo method in contract.GetMethods())
            {
                Assert.IsTrue(
                    method.Name == "get_IsAvailable" || method.Name == "get_UnavailableReason" || method.Name == "Rows",
                    "IListingSource grew a member: " + method.Name + ". Keep the seam re-pointable.");
            }
        }

        [TestMethod]
        public void TheFactoryHandsOverSomethingThatIsHonestAboutBeingEmpty()
        {
            IListingSource source = ListingSourceFactory.Create();

            Assert.IsNotNull(source);
            Assert.IsFalse(source.IsAvailable, "the library side is not in this build, so nothing is available");
        }

        [TestMethod]
        public void AnUnavailableSourceReturnsNoRowsRatherThanThrowing()
        {
            IListingSource source = new UnavailableListingSource();

            IReadOnlyList<ListingRow> rows = source.Rows(new ListingRequest("game.asm", 0x8000, 100));

            Assert.AreEqual(0, rows.Count);
        }

        [TestMethod]
        public void AnUnavailableSourceNamesWhatIsMissing()
        {
            string reason = new UnavailableListingSource().UnavailableReason;

            StringAssert.Contains(reason, "IListingService");
            StringAssert.Contains(reason, "InstructionDocs");
            StringAssert.Contains(reason, "lexer");
        }

        [TestMethod]
        public void AnUnavailableSourceSurvivesANullRequest()
        {
            // The pane must be able to ask before it knows what to ask about.
            Assert.AreEqual(0, new UnavailableListingSource().Rows(null).Count);
        }

        [TestMethod]
        public void ARowThatEmitsNothingSaysSoRatherThanShowingAddressZero()
        {
            ListingRow row = new ListingRow
            {
                LineNumber = 12,
                Address = -1,
                Bytes = new List<byte>(),
                Source = "; a comment",
            };

            Assert.IsFalse(row.EmitsBytes);

            // Zero is a real address. A comment line must not be shown as $0000.
            Assert.AreEqual(-1, row.Address);
        }

        [TestMethod]
        public void ARowWithBytesIsOneThatEmits()
        {
            ListingRow row = new ListingRow
            {
                LineNumber = 12,
                Address = 0xC01A,
                Bytes = new List<byte> { 0xA9, 0x5A },
                Cycles = 2,
            };

            Assert.IsTrue(row.EmitsBytes);
            Assert.AreEqual(2, row.Bytes.Count);
            Assert.AreEqual(2, row.Cycles);
        }

        [TestMethod]
        public void TokensCarryARoleRatherThanAColour()
        {
            ListingToken token = new ListingToken("LDA", ThemeRole.Mnemonic);

            Assert.AreEqual("LDA", token.Text);
            Assert.AreEqual(ThemeRole.Mnemonic, token.Role);
        }

        [TestMethod]
        public void AnUnknownOrMissingRoleFallsBackToDefaultRatherThanLeavingItBlank()
        {
            Assert.AreEqual(ThemeRole.Default, new ListingToken("x", null).Role);
            Assert.AreEqual(ThemeRole.Default, new ListingToken(null, "no-such-role").Role);
            Assert.AreEqual(string.Empty, new ListingToken(null, "no-such-role").Text);
        }

        [TestMethod]
        public void SetTokensReplacesRatherThanAppends()
        {
            ListingRow row = new ListingRow();
            row.SetTokens(new List<ListingToken> { new ListingToken("a", ThemeRole.Label) });
            row.SetTokens(new List<ListingToken> { new ListingToken("b", ThemeRole.Label) });

            Assert.AreEqual(1, row.Tokens.Count);
            Assert.AreEqual("b", row.Tokens[0].Text);
        }

        [TestMethod]
        public void SetTokensToleratesNull()
        {
            ListingRow row = new ListingRow();
            row.SetTokens(null);

            Assert.AreEqual(0, row.Tokens.Count);
        }

        [TestMethod]
        public void ARequestCarriesWhatTheSourceNeedsToProduce()
        {
            ListingRequest request = new ListingRequest("game.asm", 0xC000, 40);

            Assert.AreEqual("game.asm", request.SourceFile);
            Assert.AreEqual(0xC000, request.Origin);
            Assert.AreEqual(40, request.MaxRows);
        }

        [TestMethod]
        public void ARealSourceCanBeDroppedInWithoutTouchingThePanes()
        {
            // Proves the seam is implementable outside the monitor: a source written
            // against the three things the seam asks for works, and the panes cannot
            // tell it from the unavailable one except by the flag.
            IListingSource working = new StubListingSource();

            Assert.IsTrue(working.IsAvailable);
            Assert.AreEqual(string.Empty, working.UnavailableReason);
            Assert.AreEqual(1, working.Rows(new ListingRequest("game.asm", 0x8000, 10)).Count);
        }

        /// <summary>
        /// What A1 has to deliver, written out as code: a source, an origin and a
        /// row ceiling in; rows out. Nothing else.
        /// </summary>
        private sealed class StubListingSource : IListingSource
        {
            public bool IsAvailable
            {
                get { return true; }
            }

            public string UnavailableReason
            {
                get { return string.Empty; }
            }

            public IReadOnlyList<ListingRow> Rows(ListingRequest request)
            {
                ListingRow row = new ListingRow
                {
                    LineNumber = 3,
                    Address = request.Origin,
                    Bytes = new List<byte> { 0xA9, 0x5A },
                    Cycles = 2,
                    Source = "  lda #$5A",
                };
                row.SetTokens(new List<ListingToken>
                {
                    new ListingToken("  lda", ThemeRole.Mnemonic),
                    new ListingToken(" #$5A", ThemeRole.Operand),
                });

                return new List<ListingRow> { row };
            }
        }
    }
}