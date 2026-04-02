using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
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
                Background = FieldsetLegendBackgroundBrush(),
                BorderBrush = FieldsetLegendBorderBrush(),
                BorderThickness = new Thickness(IsDarkTheme() ? 1 : 0),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 1, 10, 1),
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
                Foreground = WindowForegroundBrush()
            };

            legendBorder.Child = legendText;

            var border = new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = SectionBorderBrush(),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 18, 10, 14),
                Background = SectionBackgroundBrush(),
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