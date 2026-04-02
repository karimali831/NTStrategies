using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private static TextBlock CreateFormLabel(string text, double? width = null, Thickness? margin = null)
        {
            return new TextBlock
            {
                Text = text ?? "",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = margin ?? new Thickness(0, 0, 0, 0),
                Foreground = PrimaryTextBrush(),
                FontWeight = FontWeights.SemiBold,
                Width = width ?? 80
            };
        }

        private static TextBlock CreateHintLabel(string text, Thickness? margin = null)
        {
            return new TextBlock
            {
                Text = text ?? "",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = margin ?? new Thickness(0, 0, 0, 0),
                Foreground = SecondaryTextBrush()
            };
        }
    }
}