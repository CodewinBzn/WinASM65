using System;
using System.Collections.Generic;
using System.Globalization;
using WinASM65.Monitor.Abstractions;

namespace WinASM65.Monitor.Protocol
{
    /// <summary>
    /// What a bridge of contract version 2 says about itself in answer to HELLO:
    /// <c>OK protocol=2 name=Mesen2 version=2.1.1 system=nes caps=memory,cpu,… spaces=PPU,PAL,OAM,CHR</c>.
    ///
    /// Capabilities come from the bridge instead of a table of measured emulators,
    /// so a bridge written for another emulator is taken at its word without a
    /// change here. A bridge of version 1 (no HELLO) still gets the table.
    /// </summary>
    public sealed class BridgeHello
    {
        public int Protocol { get; private set; }
        public string Name { get; private set; }
        public string Version { get; private set; }

        /// <summary>The system the bridge runs, as a WinASM65 system id, or null.</summary>
        public string System { get; private set; }

        public ExecutionCapability Capabilities { get; private set; }

        /// <summary>Memories other than the CPU's that READ and WRITE accept as SPACE:address.</summary>
        public IReadOnlyList<string> Spaces { get; private set; }

        /// <summary>Reads the payload of an OK answer to HELLO.</summary>
        public static bool TryParse(string payload, out BridgeHello hello)
        {
            hello = null;
            if (string.IsNullOrWhiteSpace(payload))
                return false;

            Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string field in payload.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = field.IndexOf('=');
                if (equals > 0)
                    fields[field.Substring(0, equals)] = field.Substring(equals + 1);
            }

            string protocolText, name;
            int protocol;
            if (!fields.TryGetValue("protocol", out protocolText)
                || !int.TryParse(protocolText, NumberStyles.Integer, CultureInfo.InvariantCulture, out protocol)
                || protocol < 2
                || !fields.TryGetValue("name", out name))
                return false;

            string version, system, caps, spaces;
            fields.TryGetValue("version", out version);
            fields.TryGetValue("system", out system);
            fields.TryGetValue("caps", out caps);
            fields.TryGetValue("spaces", out spaces);

            hello = new BridgeHello
            {
                Protocol = protocol,
                Name = name,
                Version = version ?? string.Empty,
                System = string.IsNullOrWhiteSpace(system) ? null : system,
                Capabilities = CapabilitiesOf(caps),
                Spaces = List(spaces)
            };
            return true;
        }

        /// <summary>
        /// The capability flags of a comma-separated list. An unknown word is
        /// ignored: a newer bridge may offer something this build cannot use yet.
        /// </summary>
        public static ExecutionCapability CapabilitiesOf(string list)
        {
            ExecutionCapability capabilities = ExecutionCapability.None;
            foreach (string word in List(list))
            {
                switch (word.ToLowerInvariant())
                {
                    case "memory": capabilities |= ExecutionCapability.Memory; break;
                    case "read": capabilities |= ExecutionCapability.MemoryRead; break;
                    case "write": capabilities |= ExecutionCapability.MemoryWrite; break;
                    case "cpu": capabilities |= ExecutionCapability.CpuState; break;
                    case "pause": capabilities |= ExecutionCapability.Pause; break;
                    case "resume": capabilities |= ExecutionCapability.Resume; break;
                    case "reset": capabilities |= ExecutionCapability.Reset; break;
                    case "step": capabilities |= ExecutionCapability.StepInstruction; break;
                    case "break": capabilities |= ExecutionCapability.BreakpointExecution; break;
                    case "watchread": capabilities |= ExecutionCapability.WatchpointRead; break;
                    case "watchwrite": capabilities |= ExecutionCapability.WatchpointWrite; break;
                    case "state": capabilities |= ExecutionCapability.StateSaveLoad; break;
                }
            }
            return capabilities;
        }

        private static IReadOnlyList<string> List(string text)
        {
            List<string> items = new List<string>();
            if (!string.IsNullOrWhiteSpace(text))
            {
                foreach (string item in text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    items.Add(item.Trim());
            }
            return items;
        }
    }
}
