using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static Brush TabBackgroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(34, 34, 34))
                : new SolidColorBrush(Color.FromRgb(236, 236, 236));
        }
        
        private static Brush TabBackgroundBrush(bool active)
        {
            if (IsDarkTheme())
            {
                return active
                    ? new SolidColorBrush(Color.FromRgb(28, 28, 28))
                    : new SolidColorBrush(Color.FromRgb(36, 36, 36));
            }

            return active
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(242, 242, 242));
        }

        private static Brush TabBorderBrush(bool active)
        {
            if (IsDarkTheme())
            {
                return active
                    ? new SolidColorBrush(Color.FromRgb(120, 120, 120))
                    : new SolidColorBrush(Color.FromRgb(72, 72, 72));
            }

            return active
                ? new SolidColorBrush(Color.FromRgb(170, 170, 170))
                : new SolidColorBrush(Color.FromRgb(200, 200, 200));
        }

        private static Brush TabBorderBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(70, 70, 70))
                : new SolidColorBrush(Color.FromRgb(190, 190, 190));
        }

        private static Brush TabSelectedBackgroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(28, 28, 28))
                : Brushes.White;
        }

        private static Brush TabSelectedBorderBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(95, 95, 95))
                : new SolidColorBrush(Color.FromRgb(170, 170, 170));
        }
    }
}