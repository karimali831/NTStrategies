using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static UIElement BuildTradesPlaceholder()
        {
            return new Border
            {
                Background = SectionBackgroundBrush(),
                BorderBrush = SectionBorderBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Child = new TextBlock
                {
                    Text = "Trades panel coming soon.",
                    Foreground = WindowForegroundBrush()
                }
            };
        }
    }
}