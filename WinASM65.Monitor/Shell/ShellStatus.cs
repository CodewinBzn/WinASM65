using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using WinASM65.Monitor.Abstractions;

namespace WinASM65.Monitor.Shell
{
    /// <summary>One run of text on the status line, with the role to paint it.</summary>
    public sealed class StatusSegment
    {
        public StatusSegment(string text, string role, int priority)
        {
            Text = text ?? string.Empty;
            Role = role ?? ThemeRole.Default;
            Priority = priority;
        }

        public string Text { get; }

        /// <summary>A <see cref="ThemeRole"/> name.</summary>
        public string Role { get; }

        /// <summary>
        /// Higher survives truncation. The host and whether the machine is running
        /// outrank the cycle count and the cartridge line, because they are what a
        /// user glances at; the rest is detail they can ask for.
        /// </summary>
        public int Priority { get; }
    }

    /// <summary>
    /// The status line: what the session is attached to, and what the machine is
    /// doing right now.
    ///
    /// Built as text in one method with no terminal in it, so the wording can be
    /// asserted. "Stopped" versus "running" and the frozen-machine warning are
    /// statements the user makes decisions on, and both are the kind of thing that
    /// goes quietly wrong when it is assembled inside a draw callback.
    ///
    /// The frozen warning keys on a machine that has not executed an instruction,
    /// not on one that was paused on purpose. Mesen2 is deliberately paused most of
    /// the time, and condemning that would condemn the only backend that works.
    /// </summary>
    public static class ShellStatus
    {
        public const int PriorityHost = 100;
        public const int PriorityExecution = 90;

        /// <summary>
        /// The machine's declared capability set.
        ///
        /// Above the program counter and below the run state, deliberately. It is
        /// what the user has to know before they press anything, and it is short
        /// enough ("caps: full control") to cost almost nothing on the line it
        /// displaces — but the program counter is what a user glances at while
        /// debugging, and this is what a user reads once.
        /// </summary>
        public const int PriorityCapabilities = 85;

        public const int PriorityProgramCounter = 80;
        public const int PriorityControlWarning = 58;
        public const int PriorityWarning = 55;
        public const int PriorityRegisters = 40;
        public const int PriorityBreakpoints = 35;
        public const int PriorityCartridge = 25;

        /// <summary>
        /// Reads the machine and composes the line. Never throws: a backend that
        /// refuses to report its CPU produces a line that says so, because a status
        /// line that cannot be read is the one failure that leaves the user with
        /// nothing at all.
        ///
        /// What the machine can do is read from the same flag set the key bindings
        /// are gated against, so the line and the greyed-out keys cannot contradict
        /// each other. It is a statement of what was declared, not of what was tried:
        /// nothing here probes the machine to find out what it can do.
        /// </summary>
        public static IReadOnlyList<StatusSegment> Read(IMemoryBackend backend, int breakpointCount)
        {
            if (backend == null)
                return Compose(null, null, false, null, false, breakpointCount, null, null);

            string name;
            string version;
            bool running;
            try
            {
                name = backend.EmulatorName;
                version = backend.EmulatorVersion;
                running = backend.IsRunning;
            }
            catch (MonitorException)
            {
                name = null;
                version = null;
                running = false;
            }

            ExecutionCapability capabilities = ExecutionCapabilities.Of(backend);

            ICpuStateSource source = backend as ICpuStateSource;
            CpuSnapshot cpu = ReadCpu(source);

            IRomInfoSource cartridge = backend as IRomInfoSource;
            string rom = ReadRomInfo(cartridge);

            // The flag decides whether registers are asked for, and the reading
            // decides whether they are shown. A machine that declares no CpuState
            // is never asked, so there is no reading to fail — and one that declares
            // it and then cannot answer is the case this line has always covered.
            bool canObserveCpu = ExecutionCapabilities.Has(capabilities, ExecutionCapability.CpuState)
                && source != null
                && cpu != null;

            return Compose(name, version, canObserveCpu, cpu, running, breakpointCount, rom, capabilities);
        }

        private static CpuSnapshot ReadCpu(ICpuStateSource source)
        {
            if (source == null)
                return null;

            try
            {
                return source.ReadCpuState();
            }
            catch (MonitorException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static string ReadRomInfo(IRomInfoSource cartridge)
        {
            if (cartridge == null)
                return null;

            try
            {
                return cartridge.ReadRomInfo();
            }
            catch (MonitorException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        /// <summary>
        /// The line, from what has already been read. Every parameter is a reading
        /// rather than a decision, so a caller can compose a line for a machine it
        /// could not reach and the line still comes out whole.
        /// </summary>
        /// <param name="cartridge">Cartridge line as the machine described it, or null.</param>
        /// <param name="capabilities">
        /// What the attached machine declared it can do, or null when nobody
        /// declared anything and the line has nothing to say about it.
        ///
        /// Null and <see cref="ExecutionCapability.None"/> are different facts and
        /// are rendered differently: null is "not asked", which is how a caller
        /// composes a line for a machine it has no adapter for, and None is "asked,
        /// and the answer is that nothing was measured".
        /// </param>
        public static IReadOnlyList<StatusSegment> Compose(
            string emulatorName,
            string emulatorVersion,
            bool canObserveCpu,
            CpuSnapshot cpu,
            bool running,
            int breakpointCount,
            string cartridge,
            ExecutionCapability? capabilities = null)
        {
            List<StatusSegment> segments = new List<StatusSegment>();

            segments.Add(new StatusSegment("WinASM65 monitor", ThemeRole.Chrome, PriorityHost));
            segments.Add(new StatusSegment(
                (emulatorName ?? "?") + " " + (emulatorVersion ?? "?"),
                ThemeRole.Chrome,
                PriorityHost));

            segments.Add(new StatusSegment(
                running ? "RUNNING" : "STOPPED",
                running ? ThemeRole.Label : ThemeRole.Warning,
                PriorityExecution));

            if (capabilities.HasValue)
            {
                string summary = ExecutionCapabilities.Summary(capabilities.Value);

                segments.Add(new StatusSegment(
                    "caps: " + summary,
                    capabilities.Value == ExecutionCapability.None ? ThemeRole.Warning : ThemeRole.Label,
                    PriorityCapabilities));

                // Named separately, and only when something is actually missing: the
                // summary says what there is, this says which of F9, F10 and F8 are
                // about to refuse, before the user presses them.
                string missing = ExecutionCapabilities.MissingExecutionControl(capabilities.Value);
                if (missing != null)
                {
                    segments.Add(new StatusSegment(
                        missing,
                        ThemeRole.Warning,
                        PriorityControlWarning));
                }
            }

            if (!canObserveCpu || cpu == null)
            {
                segments.Add(new StatusSegment(
                    "no CPU view: this backend cannot report registers",
                    ThemeRole.Warning,
                    PriorityWarning));
            }
            else
            {
                segments.Add(new StatusSegment(
                    "$" + cpu.Pc.ToString("X4"),
                    ThemeRole.Operand,
                    PriorityProgramCounter));

                if (cpu.LooksPoweredDown)
                {
                    segments.Add(new StatusSegment(
                        "at reset vector: nothing has executed yet",
                        ThemeRole.Warning,
                        PriorityWarning));
                }

                segments.Add(new StatusSegment(
                    string.Format(CultureInfo.InvariantCulture,
                        "A={0:X2} X={1:X2} Y={2:X2} SP={3:X2} cyc={4}",
                        cpu.A, cpu.X, cpu.Y, cpu.Sp, cpu.CycleCount),
                    ThemeRole.Gutter,
                    PriorityRegisters));
            }

            segments.Add(new StatusSegment(
                "BP=" + breakpointCount.ToString(CultureInfo.InvariantCulture),
                breakpointCount == 0 ? ThemeRole.Gutter : ThemeRole.Breakpoint,
                PriorityBreakpoints));

            if (!string.IsNullOrEmpty(cartridge))
                segments.Add(new StatusSegment(cartridge, ThemeRole.Gutter, PriorityCartridge));

            return segments;
        }

        /// <summary>
        /// The segments that fit in <paramref name="width"/>, in the order they were
        /// authored, with whole segments dropped from the least important end until
        /// the line fits.
        ///
        /// Dropping whole segments rather than cutting one in half is deliberate. A
        /// status line reading <c>PC=$C0</c> is a wrong reading; one reading
        /// <c>me mesen2 2.1.1</c> is visibly a cut line, and the user knows to
        /// widen the window. What is never dropped is the host and whether the
        /// machine is running: a line that omits those stops answering the question
        /// it exists to answer.
        ///
        /// Shared with the status pane, so the coloured line on screen and the plain
        /// line a test asserts are the same decision rather than two.
        /// </summary>
        public static List<StatusSegment> SelectFitting(IReadOnlyList<StatusSegment> segments, int width)
        {
            List<StatusSegment> kept = new List<StatusSegment>();
            if (segments == null || width <= 0)
                return kept;

            kept.AddRange(segments);
            while (kept.Count > 0 && Length(kept) > width)
            {
                int victim = LeastImportant(kept);
                if (victim < 0)
                    break;
                kept.RemoveAt(victim);
            }

            return kept;
        }

        /// <summary>The fitting segments joined into one line.</summary>
        public static string Fit(IReadOnlyList<StatusSegment> segments, int width)
        {
            return Join(SelectFitting(segments, width));
        }

        /// <summary>
        /// The least important segment that may be dropped, or -1 when only
        /// mandatory segments are left. Ties break towards the last segment, so the
        /// first thing on the line is the first thing lost when it is the last of
        /// its rank.
        /// </summary>
        private static int LeastImportant(IReadOnlyList<StatusSegment> segments)
        {
            int victim = -1;
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].Priority >= PriorityExecution)
                    continue;
                if (victim < 0 || segments[i].Priority <= segments[victim].Priority)
                    victim = i;
            }
            return victim;
        }

        private static string Join(IReadOnlyList<StatusSegment> segments)
        {
            StringBuilder text = new StringBuilder();
            foreach (StatusSegment segment in segments)
            {
                if (text.Length > 0)
                    text.Append("  ");
                text.Append(segment.Text);
            }
            return text.ToString();
        }

        private static int Length(IReadOnlyList<StatusSegment> segments)
        {
            int length = 0;
            foreach (StatusSegment segment in segments)
                length += segment.Text.Length + 2;
            return Math.Max(0, length - 2);
        }
    }
}