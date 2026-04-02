using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
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