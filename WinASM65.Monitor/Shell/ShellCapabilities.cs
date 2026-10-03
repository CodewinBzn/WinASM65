using System;
using WinASM65.Monitor.Abstractions;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// The sentences the shell uses to say that this machine cannot do what a key
    /// would do.
    ///
    /// One owner for this wording, and no Terminal.Gui in it, for the same reason
    /// <see cref="ShellLayout"/> keeps its arithmetic free of views: a refusal the
    /// user reads is a claim, and a claim has to be assertable without a terminal.
    /// <see cref="ExecutionCapabilities"/> owns the facts; this owns only how the
    /// shell phrases them.
    ///
    /// Two rules, both already stated elsewhere in this project and kept here:
    ///
    /// <list type="number">
    /// <item>A refusal is a string, never an exception. A pane must not be able to
    /// take the shell down, and a key that cannot act is an ordinary outcome, not a
    /// failure of the shell.</item>
    /// <item>The refusal names the reason. "F9 does nothing" is indistinguishable
    /// from a broken key binding; "MesenCE 2.2.1 cannot resume" is something the
    /// user can act on, which is either stopping or attaching to a different
    /// backend.</item>
    /// </list>
    ///
    /// The measurement itself goes on the help screen rather than into the message
    /// line. The message is one row wide: a refusal that does not fit is a refusal
    /// the user cannot read, and the measurement is two lines longer than the
    /// sentence it belongs to.
    /// </summary>
    public static class ShellCapabilities
    {
        /// <summary>
        /// Whether the attached machine grants <paramref name="required"/> in full.
        ///
        /// The whole gate. Every bit is asked for, not any bit, so an action that
        /// needs both run and step is offered only where both were measured.
        /// </summary>
        public static bool Allows(ExecutionCapability machine, ExecutionCapability required)
        {
            return ExecutionCapabilities.Has(machine, required);
        }

        /// <summary>
        /// What the user sees when a key names an action this machine cannot
        /// perform, in one row.
        ///
        /// The exact text is
        /// <c>&lt;key&gt; unavailable on &lt;host&gt;: this machine cannot &lt;action&gt;.
        /// F1 names the measurement.</c>
        /// — short enough to fit the message row at any supported width, and it
        /// points at the place the full reason is written down rather than
        /// truncating the reason away.
        /// </summary>
        public static string Refusal(string key, string host, ExecutionCapability required)
        {
            return (key ?? "?") + " unavailable on " + Host(host)
                + ": this machine cannot " + ExecutionCapabilities.Phrase(required)
                + ". F1 names the measurement.";
        }

        /// <summary>
        /// What the help screen prints beside a key this build binds and this
        /// machine cannot use.
        ///
        /// The measurement is in here, from
        /// <see cref="ExecutionCapabilities.Measurement"/>, because this is the one
        /// screen the user opens precisely to find out why a key refused.
        /// </summary>
        public static string HelpNote(string host, ExecutionCapability required)
        {
            return "(bound, but " + Host(host) + " cannot "
                + ExecutionCapabilities.Phrase(required) + ": "
                + ExecutionCapabilities.Measurement(required) + ")";
        }

        /// <summary>
        /// The header the help screen and the status line both take their wording
        /// from, so the two cannot describe the same machine differently.
        /// </summary>
        public static string MachineLine(string host, ExecutionCapability machine)
        {
            return "this machine: " + Host(host) + " -- " + ExecutionCapabilities.Summary(machine);
        }

        /// <summary>
        /// The host's name, however it was written. An unnamed backend is named as
        /// such rather than left blank: a refusal that does not say which machine
        /// refused cannot be acted on, and there is always a machine.
        /// </summary>
        private static string Host(string host)
        {
            return string.IsNullOrWhiteSpace(host) ? "the attached backend" : host.Trim();
        }
    }
}