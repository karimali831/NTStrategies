using System;
using System.Windows;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static ThemeMode _themeMode = ThemeMode.System;

        private static bool IsDarkTheme()
        {
            switch (_themeMode)
            {
                case ThemeMode.System:
                    return IsSystemDarkTheme();

                case ThemeMode.Light:
                    return false;

                case ThemeMode.Dark:
                    return true;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static bool IsSystemDarkTheme()
        {
            var c = SystemColors.WindowColor;
            var luminance = 0.2126 * c.R + (0.7152 * c.G) + (0.0722 * c.B);
            return luminance < 140;
        }
    }
}