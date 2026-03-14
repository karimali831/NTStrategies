using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static TextBlock CreateTableHeader(string text)
        {
            return new TextBlock
            {
                Text = text ?? "",
                Margin = new Thickness(6, 4, 6, 4),
                FontWeight = FontWeights.SemiBold,
                Foreground = PrimaryTextBrush(),
                VerticalAlignment = VerticalAlignment.Center
            };
        }
    }
}