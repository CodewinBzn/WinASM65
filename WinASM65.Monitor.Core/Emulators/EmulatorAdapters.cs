using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace WinASM65.Monitor.Emulators
{
    /// <summary>
    /// How one emulator is started with a bridge: its command line, the bridge
    /// script it loads, and whatever it needs prepared first.
    ///
    /// Everything emulator-specific about starting lives in one adapter; the rest
    /// of the debugger speaks the bridge contract (docs/site/bridges.html). A new
    /// emulator is a new adapter and a new bridge, or no code at all: the
    /// <see cref="CustomEmulatorAdapter"/> takes its command line from config.json.
    /// </summary>
    public interface IEmulatorAdapter
    {
        /// <summary>The name config.json and launch.json use, such as mesen2.</summary>
        string Name { get; }

        /// <summary>The name shown to a person, such as Mesen2.</summary>
        string DisplayName { get; }

        /// <summary>The file name of the bridge script shipped for this emulator, or null.</summary>
        string BuiltInBridge { get; }

        /// <summary>What to tell the user when the emulator exits before its bridge answers, or null.</summary>
        string EarlyExitHint { get; }

        /// <summary>Prepares the emulator before a launch; false, with the reason logged, when it cannot be.</summary>
        bool Prepare(EmulatorConfiguration emulator, TextWriter log);

        /// <summary>Fills in the command line that loads <paramref name="rom"/> with the bridge <paramref name="script"/>.</summary>
        void AddArguments(ProcessStartInfo start, EmulatorConfiguration emulator, string rom, string script, int port);
    }

    /// <summary>The adapters this build knows, by name.</summary>
    public static class EmulatorAdapters
    {
        public const string Mesen2 = "mesen2";
        public const string MesenCe = "mesence";

        private static readonly Dictionary<string, IEmulatorAdapter> Known =
            new Dictionary<string, IEmulatorAdapter>(StringComparer.OrdinalIgnoreCase)
            {
                { Mesen2, new Mesen2Adapter() },
                { MesenCe, new MesenCeAdapter() },
            };

        /// <summary>The names of the built-in adapters.</summary>
        public static IReadOnlyCollection<string> Names
        {
            get { return Known.Keys; }
        }

        /// <summary>
        /// The adapter of an emulator. A name picks a built-in adapter, or the custom
        /// one for any other name. With no name, a Mesen.exe is taken for Mesen2 (the
        /// emulator every existing project was debugged with) and anything else is
        /// custom.
        /// </summary>
        public static IEmulatorAdapter For(EmulatorConfiguration emulator)
        {
            IEmulatorAdapter adapter;
            if (emulator != null && !string.IsNullOrEmpty(emulator.Name))
                return Known.TryGetValue(emulator.Name, out adapter) ? adapter : new CustomEmulatorAdapter(emulator.Name);

            string file = emulator != null && emulator.Path != null ? Path.GetFileNameWithoutExtension(emulator.Path) : null;
            return string.Equals(file, "Mesen", StringComparison.OrdinalIgnoreCase)
                ? Known[Mesen2]
                : new CustomEmulatorAdapter(file);
        }
    }

    /// <summary>
    /// Mesen2 2.1.1: the ROM then the script on the command line, which opens its
    /// normal window and starts the script. Its Lua sandbox must allow sockets.
    /// </summary>
    public sealed class Mesen2Adapter : IEmulatorAdapter
    {
        public string Name { get { return EmulatorAdapters.Mesen2; } }
        public string DisplayName { get { return "Mesen2"; } }
        public string BuiltInBridge { get { return "bridge_mesen2.lua"; } }

        public string EarlyExitHint
        {
            get { return "If Mesen2 was already open, close it: it runs as a single instance and the new launch was handed to the old window."; }
        }

        public bool Prepare(EmulatorConfiguration emulator, TextWriter log)
        {
            return MesenSettings.Ensure(emulator.Path, log);
        }

        public void AddArguments(ProcessStartInfo start, EmulatorConfiguration emulator, string rom, string script, int port)
        {
            start.ArgumentList.Add(rom);
            start.ArgumentList.Add(script);
        }
    }

    /// <summary>MesenCE 2.2.1: its test runner, which loads the script without a window.</summary>
    public sealed class MesenCeAdapter : IEmulatorAdapter
    {
        public string Name { get { return EmulatorAdapters.MesenCe; } }
        public string DisplayName { get { return "MesenCE"; } }
        public string BuiltInBridge { get { return "bridge.lua"; } }
        public string EarlyExitHint { get { return null; } }

        public bool Prepare(EmulatorConfiguration emulator, TextWriter log)
        {
            return true;
        }

        public void AddArguments(ProcessStartInfo start, EmulatorConfiguration emulator, string rom, string script, int port)
        {
            start.ArgumentList.Add("--testrunner");
            start.ArgumentList.Add(rom);
            start.ArgumentList.Add(script);
        }
    }

    /// <summary>
    /// Any other emulator, started the way config.json says: <c>Arguments</c> is its
    /// command line, where <c>{rom}</c>, <c>{script}</c> and <c>{port}</c> are
    /// replaced, and <c>Bridge</c> the script it loads. Without <c>Arguments</c> the
    /// ROM alone is passed, which starts the emulator without a bridge.
    /// </summary>
    public sealed class CustomEmulatorAdapter : IEmulatorAdapter
    {
        private readonly string _name;

        public CustomEmulatorAdapter(string name)
        {
            _name = string.IsNullOrWhiteSpace(name) ? "custom" : name;
        }

        public string Name { get { return _name; } }
        public string DisplayName { get { return _name; } }
        public string BuiltInBridge { get { return null; } }

        public string EarlyExitHint
        {
            get { return "Check the \"Arguments\" of the Emulator section: they must load the program and the bridge script."; }
        }

        public bool Prepare(EmulatorConfiguration emulator, TextWriter log)
        {
            return true;
        }

        public void AddArguments(ProcessStartInfo start, EmulatorConfiguration emulator, string rom, string script, int port)
        {
            string template = string.IsNullOrWhiteSpace(emulator.Arguments) ? "{rom}" : emulator.Arguments;
            foreach (string argument in Split(template))
            {
                start.ArgumentList.Add(argument
                    .Replace("{rom}", rom ?? string.Empty)
                    .Replace("{script}", script ?? string.Empty)
                    .Replace("{port}", port.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>Splits a command line at spaces outside double quotes; the quotes are removed.</summary>
        public static IReadOnlyList<string> Split(string commandLine)
        {
            List<string> arguments = new List<string>();
            StringBuilder current = new StringBuilder();
            bool quoted = false, any = false;
            foreach (char c in commandLine ?? string.Empty)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                    any = true;
                }
                else if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (any)
                        arguments.Add(current.ToString());
                    current.Clear();
                    any = false;
                }
                else
                {
                    current.Append(c);
                    any = true;
                }
            }
            if (any)
                arguments.Add(current.ToString());
            return arguments;
        }
    }
}
