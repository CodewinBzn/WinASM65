using System;
using System.Collections.Generic;

namespace WinASM65.Graphics.Systems
{
    /// <summary>
    /// The machines the designer can draw for, by the id asset files name.
    /// </summary>
    public static class GraphicsSystems
    {
        private static readonly IGraphicsSystem Nes = new NesGraphics();

        private static readonly Dictionary<string, IGraphicsSystem> Known =
            new Dictionary<string, IGraphicsSystem>(StringComparer.OrdinalIgnoreCase)
            {
                { "nes", Nes },
                { "famicom", Nes },
            };

        /// <summary>Every graphics system once, in a stable order.</summary>
        public static IReadOnlyList<IGraphicsSystem> All
        {
            get { return new[] { Nes }; }
        }

        /// <summary>The graphics system of an id, or null when the designer does not know the machine.</summary>
        public static IGraphicsSystem For(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;
            IGraphicsSystem system;
            return Known.TryGetValue(id.Trim(), out system) ? system : null;
        }
    }
}
