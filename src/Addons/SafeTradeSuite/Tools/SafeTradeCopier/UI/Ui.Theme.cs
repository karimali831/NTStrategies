using System.Windows;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private bool IsDarkTheme()
        {
            var c = SystemColors.WindowColor;
            var luminance = (0.2126 * c.R) + (0.7152 * c.G) + (0.0722 * c.B);
            return luminance < 140;
        }
        
        private Brush DotConnectedOnBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(38, 200, 78))
                : new SolidColorBrush(Color.FromRgb(24, 160, 64));
        }

        private Brush DotWarningBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(255, 170, 0))
                : new SolidColorBrush(Color.FromRgb(230, 140, 0));
        }

        private Brush DotOffBrush()
        {
            return Brushes.Transparent;
        }

        private Brush DotBorderBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(120, 120, 120))
                : new SolidColorBrush(Color.FromRgb(150, 150, 150));
        }

        private Brush TableHeaderBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(38, 38, 38))
                : new SolidColorBrush(Color.FromRgb(240, 240, 240));
        }

        private Brush TableRowAltBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(30, 30, 30))
                : new SolidColorBrush(Color.FromRgb(248, 248, 248));
        }
    }
}