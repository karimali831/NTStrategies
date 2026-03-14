using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static Brush SuccessTextBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(88, 214, 141))
                : new SolidColorBrush(Color.FromRgb(24, 140, 64));
        }

        private static Brush DangerTextBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(255, 120, 120))
                : new SolidColorBrush(Color.FromRgb(180, 35, 35));
        }

        private static Brush WarningTextBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(255, 193, 79))
                : new SolidColorBrush(Color.FromRgb(184, 123, 0));
        }

        private static Brush MutedBorderBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(64, 64, 64))
                : new SolidColorBrush(Color.FromRgb(220, 220, 220));
        }

        private static Brush SoftPanelBackgroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(28, 28, 28))
                : new SolidColorBrush(Color.FromRgb(252, 252, 252));
        }
    }
}