using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

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
        public const int PriorityProgramCounter = 80;
        public const int PriorityWarning = 55;
        public const int PriorityRegisters = 40;
        public const int PriorityBreakpoints = 35;
        public const int PriorityCartridge = 25;

        /// <summary>
        /// Reads the machine and composes the line. Never throws: a backend that
        /// refuses to report its CPU produces a line that says so, because a status
        /// line that cannot be read is the one failure that leaves the user with
        /// nothing at all.
        /// </summary>
        public static IReadOnlyList<StatusSegment> Read(IMemoryBackend backend, int breakpointCount)
        {
            if (backend == null)
                return Compose(null, null, false, null, false, breakpointCount, null);

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

            ICpuStateSource source = backend as ICpuStateSource;
            CpuSnapshot cpu = ReadCpu(source);

            IRomInfoSource cartridge = backend as IRomInfoSource;
            string rom = ReadRomInfo(cartridge);

            return Compose(name, version, source != null && cpu != null, cpu, running, breakpointCount, rom);
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

        public static IReadOnlyList<StatusSegment> Compose(
            string emulatorName,
            string emulatorVersion,
            bool canObserveCpu,
            CpuSnapshot cpu,
            bool running,
            int breakpointCount,
            string cartridge)
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