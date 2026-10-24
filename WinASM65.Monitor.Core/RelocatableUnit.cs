using System;
using System.Collections.Generic;
using System.IO;
using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.Linking;
using WinASM65.Modules;

namespace WinASM65.Monitor
{
    /// <summary>
    /// A unit assembled once, ready to be put anywhere.
    ///
    /// This is the whole point of the monitor, and it is not a feature of the
    /// assembler: an assembler produces bytes frozen at one address, and asking it
    /// for the same routine at another address means assembling again. Here the
    /// assembled modules are kept, and moving the code is a second link of the same
    /// images at a different shift. Nothing is re-read, re-tokenised or re-emitted,
    /// so the routine that lands at $F000 is the routine that was assembled, with its
    /// relocations recomputed for $F000.
    ///
    /// The work belongs to the assembler's linker, not here. This class decides
    /// nothing about relocations; it keeps the images and asks.
    /// </summary>
    public sealed class RelocatableUnit
    {
        private readonly List<ModuleImage> _modules;

        /// <summary>Name used by the protocol to designate this unit.</summary>
        public string Name { get; private set; }

        /// <summary>
        /// Address the code lands at with no shift. Every later placement is this
        /// address plus a shift, so a target below it produces a negative shift: the
        /// linker wraps it into 16 bits, and refuses the placements that fall out of
        /// the address space rather than writing them anywhere.
        /// </summary>
        public int NaturalOrigin { get; private set; }

        /// <summary>Bytes the unit occupies, gaps included.</summary>
        public int Length { get; private set; }

        /// <summary>Names the unit publishes, for display and for symbol lookup.</summary>
        public IReadOnlyList<string> Exports
        {
            get
            {
                List<string> names = new List<string>();
                for (int m = 0; m < _modules.Count; m++)
                {
                    for (int e = 0; e < _modules[m].Exports.Count; e++)
                        names.Add(_modules[m].Exports[e].Name);
                }
                return names;
            }
        }

        private RelocatableUnit(string name, List<ModuleImage> modules, int origin, int length)
        {
            Name = name;
            _modules = modules;
            NaturalOrigin = origin;
            Length = length;
        }

        /// <summary>
        /// Links the unit at <paramref name="address"/> and returns the bytes to
        /// write there, plus the sites that were corrected to get there.
        /// </summary>
        public LoadedBlock PlaceAt(int address)
        {
            if (address < 0 || address > AddressSpace.MaxAddress)
                throw new MonitorException("placement address outside the address space: " + address);

            LinkerOptions options = new LinkerOptions();
            options.AddressShift = address - NaturalOrigin;

            LinkedImage image;
            OperationResult result = new Linker().Link(_modules, options, out image);
            if (!result.Success)
                throw new MonitorException("cannot place '" + Name + "' at $" + address.ToString("X4")
                    + " : " + Describe(result));

            if (image.OriginAddress != address)
            {
                // The caller asked for a specific address and got another one. A unit
                // with no origin, or one the linker aligned away, would do that, and
                // writing it anyway would put the code somewhere the caller never
                // named.
                throw new MonitorException("'" + Name + "' asked for $" + address.ToString("X4")
                    + " and landed at $" + image.OriginAddress.ToString("X4"));
            }

            return new LoadedBlock(image.OriginAddress, image.Data, image.References);
        }

        /// <summary>
        /// Assembles a source file and keeps the modules it produced.
        ///
        /// Undefined names are reported, because a typo in a label would otherwise
        /// become a relocation nothing can resolve, and the failure would surface
        /// later, at link time, naming a file the user is no longer looking at. A
        /// name declared by .import is left alone: the linker is the one that
        /// resolves it.
        /// </summary>
        public static RelocatableUnit FromSource(string sourcePath, AssemblerOptions options)
        {
            if (string.IsNullOrEmpty(sourcePath))
                throw new MonitorException("no source file given");
            if (!File.Exists(sourcePath))
                throw new MonitorException("source not found: " + sourcePath);

            string objectPath = Path.Combine(Path.GetTempPath(),
                "WinASM65Monitor_" + Guid.NewGuid().ToString("N") + ".o");
            try
            {
                AssemblyResult result = new AssemblerEngine().Assemble(sourcePath, objectPath);
                if (!result.Success)
                    throw new MonitorException("assembly failed: " + Describe(result));

                if (result.Module == null)
                {
                    // Not an incidental limitation: a unit with no .export and no
                    // .import produces no module at all, because there is nothing for
                    // a linker to hold on to. Saying so beats writing an empty block.
                    throw new MonitorException("'" + Path.GetFileName(sourcePath)
                        + "' declares no .export and no .import, so it produces no module"
                        + " to keep. Export at least its entry point.");
                }

                List<ModuleImage> modules = new List<ModuleImage> { result.Module };
                return Create(Path.GetFileNameWithoutExtension(sourcePath), modules);
            }
            finally
            {
                try
                {
                    if (File.Exists(objectPath))
                        File.Delete(objectPath);
                }
                catch (IOException)
                {
                }
            }
        }

        /// <summary>Reads .w65 modules already assembled and keeps them.</summary>
        public static RelocatableUnit FromModuleFiles(IReadOnlyList<string> paths)
        {
            if (paths == null || paths.Count == 0)
                throw new MonitorException("no module file given");

            List<ModuleImage> modules = new List<ModuleImage>();
            for (int i = 0; i < paths.Count; i++)
            {
                ModuleImage image;
                string name;
                OperationResult result = W65Format.TryRead(paths[i], out image, out name);
                if (!result.Success)
                    throw new MonitorException("cannot read module '" + paths[i] + "': " + Describe(result));
                modules.Add(image);
            }

            string unit = paths.Count == 1
                ? Path.GetFileNameWithoutExtension(paths[0])
                : "link" + paths.Count;
            return Create(unit, modules);
        }

        private static RelocatableUnit Create(string name, List<ModuleImage> modules)
        {
            LinkedImage image;
            OperationResult result = new Linker().Link(modules, out image);
            if (!result.Success)
                throw new MonitorException("cannot link '" + name + "' at its own addresses: " + Describe(result));

            return new RelocatableUnit(name, modules, image.OriginAddress, image.Data.Length);
        }

        private static string Describe(OperationResult result)
        {
            return MonitorDiagnostics.Describe(result.Diagnostics);
        }

        private static string Describe(AssemblyResult result)
        {
            return MonitorDiagnostics.Describe(result.Diagnostics);
        }
    }

    /// <summary>Bytes ready to be written, and where they go.</summary>
    public sealed class LoadedBlock
    {
        public int Address { get; private set; }
        public byte[] Bytes { get; private set; }

        /// <summary>
        /// Sites the linker had to correct to place the code here. They are the proof
        /// that the move was a link and not a copy: a block whose site list is empty
        /// after a move had nothing address-dependent in it.
        /// </summary>
        public IReadOnlyList<LinkedReference> Sites { get; private set; }

        public LoadedBlock(int address, byte[] bytes, IReadOnlyList<LinkedReference> sites)
        {
            Address = address;
            Bytes = bytes;
            Sites = sites;
        }

        public override string ToString()
        {
            return "$" + Address.ToString("X4") + " " + Bytes.Length + " bytes, "
                + Sites.Count + " site(s)";
        }
    }

    /// <summary>Diagnostics turned into one line, so a refusal names its cause.</summary>
    internal static class MonitorDiagnostics
    {
        public static string Describe(IReadOnlyList<Diagnostic> diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0)
                return "no diagnostic given";

            System.Text.StringBuilder text = new System.Text.StringBuilder();
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (i > 0)
                    text.Append(" | ");
                text.Append(diagnostics[i].Message);
            }
            return text.ToString();
        }
    }
}
