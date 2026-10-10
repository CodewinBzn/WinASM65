using System;
using SharpConsoleUI;
using SharpConsoleUI.Controls;

namespace WinASM65.Monitor.Shell
{
    /// <summary>
    /// One key the plan binds, whether this build binds it, and what it needs.
    ///
    /// This is the Terminal.Gui <c>Shortcut</c> concept carried forward: a key plus
    /// an action. SharpConsoleUI has no Shortcut type, so the shell keeps its own
    /// rather than adopting a different binding model.
    /// </summary>
    public sealed class Shortcut
    {
        public Shortcut(string key, Action action)
        {
            Key = key;
            Action = action;
        }

        public string Key { get; }
        public Action Action { get; }
    }
}