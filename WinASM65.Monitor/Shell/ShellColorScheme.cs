using System;
using SharpConsoleUI;

namespace WinASM65.Monitor.Shell
{
    public enum ColorSchemeRoles
    {
        Chrome,
        Body,
        Warning,
        Error
    }

    public static class ShellColorScheme
    {
        public static Color Foreground(ShellTheme theme, ColorSchemeRoles roles, bool focused)
        {
            ShellTheme palette = theme ?? ShellTheme.Default;
            return palette.Foreground(RoleFor(roles, focused));
        }

        private static string RoleFor(ColorSchemeRoles roles, bool focused)
        {
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