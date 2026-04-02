using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
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