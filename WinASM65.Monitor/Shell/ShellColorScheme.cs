using System;
using Terminal.Gui;

namespace WinASM65.Monitor.Shell
{
    /// <summary>Which roles a widget paints with.</summary>
    public enum ColorSchemeRoles
    {
        /// <summary>Frames, titles, dim chrome.</summary>
        Chrome,

        /// <summary>Body text.</summary>
        Body,

        /// <summary>Text that is telling the user something went wrong or is odd.</summary>
        Warning,

        /// <summary>Text that reports a failure.</summary>
        Error
    }

    /// <summary>
    /// Turns the shell palette into Terminal.Gui colour schemes.
    ///
    /// The library's colour scheme is a fixed set of five attributes — normal,
    /// focus, hot-normal, disabled, hot-focus — and the shell has twelve roles.
    /// Mapping roles onto those five, once, is what keeps the mapping in one file
    /// instead of scattered through every pane: a role added to the palette has to
    /// be assigned somewhere, and "somewhere" being a single switch is the whole
    /// reason a new role cannot be silently unpainted.
    ///
    /// Focus is deliberately one step stronger than normal rather than a different
    /// hue. A 6502 listing is read as a column of aligned hex; a focus colour that
    /// changed the hue would move nothing geometrically but would change how every
    /// byte reads, which is the opposite of what focus is for.
    /// </summary>
    public static class ShellColorScheme
    {
        public static Terminal.Gui.ColorScheme Build(ShellTheme theme, ColorSchemeRoles roles)
        {
            ShellTheme palette = theme ?? ShellTheme.Default;

            Terminal.Gui.Attribute normal = palette.Attribute(RoleFor(roles, false));
            Terminal.Gui.Attribute focus = palette.Attribute(RoleFor(roles, true));

            return new Terminal.Gui.ColorScheme(
                normal,
                focus,
                normal,
                palette.Attribute(ThemeRole.Comment),
                focus);
        }

        private static string RoleFor(ColorSchemeRoles roles, bool focused)
        {
            // A focused pane is painted one step up the same family, so focus is
            // visible without introducing a colour the palette does not define.
            switch (roles)
            {
                case ColorSchemeRoles.Chrome:
                    return focused ? ThemeRole.Default : ThemeRole.Chrome;
                case ColorSchemeRoles.Warning:
                    return focused ? ThemeRole.Warning : ThemeRole.Comment;
                case ColorSchemeRoles.Error:
                    return focused ? ThemeRole.Error : ThemeRole.Warning;
                default:
                    return focused ? ThemeRole.Operand : ThemeRole.Default;
            }
        }
    }
}