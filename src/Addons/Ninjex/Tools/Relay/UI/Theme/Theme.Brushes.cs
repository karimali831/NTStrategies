using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
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
        
        private static Brush DarkButtonBackgroundBrush()
        {
            return new SolidColorBrush(Color.FromRgb(30, 30, 30));
        }

        private static Brush DisabledBorderBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(88, 88, 88))
                : new SolidColorBrush(Color.FromRgb(190, 190, 190));
        }
        
        private static Brush HoverBackgroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(58, 58, 58))
                : new SolidColorBrush(Color.FromRgb(235, 242, 250));
        }

        private static Brush SelectedBackgroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(70, 70, 70))
                : new SolidColorBrush(Color.FromRgb(220, 232, 245));
        }
        
        private static Brush InputDisabledForegroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(185, 185, 185))
                : new SolidColorBrush(Color.FromRgb(120, 120, 120));
        }
        
        private static Brush FieldsetLegendBackgroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(24, 24, 24))
                : WindowBackgroundBrush();
        }

        private static Brush FieldsetLegendBorderBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(72, 72, 72))
                : Brushes.Transparent;
        }
        
        // Window-level
        private static Brush WindowBackgroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(30, 30, 30))
                : new SolidColorBrush(Color.FromRgb(245, 245, 245));
                // : new SolidColorBrush(Color.FromRgb(245, 245, 245));
        }

        private static Brush WindowForegroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(235, 235, 235))
                : new SolidColorBrush(Color.FromRgb(28, 28, 28));
        }

        private static Brush PanelBackgroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(30, 30, 30))
                : Brushes.White;
        }

        private static Brush SectionBackgroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(34, 34, 34))
                : new SolidColorBrush(Color.FromRgb(250, 250, 250));
        }

        private static Brush SectionBorderBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(70, 70, 70))
                : new SolidColorBrush(Color.FromRgb(190, 190, 190));
        }

        private static Brush MutedForegroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(160, 160, 160))
                : new SolidColorBrush(Color.FromRgb(105, 105, 105));
        }
        
        // dropdown
        private static Brush ComboItemHoverBackgroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(58, 58, 58))
                : new SolidColorBrush(Color.FromRgb(235, 242, 250));
        }

        private static Brush ComboItemSelectedBackgroundBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(78, 78, 78))
                : new SolidColorBrush(Color.FromRgb(220, 232, 245));
        }

        private static Brush ComboPopupBorderBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(78, 78, 78))
                : new SolidColorBrush(Color.FromRgb(190, 190, 190));
        }
    }
}