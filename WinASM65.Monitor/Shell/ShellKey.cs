using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using WinASM65.Cpu;
using WinASM65.Monitor.Abstractions;
using WinASM65.Monitor.Protocol;
using WinASM65.Projects;

namespace WinASM65.Monitor.Shell
{
    /// <summary>One key the plan binds, whether this build binds it, and what it needs.</summary>
    public sealed class ShellKey
    {
        public ShellKey(string key, string description, bool bound)
            : this(key, description, bound, ExecutionCapability.None)
        {
        }

        /// <param name="key">How the key is written on the help screen.</param>
        /// <param name="description">What it does.</param>
        /// <param name="bound">Whether this build binds it at all.</param>
        /// <param name="requires">
        /// The capability the action needs from the attached machine, or
        /// <see cref="ExecutionCapability.None"/> when the shell can do it on its
        /// own.
        /// </param>
        public ShellKey(string key, string description, bool bound, ExecutionCapability requires)
        {
            Key = key;
            Description = description;
            Bound = bound;
            Requires = requires;
        }

        /// <summary>How the key is written on the help screen.</summary>
        public string Key { get; }

        public string Description { get; }

        /// <summary>
        /// False when the action belongs to a later milestone. The key is still
        /// listed: a user reading the plan's table should find out here that a key
        /// is not there, rather than press it and conclude the shell is broken.
        ///
        /// A property of this build and of nothing else. Whether the machine behind
        /// the shell can perform the action is <see cref="Requires"/>, which is
        /// about the attached backend; keeping the two apart is what lets the help
        /// screen distinguish a key this build never wrote from a key this machine
        /// cannot answer.
        /// </summary>
        public bool Bound { get; }

        /// <summary>
        /// The capability the attached machine must declare for this key to do
        /// anything. Exactly the bit behind the command the key issues — F9 resumes,
        /// so it needs <see cref="ExecutionCapability.Resume"/> and not "running",
        /// which no backend declares and every backend does some of.
        /// </summary>
        public ExecutionCapability Requires { get; }

        /// <summary>Whether <paramref name="machine"/> grants everything this key needs.</summary>
        public bool AvailableWhen(ExecutionCapability machine)
        {
            return ExecutionCapabilities.Has(machine, Requires);
        }
    }
}