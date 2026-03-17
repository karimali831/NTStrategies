using System.Windows;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static Thickness ControlBorderThickness()
        {
            return new Thickness(1);
        }

        private static double MainButtonHeight()
        {
            return 36;
        }

        private static double InputHeight()
        {
            return 30;
        }

        private static double SmallButtonHeight()
        {
            return 24;
        }
        
        private static Brush CardBackgroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(30, 30, 30))
                : Brushes.White;
        }

        private static Brush CardBorderBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(68, 68, 68))
                : new SolidColorBrush(Color.FromRgb(200, 200, 200));
        }

        private static Brush PrimaryTextBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(232, 232, 232))
                : SystemColors.WindowTextBrush;
        }

        private static Brush SecondaryTextBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(180, 180, 180))
                : Brushes.DimGray;
        }

        private static Brush InputBackgroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(40, 40, 40))
                : Brushes.White;
        }

        private static Brush InputForegroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(235, 235, 235))
                : Brushes.Black;
        }
        
        private static Brush InputDisabledBackgroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(52, 52, 52))
                : new SolidColorBrush(Color.FromRgb(240, 240, 240));
        }
        
        private static Brush InputBorderBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(82, 82, 82))
                : new SolidColorBrush(Color.FromRgb(170, 170, 170));
        }

        private static Brush DisabledBackgroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(58, 58, 58))
                : new SolidColorBrush(Color.FromRgb(205, 205, 205));
        }

        private static Brush DisabledForegroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(210, 210, 210))
                : new SolidColorBrush(Color.FromRgb(90, 90, 90));
        }

        private static Brush OutlineNeutralBorderBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(92, 92, 92))
                : new SolidColorBrush(Color.FromRgb(170, 170, 170));
        }

        private static Brush PrimaryActionBrush()
        {
            return new SolidColorBrush(Color.FromRgb(37, 140, 242));
        }

        private static Brush SuccessActionBrush()
        {
            return new SolidColorBrush(Color.FromRgb(24, 150, 64));
        }

        private static Brush DangerActionBrush()
        {
            return new SolidColorBrush(Color.FromRgb(180, 20, 20));
        }

        private static Brush WarningActionBrush()
        {
            return new SolidColorBrush(Color.FromRgb(219, 163, 27));
        }

        private static Brush ActionForegroundBrush()
        {
            return Brushes.White;
        }

        private static Brush DotConnectedOnBrush()
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

        private static Brush DotOffBrush()
        {
            return Brushes.Transparent;
        }

        private static Brush DotBorderBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(120, 120, 120))
                : new SolidColorBrush(Color.FromRgb(150, 150, 150));
        }

        private static Brush TableHeaderBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(38, 38, 38))
                : new SolidColorBrush(Color.FromRgb(240, 240, 240));
        }

        private static Brush TableRowAltBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(30, 30, 30))
                : new SolidColorBrush(Color.FromRgb(248, 248, 248));
        }
    }
}