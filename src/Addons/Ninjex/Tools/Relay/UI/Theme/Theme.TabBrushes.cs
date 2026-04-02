using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private static Brush TabBackgroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(24, 24, 24))
                : new SolidColorBrush(Color.FromRgb(245, 245, 245));
        }

        private static Brush TabSelectedBackgroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(42, 42, 42))
                : new SolidColorBrush(Color.FromRgb(255, 255, 255));
        }

        private static Brush TabBorderBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(58, 58, 58))
                : new SolidColorBrush(Color.FromRgb(210, 210, 210));
        }
        
        private static Brush TabSelectedBorderBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(70, 70, 70))
                : new SolidColorBrush(Color.FromRgb(180, 180, 180));
        }
    }
}