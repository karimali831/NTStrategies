using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static Grid CreateTableRowGrid(Thickness? margin = null)
        {
            return new Grid
            {
                Margin = margin ?? new Thickness(2, 2, 2, 2),
                Background = TableRowAltBrush()
            };
        }
    }
}