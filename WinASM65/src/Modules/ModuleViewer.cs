// WinASM65 - Offline module viewer
//
// Reads a .w65 and writes a single HTML file describing it: segments, the
// symbols it exports and imports, and every relocatable site with the source
// that produced it. Read only: nothing here writes back to the module.
//
// The output is one file with no external reference on purpose. A viewer that
// needs a network, or a sibling directory, is useless on the machine where the
// build actually happens, which is the only place anyone opens a build log.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WinASM65.Core;
using WinASM65.Modules;
using WinASM65.Output;
using WinASM65.Targets;

namespace WinASM65.Modules
{
    public static class ModuleViewer
    {
        /// <summary>
        /// True when the buffer looks like something the viewer can read.
        /// <para>
        /// The archive magic is "W65A", which starts with the module magic "W65",
        /// so the archive has to be tested first. Testing the other way round reads
        /// an archive as a module and then reports a corrupt segment, which looks
        /// like a damaged file rather than a wrong test.
        /// </para>
        /// </summary>
        public static bool CanRead(byte[] data)
        {
            return ArchiveFormat.HasMagic(data) || W65Format.HasMagic(data);
        }

        public static OperationResult Write(string path, string sourcePath)
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            byte[] data;
            try
            {
                data = File.ReadAllBytes(sourcePath);
            }
            catch (IOException ex)
            {
                diagnostics.Add(new Diagnostic(new SourceLocation(sourcePath, 0),
                    "Cannot read '" + sourcePath + "': " + ex.Message));
                return new OperationResult(false, diagnostics);
            }

            if (ArchiveFormat.HasMagic(data))
            {
                ModuleArchive archive;
                OperationResult read = ArchiveFormat.TryRead(sourcePath, out archive);
                if (!read.Success)
                    return read;

                try
                {
                    return ExecutableFile.WriteText(path, RenderArchive(archive, sourcePath));
                }
                catch (IOException ex)
                {
                    diagnostics.Add(new Diagnostic(new SourceLocation(path, 0),
                        "Cannot write '" + path + "': " + ex.Message));
                    return new OperationResult(false, diagnostics);
                }
            }

            if (W65Format.HasMagic(data))
            {
                ModuleImage image;
                string moduleName;
                OperationResult read = W65Format.TryRead(data, moduleNameOf(sourcePath),
                    out image, out moduleName);
                if (!read.Success)
                    return read;

                try
                {
                    return ExecutableFile.WriteText(path, Render(image, sourcePath));
                }
                catch (IOException ex)
                {
                    diagnostics.Add(new Diagnostic(new SourceLocation(path, 0),
                        "Cannot write '" + path + "': " + ex.Message));
                    return new OperationResult(false, diagnostics);
                }
            }

            diagnostics.Add(new Diagnostic(new SourceLocation(sourcePath, 0),
                "'" + sourcePath + "' is neither a .w65 module nor a .w65a archive."));
            return new OperationResult(false, diagnostics);
        }

        private static string moduleNameOf(string path)
        {
            return Path.GetFileNameWithoutExtension(path);
        }

        // ---------------------------------------------------------------- render

        public static string Render(ModuleImage image, string sourcePath)
        {
            StringBuilder html = new StringBuilder();
            string title = string.IsNullOrEmpty(image.ModuleName)
                ? Path.GetFileName(sourcePath)
                : image.ModuleName;

            Head(html, title + " - module");
            Summary(html, image, title, 1);

            html.Append("<h2>Segments</h2>\n<table class=\"grid\">\n");
            html.Append("<tr><th>#</th><th>name</th><th>kind</th><th>bank</th>"
                + "<th>align</th><th>origin</th><th>size</th></tr>\n");
            for (int i = 0; i < image.Segments.Count; i++)
            {
                ModuleSegment segment = image.Segments[i];
                html.Append("<tr><td>" + i + "</td><td>" + Escape(segment.Name) + "</td><td>"
                    + segment.Kind + "</td><td>" + (segment.Kind == SegmentKind.Bss ? "-" : segment.Bank.ToString())
                    + "</td><td>" + segment.Alignment + "</td><td>$"
                    + segment.OriginAddress.ToString("X4") + "</td><td>"
                    + segment.Data.Length + "</td></tr>\n");
            }
            html.Append("</table>\n");

            Symbols(html, "Exports", image.Exports.Count, delegate(int i)
            {
                ModuleExport export = image.Exports[i];
                return "<tr><td>" + Escape(export.Name) + "</td><td>" + export.SegmentIndex
                    + "</td><td>" + export.Offset + "</td></tr>\n";
            }, "<tr><th>name</th><th>segment</th><th>offset</th></tr>\n");

            Symbols(html, "Imports", image.Imports.Count, delegate(int i)
            {
                ModuleImport import = image.Imports[i];
                string provider = string.IsNullOrEmpty(import.ModuleName) ? "-" : Escape(import.ModuleName);
                return "<tr><td>" + Escape(import.Name) + "</td><td>" + provider + "</td></tr>\n";
            }, "<tr><th>name</th><th>expected from</th></tr>\n");

            html.Append("<h2>Relocations</h2>\n");
            if (image.Relocations.Count == 0)
            {
                html.Append("<p class=\"empty\">No relocatable site: the module holds only "
                    + "fixed values.</p>\n");
            }
            else
            {
                html.Append("<table class=\"grid\">\n");
                html.Append("<tr><th>#</th><th>site</th><th>width</th><th>type</th>"
                    + "<th>address</th><th>target</th><th>source</th></tr>\n");
                for (int i = 0; i < image.Relocations.Count; i++)
                {
                    RelocationRecord record = image.Relocations[i];
                    ModuleSegment host = record.SegmentIndex >= 0
                        && record.SegmentIndex < image.Segments.Count
                        ? image.Segments[record.SegmentIndex]
                        : null;

                    string site = host == null
                        ? "?"
                        : "$" + (host.OriginAddress + record.Offset).ToString("X4");
                    string target = string.IsNullOrEmpty(record.TargetSymbol) ? "(value)" : Escape(record.TargetSymbol);
                    string from = string.IsNullOrEmpty(record.SourceFile)
                        ? ""
                        : Escape(record.SourceFile) + ":" + record.SourceLine;

                    html.Append("<tr><td>" + i + "</td><td>" + site + "</td><td>" + record.Width
                        + "</td><td>" + record.Type + "</td><td>$" + record.Address.ToString("X4")
                        + "</td><td>" + target + "</td><td>" + from + "</td></tr>\n");
                }
                html.Append("</table>\n");
            }

            Foot(html);
            return html.ToString();
        }

        public static string RenderArchive(ModuleArchive archive, string sourcePath)
        {
            StringBuilder html = new StringBuilder();
            Head(html, archive.Name + " - archive");

            // The index is built with the resolver's own code, not a second pass:
            // a viewer that disagrees with the linker about what a name resolves to
            // is worse than no viewer.
            ArchiveResolution resolution = new ArchiveResolution();
            List<Diagnostic> indexDiagnostics = new List<Diagnostic>();
            ArchiveFormat.BuildSymbolIndexInto(resolution, archive, indexDiagnostics);

            Dictionary<string, List<string>> providers = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < resolution.Symbols.Count; i++)
            {
                ArchiveSymbol symbol = resolution.Symbols[i];
                List<string> owners;
                if (!providers.TryGetValue(symbol.Name, out owners))
                {
                    owners = new List<string>();
                    providers[symbol.Name] = owners;
                }
                owners.Add(symbol.MemberName);
            }

            Dictionary<string, List<string>> users = UsersBySymbol(archive);

            html.Append("<h1>" + Escape(archive.Name) + "</h1>\n");
            html.Append("<p class=\"sub\">" + archive.Members.Count + " module(s), "
                + archive.References.Count + " reference(s), "
                + resolution.Symbols.Count + " exported symbol(s)</p>\n");

            // The duplicate-export diagnostic is the one a reader cannot reproduce
            // by looking at the members one by one, so it is shown rather than fixed.
            if (indexDiagnostics.Count > 0)
            {
                html.Append("<h2>Diagnostics (" + indexDiagnostics.Count + ")</h2>\n");
                html.Append("<ul class=\"bad\">\n");
                for (int i = 0; i < indexDiagnostics.Count; i++)
                    html.Append("<li>" + Escape(indexDiagnostics[i].Message) + "</li>\n");
                html.Append("</ul>\n");
            }

            if (archive.References.Count > 0)
            {
                html.Append("<h2>References</h2>\n<ul class=\"refs\">\n");
                for (int i = 0; i < archive.References.Count; i++)
                    html.Append("<li>" + Escape(archive.References[i]) + "</li>\n");
                html.Append("</ul>\n");
            }

            Symbols(html, "Symbol index", resolution.Symbols.Count, delegate(int i)
            {
                ArchiveSymbol symbol = resolution.Symbols[i];
                List<string> owners;
                providers.TryGetValue(symbol.Name, out owners);
                return "<tr><td>" + Escape(symbol.Name) + "</td><td>"
                    + Escape(symbol.MemberName) + "</td><td>"
                    + (owners != null && owners.Count > 1 ? "yes" : "no") + "</td><td>"
                    + Owners(owners) + "</td></tr>\n";
            }, "<tr><th>name</th><th>module</th><th>exported twice</th><th>exported by</th></tr>\n");

            for (int m = 0; m < archive.Members.Count; m++)
                html.Append("<h2>" + Escape(archive.Members[m].Name) + "</h2>\n"
                    + RenderBody(archive.Members[m].Module, providers, users));

            Foot(html);
            return html.ToString();
        }

        /// <summary>
        /// Who mentions each name, imports and relocation targets alike. A name
        /// nobody mentions is an export no member of the archive can use: dead code,
        /// or a symbol kept for something that is not here yet.
        /// </summary>
        private static Dictionary<string, List<string>> UsersBySymbol(ModuleArchive archive)
        {
            Dictionary<string, List<string>> users =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            for (int m = 0; m < archive.Members.Count; m++)
            {
                ModuleImage module = archive.Members[m].Module;
                if (module == null)
                    continue;

                if (module.Imports != null)
                {
                    for (int i = 0; i < module.Imports.Count; i++)
                        AddUser(users, module.Imports[i].Name, archive.Members[m].Name);
                }

                if (module.Relocations != null)
                {
                    for (int i = 0; i < module.Relocations.Count; i++)
                        AddUser(users, module.Relocations[i].TargetSymbol, archive.Members[m].Name);
                }
            }

            return users;
        }

        private static void AddUser(Dictionary<string, List<string>> users, string name, string member)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(member))
                return;

            List<string> mentions;
            if (!users.TryGetValue(name, out mentions))
            {
                mentions = new List<string>();
                users[name] = mentions;
            }
            if (!mentions.Contains(member))
                mentions.Add(member);
        }

        private static string Owners(List<string> owners)
        {
            if (owners == null || owners.Count == 0)
                return "-";
            return Escape(string.Join(", ", owners.ToArray()));
        }

        private static string Mentions(List<string> mentions)
        {
            if (mentions == null || mentions.Count == 0)
                return "unused";
            return Escape(string.Join(", ", mentions.ToArray()));
        }

        private static string RenderBody(ModuleImage image,
            Dictionary<string, List<string>> providers, Dictionary<string, List<string>> users)
        {
            StringBuilder html = new StringBuilder();

            html.Append("<table class=\"grid\">\n<tr><th>#</th><th>name</th><th>kind</th>"
                + "<th>bank</th><th>align</th><th>origin</th><th>size</th></tr>\n");
            for (int i = 0; i < image.Segments.Count; i++)
            {
                ModuleSegment segment = image.Segments[i];
                html.Append("<tr><td>" + i + "</td><td>" + Escape(segment.Name) + "</td><td>"
                    + segment.Kind + "</td><td>" + segment.Alignment + "</td><td>$"
                    + segment.OriginAddress.ToString("X4") + "</td><td>" + segment.Data.Length
                    + "</td></tr>\n");
            }
            html.Append("</table>\n");

            Symbols(html, "Exports", image.Exports.Count, delegate(int i)
            {
                ModuleExport export = image.Exports[i];
                List<string> mentions;
                users.TryGetValue(export.Name, out mentions);
                return "<tr><td>" + Escape(export.Name) + "</td><td>" + export.SegmentIndex
                    + "</td><td>" + export.Offset + "</td><td>"
                    + (mentions == null || mentions.Count == 0 ? "no" : "yes") + "</td><td>"
                    + Mentions(mentions) + "</td></tr>\n";
            }, "<tr><th>name</th><th>segment</th><th>offset</th><th>used</th><th>used by</th></tr>\n");

            Symbols(html, "Imports", image.Imports.Count, delegate(int i)
            {
                ModuleImport import = image.Imports[i];
                List<string> owners = null;
                bool satisfied = providers != null
                    && providers.TryGetValue(import.Name, out owners)
                    && owners != null
                    && owners.Count > 0;

                return "<tr><td>" + Escape(import.Name) + "</td><td>"
                    + (string.IsNullOrEmpty(import.ModuleName) ? "-" : Escape(import.ModuleName))
                    + "</td><td class=\"" + (satisfied ? "ok" : "no") + "\">"
                    + (satisfied ? "satisfied" : "NOT SATISFIED") + "</td><td>"
                    + (satisfied ? Owners(owners) : "") + "</td></tr>\n";
            }, "<tr><th>name</th><th>expected from</th><th>status</th><th>provided by</th></tr>\n");

            return html.ToString();
        }

        // ----------------------------------------------------------------- parts

        private static void Head(StringBuilder html, string title)
        {
            html.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n");
            html.Append("<meta charset=\"utf-8\">\n<title>" + Escape(title) + "</title>\n");
            html.Append("<style>\n");
            html.Append("body{font:13px/1.5 system-ui,sans-serif;margin:2rem;color:#222;max-width:70rem}\n");
            html.Append("h1{font-size:1.4rem}h2{font-size:1.05rem;margin-top:1.8rem}\n");
            html.Append("table.grid{border-collapse:collapse;width:100%;margin:.4rem 0}\n");
            html.Append("table.grid th,table.grid td{border:1px solid #ccc;padding:.2rem .5rem;text-align:left}\n");
            html.Append("table.grid th{background:#f2f2f2;font-weight:600}\n");
            html.Append("table.grid tr:nth-child(even) td{background:#fafafa}\n");
            html.Append("p.sub{color:#666;margin:.2rem 0}\np.empty{color:#666;font-style:italic}\n");
            html.Append("ul.refs{margin:.3rem 0;padding-left:1.4rem}\n");
            html.Append("ul.bad{margin:.3rem 0;padding-left:1.4rem}\n");
            html.Append("ul.bad li{color:#a00}\n");
            html.Append("td.ok{color:#060}\ntd.no{color:#a00;font-weight:600}\n");
            html.Append("</style>\n</head>\n<body>\n");
        }

        private static void Foot(StringBuilder html)
        {
            html.Append("<hr>\n<p class=\"sub\">Read only view, generated by WinASM65.</p>\n");
            html.Append("</body>\n</html>\n");
        }

        private static void Summary(StringBuilder html, ModuleImage image, string title, int level)
        {
            html.Append("<h" + level + ">" + Escape(title) + "</h" + level + ">\n");
            html.Append("<p class=\"sub\">" + image.Segments.Count + " segment(s), "
                + image.Exports.Count + " export(s), " + image.Imports.Count + " import(s), "
                + image.Relocations.Count + " relocation(s)</p>\n");
        }

        private static void Symbols(StringBuilder html, string title, int count,
            Func<int, string> row, string header)
        {
            html.Append("<h2>" + title + " (" + count + ")</h2>\n");
            if (count == 0)
            {
                html.Append("<p class=\"empty\">None.</p>\n");
                return;
            }
            html.Append("<table class=\"grid\">\n" + header);
            for (int i = 0; i < count; i++)
                html.Append(row(i));
            html.Append("</table>\n");
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            StringBuilder text = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '&': text.Append("&amp;"); break;
                    case '<': text.Append("&lt;"); break;
                    case '>': text.Append("&gt;"); break;
                    case '"': text.Append("&quot;"); break;
                    case '\'': text.Append("&#39;"); break;
                    default: text.Append(c); break;
                }
            }
            return text.ToString();
        }
    }
}
