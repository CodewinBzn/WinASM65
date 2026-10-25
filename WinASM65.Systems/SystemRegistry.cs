using System;
using System.Collections.Generic;

namespace WinASM65.Systems
{
    /// <summary>
    /// Finds the definition of a machine: by the id a project or a bridge names, or
    /// by the image a build produced.
    /// </summary>
    public static class SystemRegistry
    {
        private static readonly SystemDefinition Nes = new NesSystem();

        // Every id of the target catalogue that has a definition of its own.
        private static readonly Dictionary<string, SystemDefinition> Known =
            new Dictionary<string, SystemDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                { "nes", Nes },
                { "famicom", Nes },
            };

        /// <summary>The ids that have a definition of their own (others get the generic 6502).</summary>
        public static IReadOnlyCollection<string> DefinedIds
        {
            get { return Known.Keys; }
        }

        /// <summary>
        /// The definition of a system id; the generic 6502 for one without its own;
        /// null for none, and for <c>raw</c>, the target a project that names no
        /// system gets, which says nothing about the machine.
        /// </summary>
        public static SystemDefinition For(string systemId)
        {
            if (string.IsNullOrWhiteSpace(systemId) || string.Equals(systemId.Trim(), "raw", StringComparison.OrdinalIgnoreCase))
                return null;
            SystemDefinition definition;
            return Known.TryGetValue(systemId.Trim(), out definition) ? definition : new Generic6502System(systemId.Trim());
        }

        /// <summary>The machine an image's header names, or null.</summary>
        public static SystemDefinition Detect(byte[] image)
        {
            return Nes.Recognizes(image) ? Nes : null;
        }

        /// <summary>
        /// The machine of a program, from the best source available: the system the
        /// project names, then the one the bridge reports, then the image's header;
        /// the generic 6502 when none of them knows.
        /// </summary>
        public static SystemDefinition Resolve(string projectSystem, string bridgeSystem, byte[] image)
        {
            return For(projectSystem) ?? For(bridgeSystem) ?? Detect(image) ?? new Generic6502System(null);
        }
    }
}
