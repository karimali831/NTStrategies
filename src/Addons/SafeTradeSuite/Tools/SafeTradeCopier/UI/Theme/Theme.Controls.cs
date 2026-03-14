using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static void ApplyInputChrome(Control c)
        {
            if (c == null)
                return;

            c.Height = InputHeight();
            c.Background = InputBackgroundBrush();
            c.Foreground = InputForegroundBrush();
            c.BorderBrush = InputBorderBrush();
            c.BorderThickness = ControlBorderThickness();
            c.Padding = new Thickness(8, 0, 8, 0);
        }

        private static void ApplyCardChrome(Border border)
        {
            if (border == null)
                return;

            border.Background = CardBackgroundBrush();
            border.BorderBrush = CardBorderBrush();
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(8);
            border.Effect = SoftShadow();
        }
    }
}