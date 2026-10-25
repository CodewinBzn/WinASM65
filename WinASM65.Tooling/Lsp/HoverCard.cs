using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using WinASM65.Core;
using WinASM65.Cpu;
using ListingRow = WinASM65.Output.ListingRow;

namespace WinASM65.Tooling.Lsp
{
    /// <summary>
    /// The markdown of a hover, laid out like a JSDoc card: the code in a block, a
    /// title and one sentence, then "@tag" lines, then the reference table.
    /// </summary>
    public static class HoverCard
    {
        /// <summary>The card of an instruction: the form the line assembled to, and every form.</summary>
        public static string Instruction(string mnemonic, string sourceLine, InstructionDocumentation assembled,
            IReadOnlyList<InstructionDocumentation> forms, ListingRow row)
        {
            StringBuilder md = new StringBuilder();
            CodeBlock(md, sourceLine);

            InstructionDescription description;
            string upper = mnemonic.ToUpperInvariant();
            if (InstructionDescriptions.TryGet(upper, out description))
            {
                md.Append("**").Append(upper).Append("** — ").Append(description.Name).Append("\n\n");
                md.Append(description.Summary).Append("\n\n");
            }
            else
            {
                md.Append("**").Append(upper).Append("**\n\n");
            }

            if (assembled != null)
            {
                Tag(md, "mode", AddressingModes.Describe(assembled.Mode)
                    + (string.IsNullOrEmpty(assembled.Syntax) ? "" : " — `" + assembled.Mnemonic + " " + assembled.Syntax + "`"));
                if (assembled.CyclesText != null)
                    Tag(md, "cycles", assembled.CyclesText);
                Tag(md, "bytes", assembled.Length.ToString(CultureInfo.InvariantCulture) + Placement(row));
            }
            if (description != null)
                Tag(md, "flags", description.Flags.Length == 0 ? "none" : "`" + description.Flags + "`");

            if (forms.Count > 0)
            {
                md.Append("\n---\n\n");
                md.Append("| Form | Mode | Opcode | Bytes | Cycles |\n");
                md.Append("|---|---|---|---|---|\n");
                foreach (InstructionDocumentation form in forms)
                {
                    md.Append("| `").Append(form.Mnemonic).Append(string.IsNullOrEmpty(form.Syntax) ? "" : " " + form.Syntax).Append("`")
                        .Append(" | ").Append(AddressingModes.Describe(form.Mode))
                        .Append(" | `$").Append(form.Opcode.ToString("X2")).Append("`")
                        .Append(" | ").Append(form.Length.ToString(CultureInfo.InvariantCulture))
                        .Append(" | ").Append(CyclesCell(form))
                        .Append(form == assembled ? " ◀" : "")
                        .Append(" |\n");
                }

                List<string> legend = new List<string>();
                if (forms.Any(f => f.CycleNote != null && f.CycleNote.Contains("page") && !f.CycleNote.Contains("taken")))
                    legend.Add("+p " + CycleTimings.PageCrossNote);
                if (forms.Any(f => f.CycleNote != null && f.CycleNote.Contains("taken")))
                    legend.Add("+b " + CycleTimings.BranchNote);
                if (forms.Any(f => f.CycleNote != null && f.CycleNote.Contains("decimal")))
                    legend.Add("+d " + CycleTimings.DecimalNote);
                if (legend.Count > 0)
                    md.Append("\n").Append("_").Append(string.Join(" · ", legend)).Append("_\n");
            }
            return md.ToString().TrimEnd();
        }

        /// <summary>The card of a name: its value, and what the line it is read on assembled to.</summary>
        public static string Symbol(string name, long value, string sourceLine, InstructionDocumentation assembled, ListingRow row)
        {
            StringBuilder md = new StringBuilder();
            md.Append("```winasm65\n").Append(name).Append(" = $").Append(value.ToString(value > 0xFF ? "X4" : "X2", CultureInfo.InvariantCulture)).Append("\n```\n\n");
            Tag(md, "value", "`$" + value.ToString("X4", CultureInfo.InvariantCulture) + "` (" + value.ToString(CultureInfo.InvariantCulture) + ")");
            if (row != null)
            {
                md.Append("\n---\n\n");
                CodeBlock(md, sourceLine);
                if (assembled != null && assembled.CyclesText != null)
                    Tag(md, "cycles", assembled.CyclesText);
                Tag(md, "bytes", row.Bytes.Count.ToString(CultureInfo.InvariantCulture) + Placement(row));
            }
            return md.ToString().TrimEnd();
        }

        private static void CodeBlock(StringBuilder md, string sourceLine)
        {
            string code = SourceText.StripComment(sourceLine ?? string.Empty).Trim();
            if (code.Length == 0)
                return;
            md.Append("```winasm65\n").Append(code).Append("\n```\n\n");
        }

        // One tag per paragraph, as a JSDoc hover renders "@param".
        private static void Tag(StringBuilder md, string tag, string text)
        {
            md.Append("*@").Append(tag).Append("* — ").Append(text).Append("\n\n");
        }

        private static string Placement(ListingRow row)
        {
            if (row == null || !row.Address.HasValue || row.Bytes.Count == 0)
                return string.Empty;
            return " — `" + string.Join(" ", row.Bytes.Select(b => b.ToString("X2"))) + "` at `$"
                + row.Address.Value.ToString("X4", CultureInfo.InvariantCulture) + "`";
        }

        private static string CyclesCell(InstructionDocumentation form)
        {
            if (!form.Cycles.HasValue)
                return "—";
            string cell = form.Cycles.Value.ToString(CultureInfo.InvariantCulture);
            if (form.CycleNote == null)
                return cell;
            // The table keeps the short marks; the full sentence is in @cycles.
            if (form.CycleNote.Contains("taken"))
                cell += "+b";
            else if (form.CycleNote.Contains("page"))
                cell += "+p";
            if (form.CycleNote.Contains("decimal"))
                cell += "+d";
            return cell;
        }
    }
}
