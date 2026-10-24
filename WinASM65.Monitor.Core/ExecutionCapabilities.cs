using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using WinASM65.Monitor.Abstractions;

namespace WinASM65.Monitor
{
    /// <summary>
    /// What each measured backend can do, as the flag set the host greys out
    /// against, and the only words this project uses to say so out loud.
    ///
    /// This is the host's half of <c>IExecutionAdapter</c>. The contract declares
    /// the flags; an adapter answers them; this class is where the answers come
    /// from, so the host and the published capability table cannot disagree.
    ///
    /// Declared, never probed. A capability is not learned by sending PAUSE and
    /// catching the refusal: that is a capability the user has already pressed, and
    /// the point of the flag set is that they never have to. So each row below is a
    /// measurement taken on the exact build it names, and a host outside the table
    /// declares <see cref="ExecutionCapability.None"/> — not a guess, and not a
    /// default that happens to be convenient. MesenCE 2.3.0 has not been measured,
    /// and claiming 2.2.1's answers for it would be the same lie with a different
    /// version number.
    ///
    /// The measurements, and where each one comes from:
    ///
    /// <list type="bullet">
    /// <item>Mesen2 2.1.1 — <c>Bridge/bridge_mesen2.lua</c>, measured against
    /// Mesen2: <c>emu.pause</c>, <c>emu.resume</c>, <c>emu.reset</c>, stepping to
    /// <c>$C01A</c> and <c>$C01B</c> exactly, <c>BREAK SET exec</c> answering
    /// <c>OK</c>, both memory callback kinds accepted, <c>emu.getState</c> and
    /// <c>emu.setState</c> both answering. <see cref="ExecutionCapability.FullControl"/>.</item>
    /// <item>MesenCE 2.2.1 — <c>Bridge/bridge.lua</c>, which refuses PAUSE, RESUME,
    /// STEP, BREAK and RESET by name and serves memory and snapshots.
    /// <see cref="ExecutionCapability.Memory"/> plus
    /// <see cref="ExecutionCapability.StateSaveLoad"/>.</item>
    /// <item>MAME 0.289 — <c>MameCapabilityTests</c> against <c>mame famicom</c>:
    /// the program space holds the cartridge's PRG ROM, a write is observable and
    /// the original byte restored, PC/A/X/Y/SP/P are readable, and
    /// <c>debug:step</c> returns without moving the program counter while
    /// <c>debug:bpset</c> blocks. <see cref="ExecutionCapability.Memory"/> plus
    /// <see cref="ExecutionCapability.CpuState"/>, which is what keeps MAME
    /// non-blocking and in no way a replacement for Mesen2.</item>
    /// </list>
    ///
    /// MAME has no adapter yet — A6 is deferred — so its row is data with no
    /// reader. It is written down anyway, and tested, because the alternative is a
    /// capability set discovered later from a document rather than from a probe.
    /// </summary>
    public static class ExecutionCapabilities
    {
        /// <summary>Mesen2 2.1.1: every bit, and the only measured backend with all of them.</summary>
        public const ExecutionCapability Mesen2_2_1_1 = ExecutionCapability.FullControl;

        /// <summary>MesenCE 2.2.1: memory and snapshots, and no execution control at all.</summary>
        public const ExecutionCapability MesenCe_2_2_1 =
            ExecutionCapability.Memory | ExecutionCapability.StateSaveLoad;

        /// <summary>MAME 0.289: memory and registers, and nothing it would have to block for.</summary>
        public const ExecutionCapability Mame_0_289 =
            ExecutionCapability.Memory | ExecutionCapability.CpuState;

        /// <summary>
        /// The name and version as the handshake spells them. The bridges answer
        /// <c>OK Mesen2 2.1.1</c> and <c>OK MesenCE 2.2.1 nesDebug</c>, so the first
        /// two tokens are the identity and the rest is decoration.
        /// </summary>
        private sealed class MeasuredHost
        {
            public MeasuredHost(string name, string version, ExecutionCapability capabilities)
            {
                Name = name;
                Version = version;
                Capabilities = capabilities;
            }

            public string Name { get; }
            public string Version { get; }
            public ExecutionCapability Capabilities { get; }
        }

        private static readonly MeasuredHost[] Measured = new MeasuredHost[]
        {
            new MeasuredHost("Mesen2", "2.1.1", Mesen2_2_1_1),
            new MeasuredHost("MesenCE", "2.2.1", MesenCe_2_2_1),
            new MeasuredHost("MAME", "0.289", Mame_0_289),
        };

        /// <summary>
        /// What the host measured <paramref name="emulatorName"/> <paramref name="emulatorVersion"/>
        /// able to do, or <see cref="ExecutionCapability.None"/> when that build was
        /// never measured.
        ///
        /// Both halves are compared, and both matter: MesenCE 2.2.1 and MesenCE 2.3.0
        /// are different answers to give, and only the version can tell them apart.
        /// </summary>
        public static ExecutionCapability For(string emulatorName, string emulatorVersion)
        {
            foreach (MeasuredHost host in Measured)
            {
                if (string.Equals(host.Name, emulatorName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(host.Version, emulatorVersion, StringComparison.OrdinalIgnoreCase))
                {
                    return host.Capabilities;
                }
            }

            return ExecutionCapability.None;
        }

        /// <summary>Whether this exact build is one the host measured.</summary>
        public static bool IsMeasured(string emulatorName, string emulatorVersion)
        {
            return For(emulatorName, emulatorVersion) != ExecutionCapability.None;
        }

        /// <summary>
        /// What the backend attached to this session can do.
        ///
        /// Asked of the contract rather than guessed from the interface: a backend
        /// that implements <see cref="IExecutionAdapter"/> has answered for itself,
        /// and one that does not has claimed nothing, so the answer is
        /// <see cref="ExecutionCapability.None"/>. Inferring the flags from the
        /// members a type happens to implement is how a test backend ends up
        /// claiming a processor it does not have.
        ///
        /// Never throws. A backend whose <see cref="IExecutionAdapter.Capabilities"/>
        /// fails must not be able to take the status line, or the key bindings, or
        /// the session down with it.
        /// </summary>
        public static ExecutionCapability Of(IMemoryBackend backend)
        {
            if (backend == null)
                return ExecutionCapability.None;

            IExecutionAdapter adapter = backend as IExecutionAdapter;
            if (adapter == null)
                return ExecutionCapability.None;

            try
            {
                return adapter.Capabilities;
            }
            catch (MonitorException)
            {
                return ExecutionCapability.None;
            }
            catch (InvalidOperationException)
            {
                return ExecutionCapability.None;
            }
        }

        /// <summary>
        /// Whether <paramref name="capabilities"/> grants <paramref name="required"/>,
        /// all of it. The shell's whole gate is this one line, which is why it asks
        /// for every bit rather than any bit: an action that needs run and step is
        /// offered only where both were measured.
        /// </summary>
        public static bool Has(ExecutionCapability capabilities, ExecutionCapability required)
        {
            return (capabilities & required) == required;
        }

        /// <summary>
        /// What this machine can do, in the fewest words that are still true.
        ///
        /// Shared by the status line and the help screen on purpose: two wordings for
        /// one flag set is how a help screen ends up promising something the status
        /// line has denied.
        ///
        /// Bounded, because it is drawn in one row among six other facts. Eleven
        /// words would push the breakpoint count off the line, and the breakpoint
        /// count is one of the things this project refuses to hide. Execution control
        /// that is complete collapses to the single word "control" for the same
        /// reason: five words for five working verbs is a worse status line than one
        /// word that is still true.
        /// </summary>
        public static string Summary(ExecutionCapability capabilities)
        {
            if (capabilities == ExecutionCapability.None)
                return "nothing measured";

            if ((capabilities & ExecutionCapability.FullControl) == ExecutionCapability.FullControl)
                return "full control";

            List<string> parts = new List<string>();

            if (Has(capabilities, ExecutionCapability.Memory))
                parts.Add("memory");
            else
            {
                if (Has(capabilities, ExecutionCapability.MemoryRead)) parts.Add("memory read");
                if (Has(capabilities, ExecutionCapability.MemoryWrite)) parts.Add("memory write");
            }

            AddIf(parts, capabilities, ExecutionCapability.CpuState, "registers");

            if (Has(capabilities, ExecutionControl))
                parts.Add("control");
            else
            {
                AddIf(parts, capabilities, ExecutionCapability.Pause, "pause");
                AddIf(parts, capabilities, ExecutionCapability.Resume, "resume");
                AddIf(parts, capabilities, ExecutionCapability.Reset, "reset");
                AddIf(parts, capabilities, ExecutionCapability.StepInstruction, "step");
                AddIf(parts, capabilities, ExecutionCapability.BreakpointExecution, "breakpoints");
            }

            AddIf(parts, capabilities, ExecutionCapability.WatchpointRead, "read watchpoints");
            AddIf(parts, capabilities, ExecutionCapability.WatchpointWrite, "write watchpoints");
            AddIf(parts, capabilities, ExecutionCapability.StateSaveLoad, "snapshots");

            if (parts.Count <= SummaryItems)
                return string.Join(" + ", parts.ToArray());

            return string.Join(" + ", parts.GetRange(0, SummaryItems).ToArray())
                + " + " + (parts.Count - SummaryItems) + " more";
        }

        /// <summary>
        /// The execution verbs, as one word.
        ///
        /// Not a capability — nothing sets this bit and nothing reads it as one. It
        /// is the shape of the flag set as a sentence reads best, and
        /// <see cref="Summary"/> is the only place that uses it.
        /// </summary>
        private const ExecutionCapability ExecutionControl =
            ExecutionCapability.Pause | ExecutionCapability.Resume | ExecutionCapability.Reset
            | ExecutionCapability.StepInstruction | ExecutionCapability.BreakpointExecution;

        /// <summary>
        /// Words <see cref="Summary"/> spends before it starts counting the rest.
        /// Three, because three is what fits beside the host, the run state and the
        /// program counter without costing any of them.
        /// </summary>
        private const int SummaryItems = 3;

        /// <summary>
        /// What the attached machine cannot be asked to do about execution control,
        /// or null when it can.
        ///
        /// Derived from the bits rather than written down, so a backend gaining
        /// <see cref="ExecutionCapability.Resume"/> stops being told it cannot run.
        ///
        /// No host in it, because the line already names the host two segments
        /// earlier: "cannot run, step or break" beside "MesenCE 2.2.1" says who, and
        /// "cannot run, step or break on this host" would spend eleven columns saying
        /// it again — on the one row where the breakpoint count gets pushed off by a
        /// phrase that adds nothing.
        /// </summary>
        public static string MissingExecutionControl(ExecutionCapability capabilities)
        {
            if (Has(capabilities, ExecutionCapability.FullControl))
                return null;

            List<string> missing = new List<string>();

            if (!Has(capabilities, ExecutionCapability.Resume)) missing.Add("run");
            if (!Has(capabilities, ExecutionCapability.StepInstruction)) missing.Add("step");
            if (!Has(capabilities, ExecutionCapability.BreakpointExecution)) missing.Add("break");

            if (missing.Count == 0)
                return null;

            return "cannot " + Join(missing);
        }

        /// <summary>
        /// The action a bit stands for, in the words a refusal needs: "resume",
        /// "step one instruction", "save or restore a snapshot".
        ///
        /// No host in them, because a refusal already names the host and saying it
        /// twice makes the sentence worse rather than clearer.
        /// </summary>
        public static string Phrase(ExecutionCapability capability)
        {
            switch (capability)
            {
                case ExecutionCapability.Pause: return "pause";
                case ExecutionCapability.Resume: return "resume";
                case ExecutionCapability.Reset: return "reset";
                case ExecutionCapability.StepInstruction: return "step one instruction";
                case ExecutionCapability.BreakpointExecution: return "break on an address";
                case ExecutionCapability.WatchpointRead: return "watch a read";
                case ExecutionCapability.WatchpointWrite: return "watch a write";
                case ExecutionCapability.StateSaveLoad: return "save or restore a snapshot";
                case ExecutionCapability.CpuState: return "report registers";
                case ExecutionCapability.MemoryRead: return "read memory";
                case ExecutionCapability.MemoryWrite: return "write memory";
                default: return "do that";
            }
        }

        /// <summary>
        /// What was measured, in the words the measurement was written in.
        ///
        /// The bridges' own sentences, not a paraphrase: "emu.pause does not exist"
        /// is a fact about a build that a user can check, and "unsupported" is not.
        /// Read out of <c>Bridge/bridge.lua</c>, which is the code that refuses by
        /// those names.
        /// </summary>
        public static string Measurement(ExecutionCapability capability)
        {
            switch (capability)
            {
                case ExecutionCapability.Pause:
                    return "MesenCE 2.2.1 does not expose emu.pause";
                case ExecutionCapability.Resume:
                    return "emu.resume refuses calls made outside a callback";
                case ExecutionCapability.Reset:
                    return "emu.reset changes global state with no callback context";
                case ExecutionCapability.StepInstruction:
                    return "emu.step refuses calls made outside a callback";
                case ExecutionCapability.BreakpointExecution:
                case ExecutionCapability.WatchpointRead:
                case ExecutionCapability.WatchpointWrite:
                    return "emu.addMemoryCallback refuses every function, named ones included";
                case ExecutionCapability.CpuState:
                    return "emu.getCpuState does not exist in MesenCE 2.2.1";
                case ExecutionCapability.MemoryWrite:
                    return "a write into PRG ROM is dropped silently, so a write is only claimed where it comes back";
                case ExecutionCapability.StateSaveLoad:
                    return "MesenCE 2.2.1 has emu.getState and emu.setState; MAME 0.289 was not measured for it";
                case ExecutionCapability.MemoryRead:
                    return "every measured backend reads its address space";
                default:
                    return "no measurement backs this bit on the attached build";
            }
        }

        private static void AddIf(List<string> parts, ExecutionCapability capabilities,
            ExecutionCapability bit, string word)
        {
            if (Has(capabilities, bit))
                parts.Add(word);
        }

        /// <summary>"run", "run and step", "run, step or break". Two separators spelled
        /// two ways is one more than is needed already.</summary>
        private static string Join(IReadOnlyList<string> words)
        {
            StringBuilder text = new StringBuilder();

            for (int i = 0; i < words.Count; i++)
            {
                if (i > 0)
                    text.Append(i == words.Count - 1 ? " or " : ", ");

                text.Append(words[i]);
            }

            return text.ToString();
        }

        /// <summary>
        /// The published capability table, as text: one line per measured build.
        ///
        /// Kept beside the numbers so the two cannot drift apart, and rendered from
        /// the same <see cref="Summary"/> the status line uses.
        /// </summary>
        public static IReadOnlyList<string> Table()
        {
            List<string> rows = new List<string>();

            foreach (MeasuredHost host in Measured)
            {
                rows.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} {1}: {2} ({3})",
                    host.Name, host.Version, Summary(host.Capabilities), host.Capabilities));
            }

            return rows;
        }
    }
}