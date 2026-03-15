using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static UIElement BuildFieldset(string legend, UIElement content)
        {
            var host = new Grid
            {
                Margin = new Thickness(0, 0, 0, 12)
            };

            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var legendBorder = new Border
            {
                Background = SystemColors.WindowBrush,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(14, 0, 0, -10),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };

            Panel.SetZIndex(legendBorder, 2);

            var legendText = new TextBlock
            {
                Text = legend,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = SystemColors.WindowTextBrush
            };

            legendBorder.Child = legendText;

            var border = new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = SystemColors.ActiveBorderBrush,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 18, 10, 14),
                Background = SystemColors.WindowBrush,
                Child = content
            };

            Grid.SetRow(legendBorder, 0);
            Grid.SetRow(border, 1);

            host.Children.Add(border);
            host.Children.Add(legendBorder);

            return host;
        }
    }
}