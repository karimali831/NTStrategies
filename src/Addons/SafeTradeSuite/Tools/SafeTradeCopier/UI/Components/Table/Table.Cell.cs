using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static TextBlock CreateTableCell(string text, Brush foreground = null)
        {
            return new TextBlock
            {
                Text = text ?? "",
                Margin = new Thickness(6, 4, 6, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = foreground ?? PrimaryTextBrush(),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }
    }
}