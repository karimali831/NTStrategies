using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static TextBlock CreateFormLabel(string text, Thickness? margin = null)
        {
            return new TextBlock
            {
                Text = text ?? "",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = margin ?? new Thickness(0, 0, 8, 0),
                Foreground = PrimaryTextBrush(),
                FontWeight = FontWeights.SemiBold
            };
        }

        private static TextBlock CreateHintLabel(string text, Thickness? margin = null)
        {
            return new TextBlock
            {
                Text = text ?? "",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = margin ?? new Thickness(8, 0, 0, 0),
                Foreground = SecondaryTextBrush()
            };
        }
    }
}