using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Linking;
using WinASM65.Modules;
using WinASM65.Output;
using WinASM65.Targets;

namespace WinASM65.Tests
{
    /// <summary>
    /// La table de relocation d'une application GEOS.
    /// <para>
    /// Le test central n'est pas la structure de la table mais son effet : une
    /// image chargee ailleurs, corrigee par la table, doit etre identique a
    /// l'image que le linker produirait en liant a cette adresse. Une table
    /// complete et correcte ne peut pas donner autre chose, et une table qui
    /// laisse un site de cote passerait le controle de structure.
    /// </para>
    /// </summary>
    [TestClass]
    public class GeosRelocationTests
    {
        private const ushort Origin = 0x4000;
        private const ushort Data = 0x4100;
        private const ushort Out = 0x4200;

        // LDA $4100 / STA $4200 / LDA #$7E / JMP $9000
        private static byte[] Program()
        {
            return new byte[]
            {
                0xAD, 0x00, 0x41,
                0x8D, 0x00, 0x42,
                0xA9, 0x7E,
                0x4C, 0x00, 0x90
            };
        }

        private static ModuleImage App()
        {
            ModuleImage module = new ModuleImage();
            module.ModuleName = "app";
            module.AddSegment(new ModuleSegment("CODE", Program(), SegmentKind.Ro, 1, 0)
            {
                OriginAddress = Origin
            });
            module.AddExport(new ModuleExport("Data", 0, 0x100));
            module.AddExport(new ModuleExport("Out", 0, 0x200));
            module.AddRelocation(Reloc(0, 1, 0x4001, 2, RelocationType.Abs16, "Data"));
            module.AddRelocation(Reloc(0, 4, 0x4004, 2, RelocationType.Abs16, "Out"));
            return module;
        }

        private static RelocationRecord Reloc(int segment, int offset, ushort address, byte width,
            RelocationType type, string symbol)
        {
            return new RelocationRecord(segment, string.Empty, offset, address, width, type,
                new List<string> { symbol }, new SourceLocation("t.asm", 1), symbol);
        }

        private static LinkedImage Link(int shift)
        {
            LinkerOptions options = new LinkerOptions();
            options.AddressShift = shift;
            LinkedImage image;
            OperationResult result = new Linker().Link(new List<ModuleImage> { App() }, options, out image);
            if (!result.Success)
                throw new InvalidOperationException("The test program does not link: "
                    + (result.Diagnostics.Count > 0 ? result.Diagnostics[0].ToString() : "unknown"));
            return image;
        }

        [TestMethod]
        public void LaTablePortelesReferencesAbsoluesEtRienDAutre()
        {
            LinkedImage image = Link(0);
            GeosRelocationTable table = GeosRelocationTable.Build(image, Origin);

            Assert.AreEqual(Origin, table.BaseAddress);
            Assert.AreEqual(Origin, table.EntryAddress);
            Assert.AreEqual(2, table.Entries.Count, "seules les deux references absolues comptent");
            Assert.AreEqual(0x4001, table.Entries[0].Address);
            Assert.AreEqual(0x4004, table.Entries[1].Address);
        }

        [TestMethod]
        public void UneReferenceRelativeNeComptePas()
        {
            // A branch is already right wherever the code lands: moving the code
            // moves both ends. Biasing it would be as wrong as leaving an
            // absolute address alone.
            //
            // Zp8, Imm8 and Data8 are not built here because they cannot be: all
            // three carry a value that has to fit in one octet, and the module
            // API can only name a segment address, which is never that small.
            // Their exclusion is the same code path as Rel8's, and a value that
            // is below $0100 is zero page or a constant, neither of which moves
            // with the image.
            ModuleImage module = new ModuleImage();
            module.ModuleName = "only";
            module.AddSegment(new ModuleSegment("CODE", new byte[] { 0xEA, 0x60 },
                SegmentKind.Ro, 1, 0) { OriginAddress = 0x2000 });
            module.AddExport(new ModuleExport("Somewhere", 0, 1));
            module.AddRelocation(Reloc(0, 1, 0x2001, 1, RelocationType.Rel8, "Somewhere"));

            LinkedImage image;
            OperationResult linked = new Linker().Link(new List<ModuleImage> { module }, out image);
            Assert.IsTrue(linked.Success,
                linked.Diagnostics.Count > 0 ? linked.Diagnostics[0].ToString() : "echec");

            Assert.AreEqual(0, image.References.Count, "la branche ne figure pas dans les references");
            Assert.AreEqual(0, GeosRelocationTable.Build(image, 0x2000).Entries.Count);
        }

        [TestMethod]
        public void UnSiteDesigneParDeuxEnregistrementsNEstCompteQuUneFois()
        {
            // Two records naming one site is a linker input quirk, not two
            // references. Counting it twice would add the bias twice and leave an
            // address that is nowhere near the target.
            ModuleImage module = new ModuleImage();
            module.ModuleName = "twice";
            module.AddSegment(new ModuleSegment("CODE", new byte[] { 0xAD, 0x00, 0x00, 0x60 },
                SegmentKind.Ro, 1, 0) { OriginAddress = 0x2000 });
            module.AddExport(new ModuleExport("Target", 0, 0x100));
            module.AddRelocation(Reloc(0, 1, 0x2001, 2, RelocationType.Abs16, "Target"));
            module.AddRelocation(Reloc(0, 1, 0x2001, 2, RelocationType.Data16, "Target"));

            LinkedImage image;
            Assert.IsTrue(new Linker().Link(new List<ModuleImage> { module }, out image).Success);
            Assert.AreEqual(1, image.References.Count);
        }

        [TestMethod]
        public void UnSelecteurDOctetEstDecritParLaTableOuRefuse()
        {
            // "lda #<label" holds the low byte of an address, and a low byte moves
            // when the image does: adding a bias modulo 256 is exact, and addition
            // commutes with reduction. So the site is describable, and it is
            // described -- the entry carries width 1.
            //
            // The property that matters: either the site is described, or the link is
            // refused. There is no third outcome, because a site silently left out
            // relocates to a wrong address with nothing to show for it.
            ModuleImage module = new ModuleImage();
            module.ModuleName = "low";
            module.AddSegment(new ModuleSegment("CODE", new byte[] { 0xA9, 0x40, 0x60 },
                SegmentKind.Ro, 1, 0) { OriginAddress = Origin });
            module.AddExport(new ModuleExport("Routine", 0, 0));
            module.AddRelocation(Reloc(0, 1, 0x4001, 1, RelocationType.LowByte, "Routine"));

            LinkedImage image;
            OperationResult linked = new Linker().Link(new List<ModuleImage> { module }, out image);
            Assert.IsTrue(linked.Success,
                linked.Diagnostics.Count > 0 ? linked.Diagnostics[0].ToString() : "echec");

            GeosRelocationTable table = GeosRelocationTable.Build(image, Origin);
            Assert.AreEqual(1, table.Entries.Count,
                "le bas d'une adresse se deplace : le site doit etre dans la table");
            Assert.AreEqual(1, table.Entries[0].Width);

            // The strongest statement available: relocating gives exactly what a
            // link at the load address would have given.
            byte[] relocated = (byte[])image.Data.Clone();
            table.Apply(relocated, 0, 0x5010);

            // The bias is $1010, so the low byte of $4000 becomes $10 -- not $50.
            // Stating it as a hex address would hide the arithmetic that makes the
            // case work at all: only addition modulo 256 commutes like this.
            Assert.AreEqual(0x10, relocated[1], "le bas de l'adresse doit suivre l'image");
        }

        [TestMethod]
        public void UnSelecteurDOctetEstRefuseALaConstructionDeLaTableQuandIlEstLeHaut()
        {
            // "lda #>label" is the other half, and it is not describable. The new high
            // byte is a function of the whole address plus the carry out of the low
            // byte; the table carries the bias and the site address, not the value, so
            // it cannot know that carry.
            //
            // An entry claiming to describe it would relocate the program to an address
            // one page off, and nothing anywhere would say so. A refusal naming the
            // site is the only honest output.
            ModuleImage module = new ModuleImage();
            module.ModuleName = "high";
            module.AddSegment(new ModuleSegment("CODE", new byte[] { 0xA9, 0x40, 0x60 },
                SegmentKind.Ro, 1, 0) { OriginAddress = Origin });
            module.AddExport(new ModuleExport("Routine", 0, 0));
            module.AddRelocation(Reloc(0, 1, 0x4001, 1, RelocationType.HighByte, "Routine"));

            LinkedImage image;
            OperationResult linked = new Linker().Link(new List<ModuleImage> { module }, out image);
            Assert.IsTrue(linked.Success,
                linked.Diagnostics.Count > 0 ? linked.Diagnostics[0].ToString() : "echec");

            InvalidOperationException refusal =
                Assert.ThrowsException<InvalidOperationException>(
                    () => GeosRelocationTable.Build(image, Origin)) as InvalidOperationException;
            Assert.IsNotNull(refusal);
            StringAssert.Contains(refusal.Message, "4001");
            StringAssert.Contains(refusal.Message, "carry");
        }

        [TestMethod]
        public void UneImageChargeeAElsePartDonneLaMemeImageQuUnLienALAdresse()
        {
            int[] loads = new int[] { 0x0400, 0x0810, 0x2000, 0x3F00, 0x8000, 0xC000 };
            foreach (int load in loads)
            {
                LinkedImage moved = Link(0);
                GeosRelocationTable table = GeosRelocationTable.Build(moved, Origin);

                byte[] relocated = (byte[])moved.Data.Clone();
                table.Apply(relocated, 0, (ushort)load);

                byte[] expected = Link(load - Origin).Data;

                Assert.AreEqual(expected.Length, relocated.Length, "charge a $" + load.ToString("X4"));
                for (int b = 0; b < expected.Length; b++)
                {
                    Assert.AreEqual(expected[b], relocated[b],
                        "charge a $" + load.ToString("X4") + ", octet " + b
                        + " : la table ne donne pas le meme resultat qu'un lien a cette adresse");
                }
            }
        }

        [TestMethod]
        public void UneImageDeplaceeEnArriereEstEgaleAuLienCorrespondant()
        {
            // Backwards is the direction a subtraction gets wrong. The bias is
            // negative and has to survive as two's complement across the table.
            LinkedImage moved = Link(0);
            GeosRelocationTable table = GeosRelocationTable.Build(moved, Origin);

            byte[] relocated = (byte[])moved.Data.Clone();
            table.Apply(relocated, 0, (ushort)(Origin - 0x2000));

            byte[] expected = Link(-0x2000).Data;
            for (int b = 0; b < expected.Length; b++)
                Assert.AreEqual(expected[b], relocated[b], "octet " + b);
        }

        [TestMethod]
        public void LeDeplacementCorrigeLesReferencesEtRienDAutre()
        {
            // A table that is too eager is as wrong as one that is too shy: the
            // bytes that are not a site must come out untouched.
            LinkedImage image = Link(0);
            GeosRelocationTable table = GeosRelocationTable.Build(image, Origin);

            byte[] relocated = (byte[])image.Data.Clone();
            table.Apply(relocated, 0, 0x5000);

            for (int b = 0; b < relocated.Length; b++)
            {
                bool isSite = b == 1 || b == 2 || b == 4 || b == 5;
                if (isSite)
                    continue;
                Assert.AreEqual(image.Data[b], relocated[b], "l'octet " + b + " n'est pas un site");
            }
        }

        [TestMethod]
        public void LaTableSeRelit()
        {
            // An application relocates itself from these bytes, so writing them
            // without a reader would only help the machine that wrote them.
            LinkedImage image = Link(0);
            GeosRelocationTable written = GeosRelocationTable.Build(image, 0x4003);

            GeosRelocationTable read;
            Assert.IsTrue(GeosRelocationTable.TryRead(written.Data, out read), "la table ecrit se relit");

            Assert.AreEqual(written.BaseAddress, read.BaseAddress);
            Assert.AreEqual(written.EntryAddress, read.EntryAddress);
            Assert.AreEqual(written.Entries.Count, read.Entries.Count);
            for (int i = 0; i < written.Entries.Count; i++)
            {
                Assert.AreEqual(written.Entries[i].Address, read.Entries[i].Address);
                Assert.AreEqual(written.Entries[i].Width, read.Entries[i].Width);
            }
        }

        [TestMethod]
        public void UneTableQuiNEstPasLaNôtreEstRefusee()
        {
            // Anything else in that area is somebody else's. Reading it as ours
            // would relocate an image by numbers nobody wrote.
            LinkedImage image = Link(0);
            byte[] data = GeosRelocationTable.Build(image, Origin).Data;

            byte[] foreign = (byte[])data.Clone();
            foreign[0] = 0x00;
            GeosRelocationTable table;
            Assert.IsFalse(GeosRelocationTable.TryRead(foreign, out table), "magic absent");

            byte[] truncated = new byte[data.Length - 1];
            Array.Copy(data, truncated, truncated.Length);
            Assert.IsFalse(GeosRelocationTable.TryRead(truncated, out table), "table coupee");

            byte[] wide = (byte[])data.Clone();
            wide[GeosRelocationTable.HeaderSize + 2] = 3;
            Assert.IsFalse(GeosRelocationTable.TryRead(wide, out table), "largeur impossible");

            Assert.IsFalse(GeosRelocationTable.TryRead(new byte[4], out table), "trop court");
            Assert.IsFalse(GeosRelocationTable.TryRead(null, out table), "rien du tout");
        }

        [TestMethod]
        public void UnSiteHorsDeLImageEstSignalePlutotQueIgnore()
        {
            // A site the image does not contain is a table for a different image.
            // Skipping it would leave a reference uncorrected and the program would
            // fail later, somewhere else, for no visible reason. The table is built
            // by hand here because the writer would not produce one: that is the
            // point, the reader has to cope with what it is given.
            byte[] data = new byte[GeosRelocationTable.HeaderSize + GeosRelocationTable.EntrySize];
            for (int i = 0; i < GeosRelocationTable.Magic.Length; i++)
                data[i] = GeosRelocationTable.Magic[i];
            data[4] = 0x00;
            data[5] = 0x40;                   // base $4000
            data[6] = 0x01;                   // one entry
            data[10] = 0x00;
            data[11] = 0x90;                  // site $9000, past the end
            data[12] = 0x02;

            GeosRelocationTable table;
            Assert.IsTrue(GeosRelocationTable.TryRead(data, out table));

            try
            {
                table.Apply(new byte[16], 0, 0x5000);
                Assert.Fail("Un site hors de l'image devrait etre signale.");
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, "9000");
            }
        }

        [TestMethod]
        public void LeDecalageDUneImageEstAppliqueALAdresseAImage()
        {
            // The linker may be told to move the whole image, and the table it
            // produces has to describe the result rather than the intent. This is
            // the case where a table built from the pre-shift addresses would
            // still look right structurally and be wrong by the shift.
            LinkedImage image = Link(0x1000);
            Assert.AreEqual(0x5000, image.OriginAddress);
            Assert.AreEqual(0x5001, image.References[0].Address, "le site a bouge avec le code");

            GeosRelocationTable table = GeosRelocationTable.Build(image, 0x5000);
            Assert.AreEqual(0x5000, table.BaseAddress);

            byte[] relocated = (byte[])image.Data.Clone();
            table.Apply(relocated, 0, 0x6000);
            Assert.AreEqual(0x6100, cpu16(relocated, 1),
                "le site portait $5100, l'image est deplacee de $1000, donc $6100");
        }

        private static int cpu16(byte[] data, int at)
        {
            return data[at] | (data[at + 1] << 8);
        }
    }
}
